using System;
using Steamworks;

namespace RoomHub;

/// <summary>
/// Surfaces Steam lobby join failures that the game swallows silently.
///
/// The official <c>SteamLobbyHandler.OnLobbyEnter</c> only handles the success case
/// (<c>m_EChatRoomEnterResponse == Success</c>); every other response just sets
/// <c>LobbySteamId = Nil</c> with no log and no modal, so joining a FULL room (or a
/// deleted / access-denied room) looks like "nothing happened". We subscribe to the same
/// callback and show a reason modal instead.
/// </summary>
internal static class LobbyJoinFeedback
{
    private static Callback<LobbyEnter_t>? _callback;
    private static Callback<LobbyDataUpdate_t>? _lobbyDataCallback;
    private static bool _inited;
    private static float _lastShownUnscaled;
    private const float MinShowIntervalSec = 2f;

    public static void Init()
    {
        if (_inited)
            return;
        try
        {
            _callback = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            _lobbyDataCallback = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
            _inited = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"LobbyJoinFeedback init failed: {ex.Message}");
        }
    }

    public static void Shutdown()
    {
        try { _callback?.Dispose(); } catch { /* ignore */ }
        _callback = null;
        try { _lobbyDataCallback?.Dispose(); } catch { /* ignore */ }
        _lobbyDataCallback = null;
        _inited = false;
    }

    private static void OnLobbyEnter(LobbyEnter_t param)
    {
        var response = (EChatRoomEnterResponse)param.m_EChatRoomEnterResponse;
        if (response == EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            return;

        // Throttle: a rejected auto-join (e.g. Steam friend invite to a full room) and a
        // manual join can both land here within the same second, so one modal is enough.
        float now = UnityEngine.Time.unscaledTime;
        if (now - _lastShownUnscaled < MinShowIntervalSec)
            return;
        _lastShownUnscaled = now;

        string reason = Describe(response);
        Plugin.Log.LogInfo(
            $"Join lobby failed lobby={param.m_ulSteamIDLobby} " +
            $"code={(int)param.m_EChatRoomEnterResponse} ({reason})");
        // Hide the Join button for this lobby immediately and stop re-offering it for a
        // while, and a real failure is ground truth that the room is not joinable.
        JoinGate.MarkJoinFailed(param.m_ulSteamIDLobby);
        RoomJoiner.ShowFeedback(Loc.T(Loc.JoinFailedTitle), reason);
    }

    private static void OnLobbyDataUpdate(LobbyDataUpdate_t param)
    {
        if (param.m_bSuccess != 0)          // byte: 0=failure, 1=success
            return;
        if (param.m_ulSteamIDLobby == 0)
            return;
        // Only historical rooms get the cooldown: a failed update for an unrelated lobby
        // (e.g. the game's invite flow) must not accumulate entries. The official code has
        // already popped its "can't find lobby" modal, so here we only set the cooldown,
        // never a second popup.
        if (Plugin.Store.GetRoom(param.m_ulSteamIDLobby.ToString()) == null)
            return;
        Plugin.Log.LogInfo($"Lobby data fetch failed lobby={param.m_ulSteamIDLobby} — join cooldown set");
        JoinGate.MarkJoinFailed(param.m_ulSteamIDLobby);
    }

    private static string Describe(EChatRoomEnterResponse code)
    {
        switch (code)
        {
            case EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist:
                return Loc.T(Loc.JoinFailDoesntExist);
            case EChatRoomEnterResponse.k_EChatRoomEnterResponseNotAllowed:
                return Loc.T(Loc.JoinFailNotAllowed);
            case EChatRoomEnterResponse.k_EChatRoomEnterResponseFull:
                return Loc.T(Loc.JoinFailFull);
            case EChatRoomEnterResponse.k_EChatRoomEnterResponseBanned:
                return Loc.T(Loc.JoinFailBanned);
            default:
                return Loc.Tf(Loc.JoinFailOther, (int)code);
        }
    }
}
