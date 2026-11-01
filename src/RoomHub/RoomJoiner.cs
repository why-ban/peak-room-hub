using System;
using Steamworks;
using UnityEngine;
using Zorro.UI.Modal;

namespace RoomHub;

public static class RoomJoiner
{
    /// <summary>Loc key for the "already in a session" refusal, the body must not be silent</summary>
    private const string JoinBlockedKey = "ROOMHISTORY_JOIN_BLOCKED";

    public static void TryJoin(RoomRecord room, bool hidePage = true)
    {
        if (room == null)
        {
            ShowNoRecentFeedback();
            return;
        }

        TryJoinLobbyId(room.LobbyId, hidePage);
    }

    /// <summary>
    /// Quick-rejoin of the most recent room (hotkey J). Deliberately BYPASSES JoinGate:
    /// the button's conservative hide rules (age limit + failed-join cooldown) do not apply
    /// to an explicit quick-reconnect. Failures are surfaced by the official modal and
    /// LobbyJoinFeedback instead.
    /// </summary>
    public static void TryJoinMostRecent(bool showEmptyFeedback)
    {
        var recent = Plugin.Store.GetMostRecent();
        if (recent == null)
        {
            if (showEmptyFeedback)
                ShowNoRecentFeedback();
            return;
        }

        TryJoinLobbyId(recent.LobbyId, hidePage: true);
    }

    /// <summary>Single-line "no joinable room" feedback (no extra hint body).</summary>
    private static void ShowNoRecentFeedback()
    {
        ShowModal(Loc.T(Loc.NoRecent), "");
    }

    public static void TryJoinLobbyId(string lobbyId, bool hidePage)
    {
        // The game refuses a lobby join while it is already in a room, and refuses it silently
        if (!HistoryUi.IsTitleScene && InLiveSession())
        {
            Plugin.Log.LogInfo("TryJoinLobby skipped: already in a Steam lobby or Photon room.");
            ShowModal(Loc.T(Loc.JoinFailedTitle), Loc.T(JoinBlockedKey));
            return;
        }

        if (string.IsNullOrWhiteSpace(lobbyId) || !ulong.TryParse(lobbyId.Trim(), out var id) || id == 0)
        {
            ShowModal(Loc.T(Loc.InvalidId), "");
            return;
        }

        if (!SteamGate.Ready())
        {
            Plugin.Log.LogWarning("TryJoinLobby skipped: Steam API is not initialized.");
            ShowModal(Loc.T(Loc.JoinFailedTitle), "");
            return;
        }

        try
        {
            if (hidePage)
                HistoryUi.Hide();

            Plugin.Log.LogInfo($"TryJoinLobby: {id}");
            var handler = LobbyHandler();
            if (handler == null)
            {
                Plugin.Log.LogWarning("TryJoinLobby skipped: SteamLobbyHandler is not registered (NoSteam).");
                ShowModal(Loc.T(Loc.JoinFailedTitle), "");
                return;
            }

            handler.TryJoinLobby(new CSteamID(id));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"TryJoinLobby threw: {ex}");
            ShowModal(Loc.T(Loc.JoinFailedTitle), "");
        }
    }

    private static bool InLiveSession()
    {
        try
        {
            if (Peak.Network.NetCode.Matchmaking.InLobby)
                return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"InLobby probe failed: {ex.Message}");
        }

        try { return Photon.Pun.PhotonNetwork.InRoom; }
        catch { return false; }
    }

    /// <summary>GameHandler.GetService is a dictionary indexer that throws on a missing service</summary>
    private static SteamLobbyHandler? LobbyHandler()
    {
        try { return GameHandler.GetService<SteamLobbyHandler>(); }
        catch { return null; }
    }

    private static void ShowModal(string title, string body)
    {
        try
        {
            string ok;
            try { ok = LocalizedText.GetText("OK"); }
            catch { ok = "OK"; }

            // Modal is a single slot and the game closes the open one before reusing it
            // so opening over it would drop the previous onClose
            if (Modal.IsOpen)
                Modal.CloseModal();

            Modal.OpenModal(
                new DefaultHeaderModalOption(title, body),
                new ModalButtonsOption(new[] { new ModalButtonsOption.Option(ok, null) }),
                null);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Modal failed: {ex.Message}. {title}: {body}");
            Debug.LogWarning($"[RoomHub] {title}: {body}");
        }
    }

    internal static void ShowFeedback(string title, string body) => ShowModal(title, body);
}
