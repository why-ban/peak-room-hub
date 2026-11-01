using System;
using System.Reflection;
using PEAKLib.UI;
using PEAKLib.UI.Elements;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zorro.UI;
using Object = UnityEngine.Object;

namespace RoomHub;

/// <summary>
/// Safe replacement for <c>MenuAPI.CreateMenuButton</c>.
///
/// PEAKLib only populates <c>Templates.ButtonTemplate</c> from a scene button named
/// <c>"UI_MainMenuButton_LeaveGame (2)"</c> inside its <c>MainMenu.Start</c> postfix
/// (PEAKLib.UI.Hooks.PauseMenuHooks). Newer PEAK builds renamed/removed that object, so the
/// postfix throws, the template stays <c>null</c> forever, and every page button throws
/// "You're creating MenuButton too early! Prefab hasn't been loaded yet.", which is why the ribbon
/// kept working because it clones scene objects directly.
///
/// This factory keeps its own DontDestroyOnLoad copy of a real scene button and:
///   1) injects it back into <see cref="Templates"/> (restores PEAKLib and other consumers
///      such as the pause-menu button), and
///   2) falls back to its own copy when <c>MenuAPI.CreateMenuButton</c> still fails.
/// </summary>
internal static class UiButtonFactory
{
    private const string OwnedTemplateName = "RoomHubButtonTemplate";

    private static GameObject? _ownedTemplate;

    /// <summary>
    /// Register a known-good scene button as our fallback template. Called by the title ribbon
    /// as soon as it found the PlaySolo template, so the fallback exists even when the user
    /// disabled the ribbon or never opened the page on the title screen.
    /// </summary>
    public static void CaptureTemplate(GameObject source)
    {
        try
        {
            if (_ownedTemplate != null)
                return;
            if (source == null || !IsSuitableTemplate(source))
                return;
            _ownedTemplate = PrepareTemplate(source);
            // Only repair PEAKLib's template when it is actually missing, never overwrite the
            // canonical LeaveGame-derived one on healthy installs.
            if (Templates.ButtonTemplate == null)
                TryInjectIntoPeakLib(_ownedTemplate);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"UiButtonFactory.CaptureTemplate: {ex.Message}");
        }
    }

    /// <summary>Make sure a usable button template exists. Cheap when already resolved.
    /// Call early (title ribbon ready / before opening the page).
    /// </summary>
    public static void EnsureTemplate()
    {
        try
        {
            if (Templates.ButtonTemplate != null)
                return;

            if (_ownedTemplate != null)
            {
                TryInjectIntoPeakLib(_ownedTemplate);
                return;
            }

            var source = FindSceneButtonTemplate();
            if (source == null)
            {
                Plugin.Log.LogWarning("UiButtonFactory: no scene button template found; page buttons may fail.");
                return;
            }

            _ownedTemplate = PrepareTemplate(source);
            TryInjectIntoPeakLib(_ownedTemplate);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"UiButtonFactory.EnsureTemplate: {ex.Message}");
        }
    }

    /// <summary><c>MenuAPI.CreateMenuButton</c> with a scene-derived fallback when the PEAKLib template is missing.</summary>
    public static PeakMenuButton CreateMenuButtonSafe(string text)
    {
        try
        {
            return MenuAPI.CreateMenuButton(text);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"CreateMenuButtonSafe fallback ({ex.Message})");
            EnsureTemplate();
            if (_ownedTemplate == null)
                throw new InvalidOperationException(
                    "No button template available (PEAKLib and scene fallback both failed).");

            var go = Object.Instantiate(_ownedTemplate);
            go.name = "UI_MainMenuButton_" + text;
            var btn = go.AddComponent<PeakMenuButton>();
            btn.SetText(text);
            return btn;
        }
    }

    /// <summary><c>MenuAPI.CreatePauseMenuButton</c> equivalent with the same fallback.</summary>
    public static PeakMenuButton CreatePauseMenuButtonSafe(string text)
    {
        var btn = CreateMenuButtonSafe(text);
        try { btn.SetWidth(277f); } catch { /* ignore */ }
        return btn;
    }

    private static GameObject? FindSceneButtonTemplate()
    {
        // Prefer the same family PEAKLib used, then any suitably-shaped button
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go == null || !IsSuitableTemplate(go)) continue;
            if (IsOurs(go)) continue;
            if (go.name.StartsWith("UI_MainMenuButton_", StringComparison.Ordinal))
                return go;
        }

        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go == null || !IsSuitableTemplate(go)) continue;
            if (IsOurs(go)) continue;
            if (go.name.StartsWith("Button_", StringComparison.Ordinal))
                return go;
        }

        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go == null || !IsSuitableTemplate(go)) continue;
            if (IsOurs(go)) continue;
            if (go.name.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0)
                return go;
        }

        return null;
    }

    private static bool IsOurs(GameObject go)
    {
        return go.name.IndexOf("RoomHub", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// <see cref="PeakMenuButton.Awake"/> demands direct children Panel/Text/Border (+ a sibling
    /// after Border). Only accept templates that satisfy it so AddComponent cannot throw.
    /// </summary>
    private static bool IsSuitableTemplate(GameObject go)
    {
        var t = go.transform;
        // PeakMenuButton.Awake reads Button from the root object itself
        if (go.GetComponent<Button>() == null) return false;
        var panel = t.Find("Panel");
        if (panel == null || panel.GetComponent<Image>() == null) return false;
        var text = t.Find("Text");
        if (text == null || text.GetComponent<TextMeshProUGUI>() == null) return false;
        var border = t.Find("Border");
        if (border == null || border.GetComponent<Image>() == null) return false;
        // PeakMenuButton.Awake assigns the sibling right after Border to BorderBottom
        if (border.GetSiblingIndex() + 1 >= t.childCount) return false;
        var borderBottom = t.GetChild(border.GetSiblingIndex() + 1);
        return borderBottom != null && borderBottom.GetComponent<Image>() != null;
    }

    private static GameObject PrepareTemplate(GameObject source)
    {
        var template = Object.Instantiate(source);
        template.name = OwnedTemplateName;
        template.SetActive(true);

        // LocalizedText would re-translate (or clear) our labels; PEAKLib removes it too
        foreach (var loc in template.GetComponentsInChildren<LocalizedText>(true))
            Object.DestroyImmediate(loc);

        // Clone must not fire the original button's handlers
        var button = template.GetComponentInChildren<Button>(true);
        if (button != null)
            button.onClick.RemoveAllListeners();

        Object.DontDestroyOnLoad(template);
        Plugin.Log.LogInfo($"UiButtonFactory: captured button template from scene '{source.name}'.");
        return template;
    }

    /// <summary>
    /// PEAKLib <c>Templates.ButtonTemplate</c> setter is internal; restore it via reflection so
    /// MenuAPI and other consumers (pause-menu buttons, other mods) work again on builds where
    /// PEAKLib's own hook fails.
    /// </summary>
    private static void TryInjectIntoPeakLib(GameObject template)
    {
        try
        {
            var prop = typeof(Templates).GetProperty("ButtonTemplate", BindingFlags.Public | BindingFlags.Static);
            if (prop == null || !prop.CanWrite)
            {
                Plugin.Log.LogDebug("UiButtonFactory: Templates.ButtonTemplate not writable via reflection.");
                return;
            }
            prop.SetValue(null, template);
            Plugin.Log.LogInfo("UiButtonFactory: injected fallback button template into PEAKLib Templates.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"UiButtonFactory inject: {ex.Message}");
        }
    }
}
