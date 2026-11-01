using System;
using System.Collections.Generic;
using Steamworks;

namespace RoomHub;

/// <summary>
/// Sends a Steam lobby invite to a player while local is in a multiplayer lobby.
/// Uses <c>InviteUserToLobby</c>, which works for non-friends too (game invites are a
/// separate Steam channel and are NOT gated by the chat privacy setting).
///
/// Feedback is the flipped "Invited" button (plus a popup when the send fails).
/// Steam offers no delivery receipt for invites, so "sent" (InviteUserToLobby == true)
/// is the strongest confirmation available.
/// </summary>
public static class SteamInviteService
{
    /// <summary>
    /// Invites sent during the current panel session, keyed by lobby + canonical SteamID.
    /// Rows are destroyed and recreated whenever FriendRoomsService.Changed fires, so the
    /// flipped "Invited" button state must live here, not in the row's closure.
    /// Cleared when the panel closes.
    /// </summary>
    private static readonly HashSet<string> _invited = new();

    /// <summary>True if an invite was already sent to this SteamId in the current lobby.</summary>
    public static bool IsInvited(string steamIdStr)
    {
        if (!TryParseId(steamIdStr, out var id))
            return false;
        return _invited.Contains(InviteKey(id, CurrentLobbyId()));
    }

    /// <summary>Clear all sent-invite marks (panel closed / scene changed).</summary>
    public static void ResetInvited() => _invited.Clear();

    public static bool InvitePlayer(string steamIdStr)
    {
        if (!TryParseId(steamIdStr, out var id))
        {
            RoomJoiner.ShowFeedback(Loc.T(Loc.Invite), Loc.T(Loc.InviteInvalid));
            return false;
        }

        if (!SteamGate.Ready())
        {
            Plugin.Log.LogWarning("Invite skipped: Steam API is not initialized.");
            RoomJoiner.ShowFeedback(Loc.T(Loc.Invite), Loc.T(Loc.InviteFailed));
            return false;
        }

        var target = new CSteamID(id);
        try
        {
            var handler = LobbyHandler();
            if (handler == null || !handler.InSteamLobby(out var liveLobby) || liveLobby == CSteamID.Nil)
            {
                RoomJoiner.ShowFeedback(Loc.T(Loc.Invite), Loc.T(Loc.InviteNeedLobby));
                return false;
            }

            if (SteamMatchmaking.InviteUserToLobby(liveLobby, target))
            {
                Plugin.Log.LogInfo($"InviteUserToLobby ok → {id} lobby={liveLobby.m_SteamID}");
                _invited.Add(InviteKey(id, liveLobby.m_SteamID));
                // No success modal on purpose: inviting several friends in a row must
                // not be interrupted, the flipped "Invited" button is the feedback
                return true;
            }

            Plugin.Log.LogWarning($"InviteUserToLobby returned false for {id}");
            RoomJoiner.ShowFeedback(Loc.T(Loc.Invite), Loc.T(Loc.InviteFailed));
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Invite failed: {ex.Message}");
            RoomJoiner.ShowFeedback(Loc.T(Loc.Invite), Loc.T(Loc.InviteFailed));
            return false;
        }
    }

    private static bool TryParseId(string? steamIdStr, out ulong id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(steamIdStr))
            return false;
        return ulong.TryParse(steamIdStr.Trim(), out id) && id != 0;
    }

    private static string InviteKey(ulong steamId, ulong lobbyId) =>
        lobbyId.ToString() + ":" + steamId.ToString();

    /// <summary>GameHandler.GetService is a dictionary indexer that throws on a missing service</summary>
    private static SteamLobbyHandler? LobbyHandler()
    {
        try { return GameHandler.GetService<SteamLobbyHandler>(); }
        catch { return null; }
    }

    private static ulong CurrentLobbyId()
    {
        var handler = LobbyHandler();
        if (handler != null && handler.InSteamLobby(out var lobby) && lobby != CSteamID.Nil)
            return lobby.m_SteamID;
        return 0;
    }
}
