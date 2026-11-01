# 复审：移除「启用模组」总开关 + 默认房间数 20（3.1.1）

复核方式：逐行读当前工作区代码；反编译 `refs/BepInEx.dll` 的 `ConfigFile` 确认孤儿键行为；核对 `dist/` 实际产物（哈希/zip 内容）；自跑构建。
构建：`dotnet build "src\RoomHub\RoomHub.csproj"` → **0 错误 0 警告**；`build/out`、`build/plugins`、`dist/thunderstore` 三份 `RoomHub.dll` **SHA256 完全一致**（145,408 B），`dist/RoomHub-3.1.1.zip` 内也是同一份 + 同一份 README（5151 B）。

## 0. 结论

改动本身**干净**：无漏删、无误删、无行为回归；唯一需要改的是**发布用的 README 里还写着 10 个房间**（而且这份 README 就在 3.1.1 的 zip 里）。

## 1. 逐条验证

| 检查 | 证据 | 判定 |
|---|---|---|
| 无 `Enabled` 残留 | 全仓 `grep Enabled` 只剩无关的 Unity `Behaviour.enabled`（`TitleMenuController.cs:103`、`UiAvatar.cs:41/80/85`、`UiScrollView.cs:36/39`）与 `HistoryStore._storageDisabled`（磁盘不可写，与总开关无关）；`ROOMHISTORY_DISABLED` / `Loc.Disabled` 0 命中 | ✅ |
| 无死代码 | `RoomJoiner.ShowFeedback` 仍被 `LobbyJoinFeedback.cs:68` 与 `SteamInviteService.cs:41-78` 使用；`ModConfig` 仍被 8 处使用（`Plugin.cs:184/196/197/244`、`TitleMenuController.cs:74/160/178-182`、`HistoryPageController.cs:749`、`HistoryStore.cs:609/616`、`RibbonDragHandle.cs:64`） | ✅ |
| 未误删热键分支 | `Plugin.cs:184` `if (!ModConfig.EnableHotkey.Value) return;` 保留；`:188` `IsTextInputFocused`、`:199-206` 标题场景 J/K、`:208` `IsFullscreenUiActive`、`:211-212` 局内 K 全部保留 | ✅ |
| 未误删主界面按钮分支 | `Plugin.cs:241-250`：`scene.name != "Title"` 与 `ShowMainMenuButton` 两道闸都在；`TitleMenuController.cs:74` 的 `ShowMainMenuButton` 闸保留，且 `:68` `UiButtonFactory.CaptureTemplate` 在闸之前执行（关掉丝带仍能抓到按钮模板） | ✅ |
| `HistoryUi.Open` 异常路径 | `HistoryUi.cs:57-71`：`try { ClosePauseMenuIfOpen; EnsureController; _page.Open(); } catch { LogError; DropPage(); }` 完整；禁用分支删掉后没有留下"半开"状态（`DropPage` 会 `Discard` + 释放 sprite） | ✅ |
| `TryJoinMostRecent` 语义 | `RoomJoiner.cs:30-41`：现在只有"没有最近房间"一个分支，`showEmptyFeedback` 仍只控制是否弹 `NoRecent`；唯一调用点 `Plugin.cs:202` 传 true。原来"禁用时弹 Disabled"的语义随开关一起消失，没有残留调用者 | ✅ |
| `TryStartupSweep` 防护 | `Plugin.cs:137-171`：1 秒节流（`:140-142`）→ Steam id 非空（`:144-146`）→ `EnsureRoomEvents()` + `RecordingArmed`（`:149-151`）→ 不在大厅（`:154-159`）→ `Store.PruneSoloHostedRooms(self)`；删除条件在 `HistoryStore.cs:405-411`（host==self 且 `Players.Count==0`） | ✅ 充分（残留见 R-4） |
| `PlayerTracker` 7 处删守卫 | 无一处依赖该守卫做"省工作量"：`TickHostPoll`（`PlayerTracker.cs:184-192`）先看 `_activeLobbyId` 为空即 return，`SyncHostFromSteam` 再经 `TryGetLobbyId` 的 `Matchmaking.InLobby` 才碰 Steam（`:245-262`）；`OnLobbyChatUpdate`（`:162-181`）同样先比 `_activeLobbyId`；`RecordPhotonPlayer`（`:264-306`）先排除 null/本地/空 id。未进房时不会多出 Steam 调用 | ✅ |
| 默认 20 | `PluginConfig.cs:74-80`（默认 20，`AcceptableValueRange(1,50)`）；`HistoryStore.cs:616` `Math.Max(1, _config.MaxRecentRooms.Value)` + `:624` `Take(max)`；全仓无第二处硬编码 10 | ✅ |
| 20 行的 UI | 内容高 = 20×96 + 19×10 = **2110**，视口 535（840−96−209）→ 可滚；`EnsureVisible` 已夹在 `[0, contentHeight−viewportHeight]`（`UiScrollView.cs:92-96`）；行池按 key 复用，20 行 = 20 个 RowView 常驻，不随刷新增长 | ✅ |
| 旧 cfg 兼容 | BepInEx `ConfigFile`：未 `Bind` 的键进 `OrphanedEntries`（`ConfigFile.cs:21,171`），`Save()` 原样写回（`:200`），只有重新 `Bind` 才会消费它（`:276-279`）。所以 `Enable Mod = false` 被**忽略、不生效、不抛异常**，但也不会从文件里消失 | ✅（副作用见 R-2） |
| 版本 | `Plugin.cs:19` 3.1.1、`README.md:5` 3.1.1、`dist/thunderstore/manifest.json` 3.1.1 | ✅ |

## 2. 缺陷

### R-1 — MEDIUM — 发布用 README 仍写「10 个房间」，且已在 3.1.1 的 zip 里
- 证据：`dist/thunderstore/README.md:27` `- Up to **10 recent rooms** (configurable), pruned by age (default 7 days).` 与同文件 `:81` `| Max Recent Rooms | 20 | ...` 以及 `PluginConfig.cs:77` 的默认 20 自相矛盾。`dist/RoomHub-3.1.1.zip` 内的 README 就是这份（5151 B，与磁盘文件一致）。
- 影响：Thunderstore 页面正文（用户最先看的功能列表）写 10，配置表写 20。
- 最小修法：`:27` 改成 `Up to **20 recent rooms**`，重新打 zip。

### R-2 — LOW — 老 cfg 里的 `Enable Mod = false` 会永久留在文件里
BepInEx 的 `OrphanedEntries` 是 private（`ConfigFile.cs:21`），模组无法主动清除；文件里会一直留着这一行"看起来还生效"的开关。不报错、不生效（已验证）。可在 README/更新日志里写一句"旧 cfg 中的 Enable Mod 行已失效，可手动删除"。

### R-3 — LOW — 默认 20 只对全新安装生效
老用户 cfg 里已经有 `Max Recent Rooms = 10`（旧默认被 BepInEx 写进过文件），升级后仍是 10。若希望老用户也变 20，需要在更新日志里提示手动改，或做一次性迁移（不建议）。

### R-4 — LOW — 启动清扫会删掉"开关关闭期间录制"的行（一次性）
`IsSoloHostedRoom`（`HistoryStore.cs:405-411`）只要求 host==self 且 `Players.Count==0`；`RecordingArmed` 只能证明**本会话**取证管道已挂上，不能证明历史行录制时挂上过。所以曾经关着开关开房、有客人但没入表的行，会在升级后第一次启动被清扫。影响有限：这些行本来就没有任何玩家记录（删掉只丢房间行本身，不丢好友线索），且 `HistoryStore.cs:397-400` 会打出被删的 lobby id。方向符合文档"宁可漏删，不可误删"的取舍，这里只是把残留风险记一笔。

### R-5 — LOW — 设计文档两处自相矛盾/过时
- `docs/solo-room-pruning.md:142` 写"启动时 Steam 未就绪：`Update` 每帧重试"，而代码是 1 秒节流（`Plugin.cs:140-142`），同文件 `:160` 又写"按 1 秒节流重试"。
- `:149` 标题写"3.1.0 发布版"，正文 `:153` 讲的是 3.1.1 的改动。

## 3. 对六个问题的直接回答

1. **漏删**：无。代码里 `Enabled`/`ROOMHISTORY_DISABLED`/`Loc.Disabled` 零命中，无死方法、无孤儿注释（只剩 `docs/audit/solo-prune-review.md:50` 的历史 bug 复述，属于审计存档，不算孤儿声明）。
2. **误删**：无。热键分支、主界面按钮分支、模板抓取、`IsFullscreenUiActive`、`IsTextInputFocused`、`InLiveSession`、`SteamGate` 全部保留。
3. **行为变化**：安全。Open 异常路径完整；`showEmptyFeedback` 语义仍成立（只控制"没有最近房间"的提示）；清扫防护是"1s 节流 + Steam id + RecordingArmed + 不在大厅 + host==self + 0 玩家"六重；PlayerTracker 无一处靠该守卫省 Steam 调用。
4. **默认 20**：`Take(max)`、`(1,50)`、行池/滚动都正常，无第二处硬编码 10；老用户保持自己的 10（R-3）。
5. **旧配置**：被忽略、模组恒启用、不抛异常；该行会永久留在 cfg 里（R-2）。
6. **构建**：自跑 0 错误 0 警告，三份 DLL 同哈希，zip 内容与磁盘一致。

## 4. 未覆盖

- 真实游戏内 20 行的滚动观感与首次打开时 20×2 个按钮克隆的耗时（代码层面无异常）。
- PEAKLib ModConfig 设置面板里旧 `Enable Mod` 行是否还会显示（取决于 PEAKLib 如何枚举 cfg，离线不可判）。
