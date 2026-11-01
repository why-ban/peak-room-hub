# AUDIT-DATA — data layer correctness audit

Scope: `src/RoomHub/HistoryStore.cs`, `LobbyRecorder.cs`, `PlayerTracker.cs`, `JoinGate.cs`, `Models.cs`, `PluginConfig.cs`
Baseline: game v3.0.1, `D:\zhuanban\youhua\decompiled-latest`; plugin build 2.7.0 (0 errors/0 warnings).
Method: line-by-line read of all six files, plus every game symbol they touch verified against the decompile. Nothing below is inferred from the repo's own docs or from the old 2.x dump.

Severity key: **HIGH** = data loss or a documented invariant that the code can break · **MEDIUM** = a user-visible wrong result · **LOW** = wasted work, log noise, or a latent hazard.

---

## 0. Game-side ground truth this audit rests on

| # | Fact | Evidence |
|---|---|---|
| G1 | `SteamLobbyHandler.OnLobbyEnter` is a **private instance method registered as a Steam callback in the service constructor**. | `Assembly-CSharp/SteamLobbyHandler.cs:54` `Callback<LobbyEnter_t>.Create(OnLobbyEnter)`; body at `:107` |
| G2 | `OnLobbyEnter` has three early returns that still let a Harmony **postfix** run: hosting (`:109-113`), failed enter (`:114-119`), mid-session join (`:122-126`). | `SteamLobbyHandler.cs:107-158` |
| G3 | A failed `LobbyDataUpdate_t` **always** opens the game's own modal unless `_joiningFromSession`. | `SteamLobbyHandler.cs:308-321` |
| G4 | `InSteamLobby(out CSteamID)` exists and is the only lobby-state accessor. | `SteamLobbyHandler.cs:478-482` |
| G5 | PEAK 3.0.1 sets **exactly four** lobby-data keys: `PeakVersion`, `PhotonRegion`, `CurrentScene`, `RunId`. | `SteamLobbyHandler.cs:409,438,447`; `Assembly-CSharp/RunManager.cs:222` |
| G6 | Photon `Player.UserId` **is the SteamID64 decimal string** for the normal Steam path; `PublishUserId = true`. | `Photon/Portningsbolaget/Photon/SteamPhotonAuthManager.cs:31-35`; `Assembly-CSharp/Peak/Network/NetworkingUtilities.cs:85` |
| G7 | `MAX_PLAYERS` is 4. | `NetworkingUtilities.cs:25` |
| G8 | `SteamAPI.RunCallbacks()` is pumped from `Update()` — every Steam callback and every Harmony postfix on a Steam callback runs on the **main thread**. | `SteamManagerAssembly/SteamManager.cs:117-123`; `SteamCommon/SteamRuntimeManager.cs:84-90` |
| G9 | `PlayerConnectionLog` is a `MonoBehaviourPunCallbacks`; PUN only dispatches to it while an instance is enabled (registration is in `OnEnable`). | `Assembly-CSharp/PlayerConnectionLog.cs:13`; `PhotonUnityNetworking/Photon/Pun/MonoBehaviourPunCallbacks.cs:9-17` |
| G10 | A second, always-present `OnPlayerEnteredRoom` exists on the network root object; PEAK also has a non-MonoBehaviour hub `NetCode.RoomEvents`. | `NetworkConnector.cs:328`; `Peak/Network/PhotonRoomEvents.cs:22-25`, `Peak/Network/NetCode.cs:32` |
| G11 | `GameHandler.GetService<T>()` is a **dictionary indexer** — it throws `KeyNotFoundException` when the service is not registered, it does not return null. | `Assembly-CSharp/GameHandler.cs:224-227` |
| G12 | The game rewrites `NickName` (sanitized, max 32) **inside** `OnPlayerEnteredRoom`, so a postfix reads the sanitized value. | `PlayerConnectionLog.cs:62-67` |
| G13 | `OnDisconnected` does **not** leave the Steam lobby (only `HandleConnectionState` for Disconnecting/ChangeRoom does). | `NetworkConnector.cs:252-267` vs `:173-180` |

---

## 1. Findings

### D-01 — HIGH — A transient read failure of `history.tsv` destroys the whole file
`HistoryStore.cs:372-376` (`catch { … _rooms = new List<RoomRecord>(); }`), `:381-384` (`Flush`), `:499-547` (`SaveUnlocked`), `Plugin.cs:159` and `:172`.

**Scenario.** The user opens `BepInEx/config/RoomHub/history.tsv` in an editor that holds a `FileShare.None` lock (Excel/VS Code with a lock, or an AV/cloud-sync scan at launch). `File.ReadAllLines` at `:388` throws, `Load` swallows it and sets `_rooms` to an empty list. Two independent paths then write that empty list over the good file:
* join any room → `UpsertRoom` → `SaveUnlocked` (`:160`) writes a file containing only that one room;
* or simply **quit** → `OnApplicationQuit` → `Flush` (`:383`) → `SaveUnlocked` writes a header-only file. No room join is required.

**Why it is a real defect.** The code cannot distinguish "no history" from "history unreadable", and `Flush` has no dirty check, so a read-only failure becomes a destructive write. There is no `.bak` anywhere in the write path.

**Minimal fix.** Add `private bool _loadFailed;` set it in the `catch`, copy the unreadable file to `history.tsv.bak-<utc>` before continuing, and make both `SaveUnlocked` and `Flush` return early while `_loadFailed` is set. Independently, `Flush()` should be `if (!_dirty) return;`.

---

### D-02 — HIGH — The "frozen host" invariant is enforced by callers only; the store can rewrite the host
`HistoryStore.cs:259-260` → `:294-302` (writes `room.HostSteamId = hostSteamId` at `:299`), `PlayerTracker.cs:191-194`, `:143`, `:114`.

**Scenario.** `ApplySoleHostUnlocked` is reached from three places: `SetCurrentHost` (intended), `LoadFileUnlocked` (normalization), and **`UpsertPlayer(…, wasHost: true)`**. `PlayerTracker.RecordPhotonPlayer` computes `wasHost` from `_activeHostSteamId` (`:191-192`), and `_activeHostSteamId` is seeded from the **live Steam owner** by `SetActiveLobby` whenever the tracker's first sighting of a lobby comes from `EnsureActiveLobbyFromSteam` (`:114`) or `SyncHostFromSteam` (`:143`) rather than from `LobbyRecorder`'s frozen `recordedHost` (`LobbyRecorder.cs:75`). If Steam ownership had already transferred before that first sighting, the next Photon player snapshot stamps the *new* owner as host: `room.HostSteamId` is overwritten and the creator's badge is cleared (`:296-297`).

**Why it is a real defect.** Three separate comment blocks promise the host never moves (`HistoryStore.cs:138-140`, `PlayerTracker.cs:9-11`, `:24-26`), yet the store's own public API accepts a rewrite from any caller that passes `wasHost: true`. A documented invariant that only the callers respect is not an invariant.

**Minimal fix.** In `ApplySoleHostUnlocked`, assign `room.HostSteamId` only when it is empty; in `UpsertPlayer`, derive `wasHost` from `room.HostSteamId` instead of trusting the argument.

**Needs runtime evidence.** Which of `LobbyRecorder` / `EnsureActiveLobbyFromSteam` wins the first sighting in real sessions. The invariant hole is provable from code; the frequency is not.

---

### D-03 — MEDIUM — `lobby_Name` and `host_Name` are keys the game never writes
`LobbyRecorder.cs:60`, `:71`; consumers `HistoryStore.cs:141-142,153,419,518` and `HistoryRowFactory.cs:74-75`.

**Scenario.** `SteamMatchmaking.GetLobbyData(lobbyId, "lobby_Name")` returns `""` for every room ever recorded, because PEAK sets only the four keys in G5. Therefore `RoomRecord.LobbyName` is dead: the row's name line (`HistoryRowFactory.cs:74-75`) never renders, and `Loc.LobbyName` ("Name: {0}") is unreachable UI. `host_Name` likewise always falls through to `SteamFriends.GetFriendPersonaName(owner)` (`LobbyRecorder.cs:63`).

**Second half of the same defect.** For a host who is not a Steam friend, `GetFriendPersonaName` returns `""` until Steam delivers the persona (it issues the request asynchronously). That `""` becomes `Loc.UnknownHost` (`:67-68`), and `LobbyRecorder.cs:53-57` **deliberately keeps the stored name on every later rejoin**, so "Unknown Host" is permanent for that room.

**Minimal fix.** Drop the two dead `GetLobbyData` reads (or have the mod publish its own key). Re-resolve the host name on a later rejoin when the stored value equals `Loc.UnknownHost`.

**Needs runtime evidence.** How often `GetFriendPersonaName` returns `""` at `OnLobbyEnter` time in practice.

---

### D-04 — MEDIUM — `JoinedAt` is refreshed on Steam-lobby entry, not on a confirmed room entry
`LobbyRecorder.cs:74`; `HistoryStore.cs:136-137`.

**Scenario.** `UpsertRoom` sets `JoinedAtUtcTicks = now` on every successful `LobbyEnter`. A host who disconnects keeps their Steam lobby open (G13: `OnDisconnected` never calls `LeaveLobby`), so the lobby still accepts members while the Photon room is gone. Clicking Join on a two-day-old row therefore rewrites that row to "just now", moves it to the top of the list (`HistoryStore.cs:143-144`), and re-arms the 2.5 h age gate (`JoinGate.cs:46-51`) even though nothing was joined.

**Why it is a real defect.** The comment at `LobbyRecorder.cs:72-73` acknowledges that `UpsertRoom` always sets `JoinedAt`, and defers the "real" timestamp to `TouchRoomJoinedNow` (`HistoryStore.cs:181-216`, called from the Photon `OnJoinedRoom` postfix at `PlayerTracker.cs:227`). But `UpsertRoom` already moved the clock, so the deferred touch is redundant and the failure case is not covered.

**Minimal fix.** Make `UpsertRoom` metadata-only for an existing room (leave `JoinedAtUtcTicks` untouched) and let `TouchRoomJoinedNow` be the sole owner of that field.

---

### D-05 — MEDIUM — Out-of-range ticks throw inside row construction
`Models.cs:17` (`new DateTime(JoinedAtUtcTicks, DateTimeKind.Utc)`), `:32`, `:38`, `:54`; load validation is only `ticks > 0` at `HistoryStore.cs:411` and `:442`.

**Scenario.** A hand-edited or bit-rotted `history.tsv` containing `R\t<id>\t…\t9223372036854775807` parses as a `long` and is accepted. `new DateTime(ticks, Utc)` then throws `ArgumentOutOfRangeException` for any value above `3155378975999999999`, from `HistoryRowFactory.cs:49` (`Loc.Relative(captured.JoinedAtUtc)`), `HistoryRowFactory.cs:152/185`, and `JoinGate.cs:48`. The exception escapes row construction and aborts the page rebuild, so one bad line takes out the entire list.

**Minimal fix.** Validate `ticks > 0 && ticks <= DateTime.MaxValue.Ticks` at `HistoryStore.cs:411`/`:428-429`/`:442`, and have the `Models.cs` getters return `DateTime.MinValue` when out of range.

---

### D-06 — MEDIUM — A future-dated row never expires, always sorts first, and stays joinable forever
`HistoryStore.cs:488` (`r.JoinedAtUtcTicks >= cutoff`), `:491`, `:198`; `JoinGate.cs:49`.

**Scenario.** A clock rollback (user sets the clock back, or a laptop resumes with a stale RTC) or a corrupt future value produces `JoinedAtUtcTicks > now`. Such a row is `>= cutoff` on every prune, so it is never removed; it sorts first forever (`:491`); `TouchRoomJoinedNow` refuses to move it backwards (`:198`); and `JoinGate` computes a negative age, which fails the `> MaxLobbyAgeHours` test, so Join stays visible for a room that is long dead.

**Minimal fix.** On load, clamp `ticks > DateTime.UtcNow.Ticks` to `DateTime.UtcNow.Ticks` (one `Math.Min` per row). In `JoinGate`, treat a negative age as "unknown" and apply the cooldown rule instead of allowing the join.

---

### D-07 — MEDIUM — `PatchAll` is unguarded; one missing target silently disables every patch
`Plugin.cs:150-151`; string-named targets at `LobbyRecorder.cs:10` (`"OnLobbyEnter"`) and `PlayerTracker.cs:256` (`"OnLobbyDataUpdate"`).

**Scenario.** Harmony resolves string targets at runtime. A rename in a future game build makes `PatchAll` throw `ArgumentException`; the exception leaves `Awake`, so `SceneManager.sceneLoaded` (`:153`) and the log line never run, and every patch class after the failing one is skipped. The user sees a mod that loads (the `Store` is already constructed at `:141`) but records and renders nothing. Both targets exist today — `SteamLobbyHandler.cs:107` and `:306` — so this is a robustness defect, not a current break.

**Minimal fix.** Patch per class in a loop with a try/catch each, logging which class failed.

---

### D-08 — MEDIUM — The player-entry hook is a UI component, not a network callback
`PlayerTracker.cs:203-212` patches `PlayerConnectionLog.OnPlayerEnteredRoom`.

**Scenario.** Per G9, PUN dispatches to `PlayerConnectionLog` only while an instance is enabled in the live scene. In any scene without that HUD component, no entry callback fires, and the only remaining record path is the one-shot `PhotonNetwork.PlayerList` snapshot at `PlayerTracker.cs:229-234` — i.e. players who join after you are never recorded.

**Why it is a real defect.** A strictly more reliable target exists in the same build: `NetworkConnector.OnPlayerEnteredRoom` (G10) on the network root object, or `NetCode.RoomEvents.PlayerEntered`. `PlayerConnectionLog` is the on-screen "joined the expedition" toast — a presentation object.

**Needs runtime evidence.** Whether a `PlayerConnectionLog` instance exists in the Airport and gameplay scenes. Nothing in the C# dump references the type (only its own definition), so this is a scene/prefab question.

**Minimal fix.** Patch `NetworkConnector.OnPlayerEnteredRoom` instead, or subscribe to `NetCode.RoomEvents.PlayerEntered` and diff `PhotonNetwork.PlayerList`.

---

### D-09 — MEDIUM — Lowering a history setting in-game permanently deletes rows
`PluginConfig.cs:82-96`; `HistoryStore.cs:480-497` (`saveIfChanged: true` at `:495-496`), read from `GetRooms` (`:50`) and `SearchPlayers` (`:86`).

**Scenario.** `MaxRecentRooms` and `HistoryExpireHours` are read on **every** prune, including the prune performed by the read paths. A user who lowers either value in PEAKLib's settings menu loses the excess rows at the next page open — the prune writes immediately, with no confirmation, no undo and no backup.

**Minimal fix.** Do not persist a prune triggered from a read path (pass `saveIfChanged: false` at `:50`/`:86`), and write a `.bak` before any rewrite that reduces the row count.

---

### D-10 — LOW — A failed `Directory.CreateDirectory` in the constructor aborts plugin init
`HistoryStore.cs:35-36`, constructed at `Plugin.cs:141` inside `Awake`.

A read-only or permission-denied `BepInEx/config` throws before `Loc.Register()`, `_harmony.PatchAll` and the scene hook at `Plugin.cs:144-153`. Fix: wrap the directory creation and degrade to an in-memory store.

---

### D-11 — LOW — A failing save retries every 0.75 s forever and leaves a `.tmp` behind
`HistoryStore.cs:533-546`, `:324-334`.

`_dirty` is cleared only after a successful replace (`:541`), so a locked, read-only or non-NTFS `history.tsv` produces one `History save failed` error every 0.75 s for the rest of the session, and `history.tsv.tmp` is never cleaned up. Fix: record a failure timestamp and back off to ~30 s; delete the tmp in the catch.

---

### D-12 — LOW — The join cooldown is not wall-clock
`JoinGate.cs:29`, `:58`, `:74`.

`Time.unscaledTime` resets to ~0 on process start, so the 600 s cooldown is lost across a restart (the Join button returns for a room that just failed), and it stops advancing while the app is backgrounded. Fix: store `DateTime.UtcNow.Ticks` and compare against `DateTime.UtcNow`.

---

### D-13 — LOW — `TickHostPoll` ignores `Enabled`
`PlayerTracker.cs:92-100` vs the guards at `:72`, `:155`, `:221`, `:263`.

With the mod disabled the 5 s poll still runs, calls `GameHandler.GetService<SteamLobbyHandler>()` and `GetLobbyOwner`, and — via `SetActiveLobby` → `EnsureCallbacks` — registers a Steam callback. Fix: add the same `Enabled` guard.

---

### D-14 — LOW — The `"Bing Bong"` sentinel mislabels a real player
`PlayerTracker.cs:188`; the string originates as a sentinel in `PlayerConnectionLog.cs:87`.

A player whose Steam persona is literally "Bing Bong" is stored as `Loc.UnknownPlayer` and is unreachable by name search. The empty-name fallback at `:179-186` already covers the real case. Fix: drop the sentinel.

---

## 2. Checked and found CORRECT — do not re-litigate

1. **Identity keying is right.** `player.UserId` really is the SteamID64 string (G6), `PublishUserId` is on, and `CSteamID.ToString()` and `m_SteamID.ToString()` produce the same digits, so `PlayerTracker.cs:170` and `LobbyRecorder.cs:31-32` produce comparable keys.
2. **Escaping round-trips.** `Escape` (`HistoryStore.cs:582-586`) escapes the backslash first, then `\t/\n/\r`; `Unescape` (`:588-610`) is its exact inverse and degrades gracefully on unknown escapes and on a trailing lone backslash.
3. **Corrupt-line tolerance is adequate.** Short rows, non-numeric ticks, `#` comments, a missing header, a `P` row before its `R` row, and legacy 4-column V2 rows are all skipped or parsed without aborting the load (`HistoryStore.cs:399-453`). A truncated `R` row cannot be mistaken for a V2 row — the `:439` guard excludes `R`/`P` explicitly.
4. **The write is atomic on the happy path.** Temp file + `File.Replace` (`:533-539`), `File.Move` for a first write. A crash mid-write leaves the old file intact.
5. **Sole-host normalization on load works** (`:456-475`), including the "more than one legacy badge" case at `:468-473`.
6. **Rejoin dedup is correct.** Rooms key on `LobbyId` (`:132`, `:489-490`); players key on `SteamId` *within* the room, `FirstSeenUtcTicks` is preserved and `LastSeenUtcTicks` updated (`:238-256`).
7. **Search is deliberately name-only** (`:95-99`) — a SteamId substring match would make a one-digit query match everyone.
8. **A zero/negative config value cannot wipe history.** Both `MaxRecentRooms` and `HistoryExpireHours` are guarded by `Math.Max(1, …)` at `:482-483`, so `Take(0)` is unreachable.
9. **The two clocks are never mixed.** UTC ticks live only in the record model; `Time.unscaledTime` lives only in the deferred-save throttle (`:336-341`, `:330`) and in `JoinGate`. No comparison crosses the two domains.
10. **No threading hazard.** Every Steam callback and Harmony postfix runs from `Update` (G8), so `JoinGate._cooldowns` (`JoinGate.cs:36`) and the `Time.unscaledTime` reads in `PlayerTracker` need no lock. The store's own lock is held on every public entry point and every exit returns a clone (`CloneRoom` at `:549-572`), so no caller can alias `_rooms`.
11. **The `JoinGate` no-probe rationale is correct.** `SteamLobbyHandler.OnLobbyDataUpdate` really does open the game's modal on any failed update (G3), which is exactly why probing dead rooms was removed. The comment at `JoinGate.cs:18-22` is accurate, not folklore.
12. **The 2 s chat-update throttle is safe.** It is leading-edge only (`PlayerTracker.cs:80-83`), but the 5 s poll at `:96-98` covers the dropped trailing event.
13. **The 20-player cap is unreachable in practice** (`PluginConfig.cs:23` vs G7: `MAX_PLAYERS = 4`). The unstable sort at `HistoryStore.cs:266` can therefore never decide which player is kept. Leave it alone.
14. **A postfix on `OnPlayerEnteredRoom` reads the sanitized nickname**, because the game rewrites `NickName` before returning (G12). Steam persona names are ≤32 chars, so the game's truncation never loses data.
15. **`OnJoinedRoom` really does fire after `TryJoinLobbyDuringSession`** (`NetworkConnector.cs:310-315`), so the mod's postfix ordering relative to the game's own lobby fetch is correct.
16. **`Flush` on both `OnDestroy` and `OnApplicationQuit`** (`Plugin.cs:159,172`) writes twice at quit. Harmless — but see D-01, where the unconditional write is the problem.
17. **`TickDeferredSave` runs before the `Enabled` check** (`Plugin.cs:178` vs `:181`), so pending data still flushes with the mod disabled. Correct.
18. **Config ranges are sane and cannot be bypassed into an unsafe state**: `MaxRecentRooms` 1–50, `HistoryExpireHours` 1–720, button positions ±2000 (`PluginConfig.cs:53,61,88,96`).

## 3. Dropped — cannot happen in this game

* **Non-Steam identity.** With the `NoSteam` tag, Photon `UserId` becomes a random uint (`NetworkingUtilities.cs:49-68`), which would break the SteamID keying in `PlayerTracker.cs:170-186`. The shipped Steam build always takes the `SteamUser.GetSteamID()` branch; not worth defensive code.
* **`_cooldowns` unbounded growth** (`JoinGate.cs:36`). Bounded by real failed joins per session; expired entries are dropped on lookup (`:60`).
* **`GetRooms`/`SearchPlayers` cost.** Called on every page rebuild and keystroke (`HistoryPageController.cs:916,933`) and allocates, but with ≤50 rooms × ≤20 players the ceiling is ~1000 rows. Not a defect.
* **`File.Replace` durability.** No `Flush(true)` before the replace, so a power cut can lose the last write — but the previous file survives, which is the property that matters here.
