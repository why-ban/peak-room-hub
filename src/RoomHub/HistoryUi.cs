using System;
using HarmonyLib;
using RoomHub.Ui;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoomHub;

/// <summary>
/// Shared entry for opening Room Hub from the title ribbon, the pause menu, or the in-game hotkey.
/// </summary>
internal static class HistoryUi
{
    private static HistoryPageController? _page;

    /// <summary>
    /// One-frame latch consumed by <see cref="PauseInputSuppressor"/>. It must be sampled before ESC
    /// handling: the same frame's ESC closes the page, and the game's pause action has to stay
    /// suppressed for that frame only.
    /// </summary>
    internal static bool PageOpenThisFrame { get; private set; }

    public static bool IsOpen => _page != null && _page.IsPageOpen;

    public static bool IsTitleScene
    {
        get
        {
            var scene = SceneManager.GetActiveScene();
            return scene.IsValid() && string.Equals(scene.name, "Title", StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// True when the local player is inside a Steam multiplayer lobby. Read through the game's
    /// matchmaking facade, which degrades to NoMatchmaking instead of throwing when Steam is absent.
    /// </summary>
    public static bool IsInLiveLobby
    {
        get
        {
            try
            {
                var matchmaking = Peak.Network.NetCode.Matchmaking;
                return matchmaking != null && matchmaking.InLobby;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Invite only when not on the title screen and currently in a Steam lobby.</summary>
    public static bool CanShowInvite => !IsTitleScene && IsInLiveLobby;

    public static void Open()
    {
        try
        {
            // the hotkey can fire while pause is open, and both UIs must not stack
            PauseMenuHook.ClosePauseMenuIfOpen();
            EnsureController();
            _page!.Open();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"HistoryUi.Open: {ex}");
            DropPage();
        }
    }

    public static void Hide()
    {
        try { _page?.Hide(); }
        catch { /* destroyed page */ }
    }

    public static void Toggle()
    {
        if (IsOpen)
            Hide();
        else
            Open();
    }

    /// <summary>Drops the page so the next Open recreates it after a scene unload.</summary>
    public static void DropPage()
    {
        // a page that still exists is destroyed only at the end of this frame, so its Images would
        // briefly point at destroyed sprites; keep the cache and let the next drop release it
        bool wasLive = _page != null && _page.HasLivePage;

        try { _page?.Discard(); }
        catch { /* ignore */ }
        _page = null;

        if (!wasLive)
            UiSprites.Shutdown();
    }

    /// <summary>Call on every scene load: PEAKLib page objects never survive a scene change.</summary>
    public static void OnSceneLoaded()
    {
        DropPage();
    }

    public static void Tick()
    {
        PageOpenThisFrame = _page != null && _page.IsPageOpen;

        if (_page == null)
            return;

        if (!_page.HasLivePage)
        {
            DropPage();
            return;
        }

        if (!_page.IsPageOpen)
            return;

        if (Input.GetKeyDown(KeyCode.Escape) || ConsumeGamepadBack())
        {
            _page.HandleBackOrClose();
            return;
        }

        _page.TickSelection();
        _page.TickAvatarRefresh();
    }

    /// <summary>
    /// Gamepad back. The panel turns off MenuWindow's own cancel and pause handling so it can drive
    /// its own back stack, which leaves the UICancel action and the pause button unread. Both are
    /// sampled every frame by the game, so they must be cleared here or they stay latched.
    /// </summary>
    private static bool ConsumeGamepadBack()
    {
        try
        {
            var ui = Zorro.Core.Singleton<UIInputHandler>.Instance;
            if (ui != null && ui.cancelWasPressed)
            {
                ui.cancelWasPressed = false;
                return true;
            }
        }
        catch
        {
            // no UIInputHandler on the title scene
        }

        try
        {
            var character = Character.localCharacter;
            if (character != null && character.input.pauseWasPressed)
            {
                character.input.pauseWasPressed = false;
                return true;
            }
        }
        catch
        {
            // no local character outside a run
        }

        return false;
    }

    /// <summary>
    /// True while the panel's search field has TMP focus. Deliberately not derived from the
    /// EventSystem selection: TMP_InputField.DeactivateInputField leaves that selection in place and
    /// NavigationContainerHandler keeps it on purpose, which made this answer "true" forever and
    /// killed the mod hotkeys (audit R2-01).
    /// </summary>
    public static bool IsTextInputFocused()
    {
        try
        {
            return _page != null && _page.IsSearchFocused;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureController()
    {
        if (_page != null && !_page.HasLivePage)
            DropPage();
        _page ??= new HistoryPageController();
    }
}

/// <summary>
/// Swallows the game's pause action on the frame ESC closes Room Hub, otherwise the press would
/// close the panel and open the pause menu at once. One-frame latch: pauseWasPressed is re-sampled
/// every frame, so nothing accumulates.
/// </summary>
[HarmonyPatch(typeof(GUIManager), nameof(GUIManager.UpdatePaused))]
internal static class PauseInputSuppressor
{
    [HarmonyPrefix]
    internal static bool Prefix() => !HistoryUi.PageOpenThisFrame;
}
