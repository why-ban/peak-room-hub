using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoomHub.Ui;

/// <summary>
/// Builders for the plain panel primitives. Everything is constructed from code, with no game art and
/// no PEAKLib widgets (docs/ui-spec.md §0.2).
/// </summary>
internal static class UiKit
{
    public static RectTransform Rect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static Image Panel(Transform parent, string name, Color color, float radius)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.sprite = UiSprites.RoundedSprite(radius);
        image.type = Image.Type.Sliced;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>Rounded surface plus a 2px outline: outer image paints the line, inner one is inset.</summary>
    public static RectTransform Card(Transform parent, string name)
    {
        var root = Rect(parent, name);
        var line = Panel(root, "Line", new Color(UiTheme.TanBorder.r, UiTheme.TanBorder.g, UiTheme.TanBorder.b, 0.28f), UiTheme.CardRadius);
        Stretch(line.rectTransform);

        var fill = Panel(root, "Fill", UiTheme.CardFill, UiTheme.CardRadius - 1f);
        var fillRect = fill.rectTransform;
        Stretch(fillRect);
        fillRect.offsetMin = new Vector2(UiTheme.CardBorderWidth, UiTheme.CardBorderWidth);
        fillRect.offsetMax = new Vector2(-UiTheme.CardBorderWidth, -UiTheme.CardBorderWidth);
        return root;
    }

    public static TextMeshProUGUI Label(
        Transform parent,
        string name,
        string text,
        float size,
        Color color,
        FontStyles style,
        bool wrap = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);

        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.fontStyle = style;
        label.alignment = TextAlignmentOptions.Left;
        label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        // DO NOT switch this to Ellipsis, Truncate or Masking. When the rect is shorter than the
        // font's line box, TMP's Ellipsis path resets m_characterCount to 0 and ClearMesh() blanks
        // the entire label (TextMeshProUGUI.cs:2034-2042, :2681-2686); Truncate culls at the same
        // height test, and TMP silently falls back to Truncate when the font lacks U+2026 (:985).
        // Overflow never culls, so the text is always drawn and a RectMask2D does the clipping
        label.overflowMode = TextOverflowModes.Overflow;
        label.enableAutoSizing = false;
        label.raycastTarget = false;
        // names come from Steam and are never sanitised, so tags must not be interpreted
        label.richText = false;
        HistoryUiFont.Apply(label);
        return label;
    }

    /// <summary>
    /// Container that clips its children. A label cannot clip itself: MaskableGraphic only collects
    /// RectMask2D from its parents, so the mask has to sit one level above the text.
    /// </summary>
    public static RectTransform ClipBox(Transform parent, string name)
    {
        var box = Rect(parent, name);
        box.gameObject.AddComponent<RectMask2D>();
        return box;
    }

    public static Image Divider(Transform parent, string name)
    {
        var image = Panel(parent, name, UiTheme.Divider, 3f);
        image.raycastTarget = false;
        return image;
    }

    public static Image Dot(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = UiSprites.Circle();
        image.color = new Color(0.5098f, 0.8706f, 0.4863f, 1f);
        image.raycastTarget = false;
        var rect = image.rectTransform;
        rect.sizeDelta = new Vector2(UiTheme.DotSize, UiTheme.DotSize);
        return image;
    }

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    public static void Anchor(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    public static void SetActive(Component? component, bool active)
    {
        if (component == null)
            return;
        var go = component.gameObject;
        if (go.activeSelf != active)
            go.SetActive(active);
    }
}
