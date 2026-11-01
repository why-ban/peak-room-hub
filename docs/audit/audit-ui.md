# AUDIT-UI — UI layer correctness audit

Scope: `src/RoomHub/HistoryPageController.cs`, `HistoryRowFactory.cs`, `HistoryUi.cs`, `TitleMenuController.cs`, `PauseMenuHook.cs`, `UiButtonFactory.cs`, `HistoryUiFont.cs`, `RibbonDragHandle.cs`, `LocalizationSync.cs`, `Loc.cs`
Baseline: game v3.0.1 (`D:\zhuanban\youhua\decompiled-latest`), installed PEAKLib.UI 1.7.2 (`BepInEx/plugins/com.github.PEAKModding.PEAKLib.UI.dll`, 103424 B), mod build 2.7.0.
Method: line-by-line read of all ten files, and every game/PEAKLib symbol they touch verified against the decompile. Nothing here is inherited from repo docs or the old 2.x dump. PEAKLib.UI was decompiled for this audit (appendix B).

Severity key: **HIGH** = a whole input class or interaction is broken · **MEDIUM** = user-visible wrong behaviour · **LOW** = wasted work, log noise, latent hazard.
`UNPROVEN` marks a claim that needs runtime evidence; the checklist in §4 says how to get it.

---

## 0. Ground truth this audit rests on

| # | Fact | Evidence |
|---|---|---|
| G1 | `MenuWindow.Open/Close/StartClosed/ForceClose` are `internal`; `Show/Hide/SetInputActive` are public | `Assembly-CSharp/MenuWindow.cs:80,131,124,146,155,160,165` |
| G2 | `Open()` adds to `AllActiveWindows` idempotently; `Close()` and `OnDestroy()` remove | `MenuWindow.cs:83-86,134-137,105-111` |
| G3 | `CloseAllWindows()` → `ForceClose()` → `Close()` → `autoHideOnClose` → `Hide()`; **no owner callback** | `MenuWindow.cs:113-122,131-144,25` |
| G4 | `panel` is the window's own GameObject, so `Hide()` == `gameObject.SetActive(false)` | `MenuWindow.cs:33,160-163` |
| G5 | `MenuWindow.Start()`: `openOnStart==false` ⇒ `StartClosed()` (isOpen=false, panel inactive). `PeakCustomPage.OpenOnStart` defaults **false** | `MenuWindow.cs:35-48,124-129`; PEAKLib `PeakCustomPage.cs:1444` |
| G6 | `PeakCustomPage` requires Canvas + CanvasScaler + GraphicRaycaster; `Awake` sets overlay, `sortingOrder=1`, referenceResolution **1920x1080**, `matchWidthOrHeight=1` (match height) | PEAKLib `PeakCustomPage.cs:1412-1415,1458-1469` |
| G7 | PEAKLib page defaults: `CloseOnPause=CloseOnUICancel=SelectOnOpen=true`, `BlocksPlayerInput=ShowCursorWhileOpen=true` | PEAKLib `PeakCustomPage.cs:1446-1456` |
| G8 | While `isOpen`, `MenuWindow.Update` pushes itself as the active navigation container every frame and runs `TestCloseViaInput` | `MenuWindow.cs:50-57,59-74` |
| G9 | `windowBlockingInput` / `windowShowingCursor` are derived from `AllActiveWindows` in `LateUpdate` | `GUIManager.cs:357-361,394-418` |
| G10 | `UpdatePaused` opens the pause menu on `pauseWasPressed` with **no window check**; it is only called from `LateUpdate` and only when `Character.localCharacter` exists | `GUIManager.cs:420-436,361-390` |
| G11 | `pauseWasPressed = action_pause.WasPressedThisFrame()` after `ResetInput()` — re-sampled every frame, never latched | `CharacterInput.cs:212-215` |
| G12 | `CanDoInput()` is false while `windowBlockingInput`; `Sample(false)` skips look/move | `Character.cs:1711-1718`; `CharacterInput.cs:220-232`; `CharacterMovement.cs:183-192` |
| G13 | `CursorHandler` recomputes `Cursor.lockState/visible` **every frame** from `windowShowingCursor`, `isMenuScene`, `Modal.IsOpen` | `CursorHandler.cs:11-23` |
| G14 | `UIInputHandler.SetSelectedObject` is a **no-op unless the scheme is Gamepad**; `MenuWindow.SelectStartingElement` calls it with `objectToSelectOnOpen` | `UIInputHandler.cs:69-84`; `MenuWindow.cs:94-97,170-173` |
| G15 | `NavigationContainerHandler` auto-selects only in gamepad mode and only if `GetDefaultSelection()!=null`; in keyboard/mouse it deselects everything **except** a `TMP_InputField` | `NavigationContainerHandler.cs:38-79` |
| G16 | `m_quitButton` is `[SerializeField] private Button`; `Start()` wires it | `PauseMenuMainPage.cs:14-15,63-80` |
| G17 | `PauseMenuHandler.OnEnable` sets `Time.timeScale = 0` **only in `PhotonNetwork.OfflineMode`**; `OnDisable` restores it; `Update` consumes `pauseWasPressed` to close | `PauseMenuHandler.cs:19-24,35-41,43-63` |
| G18 | `OnLangugageChanged` fires at the **end** of `RefreshAllText`, which does **not** reload the table; `SetLanguage` only re-points font fallbacks | `LocalizedText.cs:497-529` |
| G19 | The game has one shared main font asset; CN/TC/JP support is a `fallbackFontAssetTable[1]` swap on it | `FontFallbackSwapper.cs:31-41` |
| G20 | `TMP_InputField` consumes `KeyCode.Escape` (`EditState.Finish`, `m_WasCanceled=true`) | `Unity.TextMeshPro/TMPro/TMP_InputField.cs:2237-2240` |
| G21 | TMP preferred width is computed with `k_LargePositiveVector2` and `TextWrappingModes.NoWrap` — i.e. the unwrapped line width | `Unity.TextMeshPro/TMPro/TMP_Text.cs:3321-3343` |
| G22 | Layout groups force child anchors to `Vector2.up` before positioning | `UnityEngine.UI/UnityEngine/UI/LayoutGroup.cs:209-220`; `HorizontalOrVerticalLayoutGroup.cs:170-238` |
| G23 | `MenuAPI.CreateMenuButton` clones `Templates.ButtonTemplate` (throws when null); PEAKLib strips only `LocalizedText` from the template | PEAKLib `MenuAPI.cs:908-919`; `Hooks/PauseMenuHooks.cs:1146-1153` |
| G24 | `Templates.ButtonTemplate` is `public static … { get; internal set; }` | PEAKLib `Templates.cs:2105` |
| G25 | PEAKLib's pause hook invokes `pauseMenuBuilderDelegate` with `PauseMenuMainPage.transform`, then dereferences `Find("SettingsPage")`/`Find("ControlsPage")` unchecked | PEAKLib `Hooks/PauseMenuMainPageHooks.cs:1185-1193` |
| G26 | The title scene is named `"Title"` | `MainMenu.cs:18`; `Pretitle.cs:24` |
| G27 | `GameHandler.GetService<T>()` is a dictionary **indexer** — it throws `KeyNotFoundException` when the service is absent | `GameHandler.cs:224-227` |
| G28 | Legacy `UnityEngine.Input` is enabled alongside the new Input System (a shipped scene reads both in one method) | `Pretitle.cs:44-57`; also `CinemaCamera.cs`, `GodCam.cs` |
| G29 | `PeakMenuButton.Awake` dereferences `Panel`/`Text`/`Border` with **no null checks** and reads the sibling after `Border` | PEAKLib `PeakMenuButton.cs:1846-1854` |

## 1. Lifecycle model as it actually behaves

| Event | Game/PEAKLib behaviour | Mod behaviour | Verdict |
|---|---|---|---|
| First open | `PeakCustomPage.Awake` builds Canvas/Scaler; `Start()` would `StartClosed()` | `EnsurePage` → flags → `MenuWindow.Open` (reflection) → `isOpen=true` before `Start` runs, so `StartClosed` is skipped | correct |
| Open twice (ribbon + hotkey) | `Open()` is idempotent for `AllActiveWindows` | no re-entrancy guard: view resets, rows rebuilt twice | UI-16 |
| ESC / pause | `closeOnPause=false` + Harmony prefix swallows `UpdatePaused` | page closes itself, pause never opens on that press | correct (§3 C3) |
| Game closes all windows (win/lose/cutscene/scene load) | `CloseAllWindows` → `Close` → `Hide`; **no notification** | `IsPageOpen` is derived from `activeInHierarchy`, so the mod self-heals on the next `Tick`/`Toggle` | correct (§3 C8) |
| Scene unload | page GO destroyed with the scene; `OnDestroy` removes it from `AllActiveWindows` | `OnSceneLoaded` → `DropPage` → `Discard` → cursor restore | correct |
| Hide → reopen | window stays alive, `isOpen=false` | rows are rebuilt on every open (`ShowRoomsView`), invite marks reset | correct |

## 2. Findings

### UI-01 — HIGH — the panel is unusable with a controller
`HistoryPageController.cs:409` (`SelectOnOpen = false`), `:405-410`; no selection is ever set anywhere in the ten files.

**Scenario.** Gamepad-only player opens the panel (hotkey cannot be pressed, so from the pink pause button). Nothing is selected, so `EventSystem` has no target for Move/Submit; `CloseOnUICancel=false` (`:172`) means B does nothing; the panel is a dead rectangle until a mouse is found.

**Evidence.** `MenuWindow.Open` calls `SelectStartingElement` only when `selectOnOpen` (`MenuWindow.cs:94-97`), which calls `UIInputHandler.SetSelectedObject(objectToSelectOnOpen)` (`:170-173`); `GetDefaultSelection()` returns `objectToSelectOnOpen` (`:180-187`) which is `null` because neither the mod nor `PeakCustomPage` ever sets it (PEAKLib has no setter, `PeakCustomPage.cs:1416-1456`). `NavigationContainerHandler` only auto-selects in gamepad mode **and only if that getter is non-null** (`NavigationContainerHandler.cs:46,59-70`). `UIInputHandler.SetSelectedObject` is itself a no-op outside gamepad (`UIInputHandler.cs:78-84`), so the mod cannot fix this by calling it blindly either.

**Minimal fix.** After `RefreshCurrentView` finishes populating, if `InputHandler.GetCurrentUsedInputScheme() == InputScheme.Gamepad` select the first row's `Button.gameObject` (fall back to the Close button). Alternatively keep `SelectOnOpen=true` and provide `objectToSelectOnOpen` from a small `PeakCustomPage` subclass. Also give the close button an explicit `Navigation` chain so up/down stays inside the page.

**UNPROVEN.** Whether the pink pause button is itself reachable by a controller (it relies on automatic `Navigation` between siblings; the game only rewires explicit links, `PauseMenuMainPage.cs:74-79,110-115`).

### UI-02 — MEDIUM — ESC stops closing the panel once the search box has been focused
`HistoryUi.cs:136` (`&& !IsTextInputFocused()`), `:145-169`.

**Scenario.** Open panel → click the search field → press ESC. TMP consumes the key to leave edit mode (G20) and the field stays the EventSystem selection, because `NavigationContainerHandler` deliberately exempts `TMP_InputField` from deselection (G15, `NavigationContainerHandler.cs:71-77`). `IsTextInputFocused()` therefore stays true, the mod keeps skipping its ESC branch, and — because the page is still open — `PauseInputSuppressor` keeps swallowing the game pause (`HistoryUi.cs:184-188`). ESC is dead until the user clicks a row or the background.

**Evidence.** G20, G15, `HistoryUi.cs:145-169` (checks `EventSystem.current.currentSelectedGameObject`), `HistoryPageController.cs:471-477` (the field is created and clickable).

**Minimal fix.** In `Tick`, when `IsTextInputFocused()` and ESC is pressed: `EventSystem.current.SetSelectedGameObject(null)` and return. Second ESC then closes. That makes the gesture deterministic instead of execution-order dependent.

**UNPROVEN.** Whether one ESC or two are needed today depends on the frame order of `Plugin.Update` vs `EventSystem.Update` (both default order 0).

### UI-03 — MEDIUM — in singleplayer the mod resumes the game while the panel is open
`PauseMenuHook.cs:180-196`; `HistoryUi.cs:68`; `HistoryUi.cs:184-188`.

**Scenario.** Solo run (`PhotonNetwork.OfflineMode`), ESC → pause (timeScale 0) → click the pink Room Hub button → `ClosePauseMenuIfOpen` deactivates the `PauseMenuHandler` GO → its `OnDisable` restores `Time.timeScale` (G17) → the world runs at full speed behind the panel, and ESC cannot re-pause because `UpdatePaused` is suppressed. The player can be grabbed or killed while reading history.

**Evidence.** `PauseMenuHandler.cs:19-24` (`timeScale=0` on enable when OfflineMode), `:35-41` (`timeScale = GameUtils.GetTimeScale()` on disable), `PauseMenuMainPage.cs:154-157` (the game's own Resume only deactivates the handler, so the mod is copying a path that also unpauses), `GUIManager.cs:420-436` + `HistoryUi.cs:184-188` (pause suppressed while open).

**Minimal fix.** In `HistoryUi.Open`, before `ClosePauseMenuIfOpen`, remember `Time.timeScale`; if it was 0 (solo pause), set it back to 0 after closing the pause menu and restore the remembered value in `Hide`/`Discard` (the same place `ReleaseGameplayInputBlock` runs).

### UI-04 — MEDIUM — friend panel and room list overlap on aspect ratios below ~1.56
`HistoryPageController.cs:20-22`, `:493-498`, `:541-546`, `:524-531`.

**Scenario.** 4:3 (1024x768, 1280x960) or 5:4 window. The friend panel (x 40…440) covers the left third of the room list, so friend rows and room rows interleave.

**Evidence.** `PeakCustomPage.Awake` sets referenceResolution 1920x1080 with `matchWidthOrHeight=1` (G6) ⇒ virtual width = `1080 × aspect`. Friend panel: anchor `(0,1)`, pivot `(0,1)`, position `(40,-150)`, width 400 ⇒ spans **40…440**. Room list: anchor/pivot `(0.5,1)`, width 800 ⇒ spans `(W-800)/2 … (W+800)/2`. Overlap when `(W-800)/2 < 440` ⇒ **W < 1680** ⇒ aspect < 1.5556. Concretely: 4:3 → W=1440, list 320…1120, overlap 120 px; 5:4 → W=1350, overlap 165 px; 3:2 → W=1620, overlap 30 px; 16:10 (W=1728) and wider are clear.
Same root cause, second symptom: the 900-px-wide title box (`:419`) starts colliding with the search field/button cluster (`:469-491`) below W≈1820, i.e. already at 16:10.

**Minimal fix.** Derive the room list's left edge from the friend panel instead of centering it (`anchorMin.x=0`, `anchoredPosition.x = FriendPanelWidth + 80`) and cap the title box width to `W - 2*(FriendPanelWidth+80)`; or hide the friend panel when the CanvasScaler-scaled width is under 1680.

**UNPROVEN.** The rendered result — the numbers are derived from the code and the CanvasScaler math, not from a screenshot.

### UI-05 — MEDIUM — every external refresh destroys and rebuilds all rows
`HistoryPageController.cs:653-658` (event handler), `:683-692` (entry), `:836-847` (destroy loops), `:872-892` (`DestroyRowSafely`); `HistoryRowFactory.cs:407-457` (one `Sprite.Create` per avatar).

**Scenario.** `FriendRoomsService.Changed` fires on Steam persona / rich-presence callbacks. While the user is scrolled halfway down 10 rooms, the whole list is destroyed and rebuilt: the ScrollRect keeps its normalised position while the content height changes twice (old+new rows coexist for the rest of the frame, then only the new ones), so the view jumps; every avatar sprite is destroyed and re-created.

**Evidence.** `Object.Destroy` is deferred to after the current update loop (Unity docs, appendix B), so the old rows are still children of `Content` for the rest of the frame in which the rebuild runs. Because `Content` has a `VerticalLayoutGroup` + `ContentSizeFitter` (`HistoryPageController.cs:500-509`; PEAKLib `PeakScrollableContent.cs:1919-1934`), its preferred height is recomputed at least once with both sets present, then again after the deferred destroy — two different content heights inside one rebuild, while the ScrollRect's `verticalNormalizedPosition` is left untouched. Subscriptions: `:572-573`, unsubscribed at `:646-650`.

**Minimal fix.** Detach before destroying (`child.SetParent(null)` then `Destroy`) or, better, key rows by id and update in place; at minimum restore `verticalNormalizedPosition` after repopulating.

**UNPROVEN.** The visible magnitude of the jump.

### UI-06 — MEDIUM — long names run underneath the row buttons
`HistoryRowFactory.cs:388-405` (`CreateBodyText`), `:107-122` (buttons at x=-16, width 150).

**Scenario.** A 32-character Steam name, or a CJK name plus a long room name, renders under the Join/View buttons. The text is still click-through (`raycastTarget=false`, `:401`) so nothing breaks functionally, but the name is unreadable.

**Evidence.** `MenuAPI.CreateText` → `PeakText.SetText`/`SetFontSize` assign `sizeDelta = GetPreferredValues()` (PEAKLib `PeakText.cs:1993-2007`), and that value is computed unbounded and unwrapped (G21). `CreateBodyText` never sets a width, `overflowMode` or `textWrappingMode`, so the label keeps TMP's default Overflow and simply draws past the row's right edge; the buttons are created afterwards, so they paint on top (`:107-122`, `:116-122`).

**Minimal fix.** Give the body text an explicit width (`ListWidth - 96 - 190`) and `overflowMode = TextOverflowModes.Ellipsis` with `textWrappingMode = NoWrap` in `CreateBodyText`.

### UI-07 — LOW (latent) — the title-scene `Show()` fallback is undone by `MenuWindow.Start`
`HistoryPageController.cs:178-186`.

**Scenario (if it ever triggers).** `MenuWindow.Open` cannot be resolved ⇒ the fallback calls `Show()` and sets `_openedViaMenuWindow=true` but never sets `isOpen`. The page's `Start` then runs `StartClosed()` and deactivates the panel, so the first open silently does nothing; the second open works because `Start` runs once.

**Evidence.** `MenuWindow.cs:35-48` (`if (!isOpen) { openOnStart ? Open() : StartClosed(); }`), `PeakCustomPage.OpenOnStart` defaults false (G5), `StartClosed` → `panel.SetActive(false)` (`MenuWindow.cs:124-129`), `IsPageOpen` requires `activeInHierarchy` (`HistoryPageController.cs:76-91`).

**Minimal fix.** Drop the half-open fallback: on a failed `Open` in the title scene, call `((MenuWindow)_page).SetInputActive(true)` and `Show()` **and** set a private `_shownWithoutMenuWindow` flag that `Start` cannot undo — or simply `Discard()` and log an error, matching the in-game branch.

### UI-08 — LOW — the PEAKLib template repair can fail with no visible signal
`UiButtonFactory.cs:203-220`, `:62-89`, `:39-57`.

**Scenario.** On a build where PEAKLib's own capture fails, the mod's fallback works, but nothing in a default log says whether the injection into `Templates.ButtonTemplate` succeeded or why it failed — every failure path is `LogDebug`, which BepInEx does not print by default.

**Evidence.** `Templates.ButtonTemplate` is `{ get; internal set; }` (G24); the code resolves the property with `BindingFlags.Public|Static` and calls `SetValue` (`:207-213`), with the exception swallowed at `:216-219`; `EnsureTemplate` swallows at `:85-88`. PEAKLib's own capture uses `Enumerable.First(...)` (G23), which **throws** when the scene object is gone, so the failure mode the comment describes is plausible — but whether `UI_MainMenuButton_LeaveGame (2)` still exists in 3.0.1 is scene data.

**Minimal fix.** Log the injection outcome (and the exception type/message) at Info/Warning once, and log once when `MenuAPI.CreateMenuButton` throws so the fallback path is visible in the log.

**UNPROVEN.** Whether PEAKLib's template is actually null on 3.0.1 (checklist R8).

### UI-09 — LOW (unproven) — the default button path does not clear the template's `onClick` listeners
`UiButtonFactory.cs:92-97` (default path) vs `:178-196` (own template does clear them).

**Scenario (if the source scene button has persistent listeners).** Every RoomHub button also fires the template button's inspector-wired action (e.g. Leave/Quit).

**Evidence.** PEAKLib strips only `LocalizedText` from its template clone (G23, `PauseMenuHooks.cs:1146-1153`); `Instantiate` copies serialized persistent calls but not runtime-added ones. The mod's own template path clears them (`UiButtonFactory.cs:189-191`), and the ribbon clears them after cloning a scene button (`TitleMenuController.cs:146`) — the default path is the only one that does not.

**Minimal fix.** In `CreateMenuButtonSafe`, after obtaining the button, `btn.Button.onClick.RemoveAllListeners()` before returning.

**UNPROVEN.** Needs a scene dump of `UI_MainMenuButton_LeaveGame (2)` to see whether its `Button` has persistent calls.

### UI-10 — LOW — the ribbon can be dragged off-screen and the position is persisted
`RibbonDragHandle.cs:39-53`, `:55-66`; `TitleMenuController.cs:156-161`, `:172-179`; `PluginConfig.cs:108-113`.

**Scenario.** Right-drag the ribbon to `y=900` (allowed by the clamp) on a 720-px-tall window ⇒ it leaves the screen. `SaveButtonPosition` sets `UseCustomButtonPos=true`, and `ClampFullyOnScreen` is skipped whenever that flag is set (`TitleMenuController.cs:156`), so it never comes back; only the hotkey still opens the panel.

**Evidence.** The clamp uses hard-coded canvas units `(-1800…200, -900…900)` (`:50-51`) unrelated to the parent rect; the save is unconditional on pointer-up (`:63-65`).

**Minimal fix.** Clamp against `_parent.rect` (and the pivot) instead of constants, and skip the save when the resulting corners are outside the parent rect. Also fix `OnPointerDown`: `_dragging=true` is set at `:33` before `ScreenToParentLocal` succeeds, so a failed conversion leaves a stale `_grabOffset` for the next drag.

### UI-11 — LOW — the panel can be opened over the end-of-run cutscene / end screen
`Plugin.cs:206-215`; `HistoryUi.cs:57-80`.

**Scenario.** Press K during the win/lose cutscene: `CloseAllWindows` already ran (so nothing blocks a new window) and only `LoadingScreenHandler.loading` is checked, so the history panel opens with a free cursor on top of the letterboxed cutscene.

**Evidence.** `PeakHandler.cs:64-83` (`PrepareEndCutscene` closes all windows and disables the HUD), `Character.cs:911` (same at win/lose), `GUIManager.cs:71,424` (`endScreen`/`endScreen.isOpen` exist and are not consulted).

**Minimal fix.** Gate `HistoryUi.Open` on `GUIManager.instance != null && GUIManager.instance.endScreen.isOpen` (and the PeakHandler cinematic flag) the same way the loading check is done.

### UI-12 — LOW — avatar sprites can outlive their textures
`HistoryRowFactory.cs:425-437`; `HistoryPageController.cs:380-389`; `SteamAvatarService.cs:71-73,95-98,32-43`.

**Scenario.** A Steam avatar update replaces the cached `Texture2D` and destroys the old one; live rows still hold a `Sprite` built from the destroyed texture, so the avatar renders blank/white until the next rebuild.

**Evidence.** The cache destroys the previous texture on replace and on `Shutdown`; nothing notifies the page. `GetAvatar` itself never returns a destroyed texture (the `tex != null` fake-null check at `SteamAvatarService.cs:57` re-requests), so this is display-only.

**Minimal fix.** Have `SteamAvatarService` raise a change event; on it, re-add affected avatar targets to `_avatarRefreshTargets` (or drop the sprite so `TickAvatarRefresh` rebuilds it).

### UI-13 — LOW — a failed search-box creation degrades into a silent no-op button
`HistoryPageController.cs:469-483`, `:485-491`, `:660-675`.

**Scenario.** If `PeakTextInput` cannot be built, `_searchInput` stays null but the Search button is still created; pressing it reads an empty query and just resets the view — no feedback, only a `LogWarning`.

**Evidence.** `PeakTextInput.TextInputPrefab` throws when `SingletonAsset<InputCellMapper>.Instance.FloatSettingCell` is null (PEAKLib `PeakTextInput.cs:2031-2036`), which the mod catches at `:479-483`; `RunPlayerSearch` then reads `_searchInput?.InputField` (`:665-666`) and calls `ShowRoomsView` (`:671-675`).

**Minimal fix.** When the input is null, disable the Search button (and/or surface `LobbyJoinFeedback`) so the failure is visible.

### UI-14 — LOW — per-row service/config lookups during a rebuild
`HistoryRowFactory.cs:97`, `:102`, `:147`, `:179`, `:194`, `:200`.

**Evidence.** `HistoryUi.IsInLiveLobby` calls `GameHandler.GetService<SteamLobbyHandler>()` (a dictionary indexer that throws when the service is missing, G27) inside a try/catch — in the title scene that is a thrown + caught exception **per room row, per rebuild**; `CanShowInvite` re-runs `IsTitleScene` + `IsInLiveLobby`; `SteamAvatarService.IsFriend` is called per player row.

**Minimal fix.** Compute `isTitleScene`, `isInLiveLobby`, `canInvite` once at the top of `RefreshCurrentView` and pass them to the factory.

### UI-15 — LOW — the cursor is force-restored to a hard-coded state, not the previous one
`HistoryPageController.cs:244-257` (`_prevLockMode = CursorLockMode.Locked; _prevCursorVisible = false;`), `:259-270`.

**Evidence.** `CursorHandler` already derives the cursor from `windowShowingCursor` every frame (G13), so while the page is in `AllActiveWindows` the game frees the cursor itself; the mod's force is redundant, and the "restore" is wrong for one frame whenever the game wanted the cursor free at that moment (e.g. a modal opened on the closing frame). Limited blast radius because `CursorHandler` recomputes next frame.

**Minimal fix.** Delete the force/restore, or capture `Cursor.lockState`/`Cursor.visible` at capture time and restore those values.

### UI-16 — LOW — `Open()` has no re-entrancy guard
`HistoryUi.cs:57-80`, `:88-94`; `HistoryPageController.cs:155-208`.

**Scenario.** A ribbon click and the hotkey in the same frame (or two paths in one frame) run `Open` twice: the view resets to rooms, rows are built twice (the first set is destroyed with deferred `Destroy`, so for one frame the content height is doubled — same mechanism as UI-05), and `_deleteMode`/`_viewingLobbyId` are reset. No crash, no leak (`AllActiveWindows` is idempotent, `_cursorCaptured` is guarded).

**Minimal fix.** At the top of `HistoryPageController.Open`, `if (IsPageOpen) return;`.

## 3. Verified correct

| # | Claim | Evidence |
|---|---|---|
| C1 | `MenuWindow.Open`/`Close` reflection is still valid on 3.0.1: one `internal virtual void Open()` and one `internal void Close()`, no overloads (no `AmbiguousMatchException`); `PeakCustomPage` does not override `Open`, so the base implementation (which registers in `AllActiveWindows`) runs | `MenuWindow.cs:80-99,131-144`; PEAKLib `PeakCustomPage.cs:1416-1542` |
| C2 | `BlocksPlayerInput`/`ShowCursorWhileOpen` are set before `Open` and again at creation, so `GUIManager.UpdateWindowStatus` sees them from the first frame; the code comment about `CharacterInput.Sample`/`CanDoInput` is accurate | `HistoryPageController.cs:170-173,407-408`; `GUIManager.cs:394-418`; `Character.cs:1711-1718`; `CharacterInput.cs:220-232` |
| C3 | The one-frame pause latch is sound: `pauseWasPressed` is re-sampled every frame (G11) so a suppressed press cannot leak; the latch is assigned before any early return and before the ESC branch; `UpdatePaused` runs in `LateUpdate`, after `Plugin.Update` | `HistoryUi.cs:119,136-140,184-188`; `GUIManager.cs:357-361,420-436`; `CharacterInput.cs:212-215` |
| C4 | `closeOnPause=false` is the right choice: with it true, `TestCloseViaInput` would `Close()` in the same frame as the mod's own ESC handling, giving two close paths | `MenuWindow.cs:59-74` |
| C5 | `m_quitButton` reflection is still correct (`[SerializeField] private Button`) and the mod parents to the quit button's real parent, which is more accurate than PEAKLib's `PauseMenuMainPage.transform` argument | `PauseMenuMainPage.cs:14-15`; PEAKLib `PauseMenuMainPageHooks.cs:1185-1187` |
| C6 | `IsSuitableTemplate` checks exactly what `PeakMenuButton.Awake` dereferences (Panel+Image, Text+TMP, Border+Image, and the sibling after Border), so `AddComponent<PeakMenuButton>` cannot throw on the accepted templates | `UiButtonFactory.cs:161-176`; PEAKLib `PeakMenuButton.cs:1846-1854` |
| C7 | `AddComponent<GraphicRaycaster>()` in `EnsurePageBlocksRaycasts` is dead code but harmless — `PeakCustomPage` already requires it | `HistoryPageController.cs:306-307`; PEAKLib `PeakCustomPage.cs:1412-1415` |
| C8 | Scene-unload and `CloseAllWindows` are handled: `OnDestroy` deregisters from `AllActiveWindows`, `IsPageOpen` derives from `activeInHierarchy` so an externally closed window reads as closed, and `OnSceneLoaded` drops the controller | `MenuWindow.cs:105-111`; `HistoryPageController.cs:61-91`; `HistoryUi.cs:105-108`; `Plugin.cs:218-221` |
| C9 | Localisation coverage is complete: all 58 `Loc.*` keys are declared **and** registered with en/zh-Hans/zh-Hant (checked mechanically across all 22 source files); the other 11 languages fall back to English because PEAKLib fills every slot with the first translation | `Loc.cs:7-64,66-139`; PEAKLib `MenuAPI.cs:966-982`; `LocalizedText.cs:14-31` |
| C10 | Custom keys survive a language switch: `RefreshAllText` only re-renders components and fires the event, it never reloads `MAIN_TABLE` | `LocalizedText.cs:521-529` vs `:206-229` |
| C11 | `LocalizationSync` is sound: `HashSet<Action>` dedupes repeated `Register` of the same instance method (delegate equality), the hook is only removed when the last handler goes, and handlers run from a snapshot outside the lock | `LocalizationSync.cs:18-76` |
| C12 | CJK font borrowing is the correct mechanism: the game has one main font asset whose `fallbackFontAssetTable[1]` is swapped for CN/TC/JP, so any label using the borrowed font gets CJK for free; the material is captured with the font | `HistoryUiFont.cs:23-85`; `FontFallbackSwapper.cs:31-41` |
| C13 | Row layout math is correct: the VLG forces child anchors to top-left (G22) and, with `childControlHeight=false`, positions children by their own `sizeDelta`; `PinRowSize` pins both `sizeDelta` and `LayoutElement` | `HistoryRowFactory.cs:459-470`; `LayoutGroup.cs:209-220`; `HorizontalOrVerticalLayoutGroup.cs:222-236` |
| C14 | Scroll content sizing is PEAKLib's own and consistent (stretched Content + VerticalLayoutGroup + `ContentSizeFitter(vertical=Preferred)`, viewport falls back to the ScrollRect rect, `Clamped` with elasticity 0) | PEAKLib `PeakScrollableContent.cs:1907-1935` |
| C15 | 0 / 1 / 60 rows are all sane: 60 rooms ⇒ content height 60·120 + 59·8 = 7672 px in a 600 px viewport (scrolls); 1 row ⇒ 120 px content, top-aligned, no scroll; 0 rows ⇒ the empty-state label is a single child whose width is the unwrapped preferred width (G21), so it does not wrap into a narrow column | `HistoryPageController.cs:493-509,931-946`; `TMP_Text.cs:3321-3343` |
| C16 | No row delegate leak: every listener is a `UnityAction` on a `Button` that dies with the row, closures capture only that row's record, and the only static events (`JoinGate.StateChanged`, `FriendRoomsService.Changed`) hold the controller's instance method, unsubscribed in `Discard` | `HistoryRowFactory.cs:86-122`; `HistoryPageController.cs:572-573,646-650` |
| C17 | The invited-button state machine is coherent: the flip is persisted in `SteamInviteService`, rebuilt rows consult `IsInvited`, and the state is reset on Hide/Discard | `HistoryRowFactory.cs:345-386`; `HistoryPageController.cs:100,104,121` |
| C18 | `IsTitleScene` matches the real scene name | `HistoryUi.cs:28-35`; `MainMenu.cs:18`; `Pretitle.cs:24` |
| C19 | The ribbon label survives scene unload (Unity fake-null early return) and the ribbon only unregisters in `OnDestroy` of a persistent GO | `TitleMenuController.cs:27-32,253-258` |
| C20 | Legacy `Input.GetKeyDown` is safe: the shipped `Pretitle` scene reads both legacy `Input` and the new Input System in one method, i.e. legacy input is enabled in this build | `Pretitle.cs:44-57` |
| C21 | A missing key is loud in the log (the game `Debug.LogError`s `Failed to load text: <ID>`) and shows the raw index in the UI, which is at least identifiable | `LocalizedText.cs:412-417`; `Loc.cs:149-162` |

## 4. Runtime evidence checklist

| # | Test | What it proves |
|---|---|---|
| R1 | Gamepad only: open from the pause menu, press every button/stick | UI-01 (expected: no focusable element; also whether the pink button is reachable) |
| R2 | Click the search field, press ESC once, then again | UI-02 (1 vs 2 presses) |
| R3 | Solo run: ESC → pink button → watch whether the world keeps running | UI-03 |
| R4 | 1024x768 and 1280x1024 windowed screenshots | UI-04 |
| R5 | With the panel open on 10 rooms, wait for a friend's rich-presence change (or trigger `FriendRoomsService.Changed`) | UI-05 (scroll jump) |
| R6 | 32-char Steam name in a room row | UI-06 |
| R7 | Switch language with the panel open, then closed-then-reopened; watch the log for `Failed to load text:` | C9-C11 |
| R8 | Set BepInEx log level to Debug once and check PEAKLib's `button is null - ` line / `Templates.ButtonTemplate` after `MainMenu.Start` | UI-08, UI-09 |
| R9 | Right-drag the ribbon to a corner, restart | UI-10 |
| R10 | Win/lose cutscene: press K | UI-11 |

## Appendix A — reflection / string-based lookup inventory

| Symbol | Where | Status on 3.0.1 | Failure mode |
|---|---|---|---|
| `MenuWindow.Open` (`Instance\|Public\|NonPublic`) | `HistoryPageController.cs:55-56` | present, single, `internal virtual` (`MenuWindow.cs:80`) | `Invoke` throws → `LogWarning` → in-game `LogError` + `Discard` (loud); title scene falls into UI-07 |
| `MenuWindow.Close` | `HistoryPageController.cs:57-58` | present, single (`MenuWindow.cs:131`) | falls back to `Hide()` (`:232-240`) |
| `PauseMenuMainPage.m_quitButton` | `PauseMenuHook.cs:19-20` | present, `private [SerializeField] Button` (`PauseMenuMainPage.cs:14-15`) | `LogWarning` + button appended at the end (`:113-118`) |
| `Templates.ButtonTemplate` | `UiButtonFactory.cs:207` | present, `internal set` (PEAKLib `Templates.cs:2105`) | `LogDebug` only (UI-08) |
| `MainMenu/Canvas/MainPage/Menu/Buttons/Button_PlaySolo` | `TitleMenuController.cs:17` | scene path — **UNPROVEN** | `LogWarning`, no ribbon; page still reachable via hotkey/pause button |
| `MainMenu/Canvas/MainPage/NextLevelUI` | `TitleMenuController.cs:18` | scene path — **UNPROVEN** | falls back to `y = 20` (`:184-197`) |
| `UI_MainMenuButton_LeaveGame (2)` | PEAKLib only (G23) | scene object — **UNPROVEN** | PEAKLib's `First` throws; the mod's fallback covers it |

## Appendix B — reproduction

```
# PEAKLib.UI 1.7.2 API surface used above
C:/Users/Administrator/.dotnet/tools/ilspycmd.exe \
  "D:\SteamLibrary\steamapps\common\PEAK\BepInEx\plugins\com.github.PEAKModding.PEAKLib.UI.dll" \
  -o %TEMP%\roomhub-audit-peaklibui
```
Line references of the form PEAKLib `X.cs:NNN` are into `%TEMP%\roomhub-audit-peaklibui\com.github.PEAKModding.PEAKLib.UI.decompiled.cs` (one file, 2580 lines; the class names in the citations are the nested types it contains). Game references are into `D:\zhuanban\youhua\decompiled-latest\<assembly>\...`.

Unity behaviour cited for UI-05: `Object.Destroy` is deferred until after the current update loop — <https://docs.unity3d.com/ScriptReference/Object.Destroy.html>.
