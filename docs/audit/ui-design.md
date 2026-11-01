# UI-DESIGN — PEAK 3.0.1 UI language → Room Hub redesign brief

Scope: information architecture + component spec for rebuilding the Room Hub panel as first-party PEAK UI.
Baseline: game **v3.0.1** (`D:\zhuanban\youhua\decompiled-latest`), PEAKLib.UI **1.7.2** (installed), RoomHub `src/RoomHub` as it stands.
Official design tokens: **`docs/audit/ui-palette.md`** (measured from the game's own assets) — this document consumes them and does not re-derive colours.
No source was modified.

摘要：PEAK 的一方 UI 是「白色九宫格遮罩 + Image.color 着色」体系，按钮解剖固定为 Shadow→Panel→Border(上/下)→Glow→Text，颜色由**角色**决定（主橙/危险红/成功绿/信息蓝/强调紫/青/金/深红）。Room Hub 采用「scrim 压暗 + 居中面板 + 40px 标签条 + 67px 行」，**颜色即语义**：三种视图各占一个角色色，行状态用 Glow 淡入与 ColorTint 派生，运行时零素材依赖（从已克隆的官方按钮模板取 sprite 与 color）。

Confidence tags on every claim:

| Tag | Meaning |
|---|---|
| **MEASURED** | Read from code or from a shipped asset; `path:line` or asset+object given. |
| **OFFICIAL** | Value taken from `docs/audit/ui-palette.md` (extracted from the game's own assets), or a rule the game's code applies to such a value. |
| **DERIVED** | My design decision, anchored on a MEASURED/OFFICIAL value. Not a game constant. |
| **UNPROVEN** | Cannot be established from available artifacts; needs runtime evidence. |

---

## 0. Method, and what is / is not knowable

| # | Fact | Evidence |
|---|---|---|
| F1 | C# decompile gives **structure and behaviour** (page/window/tab/modal classes, navigation rules, animation constants), not appearance. Appearance is serialized on prefabs. | `PauseMenuMainPage.cs:14-54` (all `[SerializeField]`); `AudioLevelSlider.cs:39-40` |
| F2 | UnityPy cannot decode MonoBehaviour type trees in this build, **but `Image.m_Color` is recoverable at a fixed serialized offset**: `m_GameObject(12) + m_Enabled(4) + m_Script(12) + m_Material(12) → m_Color @ 44`, unpacked as 4 little-endian floats. | method per `docs/audit/ui-palette.md:4-6`; extraction scripts `D:\zhuanban\youhua\scratch-assets\dump_palette.py:19-28` |
| F3 | I re-ran that method independently against `resources.assets` `ConfirmPage` and reproduced plausible in-range colours, so the token set is trustworthy. | see §1.2 "confirm-dialog" rows below, produced by my own run |
| F4 | Transform/RectTransform geometry decodes normally, so **every size, anchor, offset and child order** quoted here is exact. | UnityPy: 1 070 RectTransform in `resources.assets`; every node I walked in `Modal` and `ConfirmPage` decoded |
| F5 | The kit is **white alpha masks**; all colour comes from `Image.color`. | 11 extracted sprites are 100 % `#F8F8F8` in their opaque pixels; `UISprite` 32×32 white rounded rect (`ui-palette.md:73`) |
| F6 | **Runtime must not load game assets.** The correct zero-dependency path is to harvest `Sprite` + `Color` from the already-cloned official button template's `Panel`/`Border`/`Shadow`/`Glow` children. | `ui-palette.md:81-83`; clone source exists at `src/RoomHub/UiButtonFactory.cs:39-57, 178-196` |
| F7 | `AirportCheckInKiosk` / `AirportInviteFriendsKiosk` are **world interactibles, not UI pages**; the airport UI is `BoardingPass : MenuWindow`. | `AirportCheckInKiosk.cs:6,111-115`; `AirportInviteFriendsKiosk.cs:5,39-49`; `BoardingPass.cs:12` |
| F8 | PEAK 3.0.1 has **no `TweenHolder`** and no DOTween Sequence helper for UI. | grep `TweenHolder` over `Assembly-CSharp` → 0 hits |

---

## 1. Visual language

### 1.1 Surface tokens (official)

| Token | Value (r, g, b, a) | Used by the game on | Source |
|---|---|---|---|
| `scrim` | 0, 0, 0, **0.8667** | `SettingsPage/Background` — the full-screen dim | `ui-palette.md:52` |
| `paper` | 0.8742, 0.8567, 0.7615 | `Box`, `FirstTimeSetupPage`, PlayPage `Button` | `ui-palette.md:44` |
| `tanBorder` | 0.7529, 0.6745, 0.5529 | `Box/Border`, `Window_STATS/DotGrid` | `ui-palette.md:45` |
| `divider` | 0.3396, 0.2996, 0.2419 | `Title (1)/Line (1)` | `ui-palette.md:46` |
| `darkPanel` | 0.1792, 0.1253, 0.0905, **0.7294** | `Controls/Image`, `UI_Control (9)/Image` | `ui-palette.md:47` |
| `woodMid` | 0.566, 0.4503, 0.3284 | `Menu/Panel (2)` | `ui-palette.md:48` |
| `woodDark` | 0.4528, 0.3527, 0.2456 | `Menu/Panel (3)` | `ui-palette.md:49` |
| `woodLight` | 0.783, 0.6374, 0.4765 | `NextLevelUI/Panel` | `ui-palette.md:50` |
| `panelDrop` | 0.4497, 0.359, 0.3045 | `NextLevelUI/PanelDrop` | `ui-palette.md:51` |
| `chipBg` | 0.3585, 0.271, 0.2452 | `Icons/Image` | `ui-palette.md:53` |
| `gold` | 1.0, 0.8009, 0.0 | `SELECT` | `ui-palette.md:54` |
| `locked` | 0.5294, 0.4784, 0.4 | `Box/LockedIcon` | `ui-palette.md:55` |
| `text.primary` | 1, 1, 1, 1 | every button `Text` | `ui-palette.md:19` |

### 1.2 Role palette (official) — colour carries meaning

The game does not have "a button colour"; it has **role colours**, each shipped as a Panel/Border pair.

| Role | Panel | Border | Game usage | Source |
|---|---|---|---|---|
| **primary** (orange) | 0.6604, 0.2775, 0.0592 | 0.8774, 0.4371, 0.0 | Resume, default primary action | `ui-palette.md:31` |
| **danger** (red) | 0.5189, 0.1297, 0.1718 | 0.7453, 0.2058, 0.0 | LeaveGame | `ui-palette.md:32` |
| **success** (green) | 0.1416, 0.2925, 0.1173 | 0.0512, 0.434, 0.115 | Invite | `ui-palette.md:33` |
| **info** (blue) | 0.0588, 0.3598, 0.6588 | 0.0, 0.5177, 0.8784 | PauseMenuButton | `ui-palette.md:34` |
| **accent** (purple) | 0.3919, 0.1843, 0.6235 | 0.5569, 0.1255, 0.8491 | Controls, ResetDefaults | `ui-palette.md:35` |
| **teal** | 0.185, 0.394, 0.6226 | 0.0, 0.5612, 0.5943 | Options | `ui-palette.md:36` |
| **gold** | 0.7736, 0.5567, 0.0 | 0.9451, 0.7576, 0.0 | Badges | `ui-palette.md:37` |
| **darkred** | 0.5, 0.0637, 0.0992 | 0.6509, 0.1851, 0.0 | 4th main-menu button | `ui-palette.md:38` |
| **confirm-OK** (lime) | 0.4007, 0.6039, 0.1412 | 0.6401, 0.8208, 0.0194 | `ConfirmPage/UI_OK` | my own run, §Appendix C |
| **confirm-cancel** (orange-red) | 0.8208, 0.2683, 0.089 | 1.0, 0.4263, 0.25 | `ConfirmPage/UI_X` | my own run, §Appendix C |

The last two rows are new: they are the actual colours of the game's **yes/no dialog**, and they are not the same as `success`/`danger`. Any Room Hub confirmation built on `ConfirmPage` inherits them for free.

### 1.3 Button anatomy — fixed, and the backbone of every Room Hub surface

Child order and geometry are identical across all PEAK buttons (`ui-palette.md:8-25`; independently re-measured by me on `ConfirmPage/UI_OK`, 156.5 × 67):

| # | Child | Anchors | anchoredPosition | sizeDelta | Colour |
|---|---|---|---|---|---|
| 1 | `Shadow` | stretch | (0, **−3.3**) | (0, **+10**) | 0, 0, 0, **0.2667** |
| 2 | `Panel` | stretch | (0, 0) | (0, +10) | **role Panel** |
| 3 | `Border` (top) | top stretch | (0, **−15**) | (**−20**, **7**) | **role Border** |
| 4 | `Border` (bottom) | bottom stretch | (0, **+14**) | (−20, 7) | **role Border** |
| 5 | `Glow` | stretch | (0, 0) | (0, +10) | **1, 0.7046, 0, 0** — alpha 0 at rest, fades in on hover |
| 6 | `Text` | stretch | (0, 0) | (**−60**, **−20**) | 1, 1, 1, 1 |

Three properties of this stack drive the whole design:

1. **Panel bleeds 5 px past the button on each side** (sizeDelta +10) and Shadow sits 3.3 px lower — the "thick slab" look. A row is therefore 67 px tall but visually ~77 px of ink.
2. **Border is an inset bright bar, not an outline**: 15 px from the top edge, 7 px tall, 10 px in from each side, and a mirrored one at the bottom. That is the candy-stripe highlight; there is no perimeter stroke anywhere in the kit.
3. **Glow is the hover mechanism** — a gold wash that is alpha 0 by default (`ui-palette.md:18, 89`). Hover is a *Glow fade-in*, not a Panel recolour.

Text inset varies with width: `(−60, −20)` on 150–156 px buttons (measured on `UI_OK` 156.5 and `UI_X` 102.7), `(−50, −20)` on the 277 px pause-menu button (`ui-palette.md:24`). `UI_PauseMenuButton`'s Border carries a −6.6 x offset / −28.5 width correction that is legacy and must **not** be copied (`ui-palette.md:25`).

### 1.4 Size tiers (official)

| Tier | Size | Where | Source |
|---|---|---|---|
| Main action | **420 × 67** | main menu primary | `ui-palette.md:61` |
| Secondary | **353 × 60** | main menu secondary | `:62` |
| Pause standard | **277 × 67** | pause menu (`MenuAPI.OPTIONS_WIDTH = 277f`, `PEAKLib.UI.decompiled.cs:860`) | `:63` |
| Pause narrow | **187.8 × 67** | pause menu | `:64` |
| Small | **182 × 50** | main menu | `:65` |
| Smaller | **191.9 × 44.2**, 125.8 × 44.2 | main menu | `:66` |
| Footer strip | **150 × 67** | main menu bottom | `:67` |

### 1.5 Layout metrics from prefab geometry (MEASURED)

| Element | Value | Source |
|---|---|---|
| Modal content columns | `InputFields` 1000 wide, `TitleFields` **1400** wide | `resources.assets` `Modal` |
| Modal title / subtitle blocks | `HeaderModalField` **150** tall, `SubheaderModalField` **40** tall | same |
| Modal button field | `ButtonsInputField` **100** tall, inner button 300 × 65 | same |
| Confirm window | **550 × 250**, options row 400 × 100 | `ConfirmPage` |
| Tab strip | height **40**, pivot (0.5,1), top-stretch, spacing **20**, tab min width **220** | `PEAKLib.UI.decompiled.cs:1677-1682, 1701, 1731-1734` |
| Page canvas | ScreenSpaceOverlay, `sortingOrder 1`, scaler **1920 × 1080**, `matchWidthOrHeight = 1` | `PEAKLib.UI.decompiled.cs:1461-1468` |

`matchWidthOrHeight = 1` is a constraint: at 1080p one unit = one pixel, and at 4:3 the canvas is only ~1350 units wide (§5 R-08).

### 1.6 Type

| Fact | Evidence |
|---|---|
| PEAK's display face is **Daruma Drop One**; PEAKLib finds it by `faceInfo.familyName == "Daruma Drop One"` | `PEAKLib.UI.decompiled.cs:1949-1956` |
| `LocalizedText` carries `defaultHeaderName = "Muli"` | `LocalizedText.cs:93` |
| Interactive labels auto-size **18 → 22**, `fontStyle = 16` (Bold), alignment `514` | `PEAKLib.UI.decompiled.cs:1233-1238, 1755-1760, 2062-2066` |
| Secondary text is done with inline TMP tags: `"<alpha=#CC><size=70%>"` | `BoardingPass.cs:193` |
| All-caps is suppressed for CJK/RU/UA/JA/KO | `LocalizedText.cs:131-141` |
| CJK needs the font-asset fallback swap (`fallbackFontAssetTable[1]` + `ReadFontAssetDefinition`) | `FontFallbackSwapper.cs:31-41`, driven from `LocalizedText.cs:507-517` |
| **TMP sizes are UNPROVEN** — `TextMeshProUGUI` raw offsets are unreliable, so the game's real header sizes are unknown | `ui-palette.md:87-88` |

Adopt **18 / 22** for interactive labels (measured), 22–28 for section headers, 16 for meta (DERIVED).

### 1.7 Icons

| Fact | Evidence |
|---|---|
| Input glyphs are TMP sprite assets, 128 × 128 cells: `KeyboardSpriteSheet_0..136`, `ButtonSpriteSheet{Xbox,PS4,PS5,Switch}_0..31` | sprite inventory of `resources.assets`; `ui-palette.md:79` |
| Emitted as a TMP tag, e.g. `"<sprite=4 tint=1>+<sprite=5 tint=1>"` | `InputIcon.cs:114` |
| Sprite asset chosen per scheme **and** per pad type, refreshed on `InputSchemeChanged` | `InputIcon.cs:41-101`; `GamepadTypeIconSelector.cs:31-57` |
| Simple scheme swap = two GameObjects toggled | `ShowOnlyForScheme.cs:16-24` |
| Host is marked by a **crown sprite + gold name** | `AudioLevelSlider.cs:74,86`; `UIPlayerNames.cs:86`; `Crown` asset `ui-palette.md:78` |

---

## 2. Interaction language

### 2.1 Page model

`UIPageHandler` builds a type-keyed dictionary of **all** descendant `UIPage`s including inactive, forces them inactive, activates the start page, then calls `OnPageEnter()` + `OnPageEntered()` (`UIPageHandler.cs:15-31`).

Transition order is a contract any custom transition must honour:

```
old.OnPageExit() → old.SetActive(false) → new.SetActive(true) → new.OnPageEnter() → new.OnPageEntered()
```
`SetActivePageTransistion.cs:5-12`; `UIPageHandler.cs:51-63`; `PageBase.cs:16-22` (fans out to `IOnPageEntered`).

`UIPage` caches `pageHandler` from `GetComponentInParent<UIPageHandler>()`, and on enter deactivates all `UISubPage`s then activates `[0]` (`UIPage.cs:16-38`). Back navigation: `IHaveParentPage.GetParentPage()` returns `(UIPage, PageTransistion)`, consumed by the host handler (`IHaveParentPage.cs:5-10`; `MainMenuPageHandler.cs:80-84`; `PauseMenuHandler.cs:45-51`).

### 2.2 Window model — the only sane host for a mod panel

`MenuWindow` implements `INavigationContainer` and owns the static `AllActiveWindows` list (`MenuWindow.cs:7-9`).

| Member | Behaviour | Evidence |
|---|---|---|
| flags `openOnStart`/`selectOnOpen`/`closeOnPause`/`closeOnUICancel`/`blocksPlayerInput`/`showCursorWhileOpen`/`autoHideOnClose` | virtual; all true except `closeOnPause`/`closeOnUICancel` | `MenuWindow.cs:11-25` |
| `Open()` / `Close()` | **`internal`** — must be reflected; `Show()` alone never registers the window | `:80-99`, `:131-144` |
| `Update()` | `INavigationContainer.PushActive(this)` every frame while open — this is what keeps controller focus alive | `:50-57` |
| `GetContainerPriority()` | **1** | `:175-178` |
| `IsValidSelection` | `selection.activeInHierarchy` only | `:189-192` |
| `MenuWindowTabbed` | opens one child window, closes the rest, wraps | `MenuWindowTabbed.cs:22-54` |

Current mod already reflects `Open`/`Close` (`HistoryPageController.cs:54-58`); that stays.

### 2.3 Tabs

`TABS<ButtonType>` collects all `TAB_Button` descendants **including inactive** in `Start`, then selects `selectedButton` (`TABS.cs:14-26`); `Select` deselects previous → selects next → fires `OnSelected` (`:38-57`); `SelectRelative` wraps and **skips inactive tabs** (`:74-93`); runtime additions must use `AddButton` (`:28-36`).

`TAB_Button` wires `onClick` → `GetComponentInParent<ITABS>().SelectGeneric(this)` (`TAB_Button.cs:31-43`), tracks `hoveredOn` via pointer events (`:62-73`), and on `Select()` fans out to every `ITabAction` (`:45-54`).

First-party implementation to copy: `SettingsTABS` maps `OnSelected` → show category (`SettingsTABS.cs:12-15`) and drives `SelectNext/Previous` from two `InputActionReference`s (`:23-33`); `SettingsTABSButton` does the visual — `text.color = Lerp(current, Selected ? Color.black : Color.white, unscaledDeltaTime * 7f)` + `SelectedGraphic.SetActive(Selected)` (`SettingsTABSButton.cs:10-15`). `TABS_Navigation` is the generic version (`TABS_Navigation.cs:20-30`).

### 2.4 Focus and selection — the rules that decide the whole IA

| Rule | Evidence |
|---|---|
| A container competes for focus only on frames where it called `PushActive`; the list clears every frame | `NavigationContainerHandler.cs:22-41` |
| Winner = max priority: `PageNavigationHandler` 0, `MenuWindow` 1, `ConfirmPage` **99** | `PageNavigationHandler.cs:50-53`; `MenuWindow.cs:175-178`; `ConfirmPage.cs:68-71` |
| Auto-reselect happens **only outside KeyboardMouse** | `NavigationContainerHandler.cs:46, 61-69` |
| On KeyboardMouse the game **clears** the selection unless it is a `TMP_InputField` | `:71-78` |
| `UIInputHandler.SetSelectedObject` is a no-op outside gamepad | `UIInputHandler.cs:78-84` |
| Page transitions push the first element only for gamepad, via `INavigationPage.GetFirstSelectedGameObject()` | `PageNavigationHandler.cs:21-31` |
| Lists wire `Navigation` **by hand**, row by row | `BlockedPlayerUI.cs:48-59`; `AudioLevels.cs:109-158`; `MainMenuMainPage.cs:205-210`; `PauseMenuMainPage.cs:110-115` |
| Gamepad scrolling: element registers `ISelectHandler`, maps **active sibling index** → scrollbar value, lerp 15/s, gamepad only | `ScrollRectAutoScrollerElement.cs:10-25`; `ScrollRectAutoScroller.cs:19-37` |

**Design consequence: on keyboard+mouse the game uses no EventSystem selection at all.** The panel must be fully operable by pointer, and "selected row" is a gamepad-only state.

### 2.5 Confirm / cancel

| Input | Behaviour | Evidence |
|---|---|---|
| Pause / back | `MenuWindow.closeOnPause` consumes `pauseWasPressed`; `PauseMenuHandler` tries `IHaveParentPage` → close confirm/modal → hide | `MenuWindow.cs:59-74`; `PauseMenuHandler.cs:43-63` |
| UI cancel | `MenuWindow.closeOnUICancel` consumes `cancelWasPressed` | `MenuWindow.cs:68-72` |
| Yes/no | `ConfirmPage.Open(key, onOk, onCancel)`, both buttons **debounce 0.2 s**, default selection is **cancel** unless `okIsDefault`, priority 99 | `ConfirmPage.cs:17, 52-60, 68-89, 97-113` |
| Destructive in pause menu | opens `confirmWindow`, rewrites prompt, rebinds both buttons, then **`m_confirmCancelButton.Select()`** | `PauseMenuMainPage.cs:169-199` |

Copy the last rule verbatim: **destructive flows pre-focus Cancel.**

### 2.6 Modals

`Modal` is a `RetrievableResourceSingleton<Modal>` surviving scene loads (`Modal.cs:8-9, 32-37`).

| Fact | Evidence |
|---|---|
| "Modal up" = `canvasGroup.blocksRaycasts`, not `activeSelf` | `Modal.cs:18` |
| `OpenModal(header, body, onClose)` / `OpenYesNoModal(header, no, yes, onNo, onYes, onClose)` | `:39-54` |
| `Open` clears both parents before building — repeated opens do not accumulate | `:71-75` |
| Header = `Resources.Load("HeaderModalField")` + `("SubheaderModalField")` | `DefaultHeaderModalOption.cs:18-26` |
| Buttons = `Resources.Load("ButtonsInputField")` / `("VerticalButtonsInputField")` | `ModalButtonsOption.cs:30-35`; `VerticalModalButtonsOption.cs:18` |
| Horizontal buttons get explicit left/right navigation after instantiation | `ModalButtonsField.cs:33-48` |
| Click closes the modal **then** invokes the callback | `:22-29` |
| Fade: `Lerp(alpha, target, unscaledDeltaTime * 20f)` | `Modal.cs:77-81` |

### 2.7 Animation vocabulary (measured)

| Idiom | Numbers | Evidence |
|---|---|---|
| Modal fade | Lerp 20/s, **unscaled** | `Modal.cs:80` |
| Global UI fade | Lerp 10/s, unscaled | `GameUI.cs:195` |
| Window open/close | `DOFade(1f, 0.5f)` in, `DOFade(0f, 0.2f)` out, aborts if reopened | `BoardingPass.cs:144-173` |
| Tab label | Lerp 7/s unscaled | `SettingsTABSButton.cs:13` |
| Gamepad scroll | Lerp 15/s unscaled | `ScrollRectAutoScroller.cs:35` |
| Reticle squash | `DOSizeDelta(..., 0.33f).SetEase(Ease.InOutCubic)` | `GUIManager.cs:751` |
| Sprite flip-book | index advance on `unscaledDeltaTime >= delay` | `ImageSpriteAnimator.cs:21-34` |
| Shader fade | `_FadeTime = Time * 1.4f` | `FadeInEffect.cs:13-18`; `EffectBase.cs:52-60` |

Rules: **unscaled time always** (pause sets `Time.timeScale = 0` offline, `PauseMenuHandler.cs:21-25`) and **0.2–0.5 s** durations.

### 2.8 Sound

* **No UI sound API exists in code** — no `PlayClick`, no `UISound`; the SFX node names appear nowhere in `Assembly-CSharp` (grep → 0 hits). They exist only as prefab GameObjects (`SFX Hover (1)`, `SFX Hover (2)`, `SFX Click (1)`, `SFX Appear (1)`, `SFX Appear (2)` on `ConfirmPage/UI_OK` and `UI_X`). **Trigger mechanism UNPROVEN.**
* Playable primitive: `SFX_PlayOneShot` (`sfx`, `sfxs`, `playOnStart`, `playOnEnable`, `followTransform`, `stopOnDisable`; `Play()`/`PlayOneShot()` → `SFX_Player.instance.PlaySFX`), `SFX_PlayOneShot.cs:5-71`.
* `GUIManager` exposes only `ForcePlayStinger(AudioClip)` and `stingerSound` (`GUIManager.cs:162, 474-482`).
* **Recommendation: ship v1 silent.** If needed later, attach `SFX_PlayOneShot` to rows and drive it from the same handler as the click.

---

## 3. Building blocks

### 3.1 Reusable at runtime

| Block | Class / asset | Access | Evidence |
|---|---|---|---|
| Yes/no dialog | `Peak.UI.ConfirmPage` | `ConfirmPage.Open(key, onOk, onCancel)` | `ConfirmPage.cs:9, 52-60` |
| Modal | `Zorro.UI.Modal` | `OpenModal` / `OpenYesNoModal` / `CloseModal` / `IsOpen` | `Modal.cs:20-62` |
| Modal fields | `HeaderModalField`, `SubheaderModalField`, `ButtonsInputField`, `VerticalButtonsInputField` | `Resources.Load` | `DefaultHeaderModalOption.cs:20-21`; `VerticalModalButtonsOption.cs:18` |
| Window host | `MenuWindow` + `AllActiveWindows` | subclass + reflect `Open`/`Close` | `MenuWindow.cs:9, 80, 131` |
| Page host | `UIPageHandler` / `UIPage` | component lookup | `UIPageHandler.cs:7`; `UIPage.cs:7` |
| Tab controller | `TABS<TAB_Button>` + `ITABS` + `ITabAction` | subclass | `TABS.cs:7`; `ITABS.cs:3-9` |
| Gamepad tabs | `TABS_Navigation` | add component + 2 action refs | `TABS_Navigation.cs:7-31` |
| Gamepad scroll | `ScrollRectAutoScroller` + `...Element` | add components | `ScrollRectAutoScroller.cs:7` |
| Scheme hints | `ShowOnlyForScheme`, `GamepadTypeIconSelector` | add components | `ShowOnlyForScheme.cs:5`; `GamepadTypeIconSelector.cs:8` |
| Glyph text | `InputIcon` + `InputSpriteData` | add component | `InputIcon.cs:7-27, 114` |
| Selection colour | `OnSelectChangeGraphicColor` | add component | `OnSelectChangeGraphicColor.cs:7-28` |
| Localisation | `LocalizedText` | `SetTextLocalized` / `SetIndex` / `GetText` | `LocalizedText.cs:343-424` |
| CJK fallback | `FontFallbackSwapper.instance` | automatic via `LocalizedText.SetLanguage` | `FontFallbackSwapper.cs:6, 21-29` |
| Pause page | `GUIManager.instance.pauseMenuMainPage` | public field | `GUIManager.cs:76` |
| Sound | `SFX_PlayOneShot` + `SFX_Player.instance` | add component | `SFX_PlayOneShot.cs:5, 59-71` |

### 3.2 PEAKLib.UI 1.7.2

`UIPlugin` = `PEAKLib.UI` **1.7.2**, id `com.github.PEAKModding.PEAKLib.UI`, hooks via **MonoDetour** (`PEAKLib.UI.decompiled.cs:1052, 1061, 1065`; hooks `:1124-1208`).

| Component | What it is | Verdict | Evidence |
|---|---|---|---|
| `PeakElement` | `MonoBehaviour` caching `RectTransform` | base class | `:1646-1655` |
| `PeakText` | TMP + localisation; **forces Daruma Drop One**, white, and `sizeDelta = GetPreferredValues()` | avoid in rows | `:1937-2015` |
| `PeakButton` | self-building button (darkPanel fill + auto-size 18–22 bold) | ok for dense actions | `:1212-1265` |
| `PeakMenuButton` | clone of a **real scene menu button** with `Panel`/`Text`/`Border`/`BorderBottom` | **the clone source** | `:1836-1899` |
| `PeakScrollableContent` | ScrollRect + RectMask2D + VerticalLayoutGroup + ContentSizeFitter, sensitivity 50, clamped | yes (with §5 R-04 caveat) | `:1900-1936` |
| `PeakHorizontalTabs` | 40 px strip, 220 min width, spacing 20 | reference | `:1656-1820` |
| `PeakTextInput` | TMP_InputField, 22 px, alignment 4097 | yes | `:2016-2102` |
| `PeakDropdown` | TMP_Dropdown | situational | `:1543-1645` |
| `PeakCustomPage` | `MenuWindow` + Canvas (sortingOrder 1) + 1920×1080 scaler + black sheet | **panel host** | `:1412-1542` |
| `PeakChildPage` | `UIPage` + `IHaveParentPage` + back button | drill-in | `:1266-1411` |
| `MenuAPI` | factory for pages/buttons/text/scroll/input | yes | `:846-1050` |
| `Templates` | `ButtonTemplate`, `SettingsCellPrefab`, 4 sprite sheets | fragile (§5 R-06) | `:2103-2141` |
| `ElementExtensions` | `ParentTo`, `SetAnchorMinMax`, `AlignToParent`, `ExpandToParent` | yes | `:593-845` |
| `Utilities.GetContrastingColor` | HSV value ±0.20 | yes | `:1092-1101` |

### 3.3 The zero-asset rule (hard constraint)

**The mod must not load, bundle, or `Resources.Load` any game art at runtime.** Instead, harvest once from the already-cloned official button template:

| Harvest from | Take | Reuse for |
|---|---|---|
| `Panel` child `Image` | `sprite` (the white 9-slice slab), `material`, `color` | every panel, row, chip, skeleton bar |
| `Border` child `Image` | `sprite` (thin bar), `color` | borders, dividers, tab underline, chip edge |
| `Shadow` child `Image` | `sprite`, `color` (0,0,0,0.2667) | panel drop shadow, row shadow |
| `Glow` child `Image` | `sprite`, `color` (1,0.7046,0,0) | hover wash, focus ring, selection |
| `Text` child `TextMeshProUGUI` | `font`, `fontSharedMaterial` — **only** as a last resort (see §5 R-03) | labels |

Clone source already exists: `UiButtonFactory.PrepareTemplate` clones a real scene button, strips `LocalizedText`, clears listeners, and keeps it `DontDestroyOnLoad` (`src/RoomHub/UiButtonFactory.cs:178-196`). Extend it to also publish the four `Image`s so the skin layer never touches `Resources`.

Why this is the right call, not a workaround: the tokens are the *colour* half of the look; the *shape* half is the sprite's 9-slice border and corner radius. Harvesting the live `Sprite` object gives both, at zero packaging cost and zero version drift.

### 3.4 What we must build ourselves

There is no first-party or PEAKLib component for: a scrollable list of **selectable rows with stable identity**, a row badge/chip, a status dot, an empty-state block, a footer hint bar, a search field with live results, a two-pane layout, or a skeleton loader. All of §4's "self-draw" column is new code.

---

## 4. Room Hub redesign

### 4.1 Principles

1. **Colour is semantics.** Each view owns one official role colour; the user learns the panel by colour, the same way they learn Resume=orange, Leave=red.
2. **Reuse the exact button stack** (Shadow→Panel→Border×2→Glow→Text) for every interactive surface; the row *is* a button.
3. **Hover is Glow, not a recolour** — that is what the game does (`ui-palette.md:18, 89`).
4. **Full-screen scrim + centred panel**, which is what the game's own modal and settings page do (`ui-palette.md:52`; `Modal`/`ConfirmPage` geometry §1.5).
5. **M/K = pointer, gamepad = selection** (§2.4).
6. **Destructive flows pre-focus Cancel** (§2.5).
7. **Unscaled time everywhere** (§2.7).

### 4.2 Panel skeleton

Canvas: `PeakCustomPage` (ScreenSpaceOverlay, `sortingOrder 1`, scaler 1920×1080, match height) — see §5 R-02 for the sorting risk.

```
┌──────────────────────────── 1920 × 1080 reference ───────────────────────────────┐
│  scrim: Image, stretch, (0,0,0,0.8667)                                           │
│                                                                                  │
│    ┌──────────────── 1400 × 820, centred ───────────────┐                        │
│    │ HEADER  h=150  (HeaderModalField height)           │                        │
│    │   Title    TMP 40 bold, white, 24 px inset         │                        │
│    │   Subtitle TMP 20, white a=.70, 6 px below         │                        │
│    │   Close    102.7 × 67, right, y=-40 (UI_X)         │                        │
│    ├────────────────────────────────────────────────────┤                        │
│    │ TAB STRIP h=40, spacing 20, tabs min 220           │                        │
│    │   [ Room history ][ Search ][ Friends ]            │                        │
│    ├────────────────────────────────────────────────────┤                        │
│    │ DIVIDER h=7  (Border bar height)                   │                        │
│    ├────────────────────────────────────────────────────┤                        │
│    │ CONTENT 1400 × 480 (ScrollRect + RectMask2D)       │                        │
│    │   rows 1340 × 67, gap 6, side inset 30             │                        │
│    ├────────────────────────────────────────────────────┤                        │
│    │ FOOTER h=100  (ButtonsInputField height)           │                        │
│    │   hints left (scheme-swapped) · acts right         │                        │
│    └────────────────────────────────────────────────────┘                        │
└──────────────────────────────────────────────────────────────────────────────────┘
```

DERIVED numbers: panel **1400** = the measured `TitleFields` column; height **820** = 1080 − 2×130; content **480** = 820 − 150 − 40 − 7 − 100 − 2×22, sized so ~6.5 rows of 73 px are visible (a clipped 7th row is the "it scrolls" affordance); row width **1340** = 1400 − 2×30 inset.

### 4.3 Token → element mapping (what to use, and why)

| Element | Token | Why this one |
|---|---|---|
| Backdrop behind the panel | `scrim` (0,0,0,0.8667) | the game's own full-screen dim for settings; heavier dims read as a loading screen |
| Panel body | `darkPanel` (0.1792,0.1253,0.0905,0.7294) | the game's standard dark content surface (`Controls/Image`); translucent so the world stays faintly visible |
| Panel drop shadow | `Shadow` child colour (0,0,0,0.2667) | same shadow the buttons use; keeps one shadow value in the whole panel |
| Panel edge | `tanBorder` (0.7529,0.6745,0.5529), 2 px inset | the only "paper" edge the game uses on framed boxes (`Box/Border`) |
| Divider under the tab strip | `divider` (0.3396,0.2996,0.2419), 7 px | the game's title separator (`Title (1)/Line (1)`) |
| Title text | `text.primary` white | every button label is white; headers follow |
| Subtitle / meta | `text.primary` @ 0.70 | matches TMP `<alpha=#CC>` idiom (`BoardingPass.cs:193`) |
| Tab — unselected | `darkPanel` fill, white label | same surface family as the panel |
| Tab — selected | `paper` fill, **black** label | `SettingsTABSButton` inverts the label to black when selected (`:10-15`) — black text requires a light plate, and `paper` is the game's light plate |
| Row — room history | **primary** (0.6604,0.2775,0.0592 / 0.8774,0.4371,0.0) | room rows are the "resume" action of this panel |
| Row — player search | **info** (0.0588,0.3598,0.6588 / 0.0,0.5177,0.8784) | search results are informational, not the primary action |
| Row — friend, joinable | **success** (0.1416,0.2925,0.1173 / 0.0512,0.434,0.115) | matches the game's Invite button, which is the same social action |
| Row — friend, in a non-joinable state | **teal** (0.185,0.394,0.6226 / 0.0,0.5612,0.5943) | de-emphasised but still interactive-looking |
| Row — friend, offline | **locked** (0.5294,0.4784,0.4) fill | the game's own disabled/absent colour |
| Row — delete mode | **danger** (0.5189,0.1297,0.1718 / 0.7453,0.2058,0.0) | the game's LeaveGame red |
| Row — stale (>N days) | role fill × 0.6, `text.primary` @ 0.55 | no new constant; a multiply of the role colour |
| Host badge | **gold** (0.7736,0.5567,0.0 / 0.9451,0.7576,0.0) + `Crown` sprite | gold is the game's Badge colour, and the crown already means host (`AudioLevelSlider.cs:74`) |
| Chip background | `chipBg` (0.3585,0.271,0.2452) | the game's icon-plate colour |
| Hover wash | `Glow` colour (1,0.7046,0,0) | the official hover mechanism |
| Selected row (gamepad) | Glow at rest + `paper` border swap | no official selected-row constant exists (UNPROVEN), so reuse two known-good values rather than invent one |
| Confirm OK / Cancel | **confirm-OK** / **confirm-cancel** (§1.2) | use `ConfirmPage` and inherit them |
| Skeleton bar | `darkPanel` fill @ 0.35 | a dimmed panel, not a new colour |

### 4.4 Tab strip spec

* Three tabs: **Room history** · **Player search** · **Friends**.
* Geometry: height 40, `HorizontalLayoutGroup.spacing = 20`, `LayoutElement.minWidth = 220`, `preferredHeight = 40` — from `PeakHorizontalTabs` (`:1677-1734`).
* Children in order: `Image` fill (`darkPanel`) · `Selected` fill (`paper`, active only when selected) · `Text (TMP)` 18–22 auto bold centred.
* Selected: label lerps to **black** at 7/s and `Selected` turns on — literally `SettingsTABSButton.Update`.
* Hover (M/K and pointer): `Glow` alpha 0 → 0.6 over 0.15 s unscaled (DERIVED target; curve UNPROVEN).
* Controller: `ITABS.SelectNext/SelectPrevious` via `TABS_Navigation` or the `SettingsTABS` pattern.

### 4.5 Row anatomy

Row root is a `Selectable` at **1340 × 67**, with the official button stack as its first six children (order is load-bearing — `PeakMenuButton` resolves `BorderBottom` as the sibling *after* `Border`, `PEAKLib.UI.decompiled.cs:1846-1854`):

| # | Child | Type | Geometry | Token |
|---|---|---|---|---|
| 1 | `Shadow` | Image | stretch, sizeΔ (0, 10), pos (0, −3.3) | (0,0,0,0.2667) |
| 2 | `Panel` | Image | stretch, sizeΔ (0, 10) | role fill |
| 3 | `Border` | Image | top stretch, sizeΔ (−20, 7), pos (0, −15) | role border |
| 4 | `Border` | Image | bottom stretch, sizeΔ (−20, 7), pos (0, 14) | role border |
| 5 | `Glow` | Image | stretch, sizeΔ (0, 10) | (1,0.7046,0,0), alpha 0 |
| 6 | `Text`-container | RectTransform | stretch, sizeΔ (−60, −20) | — |
| 7 | `Avatar` | RawImage | 44 × 44, left x = 24 | no round mask in the kit → square |
| 8 | `Title` | TMP | left, x = 84, upper | 22 bold, white |
| 9 | `Meta` | TMP | left, x = 84, lower | 18, white @ 0.70 |
| 10 | `Chip` | Image + TMP | 96 × 28, after Meta | `chipBg` |
| 11 | `Time` | TMP | right-aligned | 16, white @ 0.70 |
| 12 | `Action` | Button 102.7 × 67 | right, x = −12 | own role (Join = success) |
| 13 | `SFX Hover (1)`, `SFX Hover (2)`, `SFX Click (1)` | empty GO | 100 × 100 | parity with the game's buttons |

Per-view binding:

| View | Title | Meta | Chip | Time | Action |
|---|---|---|---|---|---|
| **Room history** | host display name | room code · player count | `HOST` crown when it is your own room | "joined 2 h ago" | `Rejoin` (primary) |
| **Player search** | player name | SteamID · mutual rooms | `FRIEND` when already a friend | last seen | `Add` / `Invite` (info) |
| **Friends** | friend name | "In lobby" · "In game: Alpine" · "Offline" | status dot | last seen | `Join` (success, disabled when not joinable) |

### 4.6 State matrix

| State | Trigger | Visual | Basis |
|---|---|---|---|
| normal | — | role Panel + role Border, white text | OFFICIAL |
| hover | `IPointerEnterHandler` (both schemes) | `Glow` alpha → 0.6 over 0.15 s unscaled; Panel unchanged | mechanism OFFICIAL (`ui-palette.md:18,89`), target alpha DERIVED |
| pressed | `IPointerDown` / `ISubmitHandler` | `ColorBlock` pressed = ×0.85 of Panel colour | DERIVED (solves the missing pressed constant, `ui-palette.md:90`) |
| selected (gamepad) | `ISelectHandler` | `Glow` held at 0.6 + both `Border` bars swapped to `paper`; label stays white | DERIVED (no official constant) |
| disabled | `Selectable.interactable = false` | `ColorBlock` disabled = ×0.5 of Panel colour, `locked` text | DERIVED (solves `ui-palette.md:90`) |
| host | `IsMasterClient` | `Crown` sprite + gold title tint | OFFICIAL rule (`AudioLevelSlider.cs:74,86`) |
| stale | unseen > N days | Panel × 0.6, title @ 0.55, `STALE` chip | DERIVED |
| offline | friend in no lobby | Panel = `locked`, avatar @ 0.5, hollow dot, action disabled | DERIVED |
| danger (delete mode) | delete mode on | Panel = `danger`, action becomes `Delete` | OFFICIAL role |

Because Panel colour *is* the role colour and the `Selectable` transition is `ColorTint` (multiplicative), pressed/disabled need **no new constants** — that closes the gap the palette doc records at `ui-palette.md:90`.

### 4.7 Empty, loading, error

| State | Composition |
|---|---|
| No history | centred block: `UI_BlurCircle`-derived glow plate 96 × 96 @ 0.18, title 22 bold "No rooms yet", meta 18 "Join a room and it will appear here" |
| No search results | same block, title "No players found", meta echoes the query |
| No friends online | same block, title "No friends online", meta "Friends in a PEAK lobby show up here" |
| Loading | **6 skeleton rows** 1340 × 67, Panel at 0.35 alpha, 0.9 s unscaled alpha pulse 0.25 ↔ 0.45; no spinner (kit has none) |
| Error | one row-shaped panel in `danger`, title "Could not read history", meta = first line of the exception, action `Retry` (info) |
| Mod disabled | never render; single-button modal instead |

All three empty states are currently bare `PeakText` lines (`HistoryPageController.cs:899, 919, 936, 953`) — exactly the "stack of plain text" being removed.

### 4.8 Confirmation flows

| Flow | Mechanism | Why |
|---|---|---|
| Delete one room | `ConfirmPage.Open("ROOMHUB_DELETE_ONE", onOk, onCancel)` | game dialog, 0.2 s debounce, cancel pre-focused, priority 99 |
| Clear all history | same, prompt names the count | same |
| Leave lobby to join another | `Modal.OpenYesNoModal(new DefaultHeaderModalOption(t, b), no, yes, onNo, onYes)` | matches the game's leave-confirm pattern; auto-closes on click (`ModalButtonsField.cs:22-29`) |
| Invite a friend | no confirmation; inline result on the row | matches the game's immediate invite (`PauseMenuMainPage.cs:136-142`) |

Never build a custom confirm — `ConfirmPage` already owns priority 99 and steals focus correctly.

### 4.9 Navigation map

| Context | Input | Result |
|---|---|---|
| Panel closed | `Esc` (K) / pause (G) | nothing — Room Hub must not steal pause (`HistoryUi.cs:184-188` patches `GUIManager.UpdatePaused`) |
| Panel open | `Esc` (K) / UICancel (G) | `MenuWindow.closeOnUICancel` closes the panel; if a confirm is up, close that first (`PauseMenuHandler.cs:52-56` order) |
| Tabs | `UITabLeft`/`UITabRight` (both schemes), `Q`/`E` (K), LB/RB (G) | previous/next, wrapping, skipping inactive (`TABS.cs:74-93`; `TABS_Navigation.cs:20-30`) |
| Tabs | click / South (G) | select (`TAB_Button.ButtonClicked`) |
| Tabs → content | Down (G) | first row, or the search field on the Player-search tab |
| Content | Up/Down (G) | previous/next row via **explicit** `selectOnUp`/`selectOnDown` chained at build time (`BlockedPlayerUI.cs:48-59`) |
| Content | South (G) / click (P) | activate the row's action (`Rejoin`/`Join`) |
| Row → action | Right (G) | `selectOnRight` on the row (`AudioLevels.cs:148`) |
| Content | page up/down (G) | `ScrollRectAutoScrollerElement` per row drives the scrollbar — requires `verticalScrollbar` assigned (§5 R-04) |
| Content | wheel (P) | native `ScrollRect` |
| Search field | typing (K) | `TMP_InputField`; the engine keeps field selection on M/K (`NavigationContainerHandler.cs:74`) |
| Search field | `Esc` | blur first, then close (guard already exists, `HistoryUi.cs:145-169`) |
| Footer hints | automatic | `ShowOnlyForScheme` swaps K/G hint sets (`:16-24`); `InputIcon` picks the glyph sheet (`:54-101`) |

Hint copy — K: `Esc Close` · `Q/E Tabs` · `Enter Open` · `Del Delete`. G: `B Close` · `LB/RB Tabs` · `A Open` · `X Delete`.

### 4.10 Clone vs self-draw

| Component | Approach | Detail |
|---|---|---|
| Row background stack (Shadow/Panel/Border×2/Glow) | **clone** | instantiate the `UiButtonFactory` template, strip its `Text`, retint Panel/Border to the row role |
| Row `Action` button | **clone** | same template, own role colour, text = "Rejoin"/"Join"/"Add" |
| Tab button | **clone** | template with a `Selected` overlay plate; label tinted by `SettingsTABSButton` rule |
| Close button (X) | **clone** | template at 102.7 × 67 with confirm-cancel colour |
| Footer action buttons | **clone** | 156.5 × 67 |
| Panel body, header plate, divider, chip, status dot, skeleton bar, empty-state plate, footer bar | **self-draw** | plain `Image` using the harvested `Panel`/`Border` sprite, `Type.Sliced`, official colours; never a new texture |
| Avatar | **self-draw** | `RawImage` + Steam texture, square; kit has no round mask |
| All text | **self-draw** | `TextMeshProUGUI` + `LocalizedText` (§5 R-03), 18/22, `NoWrap` + `Ellipsis` on row labels |

Rule of thumb: **clone when the element is a button the game already has; self-draw everything else, but always with the harvested sprite and an official colour.**

### 4.11 Component list → class mapping

| Component | Class | Base | Approach |
|---|---|---|---|
| Panel host | `RoomHubPanel` | `PeakCustomPage` | clone/PEAKLib |
| Backdrop | child `Background` Image | — | self-draw, `scrim` |
| Header | `RoomHubHeader` | `PeakElement` | self-draw |
| Close | `PeakMenuButton` | — | clone |
| Tab strip | `RoomHubTabs : TABS<RoomHubTabButton>` | game `TABS<T>` | clone buttons |
| Tab | `RoomHubTabButton : TAB_Button` | game `TAB_Button` | clone + `UpdateSelection` override |
| Divider | plain Image | — | self-draw, `divider` |
| Content | `PeakScrollableContent` | PEAKLib | PEAKLib |
| Row | `RoomHubRow : Selectable` + pointer/select handlers | — | clone chrome |
| Row data | `RoomHubRowData` struct | — | new |
| Host badge | `RoomHubHostBadge` | `PeakElement` | self-draw + `Crown` sprite |
| Status dot | `RoomHubStatusDot` | `PeakElement` | self-draw |
| Avatar | `RoomHubAvatar` | `PeakElement` | self-draw, `SteamAvatarService` |
| Chip | `RoomHubChip` | `PeakElement` | self-draw, `chipBg` |
| Empty state | `RoomHubEmptyState` | `PeakElement` | self-draw |
| Skeleton | `RoomHubSkeletonRow` | `PeakElement` | self-draw |
| Footer | `RoomHubFooter` + `ShowOnlyForScheme` children | `PeakElement` | self-draw |
| Confirm | — | — | game `ConfirmPage` / `Modal` |
| Search | `PeakTextInput` | PEAKLib | PEAKLib |

### 4.12 Migration from the current panel

Keep: the window host and reflect-`Open` trick (`HistoryPageController.cs:54-58`), input-block/cursor handling (`:244-291`), scene-drop lifecycle (`HistoryUi.cs:104-108`), and `UiButtonFactory` as the single template source. Replace: title + three hint `PeakText`s → header block + footer hint bar; delete banner → row danger state; the four `Populate*` methods keep their data flow but emit `RoomHubRowData` instead of building GameObjects inline (`HistoryRowFactory.cs:44-67` builds rows directly today).

---

## 5. Risks

**R-01 — HIGH — violating the zero-asset rule breaks on the next game update.** Calling `Resources.Load("UISprite")` / shipping a copied PNG both couple the mod to asset names or to a snapshot of the art. *Mitigation:* harvest sprite + colour from the `UiButtonFactory` clone only (§3.3); treat any `Resources.Load` of game art as a review blocker.

**R-02 — HIGH — canvas sorting versus the game's modal.** `PeakCustomPage` hardcodes `sortingOrder = 1` with `overrideSorting` at default (`PEAKLib.UI.decompiled.cs:1461-1468`); the `ConfirmPage` prefab Canvas is `renderMode 0` with no explicit order (measured). If the pause canvas sorts above 1, the panel renders under it; `ConfirmPage` must sit above Room Hub and may not. *Status:* UNPROVEN which order the game uses. *Mitigation:* read the pause canvas's `sortingOrder` at open and set ours to `that + 1`; verify a delete confirm opened from inside the panel still draws on top.

**R-03 — HIGH — CJK renders as tofu if the font is set by hand.** `PeakText.Awake` assigns Daruma Drop One (`PEAKLib.UI.decompiled.cs:1988`), which has no CJK coverage; the game's fallback only swaps when `LocalizedText.SetLanguage` runs with a live `FontFallbackSwapper`, and it mutates `fallbackFontAssetTable[1]` of the **main base font** (`FontFallbackSwapper.cs:31-41`; `LocalizedText.cs:507-517`). `HistoryUiFont` currently pins whatever font a scene label happens to use plus its `fontSharedMaterial` (`HistoryUiFont.cs:55-85`), bypassing the chain. *Mitigation:* add `LocalizedText` to every label and let the game own the font; if a manual font is unavoidable, use `FontFallbackSwapper.instance.mainBaseFont` and never copy `fontSharedMaterial`.

**R-04 — HIGH — gamepad scrolling throws.** `ScrollRectAutoScroller.Update` dereferences `m_scrollRect.verticalScrollbar.value` unguarded (`ScrollRectAutoScroller.cs:35`), and `PeakScrollableContent` never creates a scrollbar (`PEAKLib.UI.decompiled.cs:1913-1934`). Adding the element component to rows over a PEAKLib scroll view → `NullReferenceException` on first pad selection. *Mitigation:* build the scroll view with an explicit vertical `Scrollbar` and assign it, or skip auto-scroll in v1 and set scroll position manually from the row index.

**R-05 — MEDIUM — Glow's hover curve is unknown.** `Glow` rests at alpha 0 and is assumed to fade in on hover (`ui-palette.md:18, 89`); the driver (`SelectableEvents`/`Animator`) is unverified. *Mitigation:* drive Glow ourselves (0 → 0.6, 0.15 s unscaled) instead of relying on the clone's own animator; disable any `Animator` on the clone.

**R-06 — MEDIUM — pressed/disabled constants do not exist.** The scenes only contain rest states (`ui-palette.md:90`). *Mitigation:* derive them multiplicatively through `Selectable`'s `ColorTint` on the role-coloured Panel (§4.6) — no new colours enter the system.

**R-07 — MEDIUM — focus is destroyed by list rebuilds.** Rows are destroyed and recreated each refresh (`HistoryPageController.cs:683-872`); `NavigationContainerHandler` reselects only on gamepad (`:59-69`) and *clears* selection on M/K (`:71-78`). *Mitigation:* diff-rebuild keyed by lobby id; re-`Select()` the previously selected row after a rebuild.

**R-08 — MEDIUM — `matchWidthOrHeight = 1` breaks a fixed 1400-wide panel at 4:3.** At 1280×1024 the canvas is ~1350 units wide. *Mitigation:* clamp width to `Mathf.Min(1400, canvasWidth - 80)` or anchor-stretch with `LayoutElement.maxWidth`.

**R-09 — MEDIUM — `Templates.ButtonTemplate` is resolved by scene object name and is fragile.** PEAKLib's postfix looks for `"UI_MainMenuButton_LeaveGame (2)"` and throws if absent (`PEAKLib.UI.decompiled.cs:1135-1145`); the mod already works around it (`UiButtonFactory.cs:122-176`). *Mitigation:* keep `UiButtonFactory` as the only button source and never call `MenuAPI.CreateMenuButton` directly.

**R-10 — MEDIUM — pause-menu time scale freezes scaled animations.** `PauseMenuHandler.OnEnable` sets `Time.timeScale = 0` offline (`:21-25`). The game's fades use unscaled time (`Modal.cs:80`; `GameUI.cs:195`). *Mitigation:* `.SetUpdate(true)` on every tween; `Time.unscaledDeltaTime` on every hand-rolled fade.

**R-11 — MEDIUM — container-priority tie with other windows.** `MenuWindow` priority is 1 for every window (`MenuWindow.cs:175-178`) and the handler takes the max (`:45`); ties resolve by enumeration order of this frame's `PushActive` calls. *Status:* whether ties occur in practice is UNPROVEN. *Mitigation:* never open Room Hub over another `MenuWindow` — close the pause menu first (`HistoryUi.cs:68` already does); verify pad focus reaches the first row after opening from the title screen.

**R-12 — MEDIUM — `PeakText` overrides size on every text set.** `PeakText.Awake`/`SetText` assign `sizeDelta = GetPreferredValues()` (`PEAKLib.UI.decompiled.cs:1990, 1997`), fighting layout groups and re-measuring on every refresh. *Mitigation:* rows use plain `TextMeshProUGUI` + `LocalizedText` with fixed rects, `NoWrap`, `Ellipsis` (the game itself sets `NoWrap` on player names, `AudioLevelSlider.cs:60-63`).

**R-13 — LOW — `LocalizedText` renders the key when a translation is missing** (`GetText` returns `"LOC: " + id`, `LocalizedText.cs:412-415`). *Mitigation:* register every key through `MenuAPI.CreateLocalization` / `TranslationKey` (`PEAKLib.UI.decompiled.cs:966-991`); never set an empty `index` (`:157-160`).

**R-14 — LOW — `Modal` parents accumulate if built by hand.** `Modal.Open` clears both parents before building (`Modal.cs:71-74`); instantiating `ButtonsInputField` ourselves would leak. *Mitigation:* only ever call `Modal.OpenModal`/`OpenYesNoModal`.

**R-15 — LOW — avatar texture churn during scroll.** `TickAvatarRefresh` polls while open (`HistoryUi.cs:142`). *Mitigation:* cache `Texture2D` by SteamID64, drop the cache on scene change (`HistoryUi.OnSceneLoaded`).

---

## Appendix A — how the token set was produced (reproducible)

1. `UnityPy.load(<file>)` over `level1` (main menu), `level3`/`level4` (pause/in-game) and `resources.assets` (modal prefabs).
2. `GameObject`/`Transform`/`RectTransform` type trees decode normally → all geometry in §1.3–§1.5.
3. MonoBehaviour type trees do **not** decode; `Image.m_Color` is read from raw bytes at fixed offset 44 (4 × float32 LE). Script: `D:\zhuanban\youhua\scratch-assets\dump_palette.py:19-28`.
4. Sprites decode normally (`obj.read().image.save(...)`) → the mask kit and corner radii.
5. Not obtained: TMP font sizes/font assets, the Glow hover curve, pressed/disabled colours (`ui-palette.md:85-90`).

## Appendix B — anchor index

| Area | Path |
|---|---|
| Official tokens | `docs/audit/ui-palette.md` |
| Page/tab/modal framework | `Zorro.UI.Runtime\Zorro\UI\*.cs` |
| Navigation/focus | `Zorro.ControllerSupport\Zorro\ControllerSupport\*.cs` |
| Game UI pages/windows | `Assembly-CSharp\{MenuWindow,GUIManager,MainMenuMainPage,PauseMenuMainPage,PauseMenuHandler,BoardingPass,SettingsTABS*,AudioLevelSlider,AudioLevels,LocalizedText,FontFallbackSwapper,InputIcon,SFX_PlayOneShot}.cs` |
| Game modal widgets | `Assembly-CSharp\{Peak\UI\ConfirmPage,Peak\UI\KickButton,Peak\UI\SelectableSlider,VerticalModalButtonsOption}.cs` |
| PEAKLib.UI 1.7.2 | `ilspycmd -o <scratch> ...\BepInEx\plugins\com.github.PEAKModding.PEAKLib.UI.dll` → one `*.decompiled.cs` |
| Shipped prefabs/sprites | `PEAK_Data\resources.assets`, `PEAK_Data\level1`, `level3`, `level4` |
| Current mod UI | `src\RoomHub\{HistoryUi,HistoryPageController,HistoryRowFactory,UiButtonFactory,HistoryUiFont}.cs` |

## Appendix C — my independent verification run

Read back from `resources.assets` `ConfirmPage` with the offset-44 method, reproducing the lead's token extraction and adding two rows to §1.2:

```
[UI_OK] size=(156.5, 67.0)
    Shadow  (0.0, 0.0, 0.0, 0.2667)
    Panel   (0.4007, 0.6039, 0.1412, 1.0)
    Border  (0.6401, 0.8208, 0.0194, 1.0)   x2
    Text    (1.0, 1.0, 1.0, 1.0)
[UI_X]  size=(102.7, 67.0)
    Shadow  (0.0, 0.0, 0.0, 0.2667)
    Panel   (0.8208, 0.2683, 0.089, 1.0)
    Border  (1.0, 0.4263, 0.25, 1.0)        x2
    Text    (1.0, 1.0, 1.0, 1.0)
```

`Glow` did not appear because its alpha is 0 and the extractor filters `a > 0.01` — consistent with `ui-palette.md:18`.