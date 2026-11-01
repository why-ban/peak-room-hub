using PEAKLib.UI.Elements;
using UnityEngine;
using UnityEngine.UI;
using Zorro.Core;
using Zorro.UI.Modal;

namespace RoomHub.Ui;

/// <summary>
/// The panel shell. A PeakCustomPage subclass is required for MenuWindow integration, and it is the
/// only way to supply <c>objectToSelectOnOpen</c>, which the base page never sets. That is why the
/// old panel could not be driven by a controller (audit-ui UI-01).
/// </summary>
internal sealed class RoomHubPage : PeakCustomPage
{
    private const int FallbackSortingOrder = 100;

    private Selectable? _initial;
    private RectTransform? _card;
    private bool _skipStartClosed;
    private Vector2 _laidOutSize = new Vector2(-1f, -1f);

    /// <summary>Raised after the card was resized, so the page can re-run width-dependent layout.</summary>
    public event System.Action? Resized;

    public override Selectable? objectToSelectOnOpen
    {
        get
        {
            if (_initial != null && _initial.IsInteractable() && _initial.gameObject.activeInHierarchy)
                return _initial;
            return null;
        }
    }

    public void SetCard(RectTransform card)
    {
        _card = card;
    }

    public void SetInitialSelection(Selectable? selectable)
    {
        _initial = selectable;
    }

    /// <summary>Lets the title-scene fallback survive MenuWindow.Start's StartClosed (audit-ui UI-07).</summary>
    public void SkipStartClosed()
    {
        _skipStartClosed = true;
    }

    protected override void Start()
    {
        if (_skipStartClosed)
        {
            _skipStartClosed = false;
            return;
        }

        base.Start();
    }

    protected override void Update()
    {
        base.Update();
        var size = ((RectTransform)transform).rect.size;
        if (size != _laidOutSize)
            Layout();
    }

    /// <summary>Card size for the current canvas rect (docs/ui-spec.md §4).</summary>
    public void Layout()
    {
        var size = ((RectTransform)transform).rect.size;
        _laidOutSize = size;

        if (_card != null)
        {
            float width = Mathf.Max(360f, Mathf.Min(UiTheme.CardMaxWidth, size.x - UiTheme.CardMargin * 2f));
            float height = Mathf.Max(260f, Mathf.Min(UiTheme.CardMaxHeight, size.y - UiTheme.CardMargin * 2f));
            _card.sizeDelta = new Vector2(width, height);
        }

        Resized?.Invoke();
    }

    /// <summary>
    /// Sorting order is derived at runtime: above the game's own overlay canvases, never above the
    /// game modal. PEAKLib hardcodes 1 and the old code guessed 500 (audit-ui-design R-02).
    /// </summary>
    public void ApplySorting(bool apply)
    {
        var canvas = GetComponent<Canvas>();
        if (canvas == null)
            return;

        // closing only reports the order: the scan is a whole-scene walk and its result is unused
        if (!apply)
        {
            Plugin.Log.LogDebug($"Room Hub canvas order {canvas.sortingOrder} on close.");
            return;
        }

        int modalOrder = int.MinValue;
        string modalName = "none";
        var modal = ModalRootCanvas();
        if (modal != null)
        {
            modalOrder = modal.sortingOrder;
            modalName = modal.name;
        }

        int best = int.MinValue;
        string bestName = "none";
        Consider(GuiRootCanvas(), canvas, ref best, ref bestName);

        try
        {
            foreach (var found in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Consider(found, canvas, ref best, ref bestName);
        }
        catch
        {
            // scanning is best effort, the fallback order is still usable
        }

        int reference = Mathf.Max(best, modalOrder);
        int order = reference == int.MinValue ? FallbackSortingOrder : reference - 1;
        if (modalOrder != int.MinValue && order >= modalOrder)
            order = modalOrder - 1;
        order = Mathf.Max(order, 1);

        if (apply)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = order;
        }

        Plugin.Log.LogDebug(
            $"Room Hub canvas order {canvas.sortingOrder} ({(apply ? "applied" : "kept")} {order}, " +
            $"reference {reference} '{bestName}', modal {modalOrder} '{modalName}')");
    }

    private static void Consider(Canvas? candidate, Canvas self, ref int best, ref string bestName)
    {
        if (candidate == null)
            return;

        var root = candidate.rootCanvas;
        if (root == null || root == self)
            return;
        if (root.renderMode != RenderMode.ScreenSpaceOverlay)
            return;

        int order = root.sortingOrder;
        if (order <= best)
            return;

        best = order;
        bestName = root.name;
    }

    private static Canvas? ModalRootCanvas()
    {
        var modal = RetrievableResourceSingleton<Modal>.GetInstanceWithoutCreating();
        if (modal == null)
            return null;

        var canvas = modal.GetComponentInChildren<Canvas>(true);
        return canvas == null ? null : canvas.rootCanvas;
    }

    private static Canvas? GuiRootCanvas()
    {
        var gui = GUIManager.instance;
        if (gui == null)
            return null;

        Canvas? best = null;
        best = Higher(best, gui.hudCanvas);
        best = Higher(best, gui.letterboxCanvas);
        best = Higher(best, gui.binocularOverlay);
        return best;
    }

    private static Canvas? Higher(Canvas? current, Canvas? candidate)
    {
        if (candidate == null)
            return current;
        if (current == null)
            return candidate;
        return candidate.sortingOrder > current.sortingOrder ? candidate : current;
    }
}
