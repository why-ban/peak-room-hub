# 3.0.0 整合计划（Lead 自用）

## A. 并行代理分工（进行中）

| 代理 | 文件范围 | 任务 |
|---|---|---|
| wp-data | HistoryStore / Models / LobbyRecorder / PlayerTracker / JoinGate / PluginConfig | audit-data D-01..D-14 |
| wp-steam | FriendRoomsService / SteamInviteService / SteamAvatarService / LobbyJoinFeedback / RoomJoiner | audit-steam S1..S15 |
| wp-entry | Plugin / PauseMenuHook / TitleMenuController / RibbonDragHandle | PatchAll 加固、单人 timeScale、过场拦截、丝带夹取 |
| ui-impl | Ui/** + HistoryPageController / HistoryUi / HistoryUiFont / HistoryRowFactory / UiButtonFactory / Loc | 面板跨代重建（docs/ui-spec.md） |

## B. 整合阶段由 Lead 亲自完成（代理范围之外）

按 `docs/audit/game-api.md` §4「应采纳的新能力」逐项落地：

1. **4.1 `NetCode.Matchmaking` 取代 `GameHandler.GetService<SteamLobbyHandler>()`**
   `NetCode.Matchmaking` 在 NoSteam 下是 `NoMatchmaking`，永不抛异常（`NetCode.cs:21-25`、`NoMatchmaking.cs:7-60`）。
   替换点：`HistoryUi.IsInLiveLobby`、`SteamInviteService`、`RoomJoiner`、`PlayerTracker`。
   注意命名冲突：`Peak.Network.SteamMatchmaking` 会遮蔽 `Steamworks.SteamMatchmaking`，**不要**加 `using Peak.Network;`，用全限定名。
2. **4.3 `NetCode.RoomEvents` 取代对 `PlayerConnectionLog` 的补丁**
   `PhotonRoomEvents` 由 `NetCode` 静态构造注册，不依赖场景对象；`PlayerConnectionLog` 是场景 MonoBehaviour，Title/机场场景可能不存在。
   事件无参，需要在回调里 diff `PhotonNetwork.PlayerList`。若 wp-data 未做，整合时补。
3. **4.4 给页面挂 `PreventUnpause` 组件**
   `PreventUnpause.UnpausePreventionActive` 阻止 `PauseMenuHandler.Update` 关掉暂停菜单；但**不能**阻止 `GUIManager.UpdatePaused` 打开暂停菜单，
   所以 `PauseInputSuppressor` 前缀补丁必须保留。
4. **4.6 `RunId` 大厅键**
   `RunManager.RunIdLobbyDataKey = "RunId"`，房主每次新跑图重写。历史里同一 lobby id 可对应多次跑图，用 RunId 去重比时间戳更准。
   读取：`NetCode.Matchmaking.TryGetLobbyData(lobbyId, "RunId", out v)`。
5. **4.8 用 public 字段取代硬编码路径**
   `GUIManager.instance.pauseMenuMainPage`、`MainMenu.playSoloButton`、`MainMenuMainPage.PlayButton`、`MainMenuPageSelector.mainPage`。
6. **4.10 `MenuWindow.isOpen` 取代 `activeInHierarchy` 推断**
   `isOpen` 是 public 属性；`CloseAllWindows()` 是游戏切场景前自己用的。
7. **画布排序自适应**（已发规格给 ui-impl）：不得写死 500，按场景内游戏 UI 画布取 `order - 1`，且永不高于游戏 Modal。

## C. 版本与发布（最后一步，需用户已确认：3.0.0）

1. `Plugin.PluginVersion = "3.0.0"`（顶部历史注释块已由 wp-entry 删除）。
2. `dist/thunderstore/manifest.json`：`version_number` → 3.0.0，`name` 保持 `Room_Hub`。
3. `README.md` 更新功能与截图说明。
4. `dotnet build` 0/0 → 复制 DLL 到 `dist/thunderstore/RoomHub.dll` 与游戏 `BepInEx/plugins`（游戏运行时会锁文件，需先确认未运行）。
5. 打包 `dist/RoomHub-3.0.0.zip`（根目录 4 文件：icon.png / README.md / manifest.json / RoomHub.dll），`tar -tf` 验证。
6. 提交前确认 `git status` 不含 `build/decompiled-peaklib`、`build/pkgs`、`build/webcache`、`tools/uimock/node_modules`。

## D. 验收清单

- [ ] 构建 0 错误 0 警告
- [ ] 四种视图 × 16:9 / 16:10 / 4:3 / 21:9 不重叠
- [ ] 手柄可完整操作（打开即有选中项、方向键移动、A 触发、B 返回）
- [ ] 搜索框聚焦后 ESC 仍能关面板
- [ ] 单人模式下打开面板：世界暂停、关闭后恢复
- [ ] 长名字（32 字符）不遮挡按钮
- [ ] 好友刷新不跳滚动、不重建未变化行
- [ ] 加入失败弹窗可见（不被面板遮挡）
- [ ] 好友生物群系显示正确
- [ ] 历史文件被占用时不丢数据（改名前备份）
- [ ] Steam 未初始化时不崩溃

## E. 已知需运行期验证（无法离线证明）

1. `steam_display` 返回原始 token（`#Status_MainMenu`）还是本地化文本 —— 新判别链已做成"token 优先 + 启发式兜底"，两种情况都成立。
2. `FontFallbackSwapper.instance` 在 Title 场景是否已存在（不存在则退回 `TMP_Settings.defaultFontAsset`）。
3. 画布排序参照对象是否可枚举到。
4. `PlayerConnectionLog` 在机场/局内场景是否存在（改用 RoomEvents 后不再是问题）。
5. 手柄焦点链在真实手柄下的表现。
6. `JoinGate` 2.5 小时年龄上限是否误伤超长对局。
