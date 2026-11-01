using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace RoomHub;

/// <summary>
/// Readiness gate for every raw Steamworks call. SteamAPI.Init can fail while the game keeps
/// running, and the wrapper then holds null interface pointers: a SteamFriends/SteamUser call
/// P/Invokes through a null vtable, which no managed catch can contain. GetHSteamPipe is a
/// plain export, so it answers "did Init succeed" without touching an interface pointer.
/// </summary>
internal static class SteamGate
{
    public static bool Ready()
    {
        try { return SteamAPI.GetHSteamPipe().m_HSteamPipe != 0; }
        catch { return false; }
    }
}

/// <summary>
/// Loads Steam friend/user avatars into Texture2D with a small cache.
/// </summary>
public static class SteamAvatarService
{
    private const float RetryIntervalSec = 10f;

    private static readonly Dictionary<ulong, Texture2D> Cache = new();

    /// <summary>Steam reported "no avatar set" (image id 0), asking again never helps.</summary>
    private static readonly HashSet<ulong> NoAvatar = new();

    /// <summary>RequestUserInformation is expensive and churns the local cache: once per user.</summary>
    private static readonly HashSet<ulong> Requested = new();

    private static readonly Dictionary<ulong, float> NextAttempt = new();

    private static Callback<AvatarImageLoaded_t>? _avatarLoaded;
    private static bool _inited;

    public static void Init()
    {
        if (_inited) return;
        try
        {
            _avatarLoaded = Callback<AvatarImageLoaded_t>.Create(OnAvatarLoaded);
            _inited = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"SteamAvatarService init failed: {ex.Message}");
        }
    }

    public static void Shutdown()
    {
        try { _avatarLoaded?.Dispose(); } catch { /* ignore */ }
        _avatarLoaded = null;
        _inited = false;
        foreach (var kv in Cache)
        {
            if (kv.Value != null)
                UnityEngine.Object.Destroy(kv.Value);
        }
        Cache.Clear();
        NoAvatar.Clear();
        Requested.Clear();
        NextAttempt.Clear();
    }

    public static Texture2D? GetAvatar(string steamIdStr)
    {
        if (!ulong.TryParse(steamIdStr, out var id) || id == 0)
            return null;
        return GetAvatar(new CSteamID(id));
    }

    public static Texture2D? GetAvatar(CSteamID steamId)
    {
        if (steamId == CSteamID.Nil || steamId.m_SteamID == 0)
            return null;

        ulong id = steamId.m_SteamID;
        if (Cache.TryGetValue(id, out var tex))
        {
            if (tex != null)
                return tex;
            Cache.Remove(id);
        }

        if (NoAvatar.Contains(id))
            return null;
        if (!SteamGate.Ready())
            return null;

        float now = Time.unscaledTime;
        if (NextAttempt.TryGetValue(id, out float due) && now < due)
            return null;
        NextAttempt[id] = now + RetryIntervalSec;

        try
        {
            // bRequireNameOnly=false also downloads the avatar image (true only fetches the name)
            bool requestedNow = Requested.Add(id);
            if (requestedNow)
                SteamFriends.RequestUserInformation(steamId, false);

            int imageId = SteamFriends.GetMediumFriendAvatar(steamId);
            if (imageId == 0)
            {
                // 0 means "no avatar set" but right after the first request it can also mean
                // "not fetched yet" so only trust it once the request has had time to land
                if (!requestedNow)
                    NoAvatar.Add(id);
                return null;
            }
            if (imageId < 0)
                return null;

            var loaded = LoadImage(imageId);
            if (loaded == null)
                return null;

            if (Cache.TryGetValue(id, out var old) && old != null && old != loaded)
                UnityEngine.Object.Destroy(old);
            Cache[id] = loaded;
            return loaded;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"GetAvatar failed for {steamId}: {ex.Message}");
        }

        return null;
    }

    private static void OnAvatarLoaded(AvatarImageLoaded_t cb)
    {
        try
        {
            var id = cb.m_steamID.m_SteamID;
            var tex = LoadImage(cb.m_iImage);
            if (tex == null)
                return;
            if (Cache.TryGetValue(id, out var old) && old != null && old != tex)
                UnityEngine.Object.Destroy(old);
            Cache[id] = tex;
            NoAvatar.Remove(id);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"OnAvatarLoaded error: {ex.Message}");
        }
    }

    private static Texture2D? LoadImage(int imageId)
    {
        if (imageId <= 0) return null;
        if (!SteamUtils.GetImageSize(imageId, out uint w, out uint h) || w == 0 || h == 0)
            return null;

        var buffer = new byte[w * h * 4];
        if (!SteamUtils.GetImageRGBA(imageId, buffer, buffer.Length))
            return null;

        var flipped = new byte[buffer.Length];
        int stride = (int)(w * 4);
        for (int y = 0; y < h; y++)
        {
            int src = y * stride;
            int dst = ((int)h - 1 - y) * stride;
            Buffer.BlockCopy(buffer, src, flipped, dst, stride);
        }

        var tex = new Texture2D((int)w, (int)h, TextureFormat.RGBA32, false);
        tex.LoadRawTextureData(flipped);
        tex.Apply(false, true);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    public static bool IsFriend(string steamIdStr)
    {
        if (!ulong.TryParse(steamIdStr, out var id) || id == 0) return false;
        if (!SteamGate.Ready()) return false;
        try
        {
            return SteamFriends.HasFriend(new CSteamID(id), EFriendFlags.k_EFriendFlagImmediate);
        }
        catch { return false; }
    }

    public static void OpenAddFriend(string steamIdStr)
    {
        if (!ulong.TryParse(steamIdStr, out var id) || id == 0) return;
        if (!SteamGate.Ready()) return;
        try
        {
            SteamFriends.ActivateGameOverlayToUser("friendadd", new CSteamID(id));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"OpenAddFriend failed: {ex.Message}");
        }
    }

    public static string? LocalSteamIdString()
    {
        if (!SteamGate.Ready()) return null;
        try { return SteamUser.GetSteamID().m_SteamID.ToString(); }
        catch { return null; }
    }
}
