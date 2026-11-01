using UnityEngine;

namespace RoomHub.Ui;

internal enum UiRole
{
    Primary,
    Success,
    Info,
    Danger,
    Ghost,
    Disabled,
}

internal enum ChipKind
{
    Host,
    Friend,
    Live,
    Mute,
}

/// <summary>
/// Design tokens for the Room Hub panel. Colours are the measured PEAK 3.0.1 palette
/// (docs/audit/ui-palette.md); type sizes are quantised to the sizes the game itself uses
/// {10,14,16,18,20,22,24,28,30,32,36} (docs/audit/ui-round2.md §1.1).
///
/// Every label rect is at least 1.75x its font size. The safe threshold is the borrowed font's
/// (ascentLine - descentLine) / pointSize, which is unknown at compile time and can exceed 1.5, so
/// the geometry stays well clear of it (docs/audit/ui-round2-defects.md §1.2).
/// </summary>
internal static class UiTheme
{
    public static readonly Color Paper = new Color(0.8742f, 0.8567f, 0.7615f, 1f);
    public static readonly Color TanBorder = new Color(0.7529f, 0.6745f, 0.5529f, 1f);
    public static readonly Color Divider = new Color(0.3396f, 0.2996f, 0.2419f, 1f);
    public static readonly Color WoodDark = new Color(0.4528f, 0.3527f, 0.2456f, 1f);
    public static readonly Color ChipBg = new Color(0.3585f, 0.271f, 0.2452f, 1f);
    public static readonly Color Gold = new Color(1f, 0.8009f, 0f, 1f);
    public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.2667f);
    public static readonly Color Scrim = new Color(0f, 0f, 0f, 0.8667f);

    // the measured darkPanel token (0.1792,0.1253,0.0905,0.73) is for small in-world chips; a
    // full-screen card at that alpha lets the scene wash the text out
    public static readonly Color CardFill = new Color(0.106f, 0.075f, 0.055f, 0.96f);
    public static readonly Color RowBg = new Color(0.055f, 0.04f, 0.03f, 0.72f);
    public static readonly Color RowBorder = new Color(0.7529f, 0.6745f, 0.5529f, 0.12f);

    // contrast against CardFill: paper 13.2:1, dim 6.6:1, faint 4.7:1 (WCAG AA needs 4.5:1)
    public static readonly Color TextDim = new Color(0.8742f, 0.8567f, 0.7615f, 0.68f);
    public static readonly Color TextFaint = new Color(0.8742f, 0.8567f, 0.7615f, 0.55f);
    public static readonly Color FieldBg = new Color(0f, 0f, 0f, 0.55f);
    public static readonly Color FieldBorder = new Color(0.7529f, 0.6745f, 0.5529f, 0.2f);
    public static readonly Color BannerBg = new Color(0.5189f, 0.1297f, 0.1718f, 0.3f);
    public static readonly Color BannerBorder = new Color(0.7453f, 0.2058f, 0f, 0.45f);
    public static readonly Color BannerText = new Color(1f, 0.7059f, 0.5882f, 1f);

    public const float CardMaxWidth = 1440f;
    public const float CardMaxHeight = 840f;
    public const float CardMargin = 70f;
    public const float CardRadius = 13f;
    public const float CardBorderWidth = 2f;

    // rhythm: outer 32 / inner 24 / between elements 16 / between rows 10
    public const float Pad = 32f;
    // header 114 = 12 top + title 64 + subtitle 32 + 6 gap before the rule
    public const float HeaderHeight = 114f;
    public const float TitleSize = 36f;
    public const float TitleHeight = 64f;
    public const float SubtitleSize = 18f;
    public const float SubtitleHeight = 32f;
    public const float RuleHeight = 3f;

    public const float TabsHeight = 92f;
    public const float TabsTopPad = 16f;
    public const float TabHeight = 67f;
    public const float TabMinWidth = 180f;
    public const float TabGap = 12f;

    public const float SearchWidth = 320f;
    public const float SearchHeight = 67f;
    public const float SearchRadius = 10f;
    public const float SearchSize = 18f;

    public const float RowHeight = 96f;
    public const float RowGap = 10f;
    public const float RowRadius = 13f;
    public const float RowPad = 32f;
    public const float AvatarGap = 24f;
    public const float ActionGap = 16f;
    public const float Line1Size = 24f;
    public const float Line2Size = 18f;
    public const float Line1Height = 42f;
    public const float Line2Height = 32f;
    public const float LineSpacing = 6f;

    public const float AvatarSize = 64f;
    public const float AvatarRadius = 11f;
    public const float AvatarInitialRatio = 0.38f;

    public const float FooterHeight = 96f;
    public const float HintSize = 16f;
    public const float HintHeight = 28f;

    public const float RailWidth = 500f;
    public const float RailLineWidth = 3f;
    public const float RailInset = 22f;
    public const float RailTitleSize = 22f;
    public const float RailTitleHeight = 40f;
    public const float RailHintSize = 16f;
    public const float RailHintHeight = 28f;
    public const float RailEmptySize = 18f;

    // the cloned game button is 150x67 with its border recipe tuned for 67; any other height
    // squeezes the bright edges (docs/audit/ui-round2.md §5.2)
    public const float ButtonHeight = 67f;
    public const float ButtonRadius = 11f;
    public const float ButtonTextSize = 24f;
    public const float ButtonPadX = 26f;
    public const float ButtonMaxWidth = 277f;
    public const float ButtonShadowDrop = 5f;
    public const float ButtonEdgeHeight = 7f;
    public const float ButtonEdgeInset = 12f;
    public const float ButtonEdgeOffset = 12f;
    public const float ButtonGap = 12f;

    /// <summary>Official button widths (docs/audit/ui-palette.md §4); widths snap up to one of these.</summary>
    private static readonly float[] ButtonWidthTiers = { 102.7f, 150f, 182f, 187.8f, 277f };

    public const float ChipHeight = 34f;
    public const float ChipRadius = 9f;
    public const float ChipSize = 16f;
    public const float ChipPadX = 13f;
    public const float DotSize = 10f;

    public const float BannerHeight = 60f;
    public const float BannerRadius = 10f;
    public const float BannerSize = 18f;

    public const float EmptyTitleSize = 24f;
    public const float EmptyTitleHeight = 42f;
    public const float EmptyHintSize = 18f;
    public const float EmptyHintHeight = 32f;

    public static Color PanelColor(UiRole role) => role switch
    {
        UiRole.Primary => new Color(0.6604f, 0.2775f, 0.0592f, 1f),
        UiRole.Success => new Color(0.1416f, 0.2925f, 0.1173f, 1f),
        UiRole.Info => new Color(0.0588f, 0.3598f, 0.6588f, 1f),
        UiRole.Danger => new Color(0.5189f, 0.1297f, 0.1718f, 1f),
        UiRole.Ghost => WoodDark,
        _ => new Color(0.30f, 0.27f, 0.24f, 0.9f),
    };

    public static Color BorderColor(UiRole role) => role switch
    {
        UiRole.Primary => new Color(0.8774f, 0.4371f, 0f, 1f),
        UiRole.Success => new Color(0.0512f, 0.434f, 0.115f, 1f),
        UiRole.Info => new Color(0f, 0.5177f, 0.8784f, 1f),
        UiRole.Danger => new Color(0.7453f, 0.2058f, 0f, 1f),
        UiRole.Ghost => new Color(1f, 1f, 1f, 0.1f),
        _ => new Color(1f, 1f, 1f, 0.05f),
    };

    public static Color ChipBack(ChipKind kind) => kind switch
    {
        ChipKind.Host => new Color(1f, 0.8009f, 0f, 0.15f),
        ChipKind.Friend => new Color(0f, 0.5177f, 0.8784f, 0.15f),
        ChipKind.Live => new Color(0.0512f, 0.434f, 0.115f, 0.2f),
        _ => new Color(0.7529f, 0.6745f, 0.5529f, 0.09f),
    };

    public static Color ChipTextColor(ChipKind kind) => kind switch
    {
        ChipKind.Host => Gold,
        ChipKind.Friend => new Color(0.4784f, 0.7765f, 1f, 1f),
        ChipKind.Live => new Color(0.5098f, 0.8706f, 0.4863f, 1f),
        _ => new Color(0.8742f, 0.8567f, 0.7615f, 0.72f),
    };

    public static Color ChipLineColor(ChipKind kind) => kind switch
    {
        ChipKind.Host => new Color(1f, 0.8009f, 0f, 0.34f),
        ChipKind.Friend => new Color(0f, 0.5177f, 0.8784f, 0.36f),
        ChipKind.Live => new Color(0.0512f, 0.434f, 0.115f, 0.48f),
        _ => new Color(0.7529f, 0.6745f, 0.5529f, 0.2f),
    };

    /// <summary>Snaps a required width up to the nearest official button width.</summary>
    public static float QuantizeButtonWidth(float required)
    {
        foreach (var tier in ButtonWidthTiers)
        {
            if (required <= tier)
                return tier;
        }
        return ButtonWidthTiers[ButtonWidthTiers.Length - 1];
    }
}
