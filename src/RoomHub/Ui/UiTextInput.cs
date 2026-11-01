using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoomHub.Ui;

/// <summary>
/// Search field built from scratch. PEAKLib's PeakTextInput clones a game settings cell and throws
/// when that prefab is not loaded yet, which used to leave a dead Search button (audit-ui UI-13).
/// </summary>
internal sealed class UiTextInput
{
    public readonly RectTransform Rect;
    public readonly TMP_InputField Field;

    private readonly TextMeshProUGUI _placeholder;

    private UiTextInput(RectTransform rect, TMP_InputField field, TextMeshProUGUI placeholder)
    {
        Rect = rect;
        Field = field;
        _placeholder = placeholder;
    }

    public string Text
    {
        get => Field.text ?? "";
        set => Field.text = value ?? "";
    }

    public bool IsFocused => Field.isFocused;

    public static UiTextInput Create(Transform parent, string name, float width, float height, Action? onSubmit)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.SetActive(false);
        root.layer = parent.gameObject.layer;
        root.transform.SetParent(parent, false);

        var rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(width, height);

        var line = root.AddComponent<Image>();
        line.sprite = UiSprites.RoundedSprite(UiTheme.SearchRadius);
        line.type = Image.Type.Sliced;
        line.color = UiTheme.FieldBorder;
        line.raycastTarget = true;

        var fill = UiKit.Panel(rect, "Fill", UiTheme.FieldBg, UiTheme.SearchRadius - 1f);
        UiKit.Stretch(fill.rectTransform);
        fill.rectTransform.offsetMin = new Vector2(2f, 2f);
        fill.rectTransform.offsetMax = new Vector2(-2f, -2f);

        var area = UiKit.Rect(rect, "Text Area");
        area.anchorMin = Vector2.zero;
        area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(18f, 0f);
        area.offsetMax = new Vector2(-18f, 0f);
        area.gameObject.AddComponent<RectMask2D>();

        var text = UiKit.Label(area, "Text", "", UiTheme.SearchSize, UiTheme.Paper, FontStyles.Normal);
        UiKit.Stretch(text.rectTransform);
        text.alignment = TextAlignmentOptions.MidlineLeft;

        var placeholder = UiKit.Label(area, "Placeholder", "", UiTheme.SearchSize, UiTheme.TextFaint, FontStyles.Normal);
        UiKit.Stretch(placeholder.rectTransform);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;

        var field = root.AddComponent<TMP_InputField>();
        field.targetGraphic = line;
        field.textViewport = area;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = 48;
        field.caretWidth = 2;
        field.customCaretColor = true;
        field.caretColor = UiTheme.Paper;
        field.selectionColor = new Color(0.66f, 0.28f, 0.06f, 0.5f);
        field.navigation = new Navigation { mode = Navigation.Mode.Explicit };
        if (onSubmit != null)
            field.onSubmit.AddListener(_ => onSubmit());

        root.SetActive(true);
        return new UiTextInput(rect, field, placeholder);
    }

    public void SetPlaceholder(string text)
    {
        if (_placeholder.text != text)
            _placeholder.text = text;
    }

    public void Deactivate()
    {
        if (Field.isFocused)
            Field.DeactivateInputField();
    }

    public void SetVisible(bool visible)
    {
        if (Rect.gameObject.activeSelf != visible)
            Rect.gameObject.SetActive(visible);
    }
}
