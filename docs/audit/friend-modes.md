# 好友状态 → 模式 → 文案键（PEAK 3.0.1 富状态）

给 UI 代理（HistoryRowFactory / HistoryPageController 重建）的契约表。数据来源：
`docs/audit/audit-steam.md` §2 真值表 + `Assembly-CSharp/SteamRichPresence.cs:10-34` 写入端。
本文件是 WP-STEAM（task-8）唯一新增的文档；代码改动只在
`FriendRoomsService.cs` / `SteamInviteService.cs` / `SteamAvatarService.cs` / `LobbyJoinFeedback.cs` / `RoomJoiner.cs`。

---

## 1. API 契约（已冻结的成员未变，以下全部为新增）

| 成员 | 说明 |
|---|---|
| `FriendGameMode.Airport` | 新增枚举值，位置在 `MainMenu` 与 `Unknown` 之间 |
| `FriendArea` | 新枚举：`None, MainMenu, Airport, Shore, Tropics, Alpine, Mesa, Caldera, Kiln, Peak, Roots, Gloom, Citadel, Nadir` |
| `FriendRoomInfo.Area` | 新只读字段，13 个富状态的可选展示（与 `Mode` 正交） |
| `FriendRoomInfo(ulong, string, ulong, FriendGameMode, FriendArea)` | 新构造重载；原 4 参构造保留并置 `Area = None` |
| `FriendRoomInfo.IsJoinable` | `Mode == Joinable && LobbyId != 0` |
| `FriendRoomInfo.IsInvitable` | `Mode == MainMenu`（机场/生物群系/未知一律 false） |
| `FriendRoomsService.Tick()` | internal，由内部 `FriendNotifyPump` 每帧调用，UI 无需关心 |
| `SteamGate.Ready()` | internal，Steam 是否可用；UI 层不需要直接调用 |

`FriendRoomInfo.SteamId / Name / LobbyId / Mode` 的字段名、类型、含义全部未变。

---

## 2. 判别链（`FriendRoomsService.Classify`）

1. 不是 PEAK（`GetFriendGamePlayed` 的 AppID ≠ 3527290）→ 不列出。
2. `keyCount == 0` 且 persona 为 `Offline` → 视为残留 game info，跳过（S12）。
3. `FriendGameInfo_t.m_steamIDLobby` 有效 → **Joinable**（Steam 自己会给"加入游戏"按钮），`Area` 仍按 token 填。
4. `keyCount == 0` → **Unknown**。
5. token 可解析（`steam_display` 形如 `#Status_*`）→ **以 token 为准**：
   - `#Status_MainMenu` → **MainMenu**
   - `#Status_Airport` → **Airport**
   - 11 个生物群系 → 有 `steam_player_group` 则 **InviteOnly**，否则 `max_players == "1"` → **Solo**，其余 **InviteOnly**（关卡场景必然在房里）
6. token 缺失/不可解析 → 回退旧链：`group` 非空 → InviteOnly；`max_players=="1"` → Solo；`players=="1"` → MainMenu；`max_players` 非空 → InviteOnly；否则 MainMenu。

token 优先的依据：`SteamRichPresence.SetState` 写的是 `"#" + state`（枚举名），`GetFriendRichPresence` 返回**存储值**而非渲染值。
旧注释"steam_display 会本地化"已删除（审计 S5）。为坐实这一点加了两条 **Info** 级日志（每会话各一次，见 §6）。

---

## 3. 13 个 RichPresenceState 的确定结论

`steam_display` 与 `RichPresenceState` 一一对应；`Area` 恒等于该状态。`Mode` 由"可加入 lobby / 房间 / 离线"决定，
因此每个状态都落到下面三类之一，不存在"说不清"。

| # | 状态（token 后缀） | Photon/Steam 情形 | players | max | group | Mode | Area | 文案键 | 邀请按钮 | 加入按钮 |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `MainMenu` | 任意 | 1 | 4 | 无 | MainMenu | MainMenu | `FRIEND_MAIN_MENU` | 是（且自己在 lobby） | 否 |
| 2 | `Airport` | 可加入 lobby 暴露 | N | MAX | 房间名 | Joinable | Airport | `FRIEND_IN_GAME` | 否 | 是 |
| 3 | `Airport` | 私房（lobby 不暴露） | N | MAX | 房间名 | Airport | Airport | `FRIEND_AIRPORT` | 否 | 否 |
| 4 | `Airport` | Photon 离线（单人） | 1 | 1 | 无 | Airport | Airport | `FRIEND_AIRPORT` | 否 | 否 |
| 5 | `Airport` | 无 Photon 房也无 lobby（审计行 4） | 1 | 4 | 无 | Airport | Airport | `FRIEND_AIRPORT` | 否 | 否 |
| 6-16 | `Shore/Tropics/Alpine/Mesa/Caldera/Kiln/Peak/Roots/Gloom/Citadel/Nadir` | 可加入 lobby | N | MAX | 房间名 | Joinable | 对应群系 | `FRIEND_IN_GAME` | 否 | 是 |
| 6-16 | 同上 | 私房 | N | MAX | 房间名 | InviteOnly | 对应群系 | `FRIEND_INVITE_ONLY` | 否 | 否 |
| 6-16 | 同上 | Photon 离线单人 | 1 | 1 | 无 | Solo | 对应群系 | `FRIEND_SOLO` | 否 | 否 |
| 17 | （无 token） | `keyCount == 0` | — | — | — | Unknown | None | `FRIEND_UNKNOWN` | 否 | 否 |

表内 `FRIEND_*` 为 `Loc` 现有常量的简写：
`Loc.FriendInGame / FriendInviteOnly / FriendSolo / FriendMainMenu / FriendUnknown`。

**机场的关键约束**：`Mode == Airport` 时**不得显示邀请按钮**。原因是游戏在 `PhotonNetwork.InRoom` 时直接丢弃邀请
（`SteamLobbyHandler.cs:335-339`，只打一行 `Debug.LogError`，玩家侧完全静默），而机场里的好友几乎总在房里。
判据统一走 `friend.IsInvitable`，不要自己比较 `Mode`。

**生物群系的可选展示**：`Area` 只用于"在哪"这类附加信息（例如第二行 `单人游戏中 · 海岸`）。
`Area` 为 `None` 时不显示任何后缀；不要因为 `Area != None` 改变按钮逻辑。

---

## 4. 需要 `Loc.cs` 新增的键（WP-STEAM 不能改 Loc.cs，请 UI 代理加）

| 键 | English | 简体 | 繁體 | 必需 |
|---|---|---|---|---|
| `ROOMHISTORY_FRIEND_AIRPORT` | `At the airport` | `在机场` | `在機場` | 是（机场模式文案） |
| `ROOMHISTORY_JOIN_BLOCKED` | `Leave your current room first.` | `请先离开当前房间。` | `請先離開目前房間。` | 是（`RoomJoiner` 已按此键名调用，缺失时弹窗会显示原始键名） |

可选（生物群系后缀，缺键时 `Loc.T` 返回键名，所以**只有在加键后**才展示后缀）：

| 键 | English | 简体 | 繁體 |
|---|---|---|---|
| `ROOMHISTORY_AREA_SHORE` | `Shore` | `海岸` | `海岸` |
| `ROOMHISTORY_AREA_TROPICS` | `Tropics` | `热带` | `熱帶` |
| `ROOMHISTORY_AREA_ALPINE` | `Alpine` | `高山` | `高山` |
| `ROOMHISTORY_AREA_MESA` | `Mesa` | `台地` | `台地` |
| `ROOMHISTORY_AREA_CALDERA` | `Caldera` | `火山口` | `火山口` |
| `ROOMHISTORY_AREA_KILN` | `Kiln` | `窑洞` | `窯洞` |
| `ROOMHISTORY_AREA_PEAK` | `Peak` | `山顶` | `山頂` |
| `ROOMHISTORY_AREA_ROOTS` | `Roots` | `根须` | `根鬚` |
| `ROOMHISTORY_AREA_GLOOM` | `Gloom` | `幽暗` | `幽暗` |
| `ROOMHISTORY_AREA_CITADEL` | `Citadel` | `堡垒` | `堡壘` |
| `ROOMHISTORY_AREA_NADIR` | `Nadir` | `深渊` | `深淵` |
| `ROOMHISTORY_AREA_AIRPORT` | `Airport` | `机场` | `機場` |

---

## 5. 按钮规则（与现有实现一致，仅改用新属性）

```
canJoin   = friend.IsJoinable  && (HistoryUi.IsTitleScene || !HistoryUi.IsInLiveLobby)
canInvite = friend.IsInvitable && HistoryUi.IsInLiveLobby
```

`RoomJoiner.TryJoinLobbyId` 现在自己也会拦一次：不在标题场景且
`Peak.Network.NetCode.Matchmaking.InLobby || PhotonNetwork.InRoom` 时**弹窗提示**而不是静默返回（审计 S3）。
所以 UI 侧沿用上面两条规则即可，重复拦截只会多一次提示。

---

## 6. 运行时可观测点（settle 审计 §6 的 UNPROVEN 项）

| 日志（Info 级，每会话最多各一条） | 判定 |
|---|---|
| `Friend rich presence token is raw: display=[#Status_...]` | token 原样到达 → §3 的 token 分支成立 |
| `Friend rich presence token unusable, falling back to keys: display=[...] group=[...] players=[...] max=[...] keys=N` | 拿到的不是 `#Status_*`（本地化或空）→ 走旧链，同时打印原始键值供比对 |

两条都只在**首次**出现时打印，不会刷屏。若只有第一条出现，说明 §3 表格是实际生效路径。

---

## 7. 审计条目落点

| id | 落点 |
|---|---|
| S1 | `SteamGate.Ready()`（`SteamAPI.GetHSteamPipe().m_HSteamPipe != 0`）：`RefreshIfDue` / `GetAvatar` / `InvitePlayer` / `IsFriend` / `OpenAddFriend` / `LocalSteamIdString` / `TryJoinLobbyId` 全部前置。不引用 `SteamManagerAssembly.dll`，也不新增运行时资源加载 |
| S2 | `Classify` token 优先 + `FriendGameMode.Airport` + `FriendArea` |
| S3 | `RoomJoiner.InLiveSession()` + 可见反馈 |
| S4 | `MarkDirty` 只置脏并重读，`_signature`（steamId/lobby/mode/area/name）不变则不重建、不通知 |
| S5 | 删除"会本地化"注释，改 token 优先；两条 Info 日志坐实 |
| S6 | `_notifyPending` + `FriendNotifyPump`（挂在 Plugin 的常驻 GameObject 上）到期补发 |
| S7 | `GetAvatar` 三态：`0` 负缓存并停、`<0` 每用户 ≥10s 重试、`>0` 解码缓存；`RequestUserInformation` 每用户仅一次 |
| S8 | `LobbyHandler()` 用 try/catch 包 `GameHandler.GetService`；异常文本不再进弹窗 |
| S9 | `RoomJoiner.ShowModal` 先 `if (Modal.IsOpen) Modal.CloseModal();`（与 `SteamLobbyHandler.cs:282-286` 同法） |
| S11 | `_invited` 键为 `lobbyId:canonicalSteamId`，解析前 Trim |
| S12 | persona `Offline` 且 `keyCount == 0` 跳过 |
| S13 | `_nextRefresh` 仅在成功构建后推进；`_lastRpRequest` 在 Shutdown 清空、每次刷新按在场好友淘汰 |
| S14 | **未改**：前提（`m_isHosting` 残留）UNPROVEN，且需要在 join 后做定时回查；S3 的拦截已挡住"游戏会静默吞掉"的那类 join，其余失败仍由 `LobbyJoinFeedback` 覆盖 |
| S15 | **无需改**：新代码用全限定 `Peak.Network.NetCode`，没有引入 `using Peak.Network;`，`SteamMatchmaking` 绑定不受影响 |
