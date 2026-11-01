using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoomHub;

[BepInDependency("com.github.PEAKModding.PEAKLib.Core", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("com.github.PEAKModding.PEAKLib.UI", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("com.github.PEAKModding.PEAKLib.ModConfig", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.roomhub.RoomHub";
    public const string PluginName = "Room Hub";
    public const string PluginVersion = "3.0.1";

    internal static Plugin Instance { get; private set; } = null!;
    internal static ManualLogSource Log { get; private set; } = null!;
    internal static PluginConfig ModConfig { get; private set; } = null!;
    internal static HistoryStore Store { get; private set; } = null!;

    private Harmony? _harmony;
    private TitleMenuController? _titleUi;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        ModConfig = new PluginConfig(Config);

        // history load resolves placeholder names through Loc, so the table must exist first
        Loc.Register();

        Store = new HistoryStore(Paths.ConfigPath, ModConfig);
        Store.Load();
        SteamAvatarService.Init();
        FriendRoomsService.Init();
        LobbyJoinFeedback.Init();
        PauseMenuHook.Register();

        ApplyPatches();

        SceneManager.sceneLoaded += OnSceneLoaded;
        Log.LogInfo($"{PluginName} v{PluginVersion} loaded. History={Store.StoragePath}");
    }

    /// <summary>
    /// Harmony resolves string targets at runtime, so one renamed private game method would
    /// otherwise throw out of <see cref="Awake"/> and skip every patch class after it
    /// </summary>
    private void ApplyPatches()
    {
        _harmony = new Harmony(PluginGuid);

        int applied = 0;
        foreach (var type in typeof(Plugin).Assembly.GetTypes())
        {
            if (!IsPatchClass(type))
                continue;

            try
            {
                _harmony.PatchAll(type);
                applied++;
            }
            catch (Exception ex)
            {
                Log.LogError($"Harmony patch {type.FullName} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (applied == 0)
            Log.LogError("Harmony: no patch class applied.");
        else
            Log.LogInfo($"Harmony: {applied} patch class(es) applied.");
    }

    private static bool IsPatchClass(Type type)
    {
        if (!type.IsClass || type.IsGenericTypeDefinition)
            return false;
        if (type.IsDefined(typeof(HarmonyPatch), inherit: false))
            return true;

        foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Instance
                                               | BindingFlags.Public | BindingFlags.NonPublic
                                               | BindingFlags.DeclaredOnly))
        {
            if (method.IsDefined(typeof(HarmonyPatch), inherit: false))
                return true;
        }

        return false;
    }

    private void OnDestroy()
    {
        try { Store?.Flush(); } catch { /* ignore */ }
        SceneManager.sceneLoaded -= OnSceneLoaded;
        HistoryUi.DropPage();
        // A throwing unpatch would skip every Shutdown below it
        try { _harmony?.UnpatchSelf(); } catch (Exception ex) { Log.LogDebug($"UnpatchSelf: {ex.Message}"); }
        SteamAvatarService.Shutdown();
        PlayerTracker.Shutdown();
        FriendRoomsService.Shutdown();
        LobbyJoinFeedback.Shutdown();
        PauseMenuHook.ReleaseHeldTimeScale();
        PauseMenuHook.Unregister();
    }

    private void OnApplicationQuit()
    {
        try { Store?.Flush(); } catch { /* ignore */ }
    }

    private void Update()
    {
        HistoryUi.Tick();
        if (!HistoryUi.IsOpen)
            PauseMenuHook.ReleaseHeldTimeScale();
        try { Store?.TickDeferredSave(Time.unscaledTime); } catch { /* ignore */ }
        PlayerTracker.TickHostPoll(Time.unscaledTime);

        if (!ModConfig.Enabled.Value || !ModConfig.EnableHotkey.Value)
            return;

        // Don't steal keys while typing in search (or any TMP/UGUI input)
        if (HistoryUi.IsTextInputFocused())
            return;

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
            return;

        bool title = string.Equals(scene.name, "Title", StringComparison.Ordinal);
        var quickKey = ModConfig.QuickJoinHotkey.Value;
        var menuKey = ModConfig.ModMenuHotkey.Value;

        if (title)
        {
            if (quickKey != KeyCode.None && Input.GetKeyDown(quickKey))
                RoomJoiner.TryJoinMostRecent(showEmptyFeedback: true);
            if (menuKey != KeyCode.None && Input.GetKeyDown(menuKey))
                HistoryUi.Toggle();
            return;
        }

        if (IsFullscreenUiActive())
            return;

        if (menuKey != KeyCode.None && Input.GetKeyDown(menuKey))
            HistoryUi.Toggle();
    }

    internal static bool IsFullscreenUiActive()
    {
        try
        {
            if (LoadingScreenHandler.loading)
                return true;

            var gui = GUIManager.instance;
            if (gui != null && gui.endScreen != null && gui.endScreen.isOpen)
                return true;

            // The end screen only opens 13 s into the cutscene, so the cinematic flag is the
            // only signal available while the cutscene itself is playing
            var peak = Zorro.Core.Singleton<PeakHandler>.Instance;
            return peak != null && peak.isPlayingCinematic;
        }
        catch { /* game API may vary */ }
        return false;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Always drop/hide history UI across loads so PEAKLib page GOs are not stale
        HistoryUi.OnSceneLoaded();
        PauseMenuHook.ReleaseHeldTimeScale();

        if (scene.name != "Title")
            return;

        if (!ModConfig.Enabled.Value || !ModConfig.ShowMainMenuButton.Value)
            return;

        if (_titleUi == null)
            _titleUi = gameObject.GetComponent<TitleMenuController>() ?? gameObject.AddComponent<TitleMenuController>();

        _titleUi.OnTitleSceneLoaded();
    }
}
