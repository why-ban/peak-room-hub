using System.Collections.Generic;
using PEAKLib.UI;
using PEAKLib.UI.Elements;
using UnityEngine;
using UnityEngine.UI;

namespace RoomHub.Ui;

/// <summary>
/// Scroll area with rows positioned by hand. PEAKLib's VerticalLayoutGroup + ContentSizeFitter is
/// switched off: rows are pooled and re-positioned in place, so layout groups would only add a
/// rebuild per refresh (audit-ui UI-05) and a child-width policy that cannot be reasoned about.
/// </summary>
internal sealed class UiScrollView
{
    public readonly RectTransform Root;
    public readonly RectTransform Content;
    public readonly RectTransform Viewport;

    private UiScrollView(RectTransform root, RectTransform content)
    {
        Root = root;
        Content = content;
        Viewport = root;
    }

    public static UiScrollView Create(Transform parent, string name)
    {
        var scroll = MenuAPI.CreateScrollableContent(name);
        var root = ((Component)scroll).transform as RectTransform;
        ((Component)scroll).transform.SetParent(parent, false);

        var content = scroll.Content;
        var layout = content.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
            layout.enabled = false;
        var fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter != null)
            fitter.enabled = false;

        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 0f);

        return new UiScrollView(root!, content);
    }

    /// <summary>Places rows top-down and sizes the content so only real overflow scrolls.</summary>
    public void LayoutRows(IReadOnlyList<RectTransform> rows, float rowHeight, float gap)
    {
        float y = 0f;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, rowHeight);
            row.anchoredPosition = new Vector2(0f, -y);
            row.SetSiblingIndex(i);
            y += rowHeight + gap;
        }

        float height = rows.Count == 0 ? 0f : y - gap;
        Content.sizeDelta = new Vector2(0f, height);
    }

    /// <summary>Keeps the focused element inside the viewport (spec §6).</summary>
    public void EnsureVisible(RectTransform? target)
    {
        if (target == null)
            return;

        Vector3 local = Viewport.InverseTransformPoint(target.position);
        float top = local.y + target.rect.height * (1f - target.pivot.y);
        float bottom = local.y - target.rect.height * target.pivot.y;

        float delta = 0f;
        if (top > Viewport.rect.yMax)
            delta = Viewport.rect.yMax - top;
        else if (bottom < Viewport.rect.yMin)
            delta = Viewport.rect.yMin - bottom;

        if (Mathf.Abs(delta) < 0.5f)
            return;

        // ScrollRect only re-clamps inside its own drag/inertia path, so a scripted move has to
        // clamp itself or the list scrolls past its end
        float contentHeight = Content.rect.height;
        float viewportHeight = Viewport.rect.height;
        float maxY = Mathf.Max(0f, contentHeight - viewportHeight);
        float y = Mathf.Clamp(Content.anchoredPosition.y + delta, 0f, maxY);
        Content.anchoredPosition = new Vector2(Content.anchoredPosition.x, y);
    }

    public void ResetScroll()
    {
        Content.anchoredPosition = Vector2.zero;
    }
}
