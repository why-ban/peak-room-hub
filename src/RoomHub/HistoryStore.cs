using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace RoomHub;

/// <summary>
/// Persistent room + met-player history (TSV). Survives quit/crash after flush.
/// Format V3 (header ROOMHISTORY_V3):
///   R\tlobbyId\thostSteamId\thostName\tlobbyName\tjoinedTicks
///   P\tlobbyId\tsteamId\tdisplayName\twasHost\tfirstTicks\tlastTicks
/// Also reads legacy RETRACE_V*, QuickRejoin V2 TSV and QR_RECENT_V* on migrate.
/// </summary>
public sealed class HistoryStore
{
    private const int FormatVersion = 3;
    private const char Sep = '\t';
    private const long SaveRetryDelayTicks = 30 * TimeSpan.TicksPerSecond;
    private static readonly System.Text.RegularExpressions.Regex RichTextTag =
        new("<[^>]+>", System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly string _filePath;
    private readonly string _legacyReTracePath;
    private readonly string _legacyQuickRejoinPath;
    private readonly PluginConfig _config;
    private readonly object _lock = new();
    private List<RoomRecord> _rooms = new();
    private bool _dirty;
    private bool _loadFailed;
    private bool _storageDisabled;
    private string? _loadBackupSource;
    private bool _loadBackupDone;
    private int _lastSavedRowCount;
    private long _saveRetryAtUtcTicks;
    private float _lastDirtyUnscaledTime;

    public HistoryStore(string configPath, PluginConfig config)
    {
        _config = config;
        var dir = Path.Combine(configPath, "RoomHub");
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            // A read-only config dir must not abort plugin init, so history degrades to memory only
            _storageDisabled = true;
            Plugin.Log.LogError($"History directory unavailable, history will not persist: {ex.Message}");
        }

        _filePath = Path.Combine(dir, "history.tsv");
        // Older package / product names
        _legacyReTracePath = Path.Combine(configPath, "ReTrace", "history.tsv");
        _legacyQuickRejoinPath = Path.Combine(configPath, "QuickRejoin", "recent_rooms.tsv");
    }

    public string StoragePath => _filePath;

    public IReadOnlyList<RoomRecord> GetRooms()
    {
        lock (_lock)
        {
            // Reads never mutate the stored list: lowering a limit must not delete rows
            return VisibleRoomsUnlocked().Select(CloneRoom).ToList();
        }
    }

    public RoomRecord? GetMostRecent()
    {
        var list = GetRooms();
        return list.Count > 0 ? list[0] : null;
    }

    public RoomRecord? GetRoom(string lobbyId)
    {
        lock (_lock)
        {
            var r = _rooms.FirstOrDefault(x => string.Equals(x.LobbyId, lobbyId, StringComparison.Ordinal));
            return r == null ? null : CloneRoom(r);
        }
    }

    /// <summary>
    /// Search met players by display name (case-insensitive substring).
    /// One row per SteamId, keeping the most recent sighting + room host label.
    /// </summary>
    public IReadOnlyList<PlayerSearchHit> SearchPlayers(string query)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0)
            return Array.Empty<PlayerSearchHit>();

        lock (_lock)
        {
            var map = new Dictionary<string, PlayerSearchHit>(StringComparer.Ordinal);
            foreach (var room in VisibleRoomsUnlocked())
            {
                var hostLabel = string.IsNullOrWhiteSpace(room.HostName)
                    ? room.LobbyId
                    : room.HostName;
                foreach (var p in room.Players)
                {
                    if (string.IsNullOrWhiteSpace(p.SteamId))
                        continue;
                    // Name only, because SteamId digits would match almost everyone (e.g. query "8")
                    if (p.DisplayName.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (!map.TryGetValue(p.SteamId, out var hit) || p.LastSeenUtcTicks > hit.LastSeenUtcTicks)
                    {
                        map[p.SteamId] = new PlayerSearchHit
                        {
                            SteamId = p.SteamId,
                            DisplayName = p.DisplayName,
                            WasHost = p.WasHost,
                            LastSeenUtcTicks = p.LastSeenUtcTicks,
                            LobbyId = room.LobbyId,
                            RoomHostName = hostLabel
                        };
                    }
                }
            }

            return map.Values
                .OrderByDescending(h => h.LastSeenUtcTicks)
                .ToList();
        }
    }

    /// <summary>
    /// Record that we entered a Steam lobby. For a room already in history this only
    /// refreshes metadata; <see cref="TouchRoomJoinedNow"/> owns JoinedAt, because a live
    /// Steam lobby does not prove the Photon room still exists.
    /// </summary>
    public void UpsertRoom(string lobbyId, string hostSteamId, string hostName, string lobbyName)
    {
        if (string.IsNullOrWhiteSpace(lobbyId))
            return;

        long nowTicks = DateTime.UtcNow.Ticks;
        bool created = false;
        string effectiveHost;

        lock (_lock)
        {
            var existing = _rooms.FirstOrDefault(r => string.Equals(r.LobbyId, lobbyId, StringComparison.Ordinal));
            if (existing != null && existing.JoinedAtUtcTicks < ExpireCutoffTicks())
                existing = null;   // already outside the retention window, record it from scratch

            if (existing != null)
            {
                // Host is frozen to the room creator; only a still-unknown name may be filled in
                if (!IsPlaceholderName(hostName) && IsPlaceholderName(existing.HostName))
                    existing.HostName = Sanitize(hostName);
                if (!string.IsNullOrWhiteSpace(lobbyName))
                    existing.LobbyName = Sanitize(lobbyName);
                effectiveHost = existing.HostName;
            }
            else
            {
                var room = new RoomRecord
                {
                    LobbyId = lobbyId.Trim(),
                    HostSteamId = (hostSteamId ?? "").Trim(),
                    HostName = Sanitize(hostName),
                    LobbyName = Sanitize(lobbyName),
                    JoinedAtUtcTicks = nowTicks
                };
                _rooms.Insert(0, room);
                created = true;
                effectiveHost = room.HostName;
            }

            PruneUnlocked(saveIfChanged: false);
            // Room enter is a durable event, so flush immediately
            SaveUnlocked();
        }

        Plugin.Log.LogInfo(
            $"Room {(created ? "recorded (new)" : "updated (rejoin)")}: lobby={lobbyId}, " +
            $"host='{effectiveHost}' → {_filePath}");
    }

    /// <summary>
    /// Bump last-joined time for an existing room without changing host metadata.
    /// Used when Photon OnJoinedRoom confirms we actually entered the session
    /// (covers cases where Steam OnLobbyEnter already ran earlier, or rejoin timing is odd).
    /// </summary>
    public void TouchRoomJoinedNow(string lobbyId, string reason)
    {
        if (string.IsNullOrWhiteSpace(lobbyId))
            return;

        long nowTicks = DateTime.UtcNow.Ticks;
        long previousTicks = 0;
        bool touched = false;

        lock (_lock)
        {
            var room = _rooms.FirstOrDefault(r => string.Equals(r.LobbyId, lobbyId, StringComparison.Ordinal));
            if (room == null)
                return;

            previousTicks = room.JoinedAtUtcTicks;
            // Only move forward in time (never shrink)
            if (nowTicks > room.JoinedAtUtcTicks)
            {
                room.JoinedAtUtcTicks = nowTicks;
                touched = true;
                MarkDirtyUnlocked();
            }
        }

        if (touched)
        {
            var deltaSec = previousTicks > 0
                ? (nowTicks - previousTicks) / TimeSpan.TicksPerSecond
                : 0;
            Plugin.Log.LogInfo(
                $"Room joinedAt touched ({reason}): lobby={lobbyId}, +{deltaSec}s since previous write");
        }
    }

    /// <summary>
    /// Record a met player under an existing room. Does <b>not</b> create a room row: a row only
    /// ever comes from a real lobby enter, so Photon-only paths cannot invent history entries.
    /// </summary>
    public void UpsertPlayer(string lobbyId, string steamId, string displayName, bool wasHost)
    {
        if (string.IsNullOrWhiteSpace(lobbyId) || string.IsNullOrWhiteSpace(steamId))
            return;

        lock (_lock)
        {
            var room = _rooms.FirstOrDefault(r => string.Equals(r.LobbyId, lobbyId, StringComparison.Ordinal));
            if (room == null)
            {
                // Room must come from UpsertRoom, never invented from a player callback
                return;
            }

            var now = DateTime.UtcNow.Ticks;
            var sid = steamId.Trim();
            var p = room.Players.FirstOrDefault(x => string.Equals(x.SteamId, sid, StringComparison.Ordinal));
            if (p == null)
            {
                p = new PlayerRecord
                {
                    SteamId = sid,
                    DisplayName = Sanitize(displayName),
                    WasHost = false,
                    FirstSeenUtcTicks = now,
                    LastSeenUtcTicks = now
                };
                room.Players.Add(p);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(displayName))
                    p.DisplayName = Sanitize(displayName);
                p.LastSeenUtcTicks = now;
            }

            // The badge follows the stored host, never the caller's claim, so wasHost only seeds
            // a room that was recorded before its host was known
            if (string.IsNullOrWhiteSpace(room.HostSteamId))
            {
                if (wasHost)
                    ApplySoleHostUnlocked(room, sid, p.DisplayName);
            }
            else if (string.Equals(room.HostSteamId, sid, StringComparison.Ordinal))
            {
                ApplySoleHostUnlocked(room, room.HostSteamId, p.DisplayName);
            }

            // Cap players per room (keep most recently seen). Fixed constant, not config
            var cap = PluginConfig.MaxPlayersPerRoomFixed;
            if (room.Players.Count > cap)
            {
                room.Players.Sort((a, b) => b.LastSeenUtcTicks.CompareTo(a.LastSeenUtcTicks));
                room.Players.RemoveRange(cap, room.Players.Count - cap);
            }

            MarkDirtyUnlocked();
        }
    }

    /// <summary>
    /// Mark one player as the only host for this room (clears previous host badges).
    /// </summary>
    public void SetCurrentHost(string lobbyId, string hostSteamId, string? hostDisplayName)
    {
        if (string.IsNullOrWhiteSpace(lobbyId) || string.IsNullOrWhiteSpace(hostSteamId))
            return;

        lock (_lock)
        {
            var room = _rooms.FirstOrDefault(r => string.Equals(r.LobbyId, lobbyId, StringComparison.Ordinal));
            if (room == null)
                return;

            ApplySoleHostUnlocked(room, hostSteamId.Trim(), hostDisplayName);
            MarkDirtyUnlocked();
        }
    }

    private static void ApplySoleHostUnlocked(RoomRecord room, string hostSteamId, string? hostDisplayName)
    {
        // Frozen host: the first creator recorded for a lobby is never replaced, so a Steam
        // ownership transfer cannot move the badge or rewrite the room owner
        if (string.IsNullOrWhiteSpace(room.HostSteamId))
            room.HostSteamId = hostSteamId;

        var host = room.HostSteamId;
        foreach (var x in room.Players)
            x.WasHost = string.Equals(x.SteamId, host, StringComparison.Ordinal);

        if (!IsPlaceholderName(hostDisplayName) && string.Equals(host, hostSteamId, StringComparison.Ordinal))
            room.HostName = Sanitize(hostDisplayName);
    }

    public void DeleteRoom(string lobbyId)
    {
        if (string.IsNullOrWhiteSpace(lobbyId))
            return;

        lock (_lock)
        {
            int removed = _rooms.RemoveAll(r => string.Equals(r.LobbyId, lobbyId, StringComparison.Ordinal));
            if (removed > 0)
            {
                // User-initiated delete, so flush immediately
                if (SaveUnlocked())
                    Plugin.Log.LogInfo($"Deleted room history: {lobbyId}");
            }
        }
    }

    /// <summary>
    /// Drop the history row of a room the local player hosted that never saw a second player.
    /// An empty player list alone proves nothing: a guest's room can have one too.
    /// </summary>
    public bool PruneSoloHostedRoom(string? lobbyId, string? localSteamId)
    {
        if (string.IsNullOrWhiteSpace(lobbyId))
            return false;

        var self = (localSteamId ?? "").Trim();
        if (self.Length == 0)
            return false;

        bool removed;
        lock (_lock)
        {
            removed = _rooms.RemoveAll(
                r => string.Equals(r.LobbyId, lobbyId, StringComparison.Ordinal) && IsSoloHostedRoom(r, self)) > 0;

            // A delete, so flush now instead of waiting for the deferred tick
            if (removed)
                SaveUnlocked();
        }

        if (removed)
            Plugin.Log.LogInfo(
                $"Pruned solo-hosted room: lobby={lobbyId}, host={self}, reason=no other player was ever recorded");

        return removed;
    }

    /// <summary>
    /// Same rule as <see cref="PruneSoloHostedRoom"/>, over every stored room. Startup sweep for
    /// the runs that crashed before a leave or quit could clean up. Returns the rows dropped.
    /// </summary>
    public int PruneSoloHostedRooms(string? localSteamId)
    {
        var self = (localSteamId ?? "").Trim();
        if (self.Length == 0)
            return 0;

        int removed = 0;
        string lobbies = "";
        lock (_lock)
        {
            var victims = _rooms.Where(r => IsSoloHostedRoom(r, self)).ToList();
            if (victims.Count > 0)
            {
                removed = _rooms.RemoveAll(victims.Contains);
                lobbies = string.Join(", ", victims.Select(r => r.LobbyId));
                SaveUnlocked();
            }
        }

        if (removed > 0)
            Plugin.Log.LogInfo(
                $"Pruned {removed} solo-hosted room(s): lobbies={lobbies}, host={self}, " +
                "reason=no other player was ever recorded");

        return removed;
    }

    private static bool IsSoloHostedRoom(RoomRecord room, string localSteamId)
    {
        // An unknown host id must never be read as "mine", so an empty one never matches
        return !string.IsNullOrEmpty(room.HostSteamId)
               && string.Equals(room.HostSteamId, localSteamId, StringComparison.Ordinal)
               && room.Players.Count == 0;
    }

    /// <summary>
    /// Flush coalesced dirty writes after ~0.75s idle. Call from Plugin.Update.
    /// </summary>
    public void TickDeferredSave(float unscaledTime)
    {
        lock (_lock)
        {
            if (!_dirty || _loadFailed || _storageDisabled)
                return;
            if (unscaledTime - _lastDirtyUnscaledTime < 0.75f)
                return;
            SaveUnlocked();
        }
    }

    private void MarkDirtyUnlocked()
    {
        _dirty = true;
        try { _lastDirtyUnscaledTime = UnityEngine.Time.unscaledTime; }
        catch { _lastDirtyUnscaledTime = 0f; }
    }

    public void Load()
    {
        lock (_lock)
        {
            _rooms = new List<RoomRecord>();
            _loadFailed = false;
            string? source = null;

            try
            {
                if (File.Exists(_filePath))
                {
                    source = _filePath;
                    LoadFileUnlocked(_filePath);
                }
                else if (File.Exists(_legacyReTracePath))
                {
                    source = _legacyReTracePath;
                    LoadFileUnlocked(_legacyReTracePath);
                    if (_rooms.Count > 0)
                        SaveUnlocked();
                    Plugin.Log.LogInfo($"Migrated {_rooms.Count} room(s) from ReTrace history.");
                }
                else if (File.Exists(_legacyQuickRejoinPath))
                {
                    source = _legacyQuickRejoinPath;
                    LoadFileUnlocked(_legacyQuickRejoinPath);
                    if (_rooms.Count > 0)
                        SaveUnlocked();
                    Plugin.Log.LogInfo($"Migrated {_rooms.Count} room(s) from Quick Rejoin TSV.");
                }

                _lastSavedRowCount = _rooms.Count;
                PruneUnlocked(saveIfChanged: true);
                Plugin.Log.LogInfo($"Loaded {_rooms.Count} room(s) from {_filePath}");
            }
            catch (Exception ex)
            {
                // An unreadable file is not an empty history: keep the good file untouched and
                // refuse every write until the next successful load
                _loadFailed = true;
                _rooms = new List<RoomRecord>();
                _loadBackupSource = source;
                _loadBackupDone = false;
                RetryLoadBackupUnlocked();
                Plugin.Log.LogError($"History load failed, writes disabled for this session: {ex}");
            }
        }
    }

    /// <summary>Force any pending dirty buffer to disk (quit / destroy).</summary>
    public void Flush()
    {
        lock (_lock)
        {
            if (_loadFailed)
            {
                RetryLoadBackupUnlocked();
                return;
            }

            if (!_dirty)
                return;
            SaveUnlocked();
        }
    }

    private void LoadFileUnlocked(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length == 0) return;

        int start = 0;
        if (lines[0].StartsWith("QR_RECENT_V", StringComparison.Ordinal) ||
            lines[0].StartsWith("RETRACE_V", StringComparison.Ordinal) ||
            lines[0].StartsWith("ROOMHISTORY_V", StringComparison.Ordinal))
            start = 1;

        var map = new Dictionary<string, RoomRecord>(StringComparer.Ordinal);

        for (int i = start; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;

            var parts = line.Split(Sep);
            if (parts.Length < 2) continue;

            // V3 typed rows
            if (parts[0] == "R" && parts.Length >= 6)
            {
                if (!long.TryParse(parts[5], out var ticks) || !UtcTicks.IsUsable(ticks)) continue;
                var id = Unescape(parts[1]);
                if (string.IsNullOrWhiteSpace(id)) continue;
                var room = new RoomRecord
                {
                    LobbyId = id,
                    HostSteamId = Unescape(parts[2]),
                    HostName = Sanitize(Unescape(parts[3])),
                    LobbyName = Sanitize(Unescape(parts[4])),
                    JoinedAtUtcTicks = ticks
                };
                map[id] = room;
            }
            else if (parts[0] == "P" && parts.Length >= 7)
            {
                var lobbyId = Unescape(parts[1]);
                if (!map.TryGetValue(lobbyId, out var room)) continue;
                if (!long.TryParse(parts[5], out var first) || !UtcTicks.IsUsable(first)) continue;
                if (!long.TryParse(parts[6], out var last) || !UtcTicks.IsUsable(last)) last = first;
                room.Players.Add(new PlayerRecord
                {
                    SteamId = Unescape(parts[2]),
                    DisplayName = Sanitize(Unescape(parts[3])),
                    WasHost = parts[4] == "1" || parts[4].Equals("true", StringComparison.OrdinalIgnoreCase),
                    FirstSeenUtcTicks = first,
                    LastSeenUtcTicks = last
                });
            }
            else if (parts.Length == 4 && !parts[0].Equals("R") && !parts[0].Equals("P"))
            {
                // V2 legacy: lobbyId host lobbyName ticks
                if (!long.TryParse(parts[3], out var ticks) || !UtcTicks.IsUsable(ticks)) continue;
                var id = Unescape(parts[0]);
                if (string.IsNullOrWhiteSpace(id)) continue;
                map[id] = new RoomRecord
                {
                    LobbyId = id,
                    HostName = Sanitize(Unescape(parts[1])),
                    LobbyName = Sanitize(Unescape(parts[2])),
                    JoinedAtUtcTicks = ticks
                };
            }
        }

        // Normalize: sanitize room fields + enforce sole host per room (legacy multi-host files)
        long nowTicks = DateTime.UtcNow.Ticks;
        foreach (var room in map.Values)
        {
            room.HostName = Sanitize(room.HostName);
            room.LobbyName = Sanitize(room.LobbyName);
            room.HostSteamId = (room.HostSteamId ?? "").Trim();

            // A timestamp ahead of the clock (rollback or corruption) would never expire and
            // would sort first forever, so pin it to now
            if (room.JoinedAtUtcTicks > nowTicks)
                room.JoinedAtUtcTicks = nowTicks;
            foreach (var p in room.Players)
            {
                if (p.FirstSeenUtcTicks > nowTicks) p.FirstSeenUtcTicks = nowTicks;
                if (p.LastSeenUtcTicks > nowTicks) p.LastSeenUtcTicks = nowTicks;
            }

            if (!string.IsNullOrWhiteSpace(room.HostSteamId))
            {
                ApplySoleHostUnlocked(room, room.HostSteamId, room.HostName);
            }
            else
            {
                var marked = room.Players.Where(p => p.WasHost).ToList();
                if (marked.Count > 1)
                {
                    var keep = marked.OrderByDescending(p => p.LastSeenUtcTicks).First();
                    ApplySoleHostUnlocked(room, keep.SteamId, keep.DisplayName);
                }
            }
        }

        _rooms = map.Values.OrderByDescending(r => r.JoinedAtUtcTicks).ToList();
    }

    private long ExpireCutoffTicks()
    {
        var hours = Math.Max(1, _config.HistoryExpireHours.Value);
        return DateTime.UtcNow.AddHours(-hours).Ticks;
    }

    /// <summary>Rooms the UI may show: inside the retention window, one row per lobby, newest first.</summary>
    private List<RoomRecord> VisibleRoomsUnlocked()
    {
        var max = Math.Max(1, _config.MaxRecentRooms.Value);
        var cutoff = ExpireCutoffTicks();

        return _rooms
            .Where(r => r != null && !string.IsNullOrWhiteSpace(r.LobbyId) && r.JoinedAtUtcTicks >= cutoff)
            .GroupBy(r => r.LobbyId)
            .Select(g => g.OrderByDescending(x => x.JoinedAtUtcTicks).First())
            .OrderByDescending(r => r.JoinedAtUtcTicks)
            .Take(max)
            .ToList();
    }

    private void PruneUnlocked(bool saveIfChanged)
    {
        int before = _rooms.Count;
        _rooms = VisibleRoomsUnlocked();

        if (saveIfChanged && _rooms.Count != before)
            SaveUnlocked();
    }

    private bool SaveUnlocked()
    {
        if (_loadFailed)
        {
            RetryLoadBackupUnlocked();
            return false;
        }

        if (_storageDisabled)
            return false;

        long nowTicks = DateTime.UtcNow.Ticks;
        if (_saveRetryAtUtcTicks != 0 && nowTicks < _saveRetryAtUtcTicks)
            return false;

        var tmp = _filePath + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // A rewrite that drops rows is the only write that can lose data, so keep a copy
            if (_rooms.Count < _lastSavedRowCount)
                BackupCurrentUnlocked();

            var sb = new StringBuilder();
            sb.Append("ROOMHISTORY_V").Append(FormatVersion).Append('\n');
            sb.Append("# R lobbyId hostSteamId hostName lobbyName joinedTicks\n");
            sb.Append("# P lobbyId steamId displayName wasHost firstTicks lastTicks\n");

            foreach (var r in _rooms.OrderByDescending(x => x.JoinedAtUtcTicks))
            {
                sb.Append("R").Append(Sep)
                    .Append(Escape(r.LobbyId)).Append(Sep)
                    .Append(Escape(r.HostSteamId)).Append(Sep)
                    .Append(Escape(r.HostName)).Append(Sep)
                    .Append(Escape(r.LobbyName)).Append(Sep)
                    .Append(r.JoinedAtUtcTicks).Append('\n');

                foreach (var p in r.Players.OrderByDescending(x => x.LastSeenUtcTicks))
                {
                    sb.Append("P").Append(Sep)
                        .Append(Escape(r.LobbyId)).Append(Sep)
                        .Append(Escape(p.SteamId)).Append(Sep)
                        .Append(Escape(p.DisplayName)).Append(Sep)
                        .Append(p.WasHost ? "1" : "0").Append(Sep)
                        .Append(p.FirstSeenUtcTicks).Append(Sep)
                        .Append(p.LastSeenUtcTicks).Append('\n');
                }
            }

            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            // Atomic replace when possible (safer than delete+move on crash mid-write)
            if (File.Exists(_filePath))
                File.Replace(tmp, _filePath, null);
            else
                File.Move(tmp, _filePath);

            _dirty = false;
            _lastSavedRowCount = _rooms.Count;
            _saveRetryAtUtcTicks = 0;
            return true;
        }
        catch (Exception ex)
        {
            // Back off so a locked or read-only file does not retry on every frame tick, and stay
            // dirty so the deferred save and the quit-time flush can still write it
            _saveRetryAtUtcTicks = nowTicks + SaveRetryDelayTicks;
            _dirty = true;
            TryDelete(tmp);
            Plugin.Log.LogError(
                $"History save failed, retrying in {SaveRetryDelayTicks / TimeSpan.TicksPerSecond}s: {ex.Message}");
            return false;
        }
    }

    private void BackupCurrentUnlocked()
    {
        try
        {
            if (File.Exists(_filePath))
                File.Copy(_filePath, _filePath + ".bak", overwrite: true);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"History backup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Copy the unreadable file aside. A lock held at startup usually clears later, so the copy
    /// is retried from every blocked write until it succeeds.
    /// </summary>
    private string? RetryLoadBackupUnlocked()
    {
        if (_loadBackupDone || _loadBackupSource == null)
            return null;

        var backup = BackupUnreadableUnlocked(_loadBackupSource, quiet: true);
        if (backup == null)
            return null;

        _loadBackupDone = true;
        Plugin.Log.LogWarning($"Unreadable history copied to {backup}");
        return backup;
    }

    private string? BackupUnreadableUnlocked(string source, bool quiet)
    {
        try
        {
            if (!File.Exists(source))
                return null;

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var backup = $"{source}.bak-{stamp}";
            for (int n = 1; File.Exists(backup) && n < 100; n++)
                backup = $"{source}.bak-{stamp}-{n}";

            File.Copy(source, backup, overwrite: false);
            return backup;
        }
        catch (Exception ex)
        {
            if (!quiet)
                Plugin.Log.LogWarning($"History backup of unreadable file failed: {ex.Message}");
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { /* the next write overwrites it anyway */ }
    }

    /// <summary>An empty or localized "unknown" label carries no information about the host.</summary>
    internal static bool IsPlaceholderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;
        return string.Equals(name, Loc.T(Loc.UnknownHost), StringComparison.Ordinal)
               || string.Equals(name, Loc.T(Loc.UnknownPlayer), StringComparison.Ordinal);
    }

    private static RoomRecord CloneRoom(RoomRecord r)
    {
        var c = new RoomRecord
        {
            LobbyId = r.LobbyId,
            HostSteamId = r.HostSteamId,
            HostName = r.HostName,
            LobbyName = r.LobbyName,
            JoinedAtUtcTicks = r.JoinedAtUtcTicks
        };
        foreach (var p in r.Players)
        {
            c.Players.Add(new PlayerRecord
            {
                SteamId = p.SteamId,
                DisplayName = p.DisplayName,
                WasHost = p.WasHost,
                FirstSeenUtcTicks = p.FirstSeenUtcTicks,
                LastSeenUtcTicks = p.LastSeenUtcTicks
            });
        }

        return c;
    }

    private static string Sanitize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // Strip simple rich-text tags sometimes present in PEAK nicknames
        var noTags = RichTextTag.Replace(s, "");
        return noTags.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static string Escape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private static string Unescape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char n = s[++i];
                sb.Append(n switch
                {
                    't' => '\t',
                    'n' => '\n',
                    'r' => '\r',
                    '\\' => '\\',
                    _ => n
                });
            }
            else sb.Append(s[i]);
        }

        return sb.ToString();
    }
}
