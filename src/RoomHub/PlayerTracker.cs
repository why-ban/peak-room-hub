using System;
using HarmonyLib;
using Photon.Pun;
using Steamworks;

namespace RoomHub;

/// <summary>
/// Tracks players met while local is in a multiplayer session.
/// Host badge is FROZEN to the room creator (first owner recorded for the lobby), so Steam
/// ownership transfers must never move the badge to the current owner.
/// </summary>
public static class PlayerTracker
{
    private static string? _activeLobbyId;
    private static string? _activeHostSteamId;
    private static Callback<LobbyChatUpdate_t>? _lobbyChatUpdate;
    private static bool _callbacksInited;
    private static bool _roomEventsHooked;
    private static float _lastHostPollUnscaled;
    private static float _lastChatSyncUnscaled;

    public static void SetActiveLobby(string lobbyId, string hostSteamId)
    {
        // Host is frozen per lobby: record it only when entering a NEW lobby. The first owner
        // we see for a lobby is treated as its creator; later Steam ownership transfers must
        // not overwrite it (matches HistoryStore's frozen-host rule).
        bool lobbyChanged = !string.Equals(_activeLobbyId, lobbyId, StringComparison.Ordinal);
        _activeLobbyId = lobbyId;
        if (lobbyChanged)
            _activeHostSteamId = hostSteamId;
        EnsureCallbacks();
    }

    public static void ClearActiveLobby()
    {
        _activeLobbyId = null;
        _activeHostSteamId = null;
    }

    public static void Shutdown()
    {
        try { _lobbyChatUpdate?.Dispose(); } catch { /* ignore */ }
        _lobbyChatUpdate = null;
        _callbacksInited = false;

        if (_roomEventsHooked)
        {
            try { Peak.Network.NetCode.RoomEvents.PlayerEntered -= OnRoomPlayerEntered; } catch { /* ignore */ }
            _roomEventsHooked = false;
        }

        ClearActiveLobby();
    }

    private static void EnsureCallbacks()
    {
        if (_callbacksInited)
            return;
        try
        {
            _lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            _callbacksInited = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"LobbyChatUpdate callback init failed: {ex.Message}");
        }

        EnsureRoomEvents();
    }

    /// <summary>
    /// PEAK dispatches room membership through <c>NetCode.RoomEvents</c>, which is registered by
    /// a static constructor and therefore exists in every scene. The PlayerConnectionLog patch
    /// below only fires while that HUD object is enabled, so players who join after us would go
    /// unrecorded wherever the HUD is absent. Both paths feed the same SteamId-keyed upsert.
    /// </summary>
    private static void EnsureRoomEvents()
    {
        if (_roomEventsHooked)
            return;
        try
        {
            Peak.Network.NetCode.RoomEvents.PlayerEntered += OnRoomPlayerEntered;
            _roomEventsHooked = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"RoomEvents hook failed: {ex.Message}");
        }
    }

    private static void OnRoomPlayerEntered()
    {
        try
        {
            if (!Plugin.ModConfig.Enabled.Value)
                return;
            var players = PhotonNetwork.PlayerList;
            if (players == null)
                return;
            foreach (var p in players)
                RecordPhotonPlayer(p, syncHost: false);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"OnRoomPlayerEntered: {ex.Message}");
        }
    }

    /// <summary>
    /// Steam fires this on member enter/leave/disconnect, so it also covers host transfer.
    /// PEAK's OnLobbyDataUpdate is mostly join version-fetch, not live owner changes.
    /// Throttled: players joining in a burst fire one event per member.
    /// </summary>
    private static void OnLobbyChatUpdate(LobbyChatUpdate_t param)
    {
        try
        {
            if (!Plugin.ModConfig.Enabled.Value)
                return;
            if (string.IsNullOrEmpty(_activeLobbyId))
                return;
            if (!string.Equals(_activeLobbyId, param.m_ulSteamIDLobby.ToString(), StringComparison.Ordinal))
                return;

            float now = UnityEngine.Time.unscaledTime;
            if (now - _lastChatSyncUnscaled < 2f)
                return;
            _lastChatSyncUnscaled = now;
            SyncHostFromSteam();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"OnLobbyChatUpdate: {ex.Message}");
        }
    }

    /// <summary>Slow poll while in lobby, a belt-and-suspenders path if chat updates go quiet.</summary>
    public static void TickHostPoll(float unscaledTime)
    {
        if (!Plugin.ModConfig.Enabled.Value)
            return;
        if (string.IsNullOrEmpty(_activeLobbyId))
            return;
        if (unscaledTime - _lastHostPollUnscaled < 5f)
            return;
        _lastHostPollUnscaled = unscaledTime;
        SyncHostFromSteam();
    }

    /// <summary>Resolve active lobby + sole host from live Steam lobby owner. Returns lobby id or null.</summary>
    public static string? EnsureActiveLobbyFromSteam()
    {
        try
        {
            if (!TryGetLobbyId(out var lid))
                return _activeLobbyId;

            var owner = SteamMatchmaking.GetLobbyOwner(lid);
            string lobbyKey = lid.m_SteamID.ToString();
            string hostSteam = owner != CSteamID.Nil ? owner.m_SteamID.ToString() : (_activeHostSteamId ?? "");
            SetActiveLobby(lobbyKey, hostSteam);
            return lobbyKey;
        }
        catch
        {
            return _activeLobbyId;
        }
    }

    /// <summary>
    /// Keep active-lobby tracking in sync with the live Steam lobby. The stored room host
    /// (the creator) is NEVER rewritten from here, so Steam ownership transfers must not move
    /// the host badge; only <see cref="LobbyRecorder"/> establishes the host on first record.
    /// </summary>
    public static void SyncHostFromSteam()
    {
        try
        {
            if (!TryGetLobbyId(out var lid))
                return;

            var owner = SteamMatchmaking.GetLobbyOwner(lid);
            if (owner == CSteamID.Nil)
                return;

            string lobbyKey = lid.m_SteamID.ToString();
            // SetActiveLobby only records the host when entering a NEW lobby (first owner =
            // creator); same-lobby ownership transfers are intentionally ignored.
            SetActiveLobby(lobbyKey, owner.m_SteamID.ToString());
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"SyncHostFromSteam: {ex.Message}");
        }
    }

    /// <summary>
    /// Lobby id from the game's matchmaking facade, which degrades to NoMatchmaking instead of
    /// throwing when Steam is unavailable (GameHandler.GetService is a dictionary indexer).
    /// </summary>
    private static bool TryGetLobbyId(out CSteamID lobbyId)
    {
        lobbyId = CSteamID.Nil;
        try
        {
            var matchmaking = Peak.Network.NetCode.Matchmaking;
            if (matchmaking == null || !matchmaking.InLobby)
                return false;
            if (!ulong.TryParse(matchmaking.LobbyId, out var raw) || raw == 0)
                return false;
            lobbyId = new CSteamID(raw);
            return lobbyId != CSteamID.Nil;
        }
        catch
        {
            return false;
        }
    }

    internal static void RecordPhotonPlayer(Photon.Realtime.Player? player, bool syncHost = true)
    {
        try
        {
            if (!Plugin.ModConfig.Enabled.Value)
                return;
            if (player == null || player.IsLocal)
                return;

            if (syncHost)
                SyncHostFromSteam();

            string? lobbyId = _activeLobbyId;
            if (string.IsNullOrEmpty(lobbyId))
                lobbyId = EnsureActiveLobbyFromSteam();

            if (string.IsNullOrEmpty(lobbyId))
                return;

            string steamId = player.UserId ?? "";
            if (string.IsNullOrWhiteSpace(steamId) || steamId == "0")
                return;

            var self = SteamAvatarService.LocalSteamIdString();
            if (self != null && string.Equals(self, steamId, StringComparison.Ordinal))
                return;

            string name = player.NickName ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                if (ulong.TryParse(steamId, out var id))
                {
                    try { name = SteamFriends.GetFriendPersonaName(new CSteamID(id)); }
                    catch { name = ""; }
                }
            }

            if (string.IsNullOrWhiteSpace(name))
                name = Loc.T(Loc.UnknownPlayer);

            bool wasHost = !string.IsNullOrEmpty(_activeHostSteamId)
                           && string.Equals(_activeHostSteamId, steamId, StringComparison.Ordinal);

            Plugin.Store.UpsertPlayer(lobbyId, steamId, name, wasHost);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"RecordPhotonPlayer: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(PlayerConnectionLog), nameof(PlayerConnectionLog.OnPlayerEnteredRoom))]
internal static class OnPlayerEnteredRoomPatch
{
    private static void Postfix(Photon.Realtime.Player newPlayer)
    {
        if (newPlayer == null || newPlayer.IsLocal)
            return;
        PlayerTracker.RecordPhotonPlayer(newPlayer);
    }
}

[HarmonyPatch(typeof(NetworkConnector), nameof(NetworkConnector.OnJoinedRoom))]
internal static class OnJoinedRoomPatch
{
    private static void Postfix()
    {
        try
        {
            if (!Plugin.ModConfig.Enabled.Value)
                return;

            var lobbyId = PlayerTracker.EnsureActiveLobbyFromSteam();
            if (!string.IsNullOrEmpty(lobbyId))
                Plugin.Store.TouchRoomJoinedNow(lobbyId, "Photon.OnJoinedRoom");

            if (PhotonNetwork.PlayerList != null)
            {
                // Snapshot without per-player host re-sync
                foreach (var p in PhotonNetwork.PlayerList)
                    PlayerTracker.RecordPhotonPlayer(p, syncHost: false);
            }

            // Final sole-host pass after full snapshot
            PlayerTracker.SyncHostFromSteam();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"OnJoinedRoom snapshot: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(NetworkConnector), nameof(NetworkConnector.OnLeftRoom))]
internal static class OnLeftRoomPatch
{
    private static void Postfix()
    {
        PlayerTracker.ClearActiveLobby();
    }
}

/// <summary>Host may change when lobby data updates, so refresh the sole host badge.</summary>
[HarmonyPatch(typeof(SteamLobbyHandler), "OnLobbyDataUpdate")]
internal static class OnLobbyDataUpdateHostPatch
{
    private static void Postfix()
    {
        try
        {
            if (!Plugin.ModConfig.Enabled.Value)
                return;
            PlayerTracker.SyncHostFromSteam();
        }
        catch { /* ignore */ }
    }
}
