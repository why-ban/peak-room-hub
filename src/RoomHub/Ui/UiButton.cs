using System;
using System.Collections.Generic;
using PEAKLib.UI.Elements;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoomHub.Ui;

/// <summary>
/// A button that clones the game's own menu button when a template can be resolved, and falls back to
/// a self-drawn one when it cannot. The fallback exists because the panel must open on every install:
/// PEAKLib's Templates.ButtonTemplate stays null when its scene hook fails, and the scene scan finds
/// nothing in a scene that has no menu button yet.
///
/// Width snaps to the game's own button widths so a row of buttons keeps the proportions the game
/// uses (docs/audit/ui-round2.md §5.3); height stays at the template's 67, because the cloned border
/// recipe is tuned for it (§5.2).
/// </summary>
internal sealed class UiButton
{
    private static bool _warnedNoTemplate;
    private static bool _loggedFontComparison;

    // preferredWidth is a full shaping pass; the label set is closed so the result is stable
    private static readonly Dictionary<string, float> WidthCache = new();

    private readonly PeakMenuButton? _peak;
    private readonly Image? _panel;
    private readonly Image? _edgeTop;
    private readonly Image? _edgeBottom;
    private readonly RectTransform _rect;
    private readonly Button _control;
    private readonly TextMeshProUGUI _text;

    private UiRole _role;
    private bool _tab;
    private bool _tabActive;
    private float _minWidth = 102.7f;

    private UiButton(
        PeakMenuButton? peak,
        RectTransform rect,
        Button control,
        TextMeshProUGUI text,
        Image? panel,
        Image? edgeTop,
        Image? edgeBottom,
        UiRole role)
    {
        _peak = peak;
        _rect = rect;
        _control = control;
        _text = text;
        _panel = panel;
        _edgeTop = edgeTop;
        _edgeBottom = edgeBottom;
        _role = role;
    }

    public RectTransform Rect => _rect;

    public Button Control => _control;

    public TextMeshProUGUI Text => _text;

    public GameObject GameObject => _rect.gameObject;

    public float Width => _rect.sizeDelta.x;

    public static UiButton Create(Transform parent, string name, string label, UiRole role)
    {
        var native = TryCreateNative(parent, name);
        var button = native != null
            ? new UiButton(native.Value.Peak, native.Value.Peak.RectTransform, native.Value.Peak.Button, native.Value.Text, null, null, null, role)
            : CreateDrawn(parent, name, role);

        button.SetLabel(label ?? "");
        button.Repaint();
        return button;
    }

    private static (PeakMenuButton Peak, TextMeshProUGUI Text)? TryCreateNative(Transform parent, string name)
    {
        try
        {
            var peak = UiButtonFactory.CreateMenuButtonSafe(name);
            if (peak == null || peak.Button == null || peak.RectTransform == null || peak.Text == null)
                throw new InvalidOperationException("template clone is missing Panel/Text/Border children");

            peak.gameObject.name = name;
            peak.transform.SetParent(parent, false);

            var text = (TextMeshProUGUI)peak.Text;
            // auto sizing would make preferredWidth depend on the last layout pass
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.fontStyle = FontStyles.UpperCase;

            LogFontComparison(text);
            return (peak, text);
        }
        catch (Exception ex)
        {
            if (!_warnedNoTemplate)
            {
                _warnedNoTemplate = true;
                Plugin.Log.LogWarning($"UiButton: no game button template ({ex.GetType().Name}: {ex.Message}) — drawing buttons instead.");
            }
            return null;
        }
    }

    /// <summary>Open question from the review: do the clone and the labels use the same face?</summary>
    private static void LogFontComparison(TextMeshProUGUI text)
    {
        if (_loggedFontComparison)
            return;
        _loggedFontComparison = true;

        var labelFont = HistoryUiFont.Resolve();
        Plugin.Log.LogInfo(
            $"UI font: button={(text.font != null ? text.font.name : "null")} label={(labelFont != null ? labelFont.name : "null")}");
    }

    private static UiButton CreateDrawn(Transform parent, string name, UiRole role)
    {
        var root = UiKit.Rect(parent, name);
        root.sizeDelta = new Vector2(150f, UiTheme.ButtonHeight);

        var shadow = UiKit.Panel(root, "Shadow", UiTheme.Shadow, UiTheme.ButtonRadius);
        UiKit.Stretch(shadow.rectTransform);
        shadow.rectTransform.anchoredPosition = new Vector2(0f, -UiTheme.ButtonShadowDrop);

        var panel = UiKit.Panel(root, "Panel", UiTheme.PanelColor(role), UiTheme.ButtonRadius);
        UiKit.Stretch(panel.rectTransform);
        panel.raycastTarget = true;

        var edgeTop = UiKit.Panel(root, "EdgeTop", UiTheme.BorderColor(role), 3f);
        var topRect = edgeTop.rectTransform;
        topRect.anchorMin = new Vector2(0f, 1f);
        topRect.anchorMax = new Vector2(1f, 1f);
        topRect.pivot = new Vector2(0.5f, 1f);
        topRect.anchoredPosition = new Vector2(0f, -UiTheme.ButtonEdgeOffset);
        topRect.sizeDelta = new Vector2(-UiTheme.ButtonEdgeInset * 2f, UiTheme.ButtonEdgeHeight);

        var edgeBottom = UiKit.Panel(root, "EdgeBottom", UiTheme.BorderColor(role), 3f);
        var bottomRect = edgeBottom.rectTransform;
        bottomRect.anchorMin = new Vector2(0f, 0f);
        bottomRect.anchorMax = new Vector2(1f, 0f);
        bottomRect.pivot = new Vector2(0.5f, 0f);
        bottomRect.anchoredPosition = new Vector2(0f, UiTheme.ButtonEdgeOffset);
        bottomRect.sizeDelta = new Vector2(-UiTheme.ButtonEdgeInset * 2f, UiTheme.ButtonEdgeHeight);

        var text = UiKit.Label(root, "Text", "", UiTheme.ButtonTextSize, Color.white, FontStyles.UpperCase);
        UiKit.Stretch(text.rectTransform);
        text.alignment = TextAlignmentOptions.Center;

        var control = root.gameObject.AddComponent<Button>();
        control.targetGraphic = panel;
        control.transition = Selectable.Transition.ColorTint;
        control.navigation = new Navigation { mode = Navigation.Mode.Explicit };

        return new UiButton(null, root, control, text, panel, edgeTop, edgeBottom, role);
    }

    public void SetMinWidth(float width)
    {
        _minWidth = width;
    }

    public void SetLabel(string label)
    {
        string text = label ?? "";
        // the cloned template's Text may be fixed width rather than stretched, in which case its
        // sizeDelta is the button width and must not be mistaken for the horizontal inset
        float inset = Mathf.Clamp(Mathf.Abs(_text.rectTransform.sizeDelta.x), 52f, 96f);

        _peak?.SetText(text);
        _text.text = text;
        _rect.sizeDelta = new Vector2(MeasureWidth(text, inset), UiTheme.ButtonHeight);
    }

    private float MeasureWidth(string text, float inset)
    {
        string key = (_peak != null ? "n:" : "d:")
                     + Mathf.RoundToInt(_minWidth) + ":"
                     + Mathf.RoundToInt(inset) + ":"
                     + text;
        if (WidthCache.TryGetValue(key, out float cached))
            return cached;

        float required = Mathf.Max(_text.preferredWidth + inset + 8f, _minWidth);
        float width = UiTheme.QuantizeButtonWidth(required);
        WidthCache[key] = width;
        return width;
    }

    public void SetRole(UiRole role)
    {
        _role = role;
        _tab = false;
        Repaint();
    }

    /// <summary>Tab variant: the active tab uses the primary role, the idle one stays wood.</summary>
    public void SetTabActive(bool active)
    {
        _tab = true;
        _tabActive = active;
        Repaint();
    }

    public void SetInteractable(bool interactable)
    {
        _control.interactable = interactable;
        Repaint();
    }

    public void SetVisible(bool visible)
    {
        if (_rect.gameObject.activeSelf != visible)
            _rect.gameObject.SetActive(visible);
    }

    private void Repaint()
    {
        UiRole shown;
        if (!_control.interactable)
            shown = UiRole.Disabled;
        else if (_tab)
            shown = _tabActive ? UiRole.Primary : UiRole.Ghost;
        else
            shown = _role;

        if (_peak != null)
        {
            _peak.SetColor(UiTheme.PanelColor(shown), automaticBorderColor: false);
            _peak.SetBorderColor(UiTheme.BorderColor(shown));
        }
        else
        {
            _panel!.color = UiTheme.PanelColor(shown);
            var line = UiTheme.BorderColor(shown);
            _edgeTop!.color = line;
            _edgeBottom!.color = line;

            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.84f, 0.84f, 0.84f, 1f);
            colors.selectedColor = new Color(1.16f, 1.16f, 1.16f, 1f);
            colors.disabledColor = Color.white;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.05f;
            _control.colors = colors;
        }

        _text.color = _control.interactable ? Color.white : new Color(1f, 1f, 1f, 0.65f);
    }
}
