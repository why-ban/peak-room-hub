using BepInEx.Configuration;
using UnityEngine;

namespace RoomHub;

public sealed class PluginConfig
{
    private readonly ConfigFile _file;

    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<bool> ShowMainMenuButton { get; }
    public ConfigEntry<bool> EnableHotkey { get; }
    public ConfigEntry<KeyCode> QuickJoinHotkey { get; }
    public ConfigEntry<KeyCode> ModMenuHotkey { get; }
    public ConfigEntry<float> ButtonPosX { get; }
    public ConfigEntry<float> ButtonPosY { get; }
    public ConfigEntry<bool> UseCustomButtonPos { get; }
    public ConfigEntry<int> MaxRecentRooms { get; }
    public ConfigEntry<int> HistoryExpireHours { get; }

    /// <summary>Hard cap met-players per room (not user-configurable).</summary>
    public const int MaxPlayersPerRoomFixed = 20;

    public PluginConfig(ConfigFile config)
    {
        _file = config;

        Enabled = config.Bind(
            "General",
            "Enable Mod (启用模组)",
            true,
            "Master switch.\n总开关。");

        ShowMainMenuButton = config.Bind(
            "UI",
            "Show Main Menu Button (显示主菜单按钮)",
            true,
            "Show Room Hub button on the title screen.\n主界面显示「房间中心」按钮。");

        UseCustomButtonPos = config.Bind(
            "UI",
            "Use Custom Button Position (使用自定义按钮位置)",
            false,
            "When true, Button Pos X/Y are applied. Also set automatically after right-click drag on the title button.\n为 true 时使用下方 XY。在主界面右键拖动按钮后会自动开启。");

        ButtonPosX = config.Bind(
            "UI",
            "Button Pos X (按钮X坐标)",
            0f,
            new ConfigDescription(
                "Anchored X relative to the right-middle of MainPage (negative = inward). Right-click-drag also saves this.\n相对主界面右中锚点的 X（负值向内）。也可右键拖动按钮保存。",
                new AcceptableValueRange<float>(-2000f, 2000f)));

        ButtonPosY = config.Bind(
            "UI",
            "Button Pos Y (按钮Y坐标)",
            0f,
            new ConfigDescription(
                "Anchored Y relative to the right-middle of MainPage. Right-click-drag also saves this.\n相对主界面右中锚点的 Y。也可右键拖动按钮保存。",
                new AcceptableValueRange<float>(-2000f, 2000f)));

        EnableHotkey = config.Bind(
            "Hotkey",
            "Enable Hotkey (启用快捷键)",
            true,
            "Master switch for both hotkeys below.\n下方两个快捷键的总开关（不是第三个按键）。");

        // Exactly two keys: quick join (title) and mod menu (title AND in-game)
        QuickJoinHotkey = config.Bind(
            "Hotkey",
            "快速加入快捷键 (Quick Join Hotkey)",
            KeyCode.J,
            "Title screen: rejoin most recent room. Default J.\n主界面：按 J 快速加入最近房间。默认 J。");

        ModMenuHotkey = config.Bind(
            "Hotkey",
            "模组菜单快捷键 (Mod Menu Hotkey)",
            KeyCode.K,
            "Title screen and in-game: open/close Room Hub panel. Default K.\n主界面与局内均可：打开/关闭房间中心面板。默认 K。");

        MaxRecentRooms = config.Bind(
            "History",
            "Max Recent Rooms (最多房间数)",
            10,
            new ConfigDescription(
                "Max rooms kept. When over limit, oldest rooms are removed first.\n最多保留房间数。超出时优先删除最旧的房间。",
                new AcceptableValueRange<int>(1, 50)));

        HistoryExpireHours = config.Bind(
            "History",
            "History Expire Hours (过期小时数)",
            168,
            new ConfigDescription(
                "Auto-remove rooms older than this many hours. Default 168 = 7 days.\n超过该小时数自动清除。默认 168 = 7 天。",
                new AcceptableValueRange<int>(1, 720)));

        // Max players per room is a fixed constant (MaxPlayersPerRoomFixed = 20), not a config entry
        // Rooms you host are always recorded: adding a stranger you invited is a core use case
    }

    /// <summary>Persist a new ribbon position (enables custom pos for next launch).</summary>
    public void SaveButtonPosition(float x, float y)
    {
        ButtonPosX.Value = x;
        ButtonPosY.Value = y;
        UseCustomButtonPos.Value = true;
        try { _file.Save(); } catch { /* ignore */ }
    }
}
