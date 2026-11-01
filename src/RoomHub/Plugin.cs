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
    public const string PluginVersion = "3.1.1";

    internal static Plugin Instance { get; private set; } = null!;
    internal static ManualLogSource Log { get; private set; } = null!;
    internal static PluginConfig ModConfig { get; private set; } = null!;
    internal static HistoryStore Store { get; private set; } = null!;

    private Harmony? _harmony;
    private TitleMenuController? _titleUi;
    private bool _soloSweepDone;
    private float _nextSweepAttempt;

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
        FlushHistoryOnExit();
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
        FlushHistoryOnExit();
    }

    /// <summary>
    /// Quitting does not always raise Photon's OnLeftRoom, so the solo-hosted room has to be
    /// settled here too. The prune is idempotent, so both exit hooks can call this.
    /// </summary>
    private void FlushHistoryOnExit()
    {
        try { PlayerTracker.PruneActiveLobbyIfSolo(); } catch { /* ignore */ }
        try { Store?.Flush(); } catch { /* ignore */ }
    }

    /// <summary>
    /// One-shot startup sweep for the runs that crashed before a leave or quit could clean up.
    /// Steam must be ready: an empty local id would compare every host against nothing useful.
    /// </summary>
    private bool TryStartupSweep()
    {
        // Retrying every frame would spam an exception if the room-event hook can never attach
        if (Time.unscaledTime < _nextSweepAttempt)
            return false;
        _nextSweepAttempt = Time.unscaledTime + 1f;

        var self = SteamAvatarService.LocalSteamIdString();
        if (string.IsNullOrEmpty(self))
            return false;

        // Same rule as the live prune: never judge a room without the pipeline that records players
        PlayerTracker.EnsureRoomEvents();
        if (!PlayerTracker.RecordingArmed)
            return false;

        // A live lobby may already be the room being judged, so wait until we are out of one
        try
        {
            if (Peak.Network.NetCode.Matchmaking?.InLobby == true)
                return false;
        }
        catch { /* game API may vary */ }

        try
        {
            Store.PruneSoloHostedRooms(self);
        }
        catch (Exception ex)
        {
            Log.LogDebug($"Startup solo-room sweep failed: {ex.Message}");
        }

        return true;
    }

    private void Update()
    {
        if (!_soloSweepDone)
            _soloSweepDone = TryStartupSweep();

        HistoryUi.Tick();
        if (!HistoryUi.IsOpen)
            PauseMenuHook.ReleaseHeldTimeScale();
        try { Store?.TickDeferredSave(Time.unscaledTime); } catch { /* ignore */ }
        PlayerTracker.TickHostPoll(Time.unscaledTime);

        if (!ModConfig.EnableHotkey.Value)
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

        if (!ModConfig.ShowMainMenuButton.Value)
            return;

        if (_titleUi == null)
            _titleUi = gameObject.GetComponent<TitleMenuController>() ?? gameObject.AddComponent<TitleMenuController>();

        _titleUi.OnTitleSceneLoaded();
    }
}
