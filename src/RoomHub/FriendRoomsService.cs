using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Steamworks;
using UnityEngine;

namespace RoomHub;

/// <summary>What a friend's PEAK session looks like to us.</summary>
public enum FriendGameMode
{
    /// <summary>Joinable lobby exposed (Friends-only room, or Public).</summary>
    Joinable,

    /// <summary>Playing in a Private "invite only" room where the lobby id is hidden by Steam.</summary>
    InviteOnly,

    /// <summary>Playing PEAK alone (Photon offline): max_players rich presence == "1".</summary>
    Solo,

    /// <summary>In PEAK but NOT in any room (rich presence shows main menu).</summary>
    MainMenu,

    /// <summary>At the airport with no joinable lobby exposed. Never offer an invite here:
    /// the game drops it while the friend is in a room.</summary>
    Airport,

    /// <summary>In PEAK but their game publishes NO rich presence at all (keyCount==0, so
    /// NoSteam mode, privacy hidden, or stale build): cannot tell main menu from in-room.</summary>
    Unknown
}

/// <summary>Where a friend's game says it is (the rich presence steam_display token).</summary>
public enum FriendArea
{
    /// <summary>No usable token (no data, or the client rendered it instead of passing it raw).</summary>
    None,
    MainMenu,
    Airport,
    Shore,
    Tropics,
    Alpine,
    Mesa,
    Caldera,
    Kiln,
    Peak,
    Roots,
    Gloom,
    Citadel,
    Nadir
}

public readonly struct FriendRoomInfo
{
    public readonly ulong SteamId;
    public readonly string Name;
    public readonly ulong LobbyId;
    public readonly FriendGameMode Mode;

    /// <summary>Optional display detail: which of the 13 rich presence states the friend is in.</summary>
    public readonly FriendArea Area;

    public FriendRoomInfo(ulong steamId, string name, ulong lobbyId, FriendGameMode mode)
        : this(steamId, name, lobbyId, mode, FriendArea.None)
    {
    }

    public FriendRoomInfo(ulong steamId, string name, ulong lobbyId, FriendGameMode mode, FriendArea area)
    {
        SteamId = steamId;
        Name = name ?? "";
        LobbyId = lobbyId;
        Mode = mode;
        Area = area;
    }

    /// <summary>A lobby id is only exposed when Steam itself would show "Join Game".</summary>
    public bool IsJoinable => Mode == FriendGameMode.Joinable && LobbyId != 0;

    /// <summary>Only main-menu friends can be pulled into our lobby; airport friends cannot.</summary>
    public bool IsInvitable => Mode == FriendGameMode.MainMenu;
}

/// <summary>
/// Steam friend list + "is friend currently in a PEAK lobby?" detection.
///
/// <c>SteamFriends.GetFriendGamePlayed</c> returns the friend's current game plus
/// <c>FriendGameInfo_t.m_steamIDLobby</c>, the lobby Steam IDs Steam uses for the
/// "Join Game" button. Works for Steam friends without them installing any mod.
/// </summary>
internal static class FriendRoomsService
{
    public const uint PeakAppId = 3527290u;

    private const float RefreshIntervalSec = 10f;
    private const float NotifyMinIntervalSec = 1.5f;
    private const float RpRequestMinIntervalSec = 30f;

    /// <summary>PEAK writes "steam_display" as "#" + RichPresenceState, e.g. "#Status_Airport".</summary>
    private const string DisplayTokenPrefix = "#Status_";

    private static Callback<PersonaStateChange_t>? _personaChanged;
    private static Callback<FriendRichPresenceUpdate_t>? _richPresenceUpdate;
    private static FriendNotifyPump? _pump;
    private static List<FriendRoomInfo>? _cache;
    private static string _signature = "";
    private static float _nextRefresh;
    private static float _lastNotify;
    private static bool _dirty;
    private static bool _notifyPending;
    private static bool _inited;
    private static bool _loggedTokenSeen;
    private static bool _loggedFallbackSeen;

    /// <summary>Per-friend throttle for RequestFriendRichPresence.</summary>
    private static readonly Dictionary<ulong, float> _lastRpRequest = new();

    /// <summary>Raised when friend persona/game state may have changed (page refreshes rows).</summary>
    public static event Action? Changed;

    public static void Init()
    {
        if (_inited)
            return;
        try
        {
            _personaChanged = Callback<PersonaStateChange_t>.Create(OnPersonaChanged);
            _richPresenceUpdate = Callback<FriendRichPresenceUpdate_t>.Create(OnFriendRichPresenceUpdate);
            _inited = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"FriendRoomsService init failed: {ex.Message}");
        }

        StartPump();
    }

    public static void Shutdown()
    {
        try { _personaChanged?.Dispose(); } catch { /* ignore */ }
        _personaChanged = null;
        try { _richPresenceUpdate?.Dispose(); } catch { /* ignore */ }
        _richPresenceUpdate = null;
        if (_pump != null)
        {
            try { UnityEngine.Object.Destroy(_pump); } catch { /* ignore */ }
            _pump = null;
        }
        _inited = false;
        _cache = null;
        _signature = "";
        _dirty = false;
        _notifyPending = false;
        _lastRpRequest.Clear();
    }

    public static IReadOnlyList<FriendRoomInfo> GetFriends()
    {
        RefreshIfDue(force: false);
        return _cache ?? (IReadOnlyList<FriendRoomInfo>)Array.Empty<FriendRoomInfo>();
    }

    /// <summary>Flush of the throttled notify, driven by <see cref="FriendNotifyPump"/>.</summary>
    internal static void Tick()
    {
        if (!_notifyPending)
            return;
        if (Time.unscaledTime - _lastNotify < NotifyMinIntervalSec)
            return;
        NotifyChanged();
    }

    private static void StartPump()
    {
        try
        {
            if (_pump != null || Plugin.Instance == null)
                return;
            var host = Plugin.Instance.gameObject;
            _pump = host.GetComponent<FriendNotifyPump>() ?? host.AddComponent<FriendNotifyPump>();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"FriendRoomsService pump failed: {ex.Message}");
        }
    }

    private static void RefreshIfDue(bool force)
    {
        if (!_inited || !SteamGate.Ready())
            return;

        float now = Time.unscaledTime;
        if (!force && !_dirty && _cache != null && now < _nextRefresh)
            return;

        var list = new List<FriendRoomInfo>();
        var present = new HashSet<ulong>();
        var sig = new StringBuilder();

        try
        {
            int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < count; i++)
            {
                var sid = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                if (sid == CSteamID.Nil || sid.m_SteamID == 0)
                    continue;
                if (!SteamFriends.GetFriendGamePlayed(sid, out var info))
                    continue;
                if (info.m_gameID.AppID().m_AppId != PeakAppId)
                    continue;

                int keyCount = 0;
                try { keyCount = SteamFriends.GetFriendRichPresenceKeyCount(sid); } catch { /* ignore */ }

                // Steam keeps stale game info for a while after a friend quits so an
                // offline friend who publishes nothing is a leftover, not a session
                if (keyCount == 0 && PersonaState(sid) == EPersonaState.k_EPersonaStateOffline)
                    continue;

                ulong lobbyId = 0;
                if (info.m_steamIDLobby.IsValid() && info.m_steamIDLobby.m_SteamID != 0)
                    lobbyId = info.m_steamIDLobby.m_SteamID;

                string display = "";
                string group = "";
                string players = "";
                string maxPlayers = "";
                if (keyCount > 0)
                    ReadPresence(sid, out display, out group, out players, out maxPlayers);

                var mode = Classify(lobbyId, keyCount, display, group, players, maxPlayers, out var area);

                RequestRichPresence(sid, now);

                string name = "";
                try { name = SteamFriends.GetFriendPersonaName(sid) ?? ""; } catch { /* ignore */ }

                present.Add(sid.m_SteamID);
                list.Add(new FriendRoomInfo(sid.m_SteamID, name, lobbyId, mode, area));
                sig.Append(sid.m_SteamID).Append(':')
                   .Append(lobbyId).Append(':')
                   .Append(keyCount).Append(':')
                   .Append((int)mode).Append(':')
                   .Append((int)area).Append(':')
                   .Append(name).Append('\n');
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"FriendRoomsService refresh failed: {ex.Message}");
            return;
        }

        PruneRpRequests(present);

        string newSignature = sig.ToString();
        bool changed = _cache == null || !string.Equals(newSignature, _signature, StringComparison.Ordinal);

        _cache = list
            .OrderByDescending(f => f.LobbyId != 0)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _signature = newSignature;
        _nextRefresh = Time.unscaledTime + RefreshIntervalSec;
        _dirty = false;

        if (changed)
            NotifyChanged();
    }

    /// <summary>
    /// Token first: PEAK writes the raw "#Status_*" token, so it names the state outright.
    /// Only when the token is missing or unrecognised fall back to the numeric keys.
    /// </summary>
    private static FriendGameMode Classify(
        ulong lobbyId, int keyCount, string display, string group, string players, string maxPlayers,
        out FriendArea area)
    {
        area = FriendArea.None;

        if (lobbyId != 0)
        {
            if (TryParseArea(display, out var joinableArea))
                area = joinableArea;
            return FriendGameMode.Joinable;
        }

        if (keyCount == 0)
            return FriendGameMode.Unknown;

        if (TryParseArea(display, out var tokenArea))
        {
            area = tokenArea;
            if (!_loggedTokenSeen)
            {
                _loggedTokenSeen = true;
                Plugin.Log.LogInfo($"Friend rich presence token is raw: display=[{display}]");
            }

            switch (tokenArea)
            {
                case FriendArea.MainMenu:
                    return FriendGameMode.MainMenu;
                case FriendArea.Airport:
                    return FriendGameMode.Airport;
                default:
                    // a level scene implies a Photon room, only the offline solo branch lacks a group
                    if (!string.IsNullOrEmpty(group))
                        return FriendGameMode.InviteOnly;
                    return IsOne(maxPlayers) ? FriendGameMode.Solo : FriendGameMode.InviteOnly;
            }
        }

        if (!_loggedFallbackSeen)
        {
            _loggedFallbackSeen = true;
            Plugin.Log.LogInfo(
                $"Friend rich presence token unusable, falling back to keys: display=[{display}] " +
                $"group=[{group}] players=[{players}] max=[{maxPlayers}] keys={keyCount}");
        }

        // In-room first: being in a room always writes the room name into the group key
        if (!string.IsNullOrEmpty(group))
            return FriendGameMode.InviteOnly;
        if (IsOne(maxPlayers))
            return FriendGameMode.Solo;
        if (IsOne(players))
            return FriendGameMode.MainMenu;
        return string.IsNullOrEmpty(maxPlayers) ? FriendGameMode.MainMenu : FriendGameMode.InviteOnly;
    }

    private static bool TryParseArea(string display, out FriendArea area)
    {
        area = FriendArea.None;
        if (string.IsNullOrEmpty(display) || !display.StartsWith(DisplayTokenPrefix, StringComparison.Ordinal))
            return false;

        switch (display.Substring(DisplayTokenPrefix.Length))
        {
            case "MainMenu": area = FriendArea.MainMenu; return true;
            case "Airport": area = FriendArea.Airport; return true;
            case "Shore": area = FriendArea.Shore; return true;
            case "Tropics": area = FriendArea.Tropics; return true;
            case "Alpine": area = FriendArea.Alpine; return true;
            case "Mesa": area = FriendArea.Mesa; return true;
            case "Caldera": area = FriendArea.Caldera; return true;
            case "Kiln": area = FriendArea.Kiln; return true;
            case "Peak": area = FriendArea.Peak; return true;
            case "Roots": area = FriendArea.Roots; return true;
            case "Gloom": area = FriendArea.Gloom; return true;
            case "Citadel": area = FriendArea.Citadel; return true;
            case "Nadir": area = FriendArea.Nadir; return true;
            default: return false;
        }
    }

    private static void ReadPresence(CSteamID sid, out string display, out string group, out string players, out string maxPlayers)
    {
        display = "";
        group = "";
        players = "";
        maxPlayers = "";
        try
        {
            display = SteamFriends.GetFriendRichPresence(sid, "steam_display") ?? "";
            group = SteamFriends.GetFriendRichPresence(sid, "steam_player_group") ?? "";
            players = SteamFriends.GetFriendRichPresence(sid, "players") ?? "";
            maxPlayers = SteamFriends.GetFriendRichPresence(sid, "max_players") ?? "";
        }
        catch
        {
            // no readable rich presence: the key count alone still classifies the row
        }
    }

    private static void RequestRichPresence(CSteamID sid, float now)
    {
        try
        {
            if (_lastRpRequest.TryGetValue(sid.m_SteamID, out float last) && now - last < RpRequestMinIntervalSec)
                return;
            _lastRpRequest[sid.m_SteamID] = now;
            SteamFriends.RequestFriendRichPresence(sid);
        }
        catch
        {
            // best effort, the push arrives on its own schedule
        }
    }

    private static void PruneRpRequests(HashSet<ulong> present)
    {
        if (_lastRpRequest.Count == 0)
            return;

        List<ulong>? gone = null;
        foreach (var id in _lastRpRequest.Keys)
        {
            if (present.Contains(id))
                continue;
            (gone ??= new List<ulong>()).Add(id);
        }

        if (gone == null)
            return;
        foreach (var id in gone)
            _lastRpRequest.Remove(id);
    }

    private static EPersonaState PersonaState(CSteamID sid)
    {
        try { return SteamFriends.GetFriendPersonaState(sid); }
        catch { return EPersonaState.k_EPersonaStateOnline; }
    }

    private static bool IsOne(string value) => string.Equals(value, "1", StringComparison.Ordinal);

    private static void OnPersonaChanged(PersonaStateChange_t cb) => MarkDirty();

    private static void OnFriendRichPresenceUpdate(FriendRichPresenceUpdate_t cb) => MarkDirty();

    /// <summary>
    /// The callback only hints that something may have moved: Steam posts it immediately for
    /// rate limited requests, so it is not proof that data arrived. Re-read and let the
    /// signature decide whether anything really changed.
    /// </summary>
    private static void MarkDirty()
    {
        _dirty = true;
        RefreshIfDue(force: false);
    }

    private static void NotifyChanged()
    {
        float now = Time.unscaledTime;
        if (now - _lastNotify < NotifyMinIntervalSec)
        {
            _notifyPending = true;
            return;
        }
        _lastNotify = now;
        _notifyPending = false;
        try { Changed?.Invoke(); } catch { /* ignore */ }
    }
}

/// <summary>Flushes the throttled notify so a burst's last event still reaches the panel.</summary>
internal sealed class FriendNotifyPump : MonoBehaviour
{
    private void Update() => FriendRoomsService.Tick();
}
