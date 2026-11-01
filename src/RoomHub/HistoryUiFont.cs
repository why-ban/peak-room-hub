using PEAKLib.UI.Elements;
using TMPro;
using UnityEngine;

namespace RoomHub;

/// <summary>
/// Font resolution for every label the panel builds. The game keeps one main font asset whose
/// <c>fallbackFontAssetTable[1]</c> is swapped for CN/TC/JP (FontFallbackSwapper.cs:31-41), so
/// borrowing it gives CJK coverage for free. Order: main base font, TMP default, scene label.
/// </summary>
internal static class HistoryUiFont
{
    private static TMP_FontAsset? _font;

    public static void Reset()
    {
        _font = null;
    }

    public static TMP_FontAsset? Resolve()
    {
        if (_font != null)
            return _font;

        var swapper = FontFallbackSwapper.instance;
        if (swapper != null && swapper.mainBaseFont != null)
        {
            Adopt(swapper.mainBaseFont, "FontFallbackSwapper.mainBaseFont");
            return _font;
        }

        var fallback = TMP_Settings.defaultFontAsset;
        if (fallback != null)
        {
            Adopt(fallback, "TMP_Settings.defaultFontAsset");
            return _font;
        }

        AdoptFromScene();
        return _font;
    }

    public static void Apply(TMP_Text? text)
    {
        if (text == null)
            return;

        var font = Resolve();
        if (font == null)
            return;
        if (text.font == font)
            return;

        text.font = font;
        if (font.material != null)
            text.fontSharedMaterial = font.material;
    }

    public static void Apply(PeakText? peakText)
    {
        Apply(peakText?.TextMesh);
    }

    private static void Adopt(TMP_FontAsset font, string source)
    {
        _font = font;
        Plugin.Log.LogInfo($"UI font from {source}: {font.name}");
    }

    private static void AdoptFromScene()
    {
        try
        {
            foreach (var label in UnityEngine.Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None))
            {
                if (label == null || label.font == null || string.IsNullOrEmpty(label.text))
                    continue;
                if (!label.gameObject.activeInHierarchy)
                    continue;
                Adopt(label.font, $"scene label {label.name}");
                return;
            }
        }
        catch
        {
            // scene probe is the last resort, a missing font only means TMP's own default
        }
    }
}
