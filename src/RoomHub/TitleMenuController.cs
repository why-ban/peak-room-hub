using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoomHub;

/// <summary>
/// Title ribbon spawn / position / drag. History page lives in <see cref="HistoryPageController"/>.
/// </summary>
public sealed class TitleMenuController : MonoBehaviour
{
    private const string ButtonObjectName = "Button_RoomHub";
    private const string ButtonsContainerPath = "MainMenu/Canvas/MainPage/Menu/Buttons";
    private const string MainPagePath = "MainMenu/Canvas/MainPage";
    private const string TemplateButtonPath = "MainMenu/Canvas/MainPage/Menu/Buttons/Button_PlaySolo";
    private const string NextLevelPath = "MainMenu/Canvas/MainPage/NextLevelUI";
    private const float MinRightEdgeInset = 28f;
    private const float ScreenMarginPx = 16f;
    private const float TemplateReadyTimeoutSec = 8f;
    private const float PostReadySettleSec = 0.35f;

    private Coroutine? _spawnRoutine;
    private TextMeshProUGUI? _ribbonText;

    private void OnDestroy()
    {
        // The ribbon text is a static TMP label (the scene's LocalizedText component is
        // removed), so refresh it manually when the game language switches.
        LocalizationSync.Unregister(RefreshRibbonLabel);
    }

    public void OnTitleSceneLoaded()
    {
        HistoryUiFont.Reset();
        if (_spawnRoutine != null)
            StopCoroutine(_spawnRoutine);
        _spawnRoutine = StartCoroutine(CreateButtonWhenReady());
    }

    private IEnumerator CreateButtonWhenReady()
    {
        var existing = GameObject.Find(ButtonObjectName);
        if (existing != null)
            Destroy(existing);

        float deadline = Time.unscaledTime + TemplateReadyTimeoutSec;
        while (Time.unscaledTime < deadline)
        {
            var buttons = GameObject.Find(ButtonsContainerPath);
            var main = GameObject.Find(MainPagePath);
            var template = GameObject.Find(TemplateButtonPath);
            if (buttons != null && main != null && template != null && template.activeInHierarchy)
                break;
            yield return null;
        }

        var readyTemplate = GameObject.Find(TemplateButtonPath);
        if (readyTemplate == null || !readyTemplate.activeInHierarchy)
        {
            Plugin.Log.LogWarning("Title ribbon: PlaySolo template not ready in time.");
            yield break;
        }

        // Capture a fallback button template as early as possible: newer PEAK builds can leave
        // PEAKLib's Templates.ButtonTemplate null, which would break the history page buttons.
        UiButtonFactory.CaptureTemplate(readyTemplate);

        // Brief settle so MainPage layout/NextLevelUI positions are stable
        yield return new WaitForSeconds(PostReadySettleSec);
        yield return null;

        if (!Plugin.ModConfig.Enabled.Value || !Plugin.ModConfig.ShowMainMenuButton.Value)
            yield break;
        if (GameObject.Find(ButtonObjectName) != null)
            yield break;

        try { SpawnMainMenuButton(); }
        catch (Exception ex) { Plugin.Log.LogError($"Create button failed: {ex}"); }
    }

    private void SpawnMainMenuButton()
    {
        var template = GameObject.Find(TemplateButtonPath);
        var mainPage = GameObject.Find(MainPagePath);
        if (template == null || mainPage == null)
        {
            Plugin.Log.LogError("Missing PlaySolo or MainPage.");
            return;
        }

        var buttonObject = Instantiate(template, mainPage.transform, false);
        buttonObject.name = ButtonObjectName;
        buttonObject.SetActive(true);
        buttonObject.transform.SetAsLastSibling();

        var animator = buttonObject.GetComponent<Animator>();
        if (animator != null)
        {
            animator.Play("Normal", 0, 1f);
            animator.Update(0f);
            animator.enabled = false;
        }

        StartCoroutine(FinalizeButton(buttonObject, mainPage));
    }

    private IEnumerator FinalizeButton(GameObject buttonObject, GameObject mainPage)
    {
        yield return new WaitForEndOfFrame();
        yield return null;
        if (buttonObject == null) yield break;

        var buttonRect = buttonObject.GetComponent<RectTransform>();
        var parentRect = mainPage.GetComponent<RectTransform>();
        if (buttonRect == null || parentRect == null) yield break;

        buttonRect.SetParent(mainPage.transform, false);
        buttonRect.localRotation = Quaternion.identity;
        buttonRect.localScale = new Vector3(1f, 1.3f, 1f);
        buttonRect.anchorMin = new Vector2(1f, 0.5f);
        buttonRect.anchorMax = new Vector2(1f, 0.5f);
        buttonRect.pivot = new Vector2(1f, 0.5f);

        ApplyRibbonPosition(buttonRect, parentRect);
        // A saved custom position may come from a larger window, and ClampFullyOnScreen below
        // is skipped for custom positions, so clamp against the live canvas here as well
        buttonRect.anchoredPosition =
            RibbonDragHandle.ClampToParent(parentRect, buttonRect, buttonRect.anchoredPosition);

        var buttonText = buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);
        if (buttonText != null)
        {
            var oldLoc = buttonText.GetComponent<LocalizedText>();
            if (oldLoc != null) Destroy(oldLoc);
            buttonText.transform.localScale = new Vector3(0.9f, 0.7f, 0.9f);
            _ribbonText = buttonText;
            RefreshRibbonLabel();
            LocalizationSync.Register(RefreshRibbonLabel);
            if (buttonText.color.a < 0.2f)
                buttonText.color = Color.white;
            buttonText.alignment = TextAlignmentOptions.Center;
        }

        var btn = buttonObject.GetComponentInChildren<Button>(true);
        if (btn != null)
        {
            btn.interactable = true;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(OpenPage);
        }

        var drag = buttonObject.GetComponent<RibbonDragHandle>()
                   ?? buttonObject.AddComponent<RibbonDragHandle>();
        drag.Init(buttonRect, parentRect);

        Canvas.ForceUpdateCanvases();
        yield return new WaitForEndOfFrame();
        if (!Plugin.ModConfig.UseCustomButtonPos.Value)
        {
            ClampFullyOnScreen(buttonRect);
            yield return null;
            ClampFullyOnScreen(buttonRect);
        }

        Plugin.Log.LogInfo(
            $"Room History ribbon ready at {buttonRect.anchoredPosition} " +
            $"(custom={Plugin.ModConfig.UseCustomButtonPos.Value})");

        // Early template repair: on builds where PEAKLib failed to find its LeaveGame template,
        // capture our own so page/pause buttons still work (also restores PEAKLib for other mods).
        UiButtonFactory.EnsureTemplate();
    }

    private static void ApplyRibbonPosition(RectTransform buttonRect, RectTransform parentRect)
    {
        if (Plugin.ModConfig.UseCustomButtonPos.Value)
        {
            buttonRect.anchoredPosition = new Vector2(
                Plugin.ModConfig.ButtonPosX.Value,
                Plugin.ModConfig.ButtonPosY.Value);
            return;
        }

        float parentW = Mathf.Abs(parentRect.rect.width);
        float inset = Mathf.Max(MinRightEdgeInset, parentW * 0.02f);
        float y = 20f;
        var nextLevelUi = GameObject.Find(NextLevelPath);
        if (nextLevelUi != null)
        {
            var nextRt = nextLevelUi.GetComponent<RectTransform>();
            if (nextRt != null)
            {
                var nextWorld = nextRt.TransformPoint(nextRt.rect.center);
                var localInMain = parentRect.InverseTransformPoint(nextWorld);
                y = localInMain.y - (nextRt.rect.height * 0.5f) - 50f;
            }
        }

        buttonRect.anchoredPosition = new Vector2(-inset, y);
    }

    private static void ClampFullyOnScreen(RectTransform buttonRect)
    {
        var canvas = buttonRect.GetComponentInParent<Canvas>();
        Camera? cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        float margin = Mathf.Max(ScreenMarginPx, Mathf.Min(Screen.width, Screen.height) * 0.01f);
        for (int pass = 0; pass < 3; pass++)
        {
            var corners = new Vector3[4];
            buttonRect.GetWorldCorners(corners);
            float minSx = float.MaxValue, maxSx = float.MinValue, minSy = float.MaxValue, maxSy = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                var sp = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
                minSx = Mathf.Min(minSx, sp.x);
                maxSx = Mathf.Max(maxSx, sp.x);
                minSy = Mathf.Min(minSy, sp.y);
                maxSy = Mathf.Max(maxSy, sp.y);
            }

            float dx = 0f, dy = 0f;
            if (maxSx > Screen.width - margin) dx = (Screen.width - margin) - maxSx;
            else if (minSx < margin) dx = margin - minSx;
            if (maxSy > Screen.height - margin) dy = (Screen.height - margin) - maxSy;
            else if (minSy < margin) dy = margin - minSy;
            if (Mathf.Abs(dx) < 0.5f && Mathf.Abs(dy) < 0.5f) return;

            var parent = buttonRect.parent as RectTransform;
            if (parent == null) return;
            var screenCenter = new Vector2((minSx + maxSx) * 0.5f, (minSy + maxSy) * 0.5f);
            var screenShifted = screenCenter + new Vector2(dx, dy);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenCenter, cam, out var a)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenShifted, cam, out var b)) return;
            buttonRect.anchoredPosition += b - a;
            Canvas.ForceUpdateCanvases();
        }
    }

    private void OpenPage()
    {
        try
        {
            RefreshRibbonLabel();
            HistoryUi.Open();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"OpenPage: {ex}");
        }
    }

    private void RefreshRibbonLabel()
    {
        if (_ribbonText == null) return;
        _ribbonText.text = Loc.T(Loc.MainButton);
        _ribbonText.ForceMeshUpdate(true);
    }
}
