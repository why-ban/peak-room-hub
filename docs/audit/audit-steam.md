# AUDIT-STEAM — Steam/network layer (friends, invite, avatar, join, plugin lifecycle)

Game: PEAK v3.0.1 · decompile `D:\zhuanban\youhua\decompiled-latest`
Mod: `src/RoomHub/*.cs` @ v2.7.0 (`Plugin.cs:18`)
Scope: `FriendRoomsService.cs`, `SteamInviteService.cs`, `SteamAvatarService.cs`, `LobbyJoinFeedback.cs`, `RoomJoiner.cs`, `Plugin.cs`
Method: every claim below was read out of the 3.0.1 decompile, the shipped Steamworks.NET wrapper (`com.rlabrecque.steamworks.net`) or Valve's API docs. Nothing was executed. Claims that need a running game are labelled **UNPROVEN**.

---

## 0. Verdict in one paragraph

The Steam plumbing is structurally sound: callback registration is managed-only and safe before `SteamAPI.Init`, dispatch is single-threaded on the Unity main thread, disposal is idempotent, and the join path uses the only game entry point that performs a full join. The defects concentrate in three places: (a) the rich-presence discriminator has no representation of the new `Status_Airport` state and silently folds it into "Main menu", (b) every Steam call is unguarded against a failed `SteamAPI.Init` (native null-instance deref), and (c) several game-side facts asserted in the mod's comments are provably wrong — notably `steam_player_group_size` is a byte-for-byte duplicate of `players`, and `FriendRichPresenceUpdate_t` is not a data-arrival signal.

## 1. Findings (ranked)

| id | sev | anchor | one line |
|----|-----|--------|----------|
| S1 | High | `FriendRoomsService.cs:120/130/160/186/218-221/254`, `SteamAvatarService.cs:63-78`, `SteamInviteService.cs:48` | no guard on a failed `SteamAPI.Init` → native null deref from `SteamFriends.*` |
| S2 | High | `FriendRoomsService.cs:231-242` | `Status_Airport` has no branch; airport-without-room reads "Main menu" and gets an Invite button |
| S3 | High | `RoomJoiner.cs:59-63` | guard tests the Steam lobby, the game tests `PhotonNetwork.InRoom` → silent no-op join |
| S4 | Medium | `FriendRoomsService.cs:82-84`, `:290-298` | `FriendRichPresenceUpdate_t` is not proof data arrived (Valve: rate-limited calls post it anyway) |
| S5 | Medium | `FriendRoomsService.cs:223-242`, `Plugin.cs:61-62` | the chain's premise ("steam_display arrives localized") contradicts Valve docs and decides the whole classification |
| S6 | Medium | `FriendRoomsService.cs:272-282`, `:290-298` | 1.5 s notify throttle drops the trailing event of a burst → stale panel |
| S7 | Medium | `SteamAvatarService.cs:63-78` | `RequestUserInformation` re-fired every 45 frames; `imageId == 0` never negative-cached |
| S8 | Medium | `RoomJoiner.cs:77-82`, `SteamInviteService.cs:45-46` | `GameHandler.GetService<T>()` throws on a missing service, so the null checks are dead |
| S9 | Medium | `RoomJoiner.cs:101-104`, `LobbyJoinFeedback.cs:68` | `Modal` is a single slot; opening without checking `Modal.IsOpen` discards the other modal's `onClose` |
| S10 | Low | `Plugin.cs:150-153` | unguarded `PatchAll` runs after the services are up; one bad target kills the rest of `Awake` |
| S11 | Low | `SteamInviteService.cs:52` | invited-set keyed by the caller's raw string, not the canonical SteamID |
| S12 | Low | `FriendRoomsService.cs:120-131` | persona state never consulted → offline friends with stale game info listed as in-game |
| S13 | Low | `FriendRoomsService.cs:109-115`, `:69-70` | `_nextRefresh` advanced before the work; `_lastRpRequest` unbounded and never cleared |
| S14 | Low | `SteamLobbyHandler.cs:398` + `:109-113` | game-side `m_isHosting` swallow makes a mod join land in the Steam lobby with no Photon room and no feedback |
| S15 | Info | `Peak/Network/SteamMatchmaking.cs:5-8` | obsolete `Peak.Network.SteamMatchmaking` will shadow `Steamworks.SteamMatchmaking` if `using Peak.Network;` is ever added |

---

## 2. Rich presence — ground truth from the current build

### 2.1 The writer

`SteamRichPresence.SetState` (`Assembly-CSharp/SteamRichPresence.cs:10-34`) is the only code in the build that writes or clears rich presence:

- `:13-14` seeds `players = 1`, `max_players = 4` before any branch.
- `:15-18` `Status_MainMenu` → `SteamFriends.ClearRichPresence()` wipes every key, then the four writes at `:30-33` repopulate `steam_display`, `players`, `steam_player_group_size`, `max_players`.
- `:19-23` `PhotonNetwork.OfflineMode` → `players = 1`, `max_players = 1`, no group key.
- `:24-29` `PhotonNetwork.InRoom` → `players = PhotonNetwork.PlayerList.Length`, `steam_player_group = PhotonNetwork.CurrentRoom.Name`, `max_players = CurrentRoom.MaxPlayers`.
- `:30-33` `steam_display = "#" + state`, then `players`, `steam_player_group_size = players`, `max_players`.

Two consequences decide this audit:

1. **`steam_player_group_size` is a duplicate of `players`** — both are written from the same local `num` (`:31-32`). A discriminator built on `group_size` is identical to one built on `players`. The rationale in `Plugin.cs:53-60` (2.6.8: "group_size read 1 for a 5-player private room, so use players instead") therefore cannot be true as written; at most one of the two readings was correct, and the decompile says the keys can never disagree. The 2.6.6 → 2.6.8 → 2.7.0 discriminator churn chased a distinction that does not exist.
2. **Keys are sticky.** `SetRichPresence` only writes; the only removal in the build is `ClearRichPresence()` at `SteamRichPresence.cs:17` (grep `ClearRichPresence` → 5 hits: 4 in the Steamworks wrapper, 1 here). Valve documents the same removal rule — "If this is set to an empty string or NULL then the key is removed" (`ISteamFriends#SetRichPresence`) — and the game never does that. So `steam_player_group` survives every transition that does not pass through the main menu.

### 2.2 Where the 13 states come from

`RichPresenceState` has 13 members (`Assembly-CSharp/RichPresenceState.cs:1-16`). Only three call sites feed it:

| caller | anchor | state written |
|---|---|---|
| `MainMenuMainPage.Start` | `MainMenuMainPage.cs:57` | `Status_MainMenu` (every title load) |
| `AirportCheckInKiosk.Start` | `AirportCheckInKiosk.cs:46` | `Status_Airport` (airport scene) |
| `MountainProgressHandler` | `:45` and `:157-178` | `Status_Shore` on scene start, then the level name → Shore/Tropics/Alpine/Mesa/Roots/Caldera/Kiln/Gloom/Citadel/Nadir/Peak (`:162-176`, unknown title → Shore) |

`RichPresenceService` re-publishes the current state on player enter/leave (`:27-28` → `:42-45`) and `InRoomState.Enter` calls `Dirty()` (`InRoomState.cs:21`), so `players` and the group key are refreshed whenever membership or the room changes.

### 2.3 Truth table (observer = a Steam friend, values as the writer produces them)

| # | writer state | Photon | steam_display | players | group_size | max_players | steam_player_group |
|---|---|---|---|---|---|---|---|
| 1 | MainMenu | any | `#Status_MainMenu` | `1` | `1` | `4` | absent (cleared) |
| 2 | Airport | OfflineMode | `#Status_Airport` | `1` | `1` | `1` | absent |
| 3 | Airport | InRoom | `#Status_Airport` | `N` | `N` | `MAX_PLAYERS` | room name |
| 4 | Airport | neither | `#Status_Airport` | `1` | `1` | `4` | absent, or stale |
| 5 | Level X | OfflineMode | `#Status_X` | `1` | `1` | `1` | absent |
| 6 | Level X | InRoom | `#Status_X` | `N` | `N` | `MAX_PLAYERS` | room name |
| 7 | Level X | neither | `#Status_X` | `1` | `1` | `4` | absent, or stale |
| 8 | keyCount == 0 | — | — | — | — | — | — |

`MAX_PLAYERS` defaults to 4 (`Peak/Network/NetworkingUtilities.cs:25`), is clamped 1..256 (`:43-47`) and is what the host room uses (`:79-87`). Row 7 is unreachable (a level scene implies a room); row 4 is reachable only in a narrow window (S2).

### 2.4 Mod chain evaluated against the table

The chain (`FriendRoomsService.cs:231-242`) only runs when Steam exposed **no** joinable lobby (`:168-172`), i.e. private lobby, solo, or no room:

| row | branch hit | result | verdict |
|---|---|---|---|
| 1 | `display == "#Status_MainMenu"` (`:231`) | MainMenu | correct **iff** the token is raw (S5) |
| 2 | `max_players == "1"` (`:235`) | Solo | correct |
| 3 | `group` non-empty (`:233`) | InviteOnly | correct |
| 4 | `players == "1"` (`:237`) | **MainMenu** | **wrong** — the friend is in the airport, not the menu (S2) |
| 5 | `max_players == "1"` | Solo | correct |
| 6 | `group` non-empty | InviteOnly | correct |
| 7 | `players == "1"` | **MainMenu** | wrong, unreachable |
| 8 | `keyCount == 0` (`:185-199`) | Unknown | correct and honest |

**Conclusion: the chain still classifies the twelve real states correctly, with one hole — airport-without-room — that exists because the enum gained `Status_Airport`. The enumeration growth itself is harmless: the chain never enumerates states, it string-matches MainMenu once and reads three numeric/string keys.** The last two branches (`:239-240` max non-empty → InviteOnly, `:241-242` else → MainMenu) are unreachable for rows 1-8 as written.

---

## 3. Findings in detail

### S1 — High — unguarded Steam calls after a failed `SteamAPI.Init`

**Anchor** `FriendRoomsService.cs:120`, `:130`, `:160`, `:186`, `:218-221`, `:254`; `SteamAvatarService.cs:63`, `:65`, `:109`, `:113`, `:138`, `:148`, `:158`; `SteamInviteService.cs:48`

**Scenario** Steam is running but `SteamAPI.Init()` fails (not logged in, `k_ESteamAPIInitResult_NoSteamClient`, API emulator mismatch). The game continues: `SteamManager.Awake` logs the error and does not quit (`SteamManagerAssembly/SteamManager.cs:81-89`), `NetworkingUtilities.GetBestUserID` falls back to a PlayerPrefs id (`NetworkingUtilities.cs:53-65`) so the title screen and Photon connect still work. `CSteamAPIContext.Init()` is only called on a successful init (`com.rlabrecque.steamworks.net/Steamworks/SteamAPI.cs:43-55`), so `m_pSteamFriends` stays `IntPtr.Zero` (`CSteamAPIContext.cs:11`, `:96-121`). `SteamFriends.GetFriendCount` then P/Invokes with a null instance pointer (`SteamFriends.cs:27-31`) — a native vtable deref, not a managed exception, so no mod `try/catch` can contain it. Opening the panel is enough to trigger it via `GetFriends`.

**Evidence** as cited; the mod contains zero occurrences of `SteamManager.Initialized`, `IsSteamRunning` or `BLoggedOn` (grep `SteamFriends\.|SteamUser\.|SteamUtils\.` over `src/RoomHub`). The one place the game guards this pattern itself is `NetworkingUtilities.cs:53`.

**Fix** one gate at the top of `RefreshIfDue`, `GetAvatar(CSteamID)` and `InvitePlayer`:

```
private static bool SteamReady()
{
    try { return SteamManager.Initialized; } catch { return false; }
}
```

`SteamManager` is public and global (`SteamManager.cs:8`, `:20-30`) but lives in `SteamManagerAssembly.dll`, which `RoomHub.csproj` does not reference (`:21-40`). Either add that reference, or use `GameHandler.GetService<SteamLobbyHandler>() != null` inside a try/catch as the proxy. `SteamManager.Initialized` itself NREs when `s_EverInitialized && Instance == null` (`:20-30`), hence the catch.

### S2 — High — `Status_Airport` folds into "Main menu"

**Anchor** `FriendRoomsService.cs:237-238` and the missing branch after `:231`; consumers `HistoryRowFactory.cs:265-267`, `:279`, `:292-296`

**Scenario** a friend stands in the airport, not in a Photon room, with no joinable lobby exposed (private lobby, or the room-create window). Table row 4: `players="1"`, `max_players="4"`, no group → the mod labels them **"In main menu"**, and because `canInvite = mode == MainMenu && IsInLiveLobby` (`HistoryRowFactory.cs:279`) it offers an **Invite** button for someone who is already hosting or inside a lobby. If they accept, the game's own guard fires: `OnLobbyDataUpdate` logs "Not joining invite because you're already in a room" and returns (`SteamLobbyHandler.cs:335-339`) — no modal, no state change, so the invitee sees nothing happen.

**Evidence** rows 1 and 4 are byte-identical except for `steam_display`, because the writer seeds `players=1, max_players=4` (`SteamRichPresence.cs:13-14`) and neither the offline nor the in-room branch runs. `Status_Airport` is new in 3.0.1 (`RichPresenceState.cs:4`) and the mod's changelog never mentions it (`Plugin.cs:19-125`).

**Reachability — UNPROVEN.** The host sets `Status_Airport` from the kiosk after the airport scene loads (`AirportCheckInKiosk.cs:43-47`), while `PhotonNetwork.CreateRoom` is issued earlier (`NetworkConnector.cs:146-157` via `SteamLobbyHandler.cs:413`), so normally `InRoom` is already true. A room-create failure, or a drop to `DefaultConnectionState` without a scene change, is the only window.

**Fix** make the token a first-class branch instead of the fallback:

```
if (display == "#Status_MainMenu") mode = FriendGameMode.MainMenu;
else if (!string.IsNullOrEmpty(group)) mode = FriendGameMode.InviteOnly;
else if (maxPlayers == "1") mode = FriendGameMode.Solo;
else if (!string.IsNullOrEmpty(display)) mode = FriendGameMode.Unknown; // airport or transitional
else mode = FriendGameMode.MainMenu;
```

and give the airport its own row text so the UI stops asserting "In main menu" and stops offering Invite (`HistoryRowFactory.cs:279`).

### S3 — High — join guard tests the wrong condition → silent no-op

**Anchor** `RoomJoiner.cs:59-63`

**Scenario** the local player is in a Photon room but **not** in a Steam lobby — exactly the state the game logs as "Failed to fetch a valid Steam lobby code … Steam invites will be broken!" (`NetworkConnector.cs:310-313`, when `TryJoinLobbyDuringSession` fails). `HistoryUi.IsInLiveLobby` (`HistoryUi.cs:38-52`) reads `SteamLobbyHandler.InSteamLobby()` (`SteamLobbyHandler.cs:473-476`), so it is false; the Join button is therefore shown (`HistoryRowFactory.cs:97`, `:274-275`) and `TryJoinLobbyId` proceeds. The game then rejects the join at `SteamLobbyHandler.cs:335-339` with a `Debug.LogError` and no modal, and `m_currentlyFetchingGameVersion` is left set (`:327-331` clears it only for a *different* lobby id). The user clicks Join, the panel hides (`RoomJoiner.cs:73-74`), and nothing else happens.

**Evidence** as cited. The comment at `RoomJoiner.cs:58` claims the guard mirrors the game, but the game's condition is `PhotonNetwork.InRoom`.

**Fix** gate on the same condition the game uses, via the public surface:

```
if (!HistoryUi.IsTitleScene && (Peak.Network.NetCode.Matchmaking.InLobby || PhotonNetwork.InRoom)) return;
```

(`IMatchmakingAPI.InLobby` → `SteamLobbyHandler.InSteamLobby()`, `SteamLobbyAPI.cs:30`; `PhotonNetwork` is already referenced, `RoomHub.csproj:38`.) Report the rejection instead of returning silently.

### S4 — Medium — `FriendRichPresenceUpdate_t` is not a data-arrival signal

**Anchor** `FriendRoomsService.cs:82-84`, `:190-198`, `:284-289`

**Scenario** the design treats the callback as "the only reliable 'friend rich presence data arrived' signal" and invalidates the cache on it. Valve: "This function is rate limited, if you call this too frequently for a particular user then it will just immediately post a callback without requesting new data from the server" (`ISteamFriends#RequestFriendRichPresence`). The callback can therefore fire with no data behind it, the mod rebuilds the whole panel on a signal that proves nothing, and the `Unknown` label can persist indefinitely while the code believes fresh data is flowing.

**Evidence** doc quote; the same section confirms the callback fires on the request path, which is what the mod relies on.

**Fix** keep the callback as a hint, drop the "proof" claim, and only invalidate when the friend's key set actually changed — compare `GetFriendRichPresenceKeyCount` plus a hash of the four values against the cached row.

### S5 — Medium — the "localized `steam_display`" premise is unsupported and decides the whole chain

**Anchor** `FriendRoomsService.cs:223-225` ("steam_display arrives LOCALIZED … so the first check rarely fires"), `Plugin.cs:61-62`

**Scenario** if the token really arrives localized, row 1 is saved only by the `players == "1"` fallback, and rows 1 and 4 become indistinguishable — the mod cannot be correct for the airport. Valve documents the opposite: `steam_display` "Names a rich presence localization token that will be displayed in the viewing user's selected language **in the Steam client UI**" (`ISteamFriends#SetRichPresence`), i.e. the token is resolved by the client UI, and `GetFriendRichPresence` "Returns an empty string if the specified key is not set" — it returns the stored value, not a rendered string.

**Evidence** doc quotes; the decompile writes the token verbatim (`SteamRichPresence.cs:30`).

**Status UNPROVEN** — one log line settles it, for one friend known to be in the main menu and one known to be in a room:

```
Plugin.Log.LogInfo("display=[" + display + "] group=[" + group + "] players=[" + players + "] max=[" + maxPlayers + "] keys=" + keyCount);
```

**Fix** if raw (expected): make the token a branch (S2 fix) and delete the "rarely fires" comment. If localized: stop asserting MainMenu for `players=="1" && max!="1" && group==""` and render "status unknown".

### S6 — Medium — the 1.5 s throttle loses the last event of a burst

**Anchor** `FriendRoomsService.cs:276-281`, `:292-297`

**Scenario** events at t = 0.0, 0.5, 1.0 s: the first notifies, the second and third are dropped, and nothing re-notifies at t ≥ 1.5 s. `_cache` is null so the next `GetFriends()` would be fresh, but the panel is only rebuilt by `Changed` (`HistoryPageController.cs:573` → `OnExternalStateChanged`), so the visible rows keep the pre-burst classification — typically the stale "Main menu"/"Invite only" the mod spent three releases trying to fix — until an unrelated event arrives.

**Evidence** the throttle returns before `Changed?.Invoke()` and stores no pending flag.

**Fix** set a `_notifyPending` flag when the throttle suppresses, and flush it from `Plugin.Update` or `HistoryUi.Tick` once the interval has elapsed.

### S7 — Medium — avatar polling never converges

**Anchor** `SteamAvatarService.cs:63`, `:65-78`; caller `HistoryPageController.cs:344-370`

**Scenario** `TickAvatarRefresh` re-polls every 45 frames for every avatar that returned null, and `GetAvatar` calls `RequestUserInformation(steamId, false)` before checking anything. For a user with no avatar set, `GetMediumFriendAvatar` "Returns 0 if no avatar is set for the user" (doc), the mod caches nothing (`:78`), and the pair repeats forever at ~1.3 Hz per row. Valve warns the call "is a lot slower to download avatars and churns the local cache" (`ISteamFriends#RequestUserInformation`).

**Evidence** doc quotes; `Cache` is only written on success (`:73`, `:98`).

**Fix** distinguish three cases — `imageId == 0` (no avatar, cache a sentinel and stop), `imageId < 0` (not ready, retry with a per-user throttle ≥ 10 s), `imageId > 0` (load and cache) — and call `RequestUserInformation` only on the first attempt per user.

### S8 — Medium — `GetService<T>()` throws, so the null checks are dead

**Anchor** `RoomJoiner.cs:77-82`, `SteamInviteService.cs:45-46`

**Scenario** `GameHandler.GetService<T>()` is `Instance?.m_gameServices[typeof(T)] as T` (`GameHandler.cs:224-227`) — a dictionary indexer, so a missing key throws `KeyNotFoundException`; the `?.` only guards a null `Instance`. `SteamLobbyHandler` is registered only when the `NoSteam` tag is absent (`GameHandler.cs:168-173`), so in that mode `handler == null` never becomes true: the join path throws into its own catch and shows a modal whose body is the raw exception text (`RoomJoiner.cs:86-90`), and the intended "mod disabled" message at `:80` is unreachable.

**Evidence** as cited.

**Fix**

```
SteamLobbyHandler? handler;
try { handler = GameHandler.GetService<SteamLobbyHandler>(); } catch { handler = null; }
```

### S9 — Medium — single-slot `Modal` clobbering

**Anchor** `RoomJoiner.cs:101-104` (used by `LobbyJoinFeedback.cs:68` and `SteamInviteService.cs:59/63/69`)

**Scenario** `Modal.OpenModal` assigns `instance.m_onClose = onClose` and then replaces the header/body children (`Zorro.UI.Runtime/Zorro/UI/Modal/Modal.cs:49-54`, `:64-75`). There is no queue. The mod opens modals from inside Steam callback dispatch (`LobbyJoinFeedback.OnLobbyEnter`) and from button clicks without checking `Modal.IsOpen`. If a game modal with a meaningful `onClose` is up — e.g. the disconnect modal whose `onClose` loads the Title scene (`NetworkConnector.cs:259-266`) — the mod's `ShowModal` overwrites `m_onClose` with null and the player keeps a dead scene. The game itself guards this pattern: `if (Modal.IsOpen) Modal.CloseModal();` (`SteamLobbyHandler.cs:282-286`).

**Evidence** as cited; `RoomJoiner.ShowModal` catches a null `Instance` (`RoomJoiner.cs:106-110`) but never inspects `IsOpen`.

**Fix** close the existing modal the way the game does before opening, or defer the mod's modal until `!Modal.IsOpen`.

### S10 — Low — unguarded `PatchAll`

**Anchor** `Plugin.cs:150-151`

**Scenario** `PatchAll(typeof(Plugin).Assembly)` applies every `[HarmonyPatch]` in the assembly, including a patch on the **private** `SteamLobbyHandler.OnLobbyEnter` (`LobbyRecorder.cs:10`) and on `SteamLobbyHandler.OnLobbyDataUpdate` (`PlayerTracker.cs:256`). If a target method is renamed in a future build, Harmony throws from `Awake` after `Store.Load`, `Loc.Register` and all four service `Init` calls (`:140-148`) but before `sceneLoaded` is hooked (`:153`): the mod half-exists, callbacks fire, no UI is ever created, and the failure surfaces as one log line.

**Evidence** as cited; no try/catch around `:150-153`.

**Fix** wrap in try/catch and log at Error; apply the critical patch classes individually so one stale target cannot disable the others.

### S11 — Low — invited-set key normalisation

**Anchor** `SteamInviteService.cs:52` vs `:28-29`

**Scenario** `_invited.Add(steamIdStr)` stores the caller's raw string and `IsInvited` compares raw strings. Room rows pass `captured.SteamId` (`HistoryRowFactory.cs:189` → `:204`), which comes from persisted history and may carry whitespace or a different format, while friend rows pass `friend.SteamId.ToString()` (`:294`). A mismatch re-enables the button for someone already invited. The set is also not scoped to a lobby id; it only survives because `ResetInvited` runs on every `Hide`/`Discard` (`HistoryPageController.cs:100/104/121`).

**Fix** `_invited.Add(id.ToString())` after the parse, and key the set by `(lobbyId, steamId)`.

### S12 — Low — persona state ignored

**Anchor** `FriendRoomsService.cs:120-131`

**Scenario** `SteamFriends.GetFriendPersonaState` exists in this wrapper (`SteamFriends.cs:45`) and is never called. If Steam keeps a stale `FriendGameInfo_t` for a friend who quit, the row lists them as playing PEAK with a mode derived from rich presence that no longer updates.

**Fix** skip friends whose persona state is `k_EPersonaStateOffline` and whose rich presence key count is 0.

### S13 — Low — refresh bookkeeping

**Anchor** `FriendRoomsService.cs:113-115`, `:69`, `:93-101`

**Scenario** `_nextRefresh` is advanced before the `try`, so a transient failure costs 10 s of no retry, and because `_cache` is only replaced at `:261` the previous list keeps being served. `_lastRpRequest` is never cleared in `Shutdown` and never pruned for friends who leave.

**Fix** set `_nextRefresh` after a successful build and clear both dictionaries in `Shutdown`.

### S14 — Low — game-side `m_isHosting` swallow

**Anchor** `SteamLobbyHandler.cs:398` + `:109-113`; mod impact at `LobbyJoinFeedback.cs:51-52`

**Scenario** `OnLobbyCreated` sets `m_isHosting = true` **before** checking `m_eResult`, so a failed create still arms the flag; the next `LobbyEnter_t` — including the mod's join — is swallowed and `LobbySteamId` is never set. The enter response is `Success`, so `LobbyJoinFeedback` stays silent, and the join never reaches `RequestPhotonRoomID`. The user sees the panel close and nothing happen. **UNPROVEN** how often `k_EResultFail` occurs on create.

**Fix** nothing on the mod side clears the flag; detect it — after a join, if `InSteamLobby()` is still false after ~5 s, show a failure modal instead of staying silent.

### S15 — Info — name shadowing

`Peak.Network.SteamMatchmaking` is `[Obsolete("Moved to Peak.Network.SteamLobbyAPI")]` and derives from `SteamLobbyAPI` (`Peak/Network/SteamMatchmaking.cs:5-8`). Any mod file that adds `using Peak.Network;` while keeping `using Steamworks;` gets an ambiguous `SteamMatchmaking`. Today every Steam call site uses `using Steamworks;` only, so the binding is correct.

---

## 4. Should the mod route through the new game APIs?

**`Peak.Network.NetCode.Matchmaking` — cannot be used for rejoin.** `IMatchmakingAPI` (`Peak/Network/IMatchmakingAPI.cs:5-28`) exposes `CreateLobby`, `LeaveLobby`, `PlayerIsInLobby`, `TrySetLobbyData`, `TryGetLobbyData`, `ConsumePendingJoin` — there is **no join-by-id**. `ConsumePendingJoin(true)` only replays a *pending* invite (`SteamLobbyAPI.cs:109-121`, guarded by `HasPendingInvite` at `:34-52`) and otherwise logs "Attempted to consume join request but we have none pending" (`:111-115`). It is fed only by `GameLobbyJoinRequested_t` (`:134-145`) — the Steam-overlay accept path — so it can never carry a historical lobby id the mod picked from its own list.

**`SteamLobbyHandler.TryJoinLobbyDuringSession(string)` — not a join.** It parses the id, sets `_joiningFromSession = RequestLobbyData(...)` and returns that bool (`SteamLobbyHandler.cs:364-372`). On `LobbyDataUpdate_t` it calls `JoinLobby` (`:322-326`), and on the resulting `LobbyEnter_t` it **returns before** reading `PhotonRegion`/`CurrentScene` (`:122-126`) — no room-id handshake, no `JoinSpecificRoomState`, no scene load. That is exactly how the game uses it: re-attaching the Steam lobby of the Photon room already joined (`NetworkConnector.cs:310`), with the id carried in the Photon room property `csid` (`NetworkConnector.cs:237`). Routing rejoin through it would move the player into a Steam lobby and leave them in the old scene.

**`SteamLobbyHandler.TryJoinLobby(CSteamID)` — the correct entry point, and what the mod already uses** (`RoomJoiner.cs:84`). It performs the whole chain: `RequestLobbyData` → version gate against `PeakVersion` with a mismatch modal (`:332-350`) → `JoinLobby` → region/scene read with up to 5 retries (`:127-157`) → `RequestPhotonRoomID` lobby-chat handshake (`:169-190`, `:246-291`) → scene load. Nothing in the new surface replaces it.

Two improvements are available without changing the path: read `NetCode.Matchmaking.InLobby`/`LobbyId` instead of reaching into `SteamLobbyHandler` for the guard (S3), and optionally subscribe `NetCode.Matchmaking.JoinLobbyFailed` (`SteamLobbyAPI.cs:56`, raised from `OnJoinRoomFailed` at `:147-150`) to cover the Photon half of a failed join that `LobbyEnter_t` cannot see. The game already opens its own modal for that case (`NetworkConnector.cs:269-292`), so only the `JoinGate` cooldown is wanted, not a second popup.

---

## 5. Verified CORRECT (do not "fix")

1. **Callback creation before `SteamAPI.Init` is safe.** `Callback<T>.Create` only touches a managed dictionary (`Steamworks/Callback.cs:30-44`, `:64-77` → `CallbackDispatcher.cs:56-69`). No native call, no dependency on init, so `Plugin.cs:145-147` is fine.
2. **Dispatch is single-threaded on the Unity main thread.** Both pumps call `SteamAPI.RunCallbacks()` from `Update` (`SteamManagerAssembly/SteamManager.cs:117-123`, `SteamCommon/SteamRuntimeManager.cs:84-90`), and `RunFrame` drains the queue, so a second pump in the same frame is a no-op. No mod callback needs locking.
3. **Multiple callbacks per identity are supported and ordered by registration.** `CallbackDispatcher.Register` appends to a list (`:56-69`) and `RunFrame` dispatches a snapshot (`:184-200`). The mod's `LobbyEnter_t`/`LobbyDataUpdate_t` handlers coexist with the game's, and the mod registers first (plugin `Awake` precedes `GameHandler.Awake`, created by `GameBooter.AutoBoot` on the first `sceneLoaded`, `GameBooter.cs:62-68`).
4. **An exception in a mod callback cannot break the game's callback.** `Callback<T>.OnRunCallback` wraps the delegate (`Callback.cs:90-100`), so the `foreach` in `RunFrame` still reaches the game's handler.
5. **Disposal is safe in any order relative to `SteamAPI.Shutdown`.** `Dispose` is idempotent (`Callback.cs:51-62`), `Unregister` is a dictionary remove (`CallbackDispatcher.cs:84-99`) that no-ops after `UnregisterAll` (`:42-54`, `:116-146`), and the finalizer calls `Dispose` (`Callback.cs:46-49`). `Plugin.cs:157-168` is correct.
6. **`LobbyJoinFeedback` complements the game instead of duplicating it.** The game handles only `m_EChatRoomEnterResponse == 1` and otherwise silently sets `LobbySteamId = Nil` (`SteamLobbyHandler.cs:114-119`); the enum value is 1 (`Steamworks/EChatRoomEnterResponse.cs:5`), the struct field is `uint` (`LobbyEnter_t.cs:18`), and `m_bSuccess` really is a `byte` (`LobbyDataUpdate_t.cs:15`). The 2 s throttle (`LobbyJoinFeedback.cs:56-59`) is necessary because the game's retry loop re-enters up to 5 times (`SteamLobbyHandler.cs:140-145`).
7. **`JoinGate`'s refusal to probe is justified.** `RequestLobbyData` on a missing lobby sets `LobbyDataUpdate_t::m_bSuccess` false (Valve doc) and the game opens a modal for any failed update (`SteamLobbyHandler.cs:306-321`). `LobbyJoinFeedback.OnLobbyDataUpdate` correctly limits the cooldown to known rooms (`LobbyJoinFeedback.cs:81-84`).
8. **`InviteUserToLobby` is used correctly and honestly documented.** Semantics match Valve ("true if the invite was successfully sent … NOTE: This call doesn't check if the other user was successfully invited"), the lobby comes from `InSteamLobby(out …)` (`SteamInviteService.cs:46`), and there is no false delivery claim.
9. **Friend enumeration pattern is correct.** `GetFriendCount(k_EFriendFlagImmediate)` + `GetFriendByIndex` + `GetFriendGamePlayed` + `m_gameID.AppID()` (`FriendGameInfo_t.cs:8-16`, `CGameID.cs:57-60`) is the documented sequence, and the hard-coded app id `3527290` matches the one the game restarts for (`SteamManager.cs:68`).
10. **`SteamAvatarService`'s image decode is correct.** `GetImageSize`/`GetImageRGBA` into an RGBA32 buffer, vertical flip (`:116-123`), `LoadRawTextureData` before `Apply(false, true)` (`:126-127`) so readable data is uploaded before the texture becomes non-readable. Old textures are destroyed on replace and on shutdown (`:71-72`, `:37-42`).
11. **`ActivateGameOverlayToUser("friendadd", …)` is a valid dialog** (Valve doc lists `friendadd`) and returns `void` (`SteamFriends.cs:184`), so giving no feedback is correct.
12. **`Plugin.Update` is cheap.** Three static calls, one `SceneManager.GetActiveScene()` struct read, at most two `Input.GetKeyDown` and one `EventSystem` component probe (`HistoryUi.cs:145-165`) per frame. The `Title` scene name is real (`MainMenu.cs:18`, `GameHandler.cs:212`), so the scene split at `Plugin.cs:192-204` is correct, as is attaching `TitleMenuController` to the persistent plugin GameObject (`:229-232`).
13. **No double patching.** `PauseMenuHook.Register` does not patch Harmony (`PauseMenuHook.cs:24-40`), so `PatchAll` is the only patcher.

---

## 6. Needs runtime evidence (do not treat as fact)

| # | question | one-line experiment |
|---|---|---|
| 1 | Is `steam_display` raw (`#Status_MainMenu`) or localized? | log the four keys for a friend known to be in the main menu |
| 2 | Does Steam deliver `steam_player_group` unchanged to an observer? | log `group`/`group_size` for a friend known to be in a 2+ player room |
| 3 | Is table row 4 (airport, no Photon room) reachable? | log keys while hosting, from lobby creation until the airport is walkable |
| 4 | Does a failed `CreateLobby` leave `m_isHosting` armed (S14)? | force a create failure (Steam offline), then join a friend's room |
| 5 | Does a failed `SteamAPI.Init` leave the game playable (S1 precondition)? | launch with Steam closed, reach the title, open the panel |
| 6 | Does `GetFriendPersonaState` lag behind `GetFriendGamePlayed` for quitters (S12)? | compare both for a friend who just closed the game |

---

## 7. Source index

- Mod (audited): `FriendRoomsService.cs` 299 L, `SteamInviteService.cs` 73, `SteamAvatarService.cs` 161, `LobbyJoinFeedback.cs` 103, `RoomJoiner.cs` 114, `Plugin.cs` 234. Read-only context: `JoinGate.cs`, `HistoryUi.cs`, `HistoryRowFactory.cs`, `HistoryPageController.cs`, `LobbyRecorder.cs`, `PlayerTracker.cs`, `PauseMenuHook.cs`, `RoomHub.csproj`.
- Game: `SteamRichPresence.cs`, `RichPresenceState.cs`, `RichPresenceService.cs`, `NoRichPresence.cs`, `MountainProgressHandler.cs`, `AirportCheckInKiosk.cs`, `MainMenuMainPage.cs`, `InRoomState.cs`, `SteamLobbyHandler.cs`, `NetworkConnector.cs`, `GameBooter.cs`, `GameHandler.cs`, `Peak/Network/{NetCode,IMatchmakingAPI,SteamLobbyAPI,NoMatchmaking,NetworkingUtilities,SteamMatchmaking}.cs`, `SteamManagerAssembly/SteamManager.cs`, `SteamCommon/SteamRuntimeManager.cs`, `Zorro.UI.Runtime/Zorro/UI/Modal/Modal.cs`.
- Steamworks.NET: `SteamAPI.cs`, `Callback.cs`, `CallbackDispatcher.cs`, `CSteamAPIContext.cs`, `SteamFriends.cs`, `SteamMatchmaking.cs`, `FriendGameInfo_t.cs`, `LobbyEnter_t.cs`, `LobbyDataUpdate_t.cs`, `FriendRichPresenceUpdate_t.cs`, `AvatarImageLoaded_t.cs`, `PersonaStateChange_t.cs`, `EChatRoomEnterResponse.cs`, `CGameID.cs`.
- Valve docs: `https://partner.steamgames.com/doc/api/ISteamFriends#SetRichPresence` (and `#GetFriendRichPresence`, `#GetFriendRichPresenceKeyCount`, `#RequestFriendRichPresence`, `#RequestUserInformation`, `#GetMediumFriendAvatar`, `#ActivateGameOverlayToUser`); `https://partner.steamgames.com/doc/api/ISteamMatchmaking#InviteUserToLobby` (and `#JoinLobby`, `#RequestLobbyData`); `https://partner.steamgames.com/doc/features/enhancedrichpresence`.
