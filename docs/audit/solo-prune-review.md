# SOLO-PRUNE 复核 — 丢弃「自己开房且从未有他人进入」的历史记录

复核对象：`HistoryStore.PruneSoloHostedRoom(s)` + 四个触发点（`PlayerTracker` / `Plugin`）。
基线：`src/RoomHub` 当前工作树，v3.0.1；游戏反编译 `D:\zhuanban\youhua\decompiled-latest`。
构建：`dotnet build "src\RoomHub\RoomHub.csproj"` → **0 错误 0 警告**（`build/out/RoomHub.dll`）。

## 结论

判定前提**成立**：全仓只有 2 个 `UpsertPlayer` 调用点，两条都在入口处排除了自己（证据见 §1），所以 `room.Players` 确实只装「除自己以外」的人。
但方案有 **2 个真实的误删路径**（都不是理论问题：都能在代码里指到具体分支），另有 3 处漏删和 2 处实现瑕疵。核心设计缺陷是：**删除决策建立在「没有证据」之上，却没有验证取证管道是否在运行**。

---

## 1. 前提核对：`Players` 是否真的不含自己

全仓 `UpsertPlayer` 调用点共 2 处，逐一核对：

| 调用点 | 排除 self 的证据 | 结论 |
|---|---|---|
| `LobbyRecorder.cs:106`（`SnapshotMembers`） | `:92` `if (member == CSteamID.Nil \|\| member == self) continue;`，`self` 取自 `:86` `SteamUser.GetSteamID()` | ✅ 不写自己 |
| `PlayerTracker.cs:296`（`RecordPhotonPlayer`） | 双重保险：`:259` `player == null \|\| player.IsLocal` → return；`:276-278` `self == steamId` → return | ✅ 不写自己 |

`RecordPhotonPlayer` 的 3 个调用者全部经由上述同一函数，没有旁路：
- `OnRoomPlayerEntered`（`PlayerTracker.cs:124-140`，`RoomEvents.PlayerEntered` 挂钩）
- `OnPlayerEnteredRoomPatch`（`:305-314`，另有 `:310` 的 `IsLocal` 前置判断）
- `OnJoinedRoomPatch`（`:330-335`，遍历 `PlayerList`）

**前提成立。** 补充两点边界，都不影响结论：
- `LocalSteamIdString()` 在 Steam 异常时返回 null（`SteamAvatarService.cs:156-160`），`:276-278` 的自比较会失效——但 `:259` 的 `IsLocal` 仍然拦得住本地玩家，所以「写入自己」依然不可达。
- `SnapshotMembers` 里的 `self` 直接取 `SteamUser.GetSteamID()`，若抛异常则被 `:109-112` 的 try/catch 整体吞掉，结果是这次快照全丢（宁可少记，不会多记）。

### 1.1 实现代理那条「前提修正」的核对结果：成立，且比它说的更强

`SnapshotMembers` 确实只在 `OnLobbyEnter` 的 postfix 里跑一次（`LobbyRecorder.cs:44`，全仓唯一调用点），所以开房后进来的人不会经 Steam 快照入表。但 Photon 侧的事件是**可靠**的，证据链完整：

- `NetCode` 静态构造里 `RoomEvents = new PhotonRoomEvents()`（`Peak/Network/NetCode.cs:32`）。
- `PhotonRoomEvents : PhotonCallbackTarget, IInRoomCallbacks`（`PhotonRoomEvents.cs:7`），基类构造函数直接 `PhotonNetwork.AddCallbackTarget(this)`（`PhotonCallbackTarget.cs:12`）——不依赖场景、不依赖 MonoBehaviour。
- PUN 在收到非本地的 Join 事件时直接派发：`LoadBalancingClient.cs:1955-1957` `InRoomCallbackTargets.OnPlayerEnteredRoom(player)`（本地玩家走 `:1953` 的 `OnJoinedRoom`，不会进这个分支）。
- 模组在 `SetActiveLobby` → `EnsureCallbacks` → `EnsureRoomEvents`（`PlayerTracker.cs:115`）里挂上 `PlayerEntered`，时机是**进大厅时**，早于任何客人。

所以「真进了 Photon 的人会被记录」这条成立，而且 `RoomEvents` 比 `PlayerConnectionLog` 那条 Harmony 路径更可靠（后者只在 HUD 对象 enabled 时才有回调，见上一轮审计 D-08）。

---

## 2. 误删风险（有效房间被当成垃圾删掉）

### S-01 — MEDIUM — 关掉模组后进来的人不会被记录，退出时房间仍会被删
`PlayerTracker.cs:257`（记录有 `Enabled` 闸门）vs `PlayerTracker.cs:51-69`（`PruneActiveLobbyIfSolo` **没有** `Enabled` 闸门）；触发点 `:352`、`:32`、`Plugin.cs:128`。

**场景**：开房（此时启用，行已写入）→ 在 PEAKLib 设置面板里关掉 Enable Mod → 朋友进 Photon 房间 → `RecordPhotonPlayer` 在 `:257` 直接 return，客人没入表 → 重新打开或直接离开 → `OnLeftRoom` postfix 调 `PruneActiveLobbyIfSolo`（无闸门）→ `Players.Count == 0` → 删掉一条**真的有客人**的记录。

**为什么是真缺陷**：取证（记录）受开关控制，判罚（删除）不受，两者不对称。用户只是关了个开关，不该导致历史被删。

**最小修法**：`PruneActiveLobbyIfSolo` 开头加 `if (!Plugin.ModConfig.Enabled.Value) return;`（顺带解决「关掉模组还在偷偷改历史」的观感问题）。

### S-02 — MEDIUM — 取证管道只初始化一次且失败不重试，失败后所有自建房都会被删
`PlayerTracker.cs:86-101`：`if (_callbacksInited) return;` 在 `:89-90`，而 `EnsureRoomEvents()` 在 `:100`——**位于早退之后**。

**场景**：首次 `EnsureRoomEvents` 抛异常（被 `:118-121` 吞掉，只记 Debug 日志），`_roomEventsHooked` 停在 false，但 `_callbacksInited` 已是 true → 之后每次 `SetActiveLobby` 都在 `:89` 早退，`PlayerEntered` **永远不会再挂上**。此时如果 `PlayerConnectionLog` 又不在当前场景（上一轮审计 D-08 的已知脆弱点），客人全程不被记录 → 每个自建房看起来都是单人房 → 退出即删。

**为什么是真缺陷**：一次性的静默失败被永久固化，而下游是**破坏性**操作。另外 `Plugin.ApplyPatches`（`Plugin.cs:57-82`）现在改成「单类失败只记 error 继续」，这是对的，但也意味着 `OnLeftRoom` 这类 patch 可能悄悄缺席——同样是「管道没通，判罚照跑」。

**最小修法**：(a) 把 `EnsureRoomEvents()` 移到 `_callbacksInited` 早退之前；(b) 增加一个 `RecordingArmed` 信号（`_roomEventsHooked || 已见到过 PlayerConnectionLog 回调`），`PruneActiveLobbyIfSolo` 在未 armed 时拒绝删除，只记一条 Info。

### S-03 — LOW — 启动清扫与「即将进入的房间」竞态
`Plugin.cs:136-160`，守卫是 `NetCode.Matchmaking?.InLobby == true → 跳过`（`:145`）。

**场景**：带 `+connect_lobby` 启动时，`Plugin.Update` 首帧跑 `TryStartupSweep`，此刻 Steam 还没进大厅 → `InLobby` 为 false → 清扫执行，删掉玩家**正要重新进入**的那条自建房记录 → 随后 `UpsertRoom`（`LobbyRecorder.cs:42`）重建，`JoinedAt = now`。

**结论**：不丢人、不丢房，只丢原始时间戳（列表排序与「joined X ago」被重置），并且顺带重新武装了 2.5h Join 门。**判定为可接受**，但值得知道。

### 已排查但**不成立**的误删路径（记录在此，避免重复讨论）
- **客人先到、Steam 大厅还没建好** → `RecordPhotonPlayer:265-270` 会 `EnsureActiveLobbyFromSteam()` 兜底；房主视角下 `_activeLobbyId` 在 `LobbyRecorder.cs:43` 进大厅时就已设好，早于任何客人。✅
- **切房时先删旧房、而旧房其实没结束** → `SetActiveLobby:32` 删的是**旧** id（`:36` 才覆盖），语义正确：你已经离开旧房了。若旧房有客人，`Players.Count > 0` 直接豁免。✅
- **客人离场后再删房** → 规格是「从未有过他人」，`Players.Count > 0` 永久豁免。✅
- **自己当客人**（`HostSteamId != self`）→ `IsSoloHostedRoom:409` 不匹配。✅
- **`OnDestroy` / `OnApplicationQuit` 双触发** → 真幂等：第二次 `RemoveAll` 匹配不到，返回 0，不写盘；`Store.Flush()` 因 `!_dirty` 早退（`HistoryStore.cs:496-497`）。✅

---

## 3. 漏删风险（单人房没被清掉）

- **`HostSteamId` 为空**：V2 遗留行（`HistoryStore.cs:561-567` 不写 HostSteamId）永远不匹配 → 永久残留。`IsSoloHostedRoom:408` 的 `!string.IsNullOrEmpty` 是**故意**的（未知房主不能当自己），方向正确，代价是老数据不清。
- **`HostSteamId == "0"`**：`LobbyRecorder.cs:37` 在 `GetLobbyOwner` 返回 Nil 时写入 `"0"`，同样永不匹配 → 残留。概率低。
- **退出时 Steam 已关闭**：`PruneActiveLobbyIfSolo:57-59` 拿不到 local id 就直接 return，退出清理静默失效；只能靠下次启动的清扫，而清扫又要求「Steam 就绪 + 不在大厅」。若玩家每次启动都自动进房（邀请链接），清扫会一直推迟到第一次离开大厅。
- **崩溃**：无退出钩子，靠启动清扫兜底——这是设计意图，成立。
- **`_loadFailed`**（历史文件读不出来）：`_rooms` 为空，不删也不写（`HistoryStore.cs:639-643`），方向正确。✅

---

## 4. 实现质量

- **锁**：`PruneSoloHostedRoom`(`:357-365`)、`PruneSoloHostedRooms`(`:386-395`) 都在 `_lock` 内调 `SaveUnlocked()`；`SaveUnlocked` 本身不加锁（`:637`），**没有二次加锁**。所有公开入口都在锁内写盘，无遗漏。
- **写盘路径**：走 `SaveUnlocked`，享受 30s 失败退避（`:21`、`:649-650`、`:705`）与「减行先备份」（`:660-661`）。失败时 `_dirty` 被重新置 true（`:706`），后续 `TickDeferredSave` 会补写——**最终一致，不会卡死**。
- **S-04 — LOW — 日志撒谎**：`PruneSoloHostedRoom:363-369` 与 `PruneSoloHostedRooms:393-400` 忽略 `SaveUnlocked()` 的返回值，即使写盘失败也打「Pruned …」。文件被占用时日志会误导排查。修法：`if (SaveUnlocked())` 再打 Info，否则 Warning。
- **S-05 — LOW — 每帧分配**：`Plugin.Update:164-165` 每帧调 `TryStartupSweep`，其第一行 `LocalSteamIdString()` 每次分配一个 17 字符 string（`SteamAvatarService.cs:158`）。玩家若整局都在大厅里，清扫永远不完成 → **整局每帧一次字符串分配** + 每帧 `GetService` 字典查找。修法：`self` 缓存到静态字段，或把清扫改成 1s 节流。
- **可观测性**：三条 Info（含 lobby 列表）+ 一条 Warning 异常，足够定位；`S-02` 的 hook 失败只记 Debug，建议升到 Warning（它是删除决策的前提）。
- **行为变更提醒**（非缺陷）：自建房历史被清后，`J` 快速加入（`RoomJoiner.TryJoinMostRecent`）不再能找回自己上次开的房。这符合本次需求，但属于用户可感知的变化。

---

## 5. 建议的最小改动清单

1. `PruneActiveLobbyIfSolo` 加 `Enabled` 闸门（S-01）。
2. `EnsureRoomEvents()` 移到 `_callbacksInited` 早退之前；hook 失败记 Warning（S-02）。
3. 增加 `RecordingArmed` 并在未 armed 时拒绝删除（S-02 的根治）。
4. 两处 prune 检查 `SaveUnlocked()` 返回值再决定日志级别（S-04）。
5. `TryStartupSweep` 缓存 local SteamID / 加节流（S-05）。

除上述 5 项外，`Load()` 与 TSV 格式**不需要**任何改动；S-03 判定为可接受，无需修改。
