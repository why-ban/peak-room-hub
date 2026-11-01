using System;
using System.Collections.Generic;

namespace RoomHub;

/// <summary>Ticks read back from disk are untrusted: DateTime throws on out-of-range values.</summary>
internal static class UtcTicks
{
    internal static bool IsUsable(long ticks) => ticks > 0 && ticks <= DateTime.MaxValue.Ticks;

    internal static DateTime ToUtc(long ticks) =>
        IsUsable(ticks) ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;
}

public sealed class RoomRecord
{
    public string LobbyId = "";
    public string HostSteamId = "";
    public string HostName = "";
    public string LobbyName = "";
    public long JoinedAtUtcTicks;
    public readonly List<PlayerRecord> Players = new();

    public DateTime JoinedAtUtc
    {
        get => UtcTicks.ToUtc(JoinedAtUtcTicks);
        set => JoinedAtUtcTicks = value.ToUniversalTime().Ticks;
    }
}

public sealed class PlayerRecord
{
    public string SteamId = "";
    public string DisplayName = "";
    public bool WasHost;
    public long FirstSeenUtcTicks;
    public long LastSeenUtcTicks;

    public DateTime FirstSeenUtc
    {
        get => UtcTicks.ToUtc(FirstSeenUtcTicks);
        set => FirstSeenUtcTicks = value.ToUniversalTime().Ticks;
    }

    public DateTime LastSeenUtc
    {
        get => UtcTicks.ToUtc(LastSeenUtcTicks);
        set => LastSeenUtcTicks = value.ToUniversalTime().Ticks;
    }
}

/// <summary>One hit from cross-room player name search.</summary>
public sealed class PlayerSearchHit
{
    public string SteamId = "";
    public string DisplayName = "";
    public bool WasHost;
    public long LastSeenUtcTicks;
    public string LobbyId = "";
    public string RoomHostName = "";

    public DateTime LastSeenUtc => UtcTicks.ToUtc(LastSeenUtcTicks);
}
