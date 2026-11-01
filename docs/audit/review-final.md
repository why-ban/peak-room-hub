# 3.0.0 对抗性复核（task-11 / review-final）

复核方式：逐条读代码 + 与 `git show HEAD` 对比 + 反编译 `refs/PEAKLib.UI.dll`、`refs/PEAKLib.Core.dll`、`refs/Zorro.UI.Runtime.dll`、`refs/Unity.TextMeshPro.dll` + 游戏 3.0.1 反编译（`D:\zhuanban\youhua\decompiled-latest`）。
构建复核：`dotnet build "src\RoomHub\RoomHub.csproj"` → **0 错误 0 警告**（已实跑）。

## 0. 结论

**不需要回滚。** 数据层/Steam 层/入口层的修复真实落地，公共 API 无删改，行池化与资源所有权正确。
**需要立刻修的是手柄路径的 2 条 high**（都不影响键鼠用户，但会让纯手柄用户卡死或无法返回）。其余为 medium/low，可随后补。

## 1. 确认成立（一行一条）

- **公共 API 无删改**：`HistoryPageController` 8 个 HEAD public 成员全在（另加 9 个 IRowHost 实现，非删改）；`HistoryUi` 11 个、`HistoryStore` 14 个、`RoomJoiner`/`SteamInviteService` 全部一致；`FriendRoomsService` 只增（`Area`/`IsJoinable`/`IsInvitable`/5 参 ctor，4 参 ctor 保留）；`SteamAvatarService` 只增 `Ready()`；`JoinGate` 只把私有嵌套类 `Entry.CooldownUntil` 换成 `CooldownUntilUtcTicks`（非公共面）；`PluginVersion` 2.7.0→3.0.0 符合预期。
- **`HistoryRowFactory` 无残留**：`grep HistoryRowFactory src/` = 0 命中（仅 docs 提及）。
- **行池化**：`Rows.cs:541 Take` 同 key 复用、`Commit:552-580` 只 Dispose 消失的 key；`Dispose:112-121` 先 `SetParent(null)` 再 Destroy，且 `Avatar.Dispose` 只销毁自己 `Sprite.Create` 的 sprite，**不碰 `UiSprites` 共享 sprite**（旧坑未复发）。
- **资源**：`UiSprites` 用 `Textures` 列表 + `Rounded`/`_circle` 全量登记，`Shutdown:38-53` 全部 Destroy；`HistoryUi.OnSceneLoaded→DropPage` 每次场景切换都释放，无跨场景残留。
- **生命周期**：`MenuWindow.Open/Close` 反射可解析（`MenuWindow.cs:80` internal virtual / `:131` internal，`GetMethod(NonPublic)` 唯一匹配）；`Open` 入 `AllActiveWindows`、`Close`/OnDestroy 出，成对；`IsPageOpen` 用 `activeInHierarchy` 与实际状态一致。
- **ESC 抑制帧序正确**：`PageOpenThisFrame` 在 `Plugin.Update`（`HistoryUi.Tick`）置位，`GUIManager.UpdatePaused` 在 **LateUpdate**（`GUIManager.cs:389`）消费 → 关闭当帧必定抑制，下一帧复位。
- **`TickInputBlock` 置空安全**：输入阻断已由 `GUIManager.UpdateWindowStatus`→`windowBlockingInput`→`Character.cs:1713` 接管，光标由 `CursorHandler.cs:13` 接管（两者都读 `AllActiveWindows`）。
- **修复抽查全部真实落地**：D-01（`_loadFailed`+备份+禁写）、D-02（`ApplySoleHostUnlocked` 只在空时赋值 / `UpsertPlayer` 从存储推导 wasHost）、D-04（`UpsertRoom` 不再写 JoinedAt）、D-09（读路径不 prune，减行前 `.bak`）；S1（`SteamGate.Ready()` 覆盖 friends/avatar/invite/join）、S2（token 优先 + Airport 分支 + 仅 MainMenu 可邀请）、S3（`InLiveSession` = InLobby ‖ Photon.InRoom + 弹窗）、S7（NoAvatar 哨兵 + 10s 节流 + Requested 去重）、S8（GetService 包 try/catch）；UI-02（`HandleBackOrClose:254-258` 先失焦）、UI-04（左右分栏按 `mainColumn/rail` 结构不重叠）、UI-05（池化）、UI-06（`ApplyLayout` 扣按钮宽度 + Ellipsis）、UI-07（`SkipStartClosed`）。
- **Loc 键完整**：代码引用的 70 个键全部在 `Loc.Register` 注册，无 "ROOMHISTORY_XXX" 直出。
- **Harmony 目标全部存在**：`GUIManager.UpdatePaused`、`SteamLobbyHandler.OnLobbyEnter`(:107)/`OnLobbyDataUpdate`(:306)、`PlayerConnectionLog.OnPlayerEnteredRoom`(:56)、`NetworkConnector.OnJoinedRoom`(:300)/`OnLeftRoom`(:220)、`NetCode.RoomEvents.PlayerEntered`。

## 2. 缺陷

### R-01 — HIGH — 手柄 B / Start 无法关闭面板（`CloseOnUICancel=false` + 只读键盘 ESC）
- 证据：`HistoryPageController.cs:131-133`、`:340-344` 同时把 `CloseOnUICancel` 和 `CloseOnPause` 置 false；唯一的返回入口是 `HistoryUi.cs:127` `Input.GetKeyDown(KeyCode.Escape)`（旧版 Input = 键盘）。游戏的取消路径被这两行关掉：`MenuWindow.cs:59-74`（`closeOnPause`→`Character.input.pauseWasPressed`；`closeOnUICancel`→`UIInputHandler.cancelWasPressed`）。
- 触发：纯手柄打开面板后按 B（或 Start）→ 无任何反应；`docs/ui-impl-notes.md` R3 / `docs/integration-plan.md` §D「B 返回」不成立。
- 为什么是 bug：手柄的 B 走新 Input System（`UIInputHandler.cs:33-42,56-63`），不会产生 `KeyCode.Escape`；两条游戏内关闭路径都被显式关掉，只剩「把焦点移到 Close 按钮按 A」。
- 最小修法：`HistoryUi.Tick` 在 ESC 分支后补一条 `else if (Singleton<UIInputHandler>.Instance.cancelWasPressed) { Singleton<UIInputHandler>.Instance.cancelWasPressed = false; _page.HandleBackOrClose(); }`（保持 `CloseOnUICancel=false`，这样「返回上一层」的语义还在）；Start 键同法读 `pauseWasPressed`。

### R-02 — HIGH — 刷新销毁「初始选中行」后 `objectToSelectOnOpen` 返回 null，手柄焦点永久丢失
- 证据：`_initial` 只在 `Open`(`HistoryPageController.cs:138`) 和 `FocusInitial`(`:985`) 赋值；`RefreshAll/RefreshMainList` 从不重设。行池销毁时 `Rows.cs:571` `_live[key].Dispose()`；`RoomHubPage.cs:30-32` 对已销毁对象返回 null；`NavigationContainerHandler.cs:47-69` 只在 `GetDefaultSelection()!=null` 时补选。
- 触发（纯手柄）：面板里只有 1 个房间 → 进删除模式 → 对着该行按 A 删除 → 行被销毁、列表变空 → `_initial` 悬空 → 默认选中为 null → 方向键无目标、A 无目标、B 又因 R-01 无效 → **只能找鼠标**。房间多时删掉第一行同样触发。
- 为什么是 bug：`NavigationContainerHandler` 是唯一的自动补选机制，它依赖页面的 `objectToSelectOnOpen`；页面把该值缓存成一次性引用且不随刷新更新。
- 最小修法：`RefreshMainList` 末尾（或 `WireNavigation` 开头）执行 `_page?.SetInitialSelection(FirstInteractive())`；若 `EventSystem.current.currentSelectedGameObject == null` 再 `UIInputHandler.SetSelectedObject(first.gameObject)`。

### R-03 — MEDIUM — 窄窗口下搜索簇与标签重叠 / 行文本列出现负宽度
- 证据：`HistoryPageController.cs:671` `Mathf.Clamp(..., 140f, SearchWidth)` 的下限 140 在可用宽度不足时仍然生效；`:450` 只收缩主列 `offsetMax`，无下限；`Rows.cs:241` 文本列 `offsetMax` 同样无下限。
- 复算（CanvasScaler matchHeight=1 ⇒ 画布高恒 1080，宽 = 1080×宽高比）：搜索视图（Clear 可见）在 **5:4（1.25）** 时 `mainW=724`，簇宽 375，左边缘 349 < 标签右边缘 372 → **Clear 压住 Players 标签约 23 px**；≤1.19 时房间视图也重叠；≤0.92 时行文本列宽度为负（0.9 → −18 px）。
- 为什么是 bug：4:3/16:9/21:9 都正常（4:3 余量 24 px），但 5:4 是可拖出来的窗口形状，且这是规格明确要求「不重叠」的场景。
- 最小修法：当 `mainWidth - tabsWidth - 24 - searchWidth - 10 - clearWidth < 140` 时把搜索簇换行/隐藏 Clear，而不是把输入框夹到 140；文本列 `offsetMax.x = -Mathf.Min(inset, rowWidth - leftInset)`。

### R-04 — LOW — 手工/旧版 history.tsv 的重复 P 行会让同一行被布局两次
- 证据：`HistoryStore.cs:470-483` P 行直接 `room.Players.Add`，无 SteamId 去重；`HistoryPageController.cs:763` 对每个 hit 调 `Take(hit.SteamId)`；`Rows.cs:552-580` `Commit` 不去重 `ordered`；`UiScrollView.cs:52-69` 按列表逐项定位。
- 触发：房间玩家列表里有两条同 SteamId 的 P 行 → 同一 RowView 进 `ordered` 两次 → 只显示一行但内容高度多算一行（列表底部空洞/滚动范围偏大）。
- 最小修法：P 行解析时按 SteamId 去重（保留 last 最大者）；`Commit` 对 `ordered` 做一次 key 去重。

### R-05 — LOW — 头像圆角实际不生效（Mask 按矩形裁剪，不按 sprite alpha）
- 证据：`UiAvatar.cs:35-36` 用 `Mask` + `showMaskGraphic=true` 盖在圆角底图上，`:77` 的贴图 sprite 是整张方形纹理。UGUI 的 Mask 写模板时对所有片元生效（默认 UI shader 不做 alpha clip），因此 80×80 的方形头像会盖住圆角底的四个角。
- 影响：`docs/ui-impl-notes.md` R7「头像四角被裁成圆角」不会成立；纯视觉。
- 最小修法：头像 Image 上方再叠一张圆角描边/遮罩图（用 `UiSprites.RoundedSprite` 的反相遮罩），或换成 alpha-clip 的自定义材质。

### R-06 — LOW — Title 兜底路径不置 `MenuWindow.isOpen`，不进 `AllActiveWindows`
- 证据：`HistoryPageController.cs:142-150` 走 `SkipStartClosed()+Show()`，从不调用 `Open()`；`MenuWindow.Open` 才写 `isOpen`/`AllActiveWindows`（`MenuWindow.cs:80-99`）。后果：`MenuWindow.Update` 不 `PushActive`（无手柄容器、无自动补选），`GUIManager.UpdateWindowStatus` 看不到该窗口（光标/输入阻断不生效）。
- 触发：仅当 `MenuWindow.Open` 反射失败（本版本不会，属未来回归面）。
- 最小修法：兜底路径改为「记录失败 → `Discard()` 并 LogError」，或反射写 `isOpen` 并把实例塞进 `AllActiveWindows`。

### R-07 — LOW — `CloseMenuWindow` 的兜底 `Hide()` 会把窗口留在 `AllActiveWindows`
- 证据：`HistoryPageController.cs:1147-1162`；`Close()` 抛异常时只 `Hide()`，`isOpen` 仍 true、仍留在 `AllActiveWindows` → `GUIManager.UpdateWindowStatus` 持续 `windowBlockingInput/windowShowingCursor=true`（`GUIManager.cs:394-418`）→ 玩家永久不能动。
- 最小修法：兜底里 `MenuWindow.AllActiveWindows.Remove((MenuWindow)_page)` 并 `SetInputActive(false)`，再 `Hide()`。

### R-08 — LOW — `HistoryUi.Open` 异常路径在页面还活着时销毁共享 sprite
- 证据：`HistoryUi.cs:75-79` catch → `DropPage()` → `UiSprites.Shutdown()`；页面 GameObject 的 Destroy 是延迟的，当帧内仍引用已销毁 sprite → 一帧白块。
- 最小修法：`DropPage` 里把 `UiSprites.Shutdown()` 延后到下一帧（或仅当 `!_page.HasLivePage` 时调用）。

## 3. 未覆盖 / 只能运行期验证

- Canvas 排序与游戏 Modal 的实际叠放（`ApplySorting` 逻辑已读，数值需进游戏看日志 `Room Hub canvas order`）。
- CJK 字体回退是否真生效（`FontFallbackSwapper.instance` 在 Title 场景是否存在）。
- `steam_display` 是否原样 token（S5）。
- ScrollRect 手工定位 + EnsureVisible 的滚动手感（`UiScrollView.EnsureVisible` 越界依赖 `MovementType.Clamped` 回夹）。
- `Plugin/PauseMenuHook/TitleMenuController` 只做了 API/补丁目标/夹取数学核对，未逐行复核 UI 观感。

## 4. 一句话给 Lead

**不用回滚**；发布前若在意纯手柄体验，先修 R-01 + R-02（各 3-6 行），否则 5:4 搜索视图的 23 px 重叠（R-03）会是最容易被截图抓到的瑕疵。
