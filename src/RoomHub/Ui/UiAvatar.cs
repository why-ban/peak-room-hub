using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoomHub.Ui;

/// <summary>
/// Rounded avatar chip. The sprite is owned here: Steam swaps the cached texture underneath live
/// rows, so the row compares the current texture on every refresh and rebuilds the sprite.
/// </summary>
internal sealed class UiAvatar
{
    public readonly RectTransform Rect;

    private readonly Image _image;
    private readonly TextMeshProUGUI _initial;
    private Sprite? _sprite;
    private Texture2D? _texture;

    private UiAvatar(RectTransform rect, Image image, TextMeshProUGUI initial)
    {
        Rect = rect;
        _image = image;
        _initial = initial;
    }

    public static UiAvatar Create(Transform parent, string name, float size)
    {
        var root = UiKit.Panel(parent, name, UiTheme.ChipBg, UiTheme.AvatarRadius);
        root.raycastTarget = false;
        var rect = root.rectTransform;
        rect.sizeDelta = new Vector2(size, size);

        var mask = root.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        var image = UiKit.Panel(rect, "Image", Color.white, UiTheme.AvatarRadius);
        UiKit.Stretch(image.rectTransform);
        image.preserveAspect = true;
        image.enabled = false;

        var initial = UiKit.Label(rect, "Initial", "?", size * UiTheme.AvatarInitialRatio, new Color(0.8742f, 0.8567f, 0.7615f, 0.85f), FontStyles.Normal);
        UiKit.Stretch(initial.rectTransform);
        initial.alignment = TextAlignmentOptions.Center;

        return new UiAvatar(rect, image, initial);
    }

    public void SetName(string? displayName)
    {
        string trimmed = (displayName ?? "").Trim();
        string initial = trimmed.Length == 0 ? "?" : StringInfo.GetNextTextElement(trimmed);
        if (_initial.text != initial)
            _initial.text = initial;
    }

    public void SetTexture(Texture2D? texture)
    {
        if (texture != null && texture == _texture)
            return;
        if (texture == null && _texture == null)
            return;

        _texture = texture;

        if (_sprite != null)
        {
            var stale = _sprite;
            _sprite = null;
            _image.sprite = null;
            UnityEngine.Object.Destroy(stale);
        }

        if (texture != null)
        {
            _sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            _sprite.hideFlags = HideFlags.DontSave;
            _image.sprite = _sprite;
            _image.enabled = true;
            _initial.gameObject.SetActive(false);
        }
        else
        {
            _image.enabled = false;
            _initial.gameObject.SetActive(true);
        }
    }

    public void Dispose()
    {
        if (_sprite == null)
            return;
        var sprite = _sprite;
        _sprite = null;
        UnityEngine.Object.Destroy(sprite);
    }
}
