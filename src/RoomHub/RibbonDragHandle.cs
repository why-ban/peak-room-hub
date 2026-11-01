using UnityEngine;
using UnityEngine.EventSystems;

namespace RoomHub;

/// <summary>
/// Right-mouse drag on the title ribbon to reposition it.
/// Left click still opens Room History (Button.onClick).
/// Saves anchored X/Y into BepInEx config when drag ends.
/// </summary>
public sealed class RibbonDragHandle : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private const float EdgeInset = 4f;

    private RectTransform? _rect;
    private RectTransform? _parent;
    private Canvas? _canvas;
    private bool _dragging;
    private Vector2 _grabOffset;

    public void Init(RectTransform rect, RectTransform parent)
    {
        _rect = rect;
        _parent = parent;
        _canvas = rect.GetComponentInParent<Canvas>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right)
            return;
        if (_rect == null || _parent == null)
            return;
        if (!ScreenToParentLocal(eventData.position, out var local))
            return;

        _grabOffset = _rect.anchoredPosition - local;
        _dragging = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging || eventData.button != PointerEventData.InputButton.Right)
            return;
        if (_rect == null || _parent == null)
            return;
        if (!ScreenToParentLocal(eventData.position, out var local))
            return;

        _rect.anchoredPosition = ClampToParent(_parent, _rect, local + _grabOffset);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_dragging || eventData.button != PointerEventData.InputButton.Right)
            return;
        _dragging = false;
        if (_rect == null || _parent == null)
            return;

        // Clamp before saving: a stored position outside the canvas is reloaded on every launch
        var pos = ClampToParent(_parent, _rect, _rect.anchoredPosition);
        _rect.anchoredPosition = pos;
        Plugin.ModConfig.SaveButtonPosition(pos.x, pos.y);
        Plugin.Log.LogInfo($"Room History button position saved: ({pos.x:F1}, {pos.y:F1})");
    }

    /// <summary>
    /// Clamp into the parent rect, which already carries the CanvasScaler-derived canvas size
    /// (reference resolution 1920x1080 matched on height, so its width follows the aspect ratio)
    /// </summary>
    internal static Vector2 ClampToParent(RectTransform parent, RectTransform rect, Vector2 anchoredPosition)
    {
        var parentRect = parent.rect;
        var size = rect.rect.size;
        var pivot = rect.pivot;
        var scale = rect.localScale;

        float halfW = Mathf.Abs(size.x * scale.x);
        float halfH = Mathf.Abs(size.y * scale.y);

        // Anchor midpoint so stretched and point anchors resolve to the same reference point
        float anchorX = parentRect.xMin + (rect.anchorMin.x + rect.anchorMax.x) * 0.5f * parentRect.width;
        float anchorY = parentRect.yMin + (rect.anchorMin.y + rect.anchorMax.y) * 0.5f * parentRect.height;

        float minX = parentRect.xMin + EdgeInset - anchorX + pivot.x * halfW;
        float maxX = parentRect.xMax - EdgeInset - anchorX - (1f - pivot.x) * halfW;
        float minY = parentRect.yMin + EdgeInset - anchorY + pivot.y * halfH;
        float maxY = parentRect.yMax - EdgeInset - anchorY - (1f - pivot.y) * halfH;

        // A rect wider than the parent keeps its centre instead of collapsing to one edge
        float x = minX <= maxX ? Mathf.Clamp(anchoredPosition.x, minX, maxX) : (minX + maxX) * 0.5f;
        float y = minY <= maxY ? Mathf.Clamp(anchoredPosition.y, minY, maxY) : (minY + maxY) * 0.5f;
        return new Vector2(x, y);
    }

    private bool ScreenToParentLocal(Vector2 screen, out Vector2 local)
    {
        local = default;
        if (_parent == null)
            return false;

        Camera? cam = null;
        if (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = _canvas.worldCamera;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, screen, cam, out local);
    }
}
