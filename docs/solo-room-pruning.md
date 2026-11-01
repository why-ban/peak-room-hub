# 单人房历史清理（solo-hosted room pruning）

3.0.1 起"自己开的房一律记录"，于是"单人开房、全程没人进来"的房间也会写进历史。
这种记录里只有房主自己（玩家列表为空），没有任何可操作的信息，属于垃圾数据。
本文记录判定依据、四个触发点、边界结论与实机验证清单。

## 1. 判断依据：`RoomRecord.Players` 里没有自己

`Players` 只写"除自己以外"的人，三条写入路径全部跳过本地玩家：

| 路径 | 位置 | 跳过条件 |
| --- | --- | --- |
| Steam 大厅成员快照 | `src/RoomHub/LobbyRecorder.cs:92` | `member == CSteamID.Nil \|\| member == self` |
| Photon 玩家回调 | `src/RoomHub/PlayerTracker.cs:259` | `player == null \|\| player.IsLocal` |
| 同上，再按 SteamID 兜底 | `src/RoomHub/PlayerTracker.cs:276-278` | `self == steamId` |
| `PlayerConnectionLog` 补丁 | `src/RoomHub/PlayerTracker.cs:310` | `newPlayer.IsLocal` |

复核方式：全仓 `UpsertPlayer` 只有两个调用点（`src/RoomHub/LobbyRecorder.cs:106`、
`src/RoomHub/PlayerTracker.cs:296`），两者都在上表内。因此 **`Players` 永远不含本地玩家**，
"房主 = 自己且 `Players.Count == 0`"等价于"这个房间从头到尾只有我一个人"。

`Players` 只增不减（唯一删除是 20 人上限截断，`src/RoomHub/HistoryStore.cs:280-286`），
所以中途有人进来过就一定留下痕迹。

## 2. 判定条件

```
!string.IsNullOrEmpty(room.HostSteamId)
&& room.HostSteamId == localSteamId
&& room.Players.Count == 0
```

实现在 `HistoryStore.IsSoloHostedRoom`（`src/RoomHub/HistoryStore.cs:405`）。

- **不能只看 `Players.Count == 0`**：作为客人加入别人的房间时玩家列表同样可能为空，那是有效记录。
- **HostSteamId 为空一律不删**（保守）：进大厅时房主可能还没解析出来，V2 老格式行也没有 host 字段，
  空值代表"不知道是谁"，不能当成"我"。

## 3. 四个触发点

| 时机 | 位置 | 说明 |
| --- | --- | --- |
| 离开房间 | `src/RoomHub/PlayerTracker.cs:352`（`OnLeftRoomPatch`） | 在 `ClearActiveLobby()` 之前结算，否则 lobby id 已丢 |
| 切换到另一个房间 | `src/RoomHub/PlayerTracker.cs:32`（`SetActiveLobby` 检测到 id 变化） | 覆盖"没走 OnLeftRoom 直接进下一个大厅"的路径 |
| 退出游戏 | `src/RoomHub/Plugin.cs:126` `FlushHistoryOnExit`，由 `OnApplicationQuit`（`:119`）与 `OnDestroy`（`:104`）各调一次 | 退出时 Photon 不保证抛 `OnLeftRoom`；删除本身幂等，重复调用不会再删 |
| 启动清扫 | `src/RoomHub/Plugin.cs:136` `TryStartupSweep`，由 `Update`（`:164-165`）触发一次 | 覆盖崩溃导致没机会清理的情况 |

统一的结算入口是 `PlayerTracker.PruneActiveLobbyIfSolo`（`src/RoomHub/PlayerTracker.cs:51`）：
读当前 lobby id → 读 `SteamAvatarService.LocalSteamIdString()` → 调 `HistoryStore.PruneSoloHostedRoom`。
本地 Steam id 取不到就直接返回（不拿空值去比较）。

### 落盘

`PruneSoloHostedRoom`（`src/RoomHub/HistoryStore.cs:347`）与 `PruneSoloHostedRooms`（`:378`）
都走现有 `_lock` + `SaveUnlocked()` 路径，和 `DeleteRoom`（`:326`）一致：删完立刻落盘，
不等 `TickDeferredSave` 的 0.75 s 合并窗口。删除失败时 `SaveUnlocked` 自己会记 Error 并安排重试，
内存里的行已经去掉，下次启动清扫会再收一次。

### 日志

Info 级，含 lobby id 与原因：

```
Pruned solo-hosted room: lobby=109775241234567890, host=76561198..., reason=no other player was ever recorded
Pruned 2 solo-hosted room(s): lobbies=109775241234567890, 109775241234567891, host=76561198..., reason=no other player was ever recorded
```

### 没有动的地方

- `HistoryStore.Load()`（`src/RoomHub/HistoryStore.cs:435`）不做清扫：那时 Steam 可能还没初始化，
  拿不到本地 id，硬扫会用一个空值去比较。
- `LobbyRecorder.cs`、`Ui/**`、`HistoryPageController.cs`、`FriendRoomsService.cs` 未改动。

## 4. 边界情况

### 4.1 邀请的人进了 Steam 大厅，但没进 Photon 房间

**结论：会被删。这条和最初的假设不同，需要如实记录。**

`SnapshotMembers` 只在**我们自己进入大厅**那一刻跑一次（`src/RoomHub/LobbyRecorder.cs:44`）。
所以：

- 客人视角（我们进别人的大厅）：此时大厅里的人会被记进 `Players`，`Players.Count >= 1` → 保留。
- 房主视角（我们开房，之后有人接受邀请进来）：`SnapshotMembers` 早已跑完，
  后来进 Steam 大厅的人**不会**被记录 —— `OnLobbyChatUpdate` 只刷新房主
  （`src/RoomHub/PlayerTracker.cs:147` → `SyncHostFromSteam`），不写玩家行。
  于是 `Players` 仍是 0，离开时这条房会被当成单人房删掉。

判断：**可以接受**。对方停在 Steam 大厅说明还没进 Photon 房间，此时这条房间记录里没有任何玩家行，
既不能"添加好友"也不能"看谁来过"，留着没有用途；而对方真正进房间的那一刻，
`NetCode.RoomEvents.PlayerEntered` 与 `OnJoinedRoom` 快照会立刻写入玩家行（`src/RoomHub/PlayerTracker.cs:253`、`:317`），
房间从此不会再被删。
若要保留"只到大厅"的房间，必须让 `OnLobbyChatUpdate` 也写玩家行 —— 那要改 `LobbyRecorder`/记录逻辑，
超出本次范围，也没有实际收益。

### 4.2 房主先单人玩很久，中途有人进来

**结论：保留，`JoinedAt` 仍是较早的时间，符合现有语义。**

`JoinedAt` 在进 Steam 大厅时写入（`UpsertRoom`，`src/RoomHub/HistoryStore.cs:139`），
之后只有 `TouchRoomJoinedNow`（`:193`）会在 Photon `OnJoinedRoom` 时把它往后推，
"有人进来"不是它的触发条件。房间行代表的是**一次会话**，会话开始于开房那一刻，
所以按会话开始时间排序是自洽的；真正的时间线细节在玩家行的 `LastSeenUtcTicks` 里。
不改：动 `JoinedAt` 会连带影响"最近房间"排序和快速重连的目标。

### 4.3 崩溃后重启清扫会不会误删"当前正在玩的房间"

**结论：不会。**

1. 插件由 BepInEx 在**进程启动**时加载，清扫在 `Update` 里跑，此时玩家在 Title 场景，
   既不在 Photon 房间也不在 Steam 大厅 —— "当前正在玩的房间"这个概念还不存在。
2. 双保险：`TryStartupSweep` 里 `NetCode.Matchmaking?.InLobby == true` 时直接返回 `false`，
   本次不扫、下帧再看。所以即使插件是被热重载进一个进行中的会话（或启动参数直接拉进大厅），
   也不会在房间还活着的时候判它。
3. 只有 `LocalSteamIdString()` 非空才扫；取不到就下一帧重试，绝不拿空 id 去比较。

### 4.4 客人视角：`HostSteamId != self` → 永不删

**成立，代码里可见**：`IsSoloHostedRoom`（`src/RoomHub/HistoryStore.cs:405`）要求
`string.Equals(room.HostSteamId, localSteamId, StringComparison.Ordinal)`；
而客人房间的 `HostSteamId` 是房主（`src/RoomHub/LobbyRecorder.cs:35-37` 冻结首个记录的 owner），
不可能等于本地 id。四个触发点全部走同一个判定，没有旁路。

### 4.5 其它

- **房主单人 → 有人进来 → 那人离开 → 房主再退出**：保留（`Players` 只增不减）。
- **`HostSteamId` 为空**（进大厅时 owner 为 `CSteamID.Nil`，或 V2 老格式行）：永不删。
- **自己开的房被转移了所有权**：`HostSteamId` 仍冻结为创建者（自己）。若全程没人进来，仍然是垃圾记录，删除正确。
- **插件中途卸载（热重载）**：`OnDestroy` 会结算一次，可能删掉当时那条单人房；之后真有人进来时，
  下一次进大厅会重新建行。可接受。

## 5. 必须进游戏验证

- [ ] **单人开房 → 直接退出到标题**：历史里不出现这条房；日志有一行 `Pruned solo-hosted room: lobby=...`。
- [ ] **单人开房 → 直接关游戏**：重启后历史里没有这条房（退出时删掉，或启动清扫删掉，两者留一行日志即可）。
- [ ] **单人开房 → 关游戏前 kill 进程（崩溃）→ 重启**：启动后日志出现 `Pruned 1 solo-hosted room(s): lobbies=...`，历史里没有这条房。
- [ ] **单人开房 → 有人进来 → 离开 → 自己退出**：这条房**必须还在**，玩家列表里有那个人。
- [ ] **单人开房 → 有人进来 → 那人离开 → 自己退出**：这条房仍然在（`Players` 只增不减）。
- [ ] **加入别人的房（客人）→ 离开**：这条房必须还在，且日志**没有** `Pruned` 行。
- [ ] **单人开房 → 不退出，直接接受邀请切到别人的房**：原来那条单人房从历史消失（切换触发点生效）。
- [ ] **同一大厅反复进出（rejoin）**：不会因为 `SetActiveLobby` 重复调用而误删。
- [ ] **启动时 Steam 未就绪**（Steam 客户端慢启动）：`Update` 每帧重试，Steam 就绪后清扫一次，日志只出现一次。
- [ ] **历史文件被占用/只读**：删除后 `SaveUnlocked` 失败会记 Error，重启后该行仍在并被再次清扫（不崩溃、不丢别的行）。

## 6. 构建

`dotnet build "src\RoomHub\RoomHub.csproj"` → 0 错误 0 警告。

## 复审后的最终形态（3.1.x 发布版）

复审（`docs/audit/solo-prune-review.md`）发现两条真实误删路径，均已修复：

1. **记录受 `Enabled` 管、删除不受**：已通过移除总开关根治（3.1.1 起 `Enable Mod` 配置项删除，模组恒为启用）。
   现在取证与判罚永远对称，不再存在"关掉模组后取证停止、判罚照跑"的状态。
2. **取证管道未就绪仍会删**：`EnsureRoomEvents()` 移到 `EnsureCallbacks` 的早退之前，失败可重试；
   新增 `PlayerTracker.RecordingArmed`，两个删除入口都要求它为 true。
   即「没有证据」不再被当作「没有玩家」。

配套：
- 启动清扫失败后按 1 秒节流重试，挂钩异常只记一次 Warning（避免每帧异常刷屏；`Plugin.TryStartupSweep` 的 `_nextSweepAttempt`）。
- `EnsureRoomEvents` 的重复调用是稳态静态 bool 读，不存在重复订阅。

**已知取舍（有意为之）**：取证管道永久挂不上时，垃圾房不会被清理，会占用 `MaxRecentRooms` 的名额。
方向是「宁可漏删，不可误删」——静默删掉一条真有客人的记录是不可接受的，列表退化是可接受的。