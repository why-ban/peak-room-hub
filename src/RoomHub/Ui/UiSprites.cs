using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoomHub.Ui;

/// <summary>
/// Runtime-generated rounded-rect sprites: the panel loads no game art (docs/ui-spec.md §0.1).
/// Every sprite is 48x48 with a border equal to its radius so Image.Type.Sliced keeps the corner
/// arc crisp at any element size. Generation failure degrades to a square (null sprite), never to
/// a missing panel.
/// </summary>
internal static class UiSprites
{
    private const int Size = 48;
    private const int SubSamples = 4;

    private static readonly Dictionary<int, Sprite?> Rounded = new();
    private static readonly List<Texture2D> Textures = new();
    private static Sprite? _circle;

    public static Sprite? RoundedSprite(float radiusPx)
    {
        int key = Mathf.Clamp(Mathf.RoundToInt(radiusPx), 0, Size / 2);
        if (Rounded.TryGetValue(key, out var cached))
            return cached;

        var built = Build(key, circle: false);
        Rounded[key] = built;
        return built;
    }

    public static Sprite? Circle()
    {
        return _circle ??= Build(Size / 2, circle: true);
    }

    public static void Shutdown()
    {
        foreach (var kv in Rounded)
            DestroySprite(kv.Value);
        Rounded.Clear();

        DestroySprite(_circle);
        _circle = null;

        foreach (var tex in Textures)
        {
            if (tex != null)
                UnityEngine.Object.Destroy(tex);
        }
        Textures.Clear();
    }

    private static void DestroySprite(Sprite? sprite)
    {
        if (sprite != null)
            UnityEngine.Object.Destroy(sprite);
    }

    private static Sprite? Build(int radius, bool circle)
    {
        Texture2D? tex = null;
        try
        {
            tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };

            var pixels = new Color32[Size * Size];
            float half = Size * 0.5f;
            float r = circle ? half : radius;
            float inner = half - r;

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < SubSamples; sy++)
                    {
                        for (int sx = 0; sx < SubSamples; sx++)
                        {
                            float px = x + (sx + 0.5f) / SubSamples - half;
                            float py = y + (sy + 0.5f) / SubSamples - half;
                            if (Inside(px, py, half, inner, r))
                                hits++;
                        }
                    }

                    byte a = (byte)Mathf.RoundToInt(255f * hits / (SubSamples * SubSamples));
                    pixels[y * Size + x] = new Color32(255, 255, 255, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            Textures.Add(tex);

            float border = circle ? 0f : r;
            var sprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, Size, Size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"UiSprites: rounded sprite generation failed ({ex.GetType().Name}: {ex.Message}) — using square corners.");
            if (tex != null)
                UnityEngine.Object.Destroy(tex);
            return null;
        }
    }

    private static bool Inside(float px, float py, float half, float inner, float radius)
    {
        float ax = Mathf.Abs(px);
        float ay = Mathf.Abs(py);
        if (ax > half || ay > half)
            return false;

        float dx = Mathf.Max(ax - inner, 0f);
        float dy = Mathf.Max(ay - inner, 0f);
        return dx * dx + dy * dy <= radius * radius;
    }
}
