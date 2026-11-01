using System;
using System.Reflection;
using PEAKLib.UI;
using PEAKLib.UI.Elements;
using UnityEngine;
using UnityEngine.UI;

namespace RoomHub;

/// <summary>
/// Pink Room History button on pause main page, placed under the real Leave/Quit button
/// via <see cref="PauseMenuMainPage"/>'s serialized <c>m_quitButton</c> (not text matching).
/// </summary>
internal static class PauseMenuHook
{
    private const string ButtonObjectName = "UI_Pause_RoomHub";
    private static readonly Color ButtonPink = new Color(0.92f, 0.42f, 0.68f, 1f);

    private static readonly FieldInfo? QuitButtonField =
        typeof(PauseMenuMainPage).GetField("m_quitButton", BindingFlags.Instance | BindingFlags.NonPublic);

    private static bool _registered;
    private static bool _holdingPausedTimeScale;

    public static void Register()
    {
        if (_registered)
            return;
        _registered = true;

        try
        {
            MenuAPI.AddToPauseMenu(BuildPauseButton);
            LocalizationSync.Register(RefreshLabel);
            Plugin.Log.LogInfo("Pause menu Room History button registered.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Pause menu register failed: {ex.Message}");
        }
    }

    /// <summary>Plugin shutdown: drop the language-switch hook we registered.</summary>
    public static void Unregister()
    {
        LocalizationSync.Unregister(RefreshLabel);
        _registered = false;
    }

    /// <summary>Language switch: re-write the pink button label if the pause menu is live.</summary>
    private static void RefreshLabel()
    {
        try
        {
            // Our pause button is the only PeakMenuButton named this way. Include inactive, because
            // when the pause menu is closed the button GO is hidden, but a language switch must
            // still refresh its label so it shows up translated the next time the menu opens.
            foreach (var btn in UnityEngine.Object.FindObjectsByType<PeakMenuButton>(
                         UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
            {
                if (btn != null && ((Component)btn).gameObject != null
                    && ((Component)btn).name == ButtonObjectName)
                {
                    btn.SetText(Loc.T(Loc.MainButton));
                    return;
                }
            }
        }
        catch { /* pause menu may be closed / button not built yet */ }
    }

    private static void BuildPauseButton(Transform parent)
    {
        if (parent == null)
            return;

        try
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child != null && child.name == ButtonObjectName)
                    return;
            }

            var btn = UiButtonFactory.CreatePauseMenuButtonSafe(Loc.T(Loc.MainButton))
                .ParentTo(parent)
                .OnClick(OnPauseRoomHubClicked);

            var ourGo = ((Component)btn).gameObject;
            ourGo.name = ButtonObjectName;

            try { btn.SetColor(ButtonPink, true); }
            catch (Exception ex) { Plugin.Log.LogDebug($"Pause button style: {ex.Message}"); }

            PlaceUnderQuitButton(parent, ourGo.transform);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Pause menu button build failed: {ex.Message}");
        }
    }

    /// <summary>
    /// PEAKLib invokes builder with <c>PauseMenuMainPage.transform</c>.
    /// Quit control is private serialized <c>m_quitButton</c>, so use that, not localization text.
    /// </summary>
    private static void PlaceUnderQuitButton(Transform pageRoot, Transform ourButton)
    {
        try
        {
            var page = pageRoot.GetComponent<PauseMenuMainPage>()
                       ?? pageRoot.GetComponentInParent<PauseMenuMainPage>();
            if (page == null || QuitButtonField == null)
            {
                ourButton.SetAsLastSibling();
                Plugin.Log.LogWarning("PauseMenuMainPage.m_quitButton not found; button at end of list.");
                return;
            }

            var quitBtn = QuitButtonField.GetValue(page) as Button;
            if (quitBtn == null)
            {
                ourButton.SetAsLastSibling();
                return;
            }

            var quitT = quitBtn.transform;
            var quitParent = quitT.parent != null ? quitT.parent : pageRoot;

            // Same parent as quit so layout/anchors match the leave row
            ourButton.SetParent(quitParent, false);
            ourButton.SetSiblingIndex(quitT.GetSiblingIndex() + 1);

            // If parent uses a layout group, sibling index is enough, because absolute Y fights VLG
            bool parentHasLayout = quitParent.GetComponent<LayoutGroup>() != null;
            if (!parentHasLayout)
            {
                var quitRt = quitBtn.GetComponent<RectTransform>();
                var ourRt = ourButton as RectTransform ?? ourButton.GetComponent<RectTransform>();
                if (quitRt != null && ourRt != null)
                {
                    ourRt.anchorMin = quitRt.anchorMin;
                    ourRt.anchorMax = quitRt.anchorMax;
                    ourRt.pivot = quitRt.pivot;
                    ourRt.sizeDelta = quitRt.sizeDelta;
                    ourRt.localScale = quitRt.localScale;
                    ourRt.localRotation = quitRt.localRotation;

                    float gap = 8f;
                    float step = Mathf.Abs(quitRt.rect.height);
                    if (step < 1f)
                        step = Mathf.Abs(quitRt.sizeDelta.y);
                    if (step < 1f)
                        step = 56f;

                    ourRt.anchoredPosition = quitRt.anchoredPosition + new Vector2(0f, -(step + gap));
                }
            }

            if (quitParent is RectTransform prt)
                LayoutRebuilder.ForceRebuildLayoutImmediate(prt);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"PlaceUnderQuitButton: {ex.Message}");
            try { ourButton.SetAsLastSibling(); } catch { /* ignore */ }
        }
    }

    private static void OnPauseRoomHubClicked()
    {
        // The end cutscene keeps the pause menu reachable until EndScreen opens
        if (Plugin.IsFullscreenUiActive())
        {
            Plugin.Log.LogDebug("Room History: blocked during loading / end cutscene / end screen.");
            return;
        }

        // HistoryUi.Open closes pause itself so hotkey K and this button share one path
        HistoryUi.Open();
    }

    /// <summary>
    /// Same as official Resume: deactivate <see cref="PauseMenuHandler"/> so pause UI
    /// does not stack under Room History (hotkey K while paused, or pink pause button).
    /// A solo pause is held at <c>timeScale 0</c> until <see cref="ReleaseHeldTimeScale"/>.
    /// </summary>
    public static void ClosePauseMenuIfOpen()
    {
        try
        {
            // Only PauseMenuHandler, matching PauseMenuMainPage.OnResumeClicked.
            // Do not scan arbitrary UIPageHandler by name (too broad).
            var handler = UnityEngine.Object.FindFirstObjectByType<PauseMenuHandler>();
            if (handler == null)
                return;

            var go = ((Component)handler).gameObject;
            if (!go.activeInHierarchy)
                return;

            // PauseMenuHandler.OnDisable restores the gameplay scale, which would resume a solo
            // run behind the panel while GUIManager.UpdatePaused stays suppressed
            bool wasPaused = Time.timeScale <= 0.0001f;

            // PauseMenuMainPage.OnDisable closes confirmWindow when the handler deactivates
            go.SetActive(false);

            if (wasPaused)
            {
                Time.timeScale = 0f;
                _holdingPausedTimeScale = true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"ClosePauseMenuIfOpen: {ex.Message}");
        }
    }

    /// <summary>
    /// Hand the time scale back to the game once the panel is gone, matching what the game's
    /// own resume does. Closing the panel therefore resumes play rather than leaving a frozen
    /// world with no pause UI on screen.
    /// </summary>
    public static void ReleaseHeldTimeScale()
    {
        if (!_holdingPausedTimeScale)
            return;
        _holdingPausedTimeScale = false;

        try { Time.timeScale = GameUtils.GetTimeScale(); }
        catch { Time.timeScale = 1f; }
    }
}
