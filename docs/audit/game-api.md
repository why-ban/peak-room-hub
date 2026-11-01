# 游戏 API 契约审计（PEAK 3.0.1）

对象：`src/RoomHub/*.cs`（22 个源文件，4400 行）对 `Assembly-CSharp` / `com.rlabrecque.steamworks.net` / `Zorro.UI.Runtime` 的每一个外部依赖。
证据基线：`D:\zhuanban\youhua\decompiled-latest`（当前版本权威反编译）。旧 `decompiled\` 目录未参与任何结论。
PEAKLib 侧只用于验证"模组调用是否成立"（本地 `refs/PEAKLib.UI.dll` 反编译，已用完即删）；PEAKLib 自身的版本/破坏性变更归 task-2。

标记约定：
- ✅ 有效（契约未变，可继续用）
- ⚠️ 有效但有副作用/需要额外防护
- ❌ 已失效或语义已错
- 🔒 非 public（必须 Harmony/反射）
- ❓ 无法静态验证，需要进游戏实测

---

## 1. 模组 → 游戏符号清单

### 1.1 Steamworks.NET（`com.rlabrecque.steamworks.net/Steamworks/`）

| 符号 | 模组使用处 | 游戏定义 | 有效 | 备注 |
|---|---|---|---|---|
| `Callback<LobbyEnter_t>.Create` | LobbyJoinFeedback.cs:29 | Callback.cs:30 | ✅ | 模组注册早于游戏（Plugin.Awake 先于 GameHandler.Awake），故模组回调**先**触发，此时 `LobbySteamId` 仍是旧值 |
| `Callback<LobbyDataUpdate_t>.Create` | LobbyJoinFeedback.cs:30 | Callback.cs:30 | ✅ | 与游戏 SteamLobbyHandler.cs:55 并行注册，互不影响 |
| `Callback<LobbyChatUpdate_t>.Create` | PlayerTracker.cs:54 | Callback.cs:30 | ✅ | 游戏自己不用这个回调，无冲突 |
| `Callback<AvatarImageLoaded_t>.Create` | SteamAvatarService.cs:23 | Callback.cs:30 | ✅ | |
| `Callback<PersonaStateChange_t>.Create` | FriendRoomsService.cs:81 | Callback.cs:30 | ✅ | |
| `Callback<FriendRichPresenceUpdate_t>.Create` | FriendRoomsService.cs:84 | Callback.cs:30 | ✅ | |
| `Callback<T>.Dispose` | LobbyJoinFeedback.cs:41-44、PlayerTracker.cs:42、SteamAvatarService.cs:34、FriendRoomsService.cs:95-98 | Callback.cs:51-62 | ✅ | `Callback<T> : IDisposable`（Callback.cs:16），另有终结器兜底 |
| `LobbyEnter_t.m_EChatRoomEnterResponse` (uint) | LobbyRecorder.cs:19、LobbyJoinFeedback.cs:50 | LobbyEnter_t.cs:18 | ✅ | 游戏比较 `!= 1`（SteamLobbyHandler.cs:114），与 `EChatRoomEnterResponse.cs:5` 一致 |
| `LobbyEnter_t.m_ulSteamIDLobby` | LobbyRecorder.cs:23-24 | LobbyEnter_t.cs:11 | ✅ | |
| `LobbyDataUpdate_t.m_bSuccess` (byte) | LobbyJoinFeedback.cs:73 | LobbyDataUpdate_t.cs:15 | ✅ | 模组注释"0=failure,1=success"与游戏 `!= 1` 判断一致（SteamLobbyHandler.cs:308） |
| `LobbyDataUpdate_t.m_ulSteamIDLobby` | LobbyJoinFeedback.cs:75,81 | LobbyDataUpdate_t.cs:11 | ✅ | |
| `LobbyChatUpdate_t.m_ulSteamIDLobby` | PlayerTracker.cs:76 | LobbyChatUpdate_t.cs:11 | ✅ | |
| `AvatarImageLoaded_t.m_steamID/.m_iImage` | SteamAvatarService.cs:92-93 | AvatarImageLoaded_t.cs:11,13 | ✅ | |
| `PersonaStateChange_t` | FriendRoomsService.cs:272 | PersonaStateChange_t.cs:7 | ✅ | 模组只用"发生了"这一事实，不读字段 |
| `FriendRichPresenceUpdate_t` | FriendRoomsService.cs:290 | FriendRichPresenceUpdate_t.cs:7 | ✅ | 同上 |
| `FriendGameInfo_t.m_gameID.AppID().m_AppId` | FriendRoomsService.cs:132 | FriendGameInfo_t.cs:8 | ✅ | `PeakAppId=3527290`（FriendRoomsService.cs:56）与 SteamManager.cs:68 `new AppId_t(3527290u)` 一致 |
| `FriendGameInfo_t.m_steamIDLobby` | FriendRoomsService.cs:168-170 | FriendGameInfo_t.cs:16 | ✅ | 只有"对我们可加入"的大厅才非 Nil，这是"仅邀请"判定的唯一硬信号 |
| `EChatRoomEnterResponse` 各值 | LobbyJoinFeedback.cs:91-98 | EChatRoomEnterResponse.cs:5-16 | ✅ | |
| `EFriendFlags.k_EFriendFlagImmediate` | FriendRoomsService.cs:120,123、SteamAvatarService.cs:138 | EFriendFlags.cs | ✅ | |
| `CSteamID(ulong)/.m_SteamID/.Nil/.IsValid()` | 19 处（HistoryUi.cs:45 … SteamInviteService.cs:46） | CSteamID.cs:34,20,10,203 | ✅ | |
| `SteamMatchmaking.GetLobbyOwner` | LobbyRecorder.cs:28、PlayerTracker.cs:111,136 | SteamMatchmaking.cs:236 | ✅ | |
| `SteamMatchmaking.GetNumLobbyMembers` | LobbyRecorder.cs:91 | SteamMatchmaking.cs:114 | ✅ | |
| `SteamMatchmaking.GetLobbyMemberByIndex` | LobbyRecorder.cs:97 | SteamMatchmaking.cs:120 | ✅ | |
| `SteamMatchmaking.GetLobbyData` | LobbyRecorder.cs:60,71 | SteamMatchmaking.cs:126 | ⚠️ | 返回 `null` 时模组的 `?? ""` 正确；但 `host_Name`/`lobby_Name` 两个键在 3.0.1 **根本没人写**，见 §3.1 |
| `SteamMatchmaking.InviteUserToLobby` | SteamInviteService.cs:48 | SteamMatchmaking.cs:108 | ✅ | 游戏自己用的是 `ActivateGameOverlayInviteDialog`（AirportInviteFriendsKiosk.cs:43、PauseMenuMainPage.cs:140），两条路都通 |
| `SteamUser.GetSteamID` | LobbyRecorder.cs:27,92、SteamAvatarService.cs:158 | SteamUser.cs:20 | ✅ | |
| `SteamFriends.GetFriendPersonaName` | LobbyRecorder.cs:63,102、PlayerTracker.cs:183、FriendRoomsService.cs:254 | SteamFriends.cs:51 | ✅ | |
| `SteamFriends.GetFriendCount/GetFriendByIndex` | FriendRoomsService.cs:120,123 | SteamFriends.cs:27,33 | ✅ | |
| `SteamFriends.GetFriendGamePlayed` | FriendRoomsService.cs:130 | SteamFriends.cs:57 | ✅ | 读本地缓存，轮询代价低 |
| `SteamFriends.RequestFriendRichPresence` | FriendRoomsService.cs:160 | SteamFriends.cs:303 | ✅ | 无返回值，只能等 `FriendRichPresenceUpdate_t` |
| `SteamFriends.GetFriendRichPresenceKeyCount` | FriendRoomsService.cs:186 | SteamFriends.cs:291 | ✅ | |
| `SteamFriends.GetFriendRichPresence` | FriendRoomsService.cs:218-221 | SteamFriends.cs:284 | ⚠️ | 键名 `steam_display`/`steam_player_group`/`players`/`max_players` 与 SteamRichPresence.cs:27-33 对得上；但 `steam_display` 的**取值形态**未证，见 §5.1 |
| `SteamFriends.HasFriend` | SteamAvatarService.cs:138 | SteamFriends.cs:111 | ✅ | |
| `SteamFriends.ActivateGameOverlayToUser` | SteamAvatarService.cs:148 | SteamFriends.cs:184 | ✅ | `"friendadd"` 是 Steam 合法 dialog 名 |
| `SteamFriends.RequestUserInformation` | SteamAvatarService.cs:63 | SteamFriends.cs:234 | ✅ | `false`=同时拉头像，与注释一致 |
| `SteamFriends.GetMediumFriendAvatar` | SteamAvatarService.cs:65 | SteamFriends.cs:222 | ✅ | `GetLargeFriendAvatar`（:228）同样可用，模组未用 |
| `SteamUtils.GetImageSize/GetImageRGBA` | SteamAvatarService.cs:109,113 | SteamUtils.cs:38,44 | ✅ | `LoadRawTextureData` 要求长度恰为 w*h*4，模组按 `RGBA32` 分配，成立 |

### 1.2 大厅 / 网络服务（`Assembly-CSharp`）

| 符号 | 模组使用处 | 游戏定义 | 有效 | 备注 |
|---|---|---|---|---|
| `SteamLobbyHandler` 类型 | 11 处（LobbyRecorder.cs:10,13 … SteamInviteService.cs:45） | SteamLobbyHandler.cs:15 | ✅ | `GameService` 子类，仅 `!CurrentPlayer.Tags.Contains("NoSteam")` 时注册（GameHandler.cs:168-173） |
| `SteamLobbyHandler.OnLobbyEnter` | LobbyRecorder.cs:10（字符串 Harmony 名） | SteamLobbyHandler.cs:107 | 🔒 | `private void OnLobbyEnter(LobbyEnter_t)`。HarmonyX 默认搜索非 public，当前成立；**字符串名一旦改名即在 PatchAll 抛异常**，无编译期保护 |
| `SteamLobbyHandler.OnLobbyDataUpdate` | PlayerTracker.cs:256 | SteamLobbyHandler.cs:306 | 🔒 | `private void OnLobbyDataUpdate(LobbyDataUpdate_t)`，同上 |
| `SteamLobbyHandler.TryJoinLobby(CSteamID)` | RoomJoiner.cs:84 | SteamLobbyHandler.cs:374-385 | ⚠️ | **不是同步加入**：只发 `RequestLobbyData`，真正 `JoinLobby` 在 `OnLobbyDataUpdate` 里（:327-341）。失败路径会弹模态框（:381-384） |
| `SteamLobbyHandler.InSteamLobby(out CSteamID)` | HistoryUi.cs:45、SteamInviteService.cs:46、PlayerTracker.cs:108,133 | SteamLobbyHandler.cs:478-482 | ✅ | `LobbySteamId != Nil`，纯本地字段 |
| `SteamLobbyHandler.LobbySteamId` | 未直接用（用 `CurrentLobbyID` 更好，:48） | SteamLobbyHandler.cs:44 | ✅ | `{ get; private set; }` |
| `SteamLobbyHandler.RequestLobbyData` | 无（2.6.1 已移除，JoinGate.cs:18-22 有说明） | SteamLobbyHandler.cs:293 | 🔒 | `internal`。**模组目前无法调用，移除决策正确** |
| `SteamLobbyHandler.TryJoinLobbyDuringSession` | 无 | SteamLobbyHandler.cs:364-372 | ⚠️ | public，但对"重进历史房间"是陷阱，见 §3.4 |
| `SteamLobbyHandler.SetLobbyData()` | 无 | SteamLobbyHandler.cs:422-455 | ✅ | 写 `PhotonRegion`+`CurrentScene`；不写 `PeakVersion`（只在 OnLobbyCreated:409 写） |
| `SteamLobbyHandler.LeaveLobby()` | 无 | SteamLobbyHandler.cs:457-471 | ✅ | 幂等（Nil 时只打日志） |
| 大厅数据键 `PeakVersion` | 间接（触发版本校验） | 写 SteamLobbyHandler.cs:409；读 :332 | ⚠️ | 值是 `BuildVersion.ToMatchmaking()` = **"主.次"**（BuildVersion.cs:73-76），3.0.1 写的是 `"3.0"`；跨大版本才不匹配 |
| 大厅数据键 `PhotonRegion`/`CurrentScene` | 无 | SteamLobbyHandler.cs:438,447 / :127,128 | ✅ | 模组可直接读来做 UI |
| 大厅数据键 `RunId` | 无 | RunManager.cs:13,219-224 | ✅ | 由 `NetCode.Matchmaking.TrySetLobbyData` 写入，见 §4.6 |
| 大厅数据键 `host_Name`/`lobby_Name` | LobbyRecorder.cs:60,71 | **全仓库 0 命中** | ❌ | 见 §3.1 |
| `GameHandler.GetService<T>()` | HistoryUi.cs:44、SteamInviteService.cs:45、PlayerTracker.cs:107,132、RoomJoiner.cs:77 | GameHandler.cs:224-227 | ⚠️ | `Instance?.m_gameServices[typeof(T)] as T`：`Instance==null` 返回 null，**但键不存在时抛 KeyNotFoundException**，不是返回 null |
| `GameHandler.Instance` | 未用 | GameHandler.cs:30 | ✅ | |
| `GameHandler.Initialized` | 未用 | GameHandler.cs:91-101 | ✅ | 模组应改用它做就绪门禁，见 §4.1 |
| `GameHandler.WaitForInitialization()` | 未用 | GameHandler.cs:229-235 | ✅ | `async Awaitable` |
| `NetworkConnector.OnJoinedRoom` | PlayerTracker.cs:214 | NetworkConnector.cs:300-315 | ✅ | `public override`。注意它内部会调 `TryJoinLobbyDuringSession`（:310） |
| `NetworkConnector.OnLeftRoom` | PlayerTracker.cs:246 | NetworkConnector.cs:220-225 | ✅ | `public override`，函数体里 `SceneManager.LoadScene(rejoinScene)` |
| `NetworkConnector.IsInitialized/ReadyToHostOrJoinRoom` | 未用 | NetworkConnector.cs:44-56 | ✅ | |
| `PlayerConnectionLog.OnPlayerEnteredRoom` | PlayerTracker.cs:203,206 | PlayerConnectionLog.cs:56-83 | ⚠️ | `public override`，但 **PlayerConnectionLog 是场景 MonoBehaviour**（有 `public TextMeshProUGUI text` 序列化字段），Title 场景不一定存在 → 补丁可能永不触发 |
| `NetworkingUtilities.Sanitize` | 未用（但影响模组数据） | NetworkingUtilities.cs:33-36 | ✅ | 去 `<[^>]*>`；PlayerConnectionLog.cs:62-67 在这里改写了 `newPlayer.NickName`（并截断 32 字符） |
| `NetworkingUtilities.MAX_PLAYERS` | 未用（但决定 `max_players`） | NetworkingUtilities.cs:25 | ✅ | 默认 4，控制台可改（:43-47）；`HostRoomOptions` 用它（:79-87） |
| `NetworkingUtilities.HostRoomOptions` | 未用 | NetworkingUtilities.cs:79-87 | ✅ | **`IsVisible=false`**：PEAK 的 Photon 房间永远不公开，模组走 Steam 大厅是唯一正确路线 |

### 1.3 Rich Presence

| 符号 | 模组使用处 | 游戏定义 | 有效 | 备注 |
|---|---|---|---|---|
| `RichPresenceState`（13 值） | 无 | RichPresenceState.cs:1-16 | ✅ | 顺序：MainMenu, Airport, Shore, Tropics, Alpine, Mesa, Caldera, Kiln, Peak, Roots, Gloom, Citadel, Nadir |
| `SteamRichPresence.SetState` | 间接（读键） | SteamRichPresence.cs:10-34 | ⚠️ | 见 §3.3：`steam_player_group` 只在 InRoom 分支写（:27），只有 MainMenu 分支 `ClearRichPresence()`（:17） |
| `RichPresenceService` | 未用 | RichPresenceService.cs:6-45 | ✅ | `GameService`，GameHandler.cs:162 注册 |
| `RichPresenceService.SetState/Dirty` | 未用 | RichPresenceService.cs:37-45 | ✅ | `Dirty()` 重发当前状态（InRoomState.cs:21 用它刷新人数） |
| `IRichPresence` / `NoRichPresence` | 未用 | IRichPresence.cs:1-6 / NoRichPresence.cs:1-8 | ✅ | 都是 `internal`；`NoSteam` 时状态恒为 MainMenu（RichPresenceService.cs:16-25） |
| 状态写入点 | — | MainMenuMainPage.cs:57 / AirportCheckInKiosk.cs:46 / MountainProgressHandler.cs:45,159-177 | ✅ | 13 个状态的唯一来源；biome 由 `ProgressPoint.title` 字符串映射（:162-176） |

### 1.4 Photon / 玩家

| 符号 | 模组使用处 | 游戏定义 | 有效 | 备注 |
|---|---|---|---|---|
| `PhotonNetwork.PlayerList` | PlayerTracker.cs:229,232 | PhotonNetwork.cs:480 | ✅ | 快照里的昵称**未经** `Sanitize` |
| `Photon.Realtime.Player.IsLocal` | PlayerTracker.cs:157,208 | PhotonRealtime/Photon/Realtime/Player.cs:10 | ✅ | `public readonly bool` |
| `.NickName` | PlayerTracker.cs:178 | 同文件 :22-39 | ✅ | setter 会写 `SetPlayerNameProperty()`（仅本地玩家） |
| `.UserId` | PlayerTracker.cs:170 | 同文件 :41 | ✅ | `{ get; internal set; }`，Steam 场景下等于 SteamID64 |
| `.ActorNumber/.IsMasterClient/.CustomProperties` | 未用 | 同文件 :18,43,57 | ✅ | |
| `PhotonNetwork.InRoom/OfflineMode/CurrentRoom/MasterClient` | 未用（游戏内部用） | PhotonNetwork.cs:697,509,440,681 | ✅ | |
| `Photon.Realtime.Room.MaxPlayers` | 未用 | PhotonRealtime/Photon/Realtime/Room.cs:70 | ✅ | = `MAX_PLAYERS`，即 `max_players` 富状态的来源 |
| `PlayerHandler` | 未用 | PlayerHandler.cs:12,59,61 | ✅ | `Instance => GameHandler.GetService<PlayerHandler>()`（:61） |

### 1.5 UI / MenuWindow / Zorro.UI

| 符号 | 模组使用处 | 游戏定义 | 有效 | 备注 |
|---|---|---|---|---|
| `MenuWindow.AllActiveWindows` | 未直接用（靠 Open 副作用） | MenuWindow.cs:9 | ✅ | `public static List<MenuWindow>` |
| `MenuWindow.Open()` | HistoryPageController.cs:55-56,210-224（反射） | MenuWindow.cs:80-99 | 🔒 | `internal virtual`。只有它会 `AllActiveWindows.Add`（:83-86），`Show()` 不会 |
| `MenuWindow.Close()` | HistoryPageController.cs:57-58,226-241（反射） | MenuWindow.cs:131-144 | 🔒 | `internal`，**非 virtual**；`ForceClose()`（:146）同样 internal |
| `MenuWindow.Show()/Hide()` | HistoryPageController.cs:112,183,235,239 | MenuWindow.cs:155,160 | ✅ | `panel.SetActive`，不维护 isOpen |
| `blocksPlayerInput/showCursorWhileOpen` | HistoryPageController.cs:170-171,406-408 | MenuWindow.cs:21,23 | ✅ | 由 `GUIManager.UpdateWindowStatus` 消费（GUIManager.cs:398-408） |
| `closeOnPause/closeOnUICancel` | HistoryPageController.cs:172-173,406 | MenuWindow.cs:17,19 | ✅ | 消费点 MenuWindow.cs:59-73（需要 `inputActive`） |
| `selectOnOpen` | HistoryPageController.cs:409 | MenuWindow.cs:13 | ✅ | 关掉可避免手柄自动选中（:170-173 只在手柄下生效） |
| `MenuWindow.Start()` | 间接 | MenuWindow.cs:35-48 | ⚠️ | `!isOpen` 时若 `openOnStart==false` 会 `StartClosed()` 把 GO 关掉（:124-129），见 §3.2 |
| `MenuWindow.AllActiveWindows` 注销 | 间接 | MenuWindow.cs:105-111 | ✅ | `OnDestroy` 自动摘除，模组 Destroy 页面即可 |
| `MenuWindow.CloseAllWindows()` | 未用 | MenuWindow.cs:113-122 | ✅ | 与 AirportCheckInKiosk.cs:154 同一用法 |
| `GUIManager.UpdatePaused()` | HistoryUi.cs:184（Prefix） | GUIManager.cs:420-436 | ✅ | `public void`，可 Prefix 返回 bool 跳过 |
| `GUIManager.UpdatePaused` 调用点 | 间接 | GUIManager.cs:389 | ⚠️ | 在 `if ((bool)Character.localCharacter)` 内（:361）→ Title 场景根本不会调用 |
| `GUIManager.UpdateWindowStatus()` | 未用 | GUIManager.cs:394-418 | ✅ | `public` |
| `GUIManager.InPauseMenu` | 未用 | GUIManager.cs:304 | ✅ | `instance?.pauseMenu.activeSelf`，`pauseMenu` 是 private 序列化字段（:73-74） |
| `GUIManager.instance` | 未用 | GUIManager.cs:20 | ✅ | public static 字段 |
| `GUIManager.pauseMenuMainPage` | 未用（用 Find 代替） | GUIManager.cs:76 | ✅ | **public 字段**，见 §4.8 |
| `GUIManager.windowBlockingInput` | 未用（靠副作用） | GUIManager.cs:310,397-417 | ✅ | 消费点 Character.cs:1713 `CanDoInput()` |
| `CharacterInput.pauseWasPressed` | 未用（直接读 `Input`） | CharacterInput.cs:83,215 | ✅ | `Sample()` 每帧 `ResetInput()`+重采样（:212-215），模组"不累积"的判断成立 |
| `CharacterInput.Sample(bool)` | 未用 | CharacterInput.cs:212；调用点 CharacterMovement.cs:191 | 🔒 | `internal`；`pauseWasPressed` 的赋值在 `if(playerMovementActive)` **之外**，所以面板挡住输入时仍会采样 |
| `Character.CanDoInput()` | 未用 | Character.cs:1711-1718 | 🔒 | `internal`，读 `GUIManager.instance.windowBlockingInput` |
| `CursorHandler` | 未用（模组自己抢光标） | CursorHandler.cs:11-23 | ✅ | 只要 `windowShowingCursor` 为真游戏就自己解锁光标，见 §4.9 |
| `UIInputHandler.Instance.cancelWasPressed` | 未用 | UIInputHandler.cs:9,23,56-63 | ✅ | 游戏自己的 UICancel 动作采样 |
| `UIInputHandler.SetSelectedObject` | 未用 | UIInputHandler.cs:78-84 | ✅ | 仅手柄生效 |
| `EventSystem.current.currentSelectedGameObject` | HistoryUi.cs:149-152 | UnityEngine（非本游戏） | ⚠️ | 键鼠下常常为 null，见 §3.9 |
| `PauseMenuMainPage` 类型 | PauseMenuHook.cs:12,111-112 | PauseMenuMainPage.cs:12 | ✅ | `UIPage, INavigationPage` |
| `PauseMenuMainPage.m_quitButton` | PauseMenuHook.cs:20,120 | PauseMenuMainPage.cs:14-15 | ✅ | `[SerializeField] private Button`，字段名精确匹配 |
| `PauseMenuMainPage.confirmWindow/resumeButton` | 未用 | PauseMenuMainPage.cs:38,56 | ✅ | |
| `PauseMenuMainPage.OnDisable` | 间接（关暂停菜单会连带关 Modal） | PauseMenuMainPage.cs:88-98 | ⚠️ | 见 §3.11 |
| `PauseMenuHandler`（SetActive(false)） | PauseMenuHook.cs:186-195 | PauseMenuHandler.cs:7,43-62 | ✅ | 与 `OnResumeClicked`（PauseMenuMainPage.cs:154-157）和 `:59` 完全一致 |
| `PreventUnpause` | 未用 | PreventUnpause.cs:3-16 | ✅ | `public static bool UnpausePreventionActive`，见 §4.4 |
| `LoadingScreenHandler.loading` | Plugin.cs:209 | LoadingScreenHandler.cs:30-44 | ✅ | 纯静态字段读取，不会抛 |
| `Modal.OpenModal(header,body,onClose)` | RoomJoiner.cs:101 | Modal.cs:49-54 | ⚠️ | 无 Instance null 检查；`onClose` 有默认值，模组传 `null` 合法 |
| `DefaultHeaderModalOption(string,string)` | RoomJoiner.cs:102 | DefaultHeaderModalOption.cs:12-16 | ⚠️ | `Setup` 里 `Resources.Load("HeaderModalField"/"SubheaderModalField")` 无 null 检查（:20-25） |
| `ModalButtonsOption` + `Option(string,Action)` | RoomJoiner.cs:103 | ModalButtonsOption.cs:25-35,14-19 | ✅ | |
| `Modal.IsOpen/CloseModal` | 未用 | Modal.cs:20-30,56-62 | ✅ | |

### 1.6 本地化 / 字体 / 标题页

| 符号 | 模组使用处 | 游戏定义 | 有效 | 备注 |
|---|---|---|---|---|
| `LocalizedText.GetText(string,bool)` | Loc.cs:153（`printDebug: false`） | LocalizedText.cs:385-424 | ✅ | **必须传 false**：传 true 时缺失键返回 `"LOC: <ID>"`（:412-415）而不是空串 |
| `LocalizedText.Language`（15 值） | Loc.cs:144-146 | LocalizedText.cs:14-31 | ✅ | |
| `LocalizedText.OnLangugageChanged` | LocalizationSync.cs:33,55 | LocalizedText.cs:54 | ✅ | **是 public static 字段，不是 event**；`+=`/`-=` 合法，但任何程序集都能整体覆盖 |
| `LocalizedText.RefreshAllText` 触发点 | 间接 | LocalizedText.cs:521-529 | ✅ | `SetLanguage`（:497）→ `RefreshAllText` → 末尾 `OnLangugageChanged?.Invoke()` |
| `LocalizedText.SetTextLocalized` | 未用 | LocalizedText.cs:366-369 | ⚠️ | 内部 `SetText` 会把 `index` 清空（:363），之后语言切换该组件会显示 `"LOC: "`。模组销毁模板上的 LocalizedText 是对的（UiButtonFactory.cs:184-186、TitleMenuController.cs:131-132） |
| `LocalizedText.mainTable` | 未用 | LocalizedText.cs:105-115 | ✅ | `Dictionary<string,List<string>>`，14 列 |
| `LocalizedText.GetText(string,Language)` | 未用 | LocalizedText.cs:426-436 | ✅ | 见 §4.5 |
| `FontFallbackSwapper` | 未用（模组自己抢字体） | FontFallbackSwapper.cs:4-41 | ✅ | 见 §4.7 |
| `MainMenu`（Title 场景组件） | 未用 | MainMenu.cs:10,18 | ✅ | `SceneName="Title"`（:18）证实模组的 `scene.name=="Title"` 判定（HistoryUi.cs:33） |
| `MainMenu.playSoloButton` 等 public Button | 未用（走字符串路径） | MainMenu.cs:22-40,28 | ✅ | 见 §4.8 |
| `MainMenuMainPage` | 未用 | MainMenuMainPage.cs:16,47,49-59 | ✅ | `PlayButton => m_playButton`（:47）public；`Start()` 里 `SetState(Status_MainMenu)`（:57） |
| `MainMenuPageSelector` | 未用 | MainMenuPageSelector.cs:4-20 | ⚠️ | 首启时起始页是 `MainMenuFirstTimeSetupPage`（:10-19），`MainMenuMainPage` 可能 inactive |
| `MainMenuPlayPage` | 未用 | MainMenuPlayPage.cs:7,40-43 | ✅ | 直连 `JoinSpecificRoomState`，与模组无关 |
| `AirportCheckInKiosk` | 未用 | AirportCheckInKiosk.cs:6,43-47,111-124 | ✅ | `Start()` 写 `Status_Airport`（:46）；`Interact_CastFinished` 开 `GUIManager.instance.boardingPass`（:113-114） |
| `AirportInviteFriendsKiosk` | 未用 | AirportInviteFriendsKiosk.cs:5,28-49 | ✅ | `ActivateGameOverlayInviteDialog`（:43），`OfflineMode` 时整只 GO 关掉（:36） |
| `BoardingPass` | 未用 | BoardingPass.cs:12,63-71 | ✅ | `MenuWindow` 子类范本：`openOnStart=false`/`autoHideOnClose=false`/`closeOnPause=true` |
| `UIPage/UIPageHandler/TABS/TAB_Button` | 未用 | UIPage.cs:5,16,51；UIPageHandler.cs:9,33；TABS.cs:7；TAB_Button.cs:9 | ✅ | 模组走 PEAKLib 页面而非 Zorro 页面，二者不冲突 |
| `GUIManager.boardingPass` | 未用 | GUIManager.cs:28 | ✅ | |
| `SteamManager`（回调泵） | 间接（`Callback<T>` 依赖） | SteamManager.cs:117-123 | ⚠️ | `Update()→SteamAPI.RunCallbacks()`；只在 `!NoSteam` 时由 GameHandler.cs:172 `AddComponent` 挂上 |
| `RetrievableResourceSingleton<Modal>.Instance` | 间接 | Zorro.Core.Runtime/Zorro/Core/RetrievableResourceSingleton.cs:12-46 | ⚠️ | 按需 `Resources.Load(typeof(T).Name)` 实例化；非 Play 模式直接 `throw`（:39-42） |

### 1.7 PEAKLib（第三方，契约由 task-2 负责；此处只确认"模组调用成立"）

| 符号 | 模组使用处 | PEAKLib 定义（本地 `refs/PEAKLib.UI.dll`） | 有效 | 备注 |
|---|---|---|---|---|
| `MenuAPI.CreatePageWithBackground` | HistoryPageController.cs:405 | MenuAPI.cs:59-62 | ✅ | |
| `MenuAPI.CreateMenuButton` | UiButtonFactory.cs:96 | MenuAPI.cs:64-75 | ⚠️ | `Templates.ButtonTemplate==null` 时抛 `"You're creating MenuButton too early!..."`（:67-70），模组注释引用准确 |
| `MenuAPI.CreatePauseMenuButton` | 未用（模组自造 277 宽） | MenuAPI.cs:77-80 | ✅ | 宽度常量 `OPTIONS_WIDTH=277`（:25）与 PauseMenuHook.cs 一致 |
| `MenuAPI.CreateText` | 15 处 | MenuAPI.cs:82-94 | ✅ | |
| `MenuAPI.CreateButton` | HistoryRowFactory.cs:51,133,165,233 | MenuAPI.cs:96-101 | ✅ | |
| `MenuAPI.CreateScrollableContent` | HistoryPageController.cs:493,541 | MenuAPI.cs:103-106 | ✅ | |
| `MenuAPI.CreateTextInput` | HistoryPageController.cs:471 | MenuAPI.cs:108-113 | ✅ | |
| `MenuAPI.CreateLocalization(...).AddLocalization` | Loc.cs:143-146 | MenuAPI.cs:131-138 + 115-129 | ⚠️ | 新键会用**同一个文案填满 14 列**（:120-122），模组只覆盖 3 种语言，其余 11 种显示英文 |
| `MenuAPI.AddToPauseMenu` | PauseMenuHook.cs:32 | MenuAPI.cs:32-35 | ✅ | |
| `Templates.ButtonTemplate`（反射写） | UiButtonFactory.cs:50,66,207-214 | Templates.cs:8 | ✅ | `public static GameObject? ButtonTemplate { get; internal set; }`：`GetProperty(Public\|Static)` 能拿到、`CanWrite==true`、`SetValue` 可写 → 模组的注入手段成立 |
| PEAKLib 模板来源失效 | UiButtonFactory.cs:16-21（注释） | PEAKLib.UI/Hooks/PauseMenuHooks.cs:20-41 | ✅ | 注释里的判断**完全属实**：`source.First(n => n.name=="UI_MainMenuButton_LeaveGame (2)")`（:25）在没有该对象时抛 `InvalidOperationException`，`Templates.ButtonTemplate` 永久为 null。`MainMenu` 类型仍存在（MainMenu.cs:10），钩子本身会跑，但对象名未证 |
| `PeakCustomPage` | HistoryPageController.cs:26,60 | PEAKLib.UI.Elements/PeakCustomPage.cs:11 | ✅ | `public class PeakCustomPage : MenuWindow` → 模组的 `(MenuWindow)_page` 转换成立 |
| `PeakCustomPage.BlocksPlayerInput` 等 | HistoryPageController.cs:170-173,406-409 | PeakCustomPage.cs:25-51 | ✅ | 全部 `override` 到 MenuWindow 同名虚属性，语义一致 |
| `PeakMenuButton.Awake` 结构要求 | UiButtonFactory.cs:161-176 | PeakMenuButton.cs:19-27 | ✅ | 直接子物体 `Panel`/`Text`/`Border` + Border 的下一个兄弟；`GetComponent` 无 null 检查 → 模组的 `IsSuitableTemplate` 是**必要**防护 |
| `PeakMenuButton.SetColor/SetWidth/SetText/OnClick` | 多处 | PeakMenuButton.cs:29-64 | ⚠️ | `SetColor` 内部 `ThrowHelper.ThrowIfFieldNull`（:37）会抛；模组已 try/catch（HistoryPageController.cs:707-711、PauseMenuHook.cs:92-93） |

---

## 2. 命名空间陷阱（编译期）

`Peak.Network` 下存在一个 **与 `Steamworks.SteamMatchmaking` 同名的类**：

- `Assembly-CSharp/Peak/Network/SteamMatchmaking.cs:5-7` — `[Obsolete("Moved to Peak.Network.SteamLobbyAPI")] public class SteamMatchmaking : SteamLobbyAPI`
- 任何同时 `using Peak.Network;` 和 `using Steamworks;` 的文件，写裸 `SteamMatchmaking.GetLobbyData(...)` 会得到 **CS0104 二义性**编译错误。
- 现有 22 个文件都没 `using Peak.Network;`（grep 确认 `NetCode.` 零命中），所以当前能编译。**任何"迁移到 NetCode"的改动都必须写全限定名 `Steamworks.SteamMatchmaking`**（游戏自己就是这么写的，SteamLobbyAPI.cs:80,97,100,125,130）。

`Crab.Network.*` 全是空壳（`Crab/Network/IMatchmakingAPI.cs:6-9`、`PhotonShim.cs:5-9`、`PhotonRoomEvents.cs:5-8`、`INetworkRoomEvents.cs:5-9`、`IMatchMakingEvents.cs:5-9`），一律 `[Obsolete("Moved to Peak.Network")]`。不要引用。

---

## 3. 失效或有风险的假设（按严重度）

### 3.1 【已失效】`host_Name` / `lobby_Name` 大厅键在 3.0.1 不存在

- 模组：`LobbyRecorder.cs:60` 读 `host_Name`、`LobbyRecorder.cs:71` 读 `lobby_Name`。
- 游戏：`Assembly-CSharp` / `PortningsbolagetMemory` / `Zorro.Core.Runtime` / `SteamCommon` / `Managers` / `Platforms` / `PlatformProviders` / `User` / `Utilities` / `pworld` / `EpicCommon` 全部 0 命中 `host_Name|lobby_Name`。3.0.1 只写 `PeakVersion`（SteamLobbyHandler.cs:409）、`PhotonRegion`+`CurrentScene`（:438,447）、`RunId`（RunManager.cs:222）。
- 失败模式：`GetLobbyData` 返回 null → `?? ""` → `lobbyName` 恒为空 → `UpsertRoom` 的 `if (!string.IsNullOrWhiteSpace(lobbyName))`（HistoryStore.cs:141-142）永不成立 → `CreateRoomRow` 的 `if (!string.IsNullOrWhiteSpace(captured.LobbyName))`（HistoryRowFactory.cs:74-75）永不渲染。**"名称：xxx"这一行是死代码，不会崩**。`hostName` 走 `GetFriendPersonaName` 兜底（LobbyRecorder.cs:63），功能正常。
- 建议：删掉 `lobby_Name` 分支；若确实想要房间显示名，用 `PhotonNetwork.CurrentRoom.Name`（`CurrentRoom` 公开，PhotonNetwork.cs:440）或 `NetCode.Session.RoomName`（PhotonShim.cs:68-78）。

### 3.2 【已失效】Title 回退路径会被 `MenuWindow.Start()` 撤销

- 模组：`HistoryPageController.cs:178-186` — `OpenMenuWindow()` 失败时在 Title 场景退化为 `Show()`。
- 游戏：`MenuWindow.Start()`（MenuWindow.cs:35-48）在页面 GO 创建后的**下一帧**执行：`if (!isOpen) { if (openOnStart) Open(); else StartClosed(); }`；`StartClosed()`（:124-129）执行 `panel.SetActive(false)`。
- 失败模式：模组只 `Show()` 没有置 `isOpen`，所以 `Start()` 走 `StartClosed()` → 面板刚出现就被隐藏，`IsPageOpen` 永远 false，`HistoryUi.Tick()`（HistoryUi.cs:119-131）直接 return，ESC/键盘全失效。`_openedViaMenuWindow=true` 救不了，因为它同时要求 `activeInHierarchy`（HistoryPageController.cs:76-91）。
- 建议：这条路径要么改成"直接 `Discard()` 并报错"，要么反射同时写 `isOpen`（`MenuWindow.isOpen` 是 `{ get; private set; }`，MenuWindow.cs:27）。更稳的是保证 `Open` 反射永不失败——它是 `internal virtual`，只要 PEAKLib 仍继承 `MenuWindow` 就一定能取到。

### 3.3 【高风险】`steam_player_group` 可能残留，把"单人/主菜单"误判成"仅邀请"

- 模组：`FriendRoomsService.cs:231-242` 的判定顺序，`group` 非空 ⇒ `InviteOnly` 优先于 `max_players=="1"`。
- 游戏：`SteamRichPresence.SetState`（SteamRichPresence.cs:10-34）：
  - `Status_MainMenu` → `ClearRichPresence()`（:17），**清掉 group**；
  - `OfflineMode` → 只写 `players="1"`、`max_players="1"`（:19-23），**不清也不写 group**；
  - `InRoom` → 写 `steam_player_group=CurrentRoom.Name`（:27）；
  - 三个分支之后**无条件**写 `steam_display`/`players`/`steam_player_group_size`/`max_players`（:30-33）。
- 失败模式：若存在"从房间直接掉到 OfflineMode 而不经过 `Status_MainMenu`"的路径，group 会保留上一个房间名 → 单人游戏被标成"仅邀请"。是否可达未证（见 §5.2）。
- 建议：把判据换成"**本次会话**的 `steam_display` 是否属于 biome/Airport" + `players`/`max_players` 组合，不要单独依赖 group；或先用 `LocalizedText.GetText("#Status_MainMenu", Language.English)`（LocalizedText.cs:426）确认 `steam_display` 的真实取值形态（§5.1）。

### 3.4 【高风险】`TryJoinLobbyDuringSession` 对"重进历史房间"是陷阱，不能用来替代 `TryJoinLobby`

- 模组：未使用（正确）。
- 游戏：`SteamLobbyHandler.cs:364-372` 只设 `_joiningFromSession = RequestLobbyData(...)`；随后 `OnLobbyDataUpdate` 成功分支直接 `JoinLobby`（:322-326）**跳过版本校验**；`OnLobbyEnter` 里 `_joiningFromSession` 为真时**提前 return**（:122-126），因此 `m_currentlyWaitingForRoomID` 永不赋值（:156），`Update()`（:160-167）不会发 `RequestRoomID`，**永远进不了 Photon 房间**。
- 失败模式：如果为了"不弹模态框"而改用它，玩家会卡在一个只有 Steam 大厅、没有 Photon 房间的状态。
- 建议：重进历史房间继续用 `TryJoinLobby`；只在"已在 Photon 房间、只缺 Steam 大厅"时用 `TryJoinLobbyDuringSession`（这正是游戏自己在 NetworkConnector.cs:310 的用法）。

### 3.5 【高风险】`TryJoinLobby` 在"已在 Photon 房间"时静默失败，而模组门禁只看 Steam 大厅

- 模组：`HistoryUi.cs:38-52` `IsInLiveLobby` = `SteamLobbyHandler.InSteamLobby()`；`RoomJoiner.cs:59-63`、`HistoryRowFactory.cs:97`、`HistoryRowFactory.cs:274-275` 都用它当"不能加入"的判据。
- 游戏：`OnLobbyDataUpdate` 版本匹配后 `if (PhotonNetwork.InRoom) { Debug.LogError(...); return; }`（SteamLobbyHandler.cs:335-339）——**只有一行 Debug.LogError，无模态框、无回调**。而 `InSteamLobby()` 只反映 `LobbySteamId`（:473-476），与 Photon 房间无关。
- 失败模式：玩家在 Photon 房间但 Steam 大厅为 Nil（例如 `csid` 附加失败，NetworkConnector.cs:310-313）时，模组显示"加入"按钮，点击后毫无反应。
- 建议：门禁改成 `NetCode.Session.InRoom`（PhotonShim.cs:66）或 `PhotonNetwork.InRoom`（PhotonNetwork.cs:697）与 `NetCode.Matchmaking.InLobby`（SteamLobbyAPI.cs:30）的并集。

### 3.6 【中】跨大版本重进必弹"版本不匹配"模态框

- 模组：`RoomJoiner.cs:84` 直接 `TryJoinLobby`。
- 游戏：`OnLobbyDataUpdate` 用 `PeakVersion` 与 `new BuildVersion(Application.version).ToMatchmaking()` 比较（SteamLobbyHandler.cs:332-333）；不匹配弹 `MODAL_MISMATCH_TITLE`（:344-350）。`ToMatchmaking()` 只返回 **"主.次"**（BuildVersion.cs:73-76），所以 3.0.x 内部互通，2.x 房间必弹。
- 失败模式：历史里 2.x 时期的 lobby id 点"加入"，玩家看到的是"游戏版本不匹配"而不是"房间不存在"，且模态框是游戏弹的，模组无法拦截。
- 建议：`JoinGate` 除了 2.5h 年龄上限，再加一条"记录房间时的游戏版本"过滤（模组可在 `RoomRecord` 里存 `Application.version` 的 major.minor）；或至少把文案改成"该房间来自旧版本"。

### 3.7 【中】`m_isHosting` 残留会吞掉下一次 `LobbyEnter`

- 游戏：`OnLobbyCreated` 一进来就 `m_isHosting = true`（SteamLobbyHandler.cs:396-398），**在检查 `m_eResult` 之前**；失败分支直接 `return`（:399-406）没复位。之后 `OnLobbyEnter` 第一件事是 `if (m_isHosting) { m_isHosting=false; return; }`（:109-113），**在成功判断之前**。
- 失败模式：一次失败的开房之后，玩家加入别人的大厅时 `LobbySteamId` 不会被赋值 → 模组 `IsInLiveLobby` 为 false（会显示"加入"按钮、允许发邀请），而游戏侧邀请是坏的。
- 建议：模组不要用 `InSteamLobby()` 作为唯一真相；额外用 `NetCode.Matchmaking.JoinedLobby`（SteamLobbyAPI.cs:165-168）交叉验证，并在 `LobbyJoinFeedback` 里对 `m_EChatRoomEnterResponse==1` 但 `LobbySteamId==Nil` 的情况打一条诊断日志。

### 3.8 【中】玩家名来源不一致：`Sanitize` 只覆盖一条路径

- 模组：`PlayerTracker.cs:203-211`（`PlayerConnectionLog.OnPlayerEnteredRoom` Postfix）与 `PlayerTracker.cs:229-234`（`OnJoinedRoom` 里遍历 `PhotonNetwork.PlayerList` 快照）两条录入路径。
- 游戏：只有 `PlayerConnectionLog.OnPlayerEnteredRoom`（PlayerConnectionLog.cs:56-83）会对**已在房间里的人**做 `NetworkingUtilities.Sanitize(newPlayer.NickName)` + 截断 32 字符 + 原地写回（:62-67）。`PlayerList` 快照拿到的昵称没经过这一步；`OnJoinedRoom` 时房间里已有的人也不会再触发 `OnPlayerEnteredRoom`。
- 失败模式：同一玩家在不同路径下存进历史的 `DisplayName` 可能一个带富文本标签、一个不带；`PlayerConnectionLog` 不在该场景时（Title）两条路径都没有 sanitize。
- 建议：模组自己调 `NetworkingUtilities.Sanitize`（public static，NetworkingUtilities.cs:33）+ 截断 32，别依赖游戏的副作用。同时 `PlayerTracker.cs:188` 的 `name == "Bing Bong"` 魔法字符串应对齐 PlayerConnectionLog.cs:87。

### 3.9 【中】`IsTextInputFocused` 在键鼠下不可靠，K 键可能穿透到输入框

- 模组：`HistoryUi.cs:145-169` 读 `EventSystem.current.currentSelectedGameObject`；`Plugin.cs:185-186` 用它决定是否吞掉快捷键。
- 游戏：`UIInputHandler.SetSelectedObject`（UIInputHandler.cs:78-84）**只在手柄输入方案下**才写 `EventSystem.current.SetSelectedGameObject`；键鼠下点输入框不会成为 `currentSelectedGameObject`。
- 失败模式：键鼠玩家在搜索框里打 "k" 时，`Plugin.Update` 的 `Input.GetKeyDown(KeyCode.K)` 可能同时触发开关面板。
- 建议：改用 `TMP_InputField.isFocused`（扫描 `FindObjectsByType<TMP_InputField>`）或直接在 `_searchInput.InputField.onFocusSelectAll`/`onEndEdit` 上维护一个 bool；`PeakTextInput.InputField` 已经在用（HistoryPageController.cs:161,665）。

### 3.10 【中】ESC 用的是 `KeyCode.Escape`，不是游戏的 Pause/UICancel 动作

- 模组：`HistoryUi.cs:136`。
- 游戏：暂停是 `InputSystem.actions.FindAction("Pause")`（CharacterInput.cs:144）采样成 `pauseWasPressed`（:215）；UI 取消是 `action_cancel = FindAction("UICancel")`（UIInputHandler.cs:36）采样成 `cancelWasPressed`（:59）。`MenuWindow.TestCloseViaInput`（MenuWindow.cs:59-73）消费的正是这两个。
- 失败模式：玩家把 Pause 改绑到别的键后，ESC 仍会关模组面板（无害），但如果 ESC 同时是 Pause 的绑定，模组的 `GUIManager.UpdatePaused` Prefix（HistoryUi.cs:184-188）只挡一帧，靠 `pauseWasPressed` 每帧重采样（CharacterInput.cs:212-215）兜底 —— 当前成立，但没有语义保证。
- 建议：`Input.GetKeyDown(KeyCode.Escape)` → `Singleton<UIInputHandler>.Instance.cancelWasPressed`，与游戏 UI 完全同源。（注意 `GUIManager.UpdatePaused` 仍读 `pauseWasPressed`，Prefix 还是要留。）

### 3.11 【低】关暂停菜单会连带关掉游戏 Modal

- 模组：`PauseMenuHook.cs:180-201` 直接 `go.SetActive(false)` 关 `PauseMenuHandler`。
- 游戏：`PauseMenuMainPage.OnDisable`（PauseMenuMainPage.cs:88-98）在 `confirmWindow` 打开时关它，并 `if (Modal.IsOpen) Modal.CloseModal();`（:94-97）。
- 失败模式：`HistoryUi.Open()` 先 `ClosePauseMenuIfOpen()`（HistoryUi.cs:68）再开面板 —— 顺序没问题；但如果哪天在暂停菜单开着的时候调 `RoomJoiner.ShowModal`，模态框会被立刻关掉。
- 建议：`RoomJoiner.ShowModal`（RoomJoiner.cs:93-111）加一条"暂停菜单是否开着"的日志，或者干脆先关暂停再弹。

### 3.12 【低】`GameHandler.GetService<SteamLobbyHandler>()` 在 NoSteam 客户端抛异常并弹出异常文本

- 模组：`RoomJoiner.cs:77-82`：`handler == null` 时弹 `Loc.Disabled`，但真实情况是**抛** `KeyNotFoundException`，被外层 catch（:86-90）接住后 `ShowModal(Loc.PageTitle, ex.Message)` —— 正文是 `"The given key 'SteamLobbyHandler' was not present in the dictionary."`。
- 游戏：`GetService<T>` 是字典索引器（GameHandler.cs:226）；`SteamLobbyHandler` 仅在 `!NoSteam` 注册（:168-173）。
- 建议：先用 `GameHandler.Initialized`（:91）+ `NetCode.Matchmaking`（NoSteam 时是 `NoMatchmaking`，InLobby 恒 false，NoMatchmaking.cs:13）判定，再决定是否 `GetService`；或 `try { } catch (KeyNotFoundException) { }`。

### 3.13 【低】标题页硬编码路径无法静态验证，且首启可能命中 inactive 页面

- 模组：`TitleMenuController.cs:15-18` 的 `"MainMenu/Canvas/MainPage/Menu/Buttons"`、`"…/Button_PlaySolo"`、`"…/NextLevelUI"`；`TitleMenuController.cs:54` 还要求 `template.activeInHierarchy`。
- 证据：`Button_PlaySolo`、`UI_MainMenuButton`、`Menu/Buttons` 在 `Assembly-CSharp` **零命中** —— 这些是纯场景对象名，静态无法证实（§5.3）。另外 `MainMenuPageSelector.GetStartPage()`（MainMenuPageSelector.cs:10-19）在首次启动（无 `FirstTimeStartup2` 键）返回 `MainMenuFirstTimeSetupPage`，`MainMenuMainPage` 会 `SetActive(false)`（UIPageHandler.cs:17-22）。
- 失败模式：首启玩家看不到 ribbon（8 秒超时后放弃，TitleMenuController.cs:48-64）。
- 建议：模板源改成 `FindFirstObjectByType<MainMenu>().playSoloButton`（MainMenu.cs:28，public 字段）或 `MainMenuMainPage.PlayButton`（MainMenuMainPage.cs:47），彻底去掉字符串路径；等待条件放宽到"按钮存在"而非"activeInHierarchy"。

### 3.14 【低】`UiButtonFactory` 反射写 `Templates.ButtonTemplate` 依赖 PEAKLib 的 internal setter

- 模组：`UiButtonFactory.cs:203-220`。
- PEAKLib：`Templates.cs:8` `public static GameObject? ButtonTemplate { get; internal set; }`。`GetProperty(name, Public|Static)` 返回该属性且 `CanWrite==true`，`SetValue` 在完全信任下可用 → **当前成立**。
- 风险：PEAKLib 若改成 `{ get; }` 或加 `[Obsolete(error:true)]`，模组会静默降级到"自己的模板"（`_ownedTemplate` 仍在），功能不坏但其他 mod 得不到修复。建议保留，但要打 warning 日志（现在只 `LogDebug`）。

### 3.15 【低】`JoinGate` 的 2.5 小时年龄上限是纯假设

- 模组：`JoinGate.cs:27`、`JoinGate.cs:46-51`。
- 证据：游戏里没有任何"会话最长 2.5 小时"的常量；反向证据是 `LoadingScreenHandler.WaitForCharacterSpawn(timeout=200f)`（LoadingScreenHandler.cs:246）之类都只是超时。**UNPROVEN - needs runtime evidence**。
- 影响：偏保守（宁可少显示"加入"），不会崩；但会把仍然活着的长会话房间隐藏掉。

---

## 4. 应采纳的新能力

### 4.1 `Peak.Network.NetCode` 取代直接抓 `SteamLobbyHandler`

`NetCode.cs:10-16`：`Session`(ISessionAPI/PhotonShim)、`Matchmaking`(IMatchmakingAPI)、`ConnectionEvents`、`RoomEvents` 全是 static 且已就绪。`NetCode.Matchmaking` 在 `NoSteam` 时是 `NoMatchmaking`（NetCode.cs:21-25、NoMatchmaking.cs:7-60），**永不抛异常**——比 `GameHandler.GetService<SteamLobbyHandler>()` 安全得多。

关键成员：`InLobby`(SteamLobbyAPI.cs:30)、`LobbyId`(:16)、`TryGetLobbyData`(:128)、`TrySetLobbyData`(:123)、`CreateLobby(LobbyType)`(:78)、`LeaveLobby`(:83)、`HasPendingInvite`(:34)、`ConsumePendingJoin`(:109)、事件 `InviteReceived`/`JoinedLobby`/`LeftLobby`/`JoinLobbyFailed`(:54-60)。

替代：`HistoryUi.cs:44-45`、`SteamInviteService.cs:45-46`、`PlayerTracker.cs:107,132`、`RoomJoiner.cs:77`。

### 4.2 `NetCode.Session` 取代 `PhotonNetwork` 直连

`PhotonShim.cs:25-78`：`IsHost`、`InRoom`、`IsOffline`、`RoomName`、`SeatNumber`、`NickName`、`TryGetCustomProperty`。`InRoom` 的实现是 `NetworkClientState == ClientState.Joined`（:66），比 `PhotonNetwork.InRoom`（PhotonNetwork.cs:697，同表达式）语义相同但多一层抽象，未来换后端不动模组。`TryGetCustomProperty("csid")` 能直接拿到房间对应的 Steam 大厅 id（NetworkConnector.cs:310 的同一个键）。

### 4.3 `NetCode.RoomEvents` 取代对 `PlayerConnectionLog` 的 Harmony 补丁

`PhotonRoomEvents.cs:9-13,22-43` 提供 `PlayerEntered`/`PlayerLeft`/`RoomOwnerChanged`。它是 `PhotonCallbackTarget`，构造时 `PhotonNetwork.AddCallbackTarget(this)`（PhotonCallbackTarget.cs:9-13），由 `NetCode` 静态构造创建（NetCode.cs:32），**不依赖场景对象**——而 `PlayerConnectionLog` 是场景 MonoBehaviour（PlayerConnectionLog.cs:13-31 有序列化字段），Title 场景没有它。

**代价**：`PlayerEntered` 是 `Action`（无参，PhotonRoomEvents.cs:24），拿不到 `Player`。要在事件里识别新玩家必须自己 diff `PhotonNetwork.PlayerList`（PhotonNetwork.cs:480）。`RoomOwnerChanged` 同理无参（:37），但正好可用于替换 `PlayerTracker.cs:256-268` 的 `OnLobbyDataUpdate` 补丁——语义比"大厅数据更新"更准。

### 4.4 `PreventUnpause` 与 `MenuWindow` 的既有开关

`PreventUnpause.cs:3-16`：`public static bool UnpausePreventionActive`，`OnEnable`/`OnDisable` 维护。`PauseMenuHandler.Update` 在 `else if (!PreventUnpause.UnpausePreventionActive)` 之后才 `SetActive(false)`（PauseMenuHandler.cs:57-60）。给面板 GO 挂一个 `PreventUnpause` 就能保证"面板开着时 ESC 不会顺手关掉暂停菜单"。
注意它**不能**阻止 `GUIManager.UpdatePaused` 打开暂停菜单（GUIManager.cs:420-436 不查这个标志），所以 `HistoryUi.cs:184-188` 的 Prefix 仍需保留。

### 4.5 富状态 `steam_display` 提供精确场景

`SteamRichPresence.cs:30` 无条件写 `steam_display = "#" + state`，`RichPresenceState`（RichPresenceState.cs:1-16）13 个值与场景一一对应。朋友面板现在只能区分"主菜单/单人/仅邀请/可加入"；拿到 `steam_display` 就能显示"在 Airport 等人"还是"在 Gloom"。取值形态未证（§5.1），但**键名和写入点是确定的**。
配套：`LocalizedText.GetText(string, Language)`（LocalizedText.cs:426-436）可以拿到 `#Status_MainMenu` 在任意语言下的实际文案，用来做一次自校准。

### 4.6 `RunId` 大厅键区分"同一大厅的不同跑图"

`RunManager.cs:13` `public const string RunIdLobbyDataKey = "RunId"`；`:219-224` 房主每次开新跑图都 `TrySetLobbyData("RunId", guid)` 并 RPC 给其他人。历史里同一个 lobby id 可能对应多次跑图，用 `RunId` 去重比"时间戳最近"更准。读取方式：`NetCode.Matchmaking.TryGetLobbyData(lobbyId, RunManager.RunIdLobbyDataKey, out var v)`（SteamLobbyAPI.cs:128）。

### 4.7 `FontFallbackSwapper` 是官方 CJK 字体入口

`FontFallbackSwapper.cs:6,9,21-41`：`public static FontFallbackSwapper instance`、`public TMP_FontAsset mainBaseFont`、`SwitchToSimplified()/SwitchToTraditional()`。游戏在 `LocalizedText.SetLanguage`（LocalizedText.cs:507-517）里调用它切换 fallback。
模组 `HistoryUiFont`（HistoryUiFont.cs:55-77）目前靠"抓场景里任意一个含 CJK 的 TMP 标签的 font"，标题页没有 CJK 标签时会退化到 `CreateMenuButtonSafe("字")` 造一个按钮再销毁（:32-47）——每次场景切换都要重来。改用 `FontFallbackSwapper.instance.mainBaseFont` 即可拿到带完整 fallback 链的官方字体，且不需要在场景里找样本。

### 4.8 用 public 字段取代硬编码路径和 `FindObjectsByType`

- `GUIManager.instance.pauseMenuMainPage` — GUIManager.cs:76，**public 字段**，可直接替代 `PauseMenuHook.cs:111-112` 的 `GetComponent/GetComponentInParent`。
- `MainMenu.playSoloButton` — MainMenu.cs:28，**public Button**，替代 `TitleMenuController.cs:17` 的 `"…/Button_PlaySolo"`。
- `MainMenuMainPage.PlayButton` — MainMenuMainPage.cs:47，替代同一处。
- `MainMenuPageSelector.mainPage` — MainMenuPageSelector.cs:6。
- `GUIManager.instance.boardingPass` — GUIManager.cs:28（如果要做登机牌入口）。

### 4.9 光标交给 `CursorHandler`，别再自己抢

`CursorHandler.cs:11-23` 每帧检查：非菜单场景下只要 `GUIManager.instance.windowShowingCursor`（由 `MenuWindow.showCursorWhileOpen` 汇总，GUIManager.cs:404-407）或 `Modal.IsOpen` 为真就 `Cursor.lockState=None; visible=true`，否则强制锁回。
模组 `HistoryPageController.cs:244-289` 的 `CaptureGameplayInputBlock/TickInputBlock` 每帧写 `Cursor.*`，与 `CursorHandler` 抢同一个状态，且无条件 `visible=true`（对手柄玩家是错的，CursorHandler.cs:13 在非键鼠方案下根本不看 `windowShowingCursor`）。面板已经 `ShowCursorWhileOpen=true`（:171,408），这块可以整体删掉。

### 4.10 `MenuWindow` 的 public 控制面

`isOpen`(MenuWindow.cs:27)、`inputActive`(:29)、`initialized`(:31)、`SetInputActive(bool)`(:165)、`Show/Hide`(:155,160)、`CloseAllWindows()`(:113)。模组目前完全靠"读 `activeInHierarchy`"推断开关状态（HistoryPageController.cs:76-91），改用 `isOpen` 更直接；`CloseAllWindows()` 是游戏自己切场景前用的（AirportCheckInKiosk.cs:154）。

---

## 5. 无法静态验证（必须进游戏实测）

### 5.1 `steam_display` 的取值形态
`SteamRichPresence.cs:30` 写的是 `"#" + state`。模组注释（FriendRoomsService.cs:223-225）称 `GetFriendRichPresence` 返回的是**已本地化**文案（如 `"Main Menu"`）而非 `"#Status_MainMenu"`，因此 `FriendRoomsService.cs:231` 的首个判断"很少触发"。这是 Steam 客户端行为，反编译里没有证据。**UNPROVEN - needs runtime evidence**。
验证方法：在游戏里设成英文，读一个在线好友的 `steam_display`；再用 `LocalizedText.GetText("#Status_MainMenu", LocalizedText.Language.English)`（LocalizedText.cs:426）对比。

### 5.2 `steam_player_group` 残留是否可达
见 §3.3。需要实测：进一个多人房间 → 不断开网络直接回单人（`PhotonNetwork.OfflineMode=true`，路径见 MainMenu.cs:139-151）→ 看好友侧 `steam_player_group` 是否还留着房间名。

### 5.3 标题页场景对象名
`TitleMenuController.cs:15-18` 的四个路径、`PauseMenuHook.cs:16` 的按钮名、`UiButtonFactory.cs:129,137` 的 `"UI_MainMenuButton_" / "Button_"` 前缀，在 `Assembly-CSharp` 里零命中，全部是场景/预制体对象名。**UNPROVEN - needs runtime evidence**（用 `DebugUIHandler` 或临时日志打印 `MainMenu/Canvas/MainPage` 的子物体树）。

### 5.4 PEAKLib 的模板对象是否还存在
`PauseMenuHooks.cs:25` 找 `"UI_MainMenuButton_LeaveGame (2)"`、`:23` 找 `"SettingsCell"`。3.0.1 是否仍存在这两个对象未证；若不存在，PEAKLib 的 Postfix 会抛 `InvalidOperationException`（`Enumerable.First` 无匹配），`Templates.ButtonTemplate` 恒 null —— 这时模组的 `UiButtonFactory` 兜底是**唯一**能让页面按钮出现的东西。需实测一次 `Templates.ButtonTemplate` 是否为 null。

### 5.5 `PlayerConnectionLog` 在哪些场景存在
补丁（PlayerTracker.cs:203）只在场景里有该 MonoBehaviour 时才有意义。需要确认 Title / Airport / 各 Island 场景里是否都有它。

### 5.6 `Callback<T>` 的注册顺序
模组在 `Plugin.Awake`（Plugin.cs:145-148）注册，游戏在 `GameHandler.Awake`（GameHandler.cs:150-179，`[DefaultExecutionOrder(-100)]`）注册。按 BepInEx 的加载时序模组先注册，但**这是时序推断，不是契约**。若顺序反过来，`LobbyJoinFeedback.OnLobbyEnter`（LobbyJoinFeedback.cs:48）读到 `LobbySteamId` 时它已经被游戏清成 Nil，行为不同。需实测一次失败加入的日志顺序。

### 5.7 `m_isHosting` 残留路径（§3.7）
需要实测：让一次 `CreateLobby` 失败（例如已被封禁或 Steam 拒绝），再接受一个好友邀请，看 `InSteamLobby()` 是否为 false。

### 5.8 `JoinGate` 的 2.5 小时上限（§3.15）
需要实测一次超长会话的 lobby id 是否仍可加入。

### 5.9 首次启动时 `MainMenuMainPage` 是否 inactive（§3.13）
`MainMenuPageSelector.cs:10-19` 的 `FirstTimeStartup2` 分支决定了首启起始页。需要清 `PlayerPrefs` 后跑一次，确认 ribbon 是否出现、`MainMenuMainPage.Start()`（→ `SetState(Status_MainMenu)`，MainMenuMainPage.cs:57）是否执行（不执行的话好友侧永远看不到"在主菜单中"）。

---

## 6. 结论摘要

- Steamworks.NET 层的 40+ 个成员**全部**与 3.0.1 一致，没有签名破坏。模组的大头风险不在 Steamworks。
- 真正的破坏集中在三处：(a) `host_Name`/`lobby_Name` 键消失（§3.1，功能死但无害）；(b) `MenuWindow`/`MenuWindow.Start` 的生命周期（§3.2）；(c) 富状态语义（§3.3）。
- 模组对 `SteamLobbyHandler` 的 4 个补丁点中，2 个是 private 字符串名（`OnLobbyEnter`、`OnLobbyDataUpdate`），没有编译期保护；另外 2 个（`NetworkConnector`、`GUIManager`）是 public 成员，稳。
- `Peak.Network` 那一层（NetCode/IMatchmakingAPI/ISessionAPI/PhotonShim/PhotonRoomEvents）目前**完全没被模组使用**，但它正是为了消除本节所有 `GameHandler.GetService` + 原始 Steamworks 调用而存在的；迁移时唯一要小心的是 §2 的 `SteamMatchmaking` 命名冲突。
