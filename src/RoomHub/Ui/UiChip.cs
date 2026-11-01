using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoomHub.Ui;

/// <summary>Small rounded capsule used for host / friend / live / muted badges.</summary>
internal sealed class UiChip
{
    public readonly RectTransform Rect;

    private readonly Image _bg;
    private readonly Image _line;
    private readonly TextMeshProUGUI _label;
    private ChipKind _kind;

    private UiChip(RectTransform rect, Image bg, Image line, TextMeshProUGUI label, ChipKind kind)
    {
        Rect = rect;
        _bg = bg;
        _line = line;
        _label = label;
        _kind = kind;
    }

    public float Width => Rect.sizeDelta.x;

    public static UiChip Create(Transform parent, string name, string text, ChipKind kind)
    {
        var root = UiKit.Rect(parent, name);
        root.sizeDelta = new Vector2(0f, UiTheme.ChipHeight);
        // a long badge label would otherwise draw outside the capsule
        root.gameObject.AddComponent<RectMask2D>();

        var line = UiKit.Panel(root, "Line", UiTheme.ChipLineColor(kind), UiTheme.ChipRadius);
        UiKit.Stretch(line.rectTransform);

        var bg = UiKit.Panel(root, "Bg", UiTheme.ChipBack(kind), UiTheme.ChipRadius - 1f);
        UiKit.Stretch(bg.rectTransform);
        bg.rectTransform.offsetMin = new Vector2(1f, 1f);
        bg.rectTransform.offsetMax = new Vector2(-1f, -1f);

        var label = UiKit.Label(root, "Text", "", UiTheme.ChipSize, UiTheme.ChipTextColor(kind), FontStyles.Normal);
        UiKit.Stretch(label.rectTransform);
        label.alignment = TextAlignmentOptions.Center;

        var chip = new UiChip(root, bg, line, label, kind);
        chip.SetText(text, kind);
        return chip;
    }

    public void SetText(string text, ChipKind kind)
    {
        string value = text ?? "";
        // preferredWidth runs a full shaping pass, so an unchanged badge costs nothing
        if (_kind == kind && string.Equals(_label.text, value, StringComparison.Ordinal))
            return;

        _kind = kind;
        _label.text = value;
        _label.color = UiTheme.ChipTextColor(kind);
        _bg.color = UiTheme.ChipBack(kind);
        _line.color = UiTheme.ChipLineColor(kind);
        float width = Mathf.Clamp(_label.preferredWidth + UiTheme.ChipPadX * 2f, 44f, 260f);
        Rect.sizeDelta = new Vector2(width, UiTheme.ChipHeight);
    }

    public void SetKind(ChipKind kind)
    {
        SetText(_label.text, kind);
    }

    public ChipKind Kind => _kind;

    public void SetVisible(bool visible)
    {
        if (Rect.gameObject.activeSelf != visible)
            Rect.gameObject.SetActive(visible);
    }
}
