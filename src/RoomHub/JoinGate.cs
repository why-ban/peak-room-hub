using System;
using System.Collections.Generic;

namespace RoomHub;

/// <summary>
/// Decides whether the Join button may be shown for a historical room.
///
/// Two rules, both cheap and local:
/// 1. AGE: a PEAK session never outlives ~2.5 hours and a Steam lobby id is never reused,
///    so a room we last joined more than <see cref="MaxLobbyAgeHours"/> ago is guaranteed
///    dead. Hiding Join for those needs no network probe at all.
/// 2. COOLDOWN: after a real join attempt failed (full / gone / not allowed), hide Join for
///    this lobby for <see cref="JoinFailCooldownSec"/> so the button does not immediately
///    reappear for a room that is actually dead.
///
/// NOTE: probing via <c>SteamMatchmaking.RequestLobbyData</c> is intentionally NOT used.
/// PEAK's own <c>SteamLobbyHandler.OnLobbyDataUpdate</c> opens a "can't find lobby" modal on
/// ANY failed <c>LobbyDataUpdate_t</c>, including the ones our probe triggered, so probing
/// dead rooms makes the game itself pop "无法找到该房间 / 该邀请已过期" modals. Age +
/// cooldown replace the probe entirely.
/// </summary>
internal static class JoinGate
{
    /// <summary>Longest plausible PEAK session; older rooms are treated as dead.</summary>
    public const double MaxLobbyAgeHours = 2.5;

    private const double JoinFailCooldownSec = 600d;
    private static readonly long JoinFailCooldownTicks = (long)(JoinFailCooldownSec * TimeSpan.TicksPerSecond);
    private static readonly long MaxLobbyAgeTicks = (long)(MaxLobbyAgeHours * TimeSpan.TicksPerHour);

    private sealed class Entry
    {
        public long CooldownUntilUtcTicks;
    }

    private static readonly Dictionary<ulong, Entry> _cooldowns = new();

    /// <summary>Raised when a failed join changes a room's Join visibility (row refresh).</summary>
    public static event Action? StateChanged;

    /// <summary>Whether the Join button may be offered for this historical room.</summary>
    public static bool IsJoinAllowed(string lobbyId, long joinedAtUtcTicks)
    {
        // Age rule: a lobby id is never reused and one session cannot outlive the cap,
        // so anything older than a max-length session is guaranteed gone.
        if (joinedAtUtcTicks > 0 && joinedAtUtcTicks <= DateTime.MaxValue.Ticks)
        {
            var ageTicks = DateTime.UtcNow.Ticks - joinedAtUtcTicks;
            // A negative age means the clock moved backwards, so the room's real age is unknown
            // and only the cooldown rule below may hide Join
            if (ageTicks > MaxLobbyAgeTicks)
                return false;
        }

        // Cooldown after a real failed join attempt, kept in wall-clock time so a restart
        // cannot resurrect a room that just failed
        if (ulong.TryParse(lobbyId, out var id) && id != 0)
        {
            if (_cooldowns.TryGetValue(id, out var e))
            {
                if (DateTime.UtcNow.Ticks < e.CooldownUntilUtcTicks)
                    return false;
                _cooldowns.Remove(id);   // expired entry removed eagerly
            }
        }

        return true;
    }

    /// <summary>Record a real join failure: hide Join for this lobby and stop re-offering
    /// it for the cooldown window (a real failure is ground truth that the room is not
    /// joinable right now).</summary>
    public static void MarkJoinFailed(ulong lobbyId)
    {
        if (lobbyId == 0)
            return;
        _cooldowns[lobbyId] = new Entry { CooldownUntilUtcTicks = DateTime.UtcNow.Ticks + JoinFailCooldownTicks };
        try { StateChanged?.Invoke(); } catch { /* ignore */ }
    }
}
