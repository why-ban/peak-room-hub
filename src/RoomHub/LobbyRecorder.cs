using System;
using HarmonyLib;
using Steamworks;

namespace RoomHub;

/// <summary>
/// On successful Steam lobby enter: save room + snapshot current members.
/// </summary>
[HarmonyPatch(typeof(SteamLobbyHandler), "OnLobbyEnter")]
internal static class SteamLobbyEnterPatch
{
    private static void Postfix(SteamLobbyHandler __instance, LobbyEnter_t param)
    {
        try
        {
            if (!Plugin.ModConfig.Enabled.Value)
                return;
            if (param.m_EChatRoomEnterResponse
                != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
                return;

            var lobbyId = new CSteamID(param.m_ulSteamIDLobby);
            if (lobbyId == CSteamID.Nil || param.m_ulSteamIDLobby == 0)
                return;

            var self = SteamUser.GetSteamID();
            var owner = SteamMatchmaking.GetLobbyOwner(lobbyId);
            bool isHost = owner == self;

            string lobbyKey = lobbyId.m_SteamID.ToString();

            // The host is FROZEN to the first owner we recorded for this room (the creator).
            // Rejoining must not rewrite it even if Steam ownership has since transferred, so
            // only a brand-new room adopts the current owner as its host.
            var existing = Plugin.Store.GetRoom(lobbyKey);
            string recordedHost = (existing != null && !string.IsNullOrWhiteSpace(existing.HostSteamId))
                ? existing.HostSteamId
                : owner.m_SteamID.ToString();

            if (isHost && !Plugin.ModConfig.RecordWhenHosting.Value)
            {
                // No room row, but keep active lobby for Photon tracking / invites context.
                // Pass the recorded host (creator), not ourselves, because we may have been transferred
                // ownership of a room that was already recorded under its original creator.
                Plugin.Log.LogDebug("Skip room list entry (hosting).");
                PlayerTracker.SetActiveLobby(lobbyKey, recordedHost);
                return;
            }

            string hostName = ResolveHostName(existing, recordedHost);
            // PEAK publishes only PeakVersion / PhotonRegion / CurrentScene / RunId, so there is
            // no lobby name to read from Steam and the record field stays empty
            Plugin.Store.UpsertRoom(lobbyKey, recordedHost, hostName, "");
            PlayerTracker.SetActiveLobby(lobbyKey, recordedHost);
            SnapshotMembers(lobbyId, recordedHost);
            // Sole host badge for the room creator, established when the room is first recorded
            if (existing == null || string.IsNullOrWhiteSpace(existing.HostSteamId))
                Plugin.Store.SetCurrentHost(lobbyKey, recordedHost, hostName);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Lobby record failed: {ex}");
        }
    }

    /// <summary>
    /// Resolve the frozen host's persona, but only while the stored name is still the unknown
    /// placeholder: Steam answers with an empty string for a non-friend until the persona arrives,
    /// and a one-shot read at first entry would otherwise freeze "Unknown" forever.
    /// </summary>
    private static string ResolveHostName(RoomRecord? existing, string hostSteamId)
    {
        if (existing != null
            && !string.IsNullOrWhiteSpace(existing.HostSteamId)
            && !HistoryStore.IsPlaceholderName(existing.HostName))
            return existing.HostName;

        if (!ulong.TryParse(hostSteamId, out var id) || id == 0)
            return "";

        try
        {
            var name = SteamFriends.GetFriendPersonaName(new CSteamID(id));
            return string.IsNullOrWhiteSpace(name) ? "" : name;
        }
        catch
        {
            return "";
        }
    }

    private static void SnapshotMembers(CSteamID lobbyId, string hostSteamId)
    {
        try
        {
            int n = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            var self = SteamUser.GetSteamID();
            string lobbyKey = lobbyId.m_SteamID.ToString();

            for (int i = 0; i < n; i++)
            {
                var member = SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);
                if (member == CSteamID.Nil || member == self)
                    continue;

                string name;
                try { name = SteamFriends.GetFriendPersonaName(member); }
                catch { name = ""; }

                if (string.IsNullOrWhiteSpace(name))
                    name = Loc.T(Loc.UnknownPlayer);

                // wasHost only for the room creator (first recorded host). The host is frozen,
                // so a transferred current owner is NOT marked, because UpsertPlayer clears other host
                // flags when wasHost is true.
                bool wasHost = member.m_SteamID.ToString() == hostSteamId;
                Plugin.Store.UpsertPlayer(lobbyKey, member.m_SteamID.ToString(), name, wasHost);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"SnapshotMembers: {ex.Message}");
        }
    }
}
