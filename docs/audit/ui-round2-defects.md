# AUDIT-UI round 2 — new UI layer defect review

Scope: `src/RoomHub/Ui/*.cs` (12 files), `HistoryPageController.cs`, `HistoryUi.cs`, `HistoryUiFont.cs`.
Baseline: game v3.0.1 (`D:\zhuanban\youhua\decompiled-latest`), PEAKLib.UI 1.7.2.
**Snapshot:** the tree was being edited *while this audit ran* — `UiTheme.cs`, `UiKit.cs`, `UiChip.cs`, `UiAvatar.cs`, `UiButton.cs` all changed between 15:45 and 15:52, and `build/out/RoomHub.dll` (15:39:05) is **older than the sources**, so the installed DLL is not the code audited here. Every geometry number below is quoted as `CONSTANT/size` with the constant's current value, so it can be re-derived with one grep instead of trusting a literal. Line numbers are from the 15:52 state.

**Snapshot drift:** `UiButton.cs`, `UiKit.cs`, `UiTheme.cs`, `UiChip.cs`, `UiAvatar.cs`, `Rows.cs` and `HistoryPageController.cs` were all rewritten between 15:45 and 15:52 while this audit ran (`UiButton` gained a drawn fallback and `UiKit.Label` changed signature to `FontStyles style`). Findings are stated in terms of the *mechanism* plus the constant/identifier that controls it, so they survive a tuning pass; line numbers can be a few lines off in the files edited after 15:50.

Severity: **HIGH** = wrong output / dead input class · **MEDIUM** = user-visible wrong behaviour · **LOW** = waste, noise, latent hazard. `UNPROVEN` = needs runtime evidence (§6).

---

## 1. Root-cause re-verification — the invisible title / subtitle / row text

**Verdict: the diagnosis is correct in mechanism, and `Overflow` is the right fix. Two refinements matter, and one part of the stated framing does not hold.**

### 1.1 What TMP actually does (independently traced)

`TextMeshProUGUI.cs` (game copy, same TMP version the game ships):

| Step | Code | Meaning |
|---|---|---|
| 1 | `:1343` `m_marginHeight = rect.height - m_margin.y - m_margin.w` | the label's usable height = its RectTransform height (margins are 0 for every `UiKit.Label`) |
| 2 | `:1582` `float num11 = (m_marginHeight > 0f) ? m_marginHeight : 0f` | clamped to ≥ 0 |
| 3 | `:2002` `num44 = m_maxTextAscender - (m_maxLineDescender - m_lineOffset) + …` | height the text needs **right now**; on line 1 this is exactly `(ascentLine − descentLine) × fontScale`, i.e. the font's line box at the requested size (`:1732/1741` feed `ascentLine`/`descentLine`, `:1950-1963` scale them) |
| 4 | `:2004` `if (num44 > num11 + 0.0001f)` | **trigger**: the line box is taller than the rect |
| 5 | `:2034-2042` `case Ellipsis:` → `if (m_EllipsisInsertionCandidateStack.Count == 0) { i = -1; m_characterCount = 0; characterSubstitution.index = 0; unicode = 3; }` | no ellipsis candidate ⇒ **the whole string is reset to character 0 and terminated** |
| 6 | `:2681-2686` `if (m_characterCount == 0 …) { ClearMesh(); return; }` | nothing is drawn at all |

So the failure is not "one line is clipped", it is **the entire label is emptied**. That is exactly the reported symptom and it is unique to `Ellipsis`: `Truncate` (`:2029-2033`) keeps everything up to the overflow point, and `Masking`/`ScrollRect`/`Overflow` never cull. The candidate stack is only filled when a line break or a word-break opportunity is saved (`:2424-2425`), which for a **single-line** label never happens before the first line overflows — so *any* single-line label whose rect is below the font line box is blanked, not truncated.

### 1.2 Refinement A — the threshold is the font's line box, not "≈1.2 × font size"

The trigger compares against `(ascentLine − descentLine)/pointSize`, which is a property of the borrowed font asset (`HistoryUiFont.Resolve()` → `FontFallbackSwapper.mainBaseFont`). For most fonts that ratio is 1.15–1.45; for CJK-capable faces it can exceed 1.5. The "1.2" in the bug report is an observation, not the rule. The rule is:

> a label is safe under `Ellipsis`/`Truncate` iff `rectHeight / fontSize ≥ (ascentLine − descentLine) / pointSize` **of the font that is actually applied**.

### 1.3 Refinement B — the relaxed geometry does not carry the fix; `Overflow` does

Ratios at this snapshot (`UiTheme.cs`):

| label | rect | font | ratio |
|---|---|---|---|
| Title | `TitleHeight` 54 | `TitleSize` 36 | 1.500 |
| Subtitle | `SubtitleHeight` 28 | `SubtitleSize` 18 | 1.556 |
| Row line 1 | `Line1Height` 34 | `Line1Size` 24 | 1.417 |
| Row line 2 | `Line2Height` 26 | `Line2Size` 18 | 1.444 |
| Footer hint | `HintHeight` 24 | `HintSize` 16 | 1.500 |
| Rail title | `RailTitleHeight` 36 | `RailTitleSize` 22 | 1.636 |
| Empty title | `EmptyTitleHeight` 34 | `EmptyTitleSize` 24 | 1.417 |
| Empty hint | `EmptyHintHeight` 26 | `EmptyHintSize` 18 | 1.444 |

Because the *pre-fix* bug proves the font ratio is **above** the smallest pre-fix ratio (otherwise nothing would have been culled), and the pre-fix ratios were in the same 1.2–1.5 band, the current numbers are still **inside the failure band**. Concretely: if the font ratio is 1.45 (a very ordinary value), then line 1 (1.417) and the empty title (1.417) would be blanked again the moment anyone sets `Ellipsis`/`Truncate` on them — including TMP's own fallback path: if the borrowed font lacks U+2026, TMP logs *"Switching Text Overflow mode to Truncate"* (`:985`) and Truncate culls at the same `:2004` test.

**Conclusion:** keep `Overflow` as the invariant (`UiKit.cs:72`), and treat any future `Ellipsis`/`Truncate`/`Masking` on a `UiKit.Label` as a regression. Do not "fix" the ratios — they are a tuning knob, not the safety mechanism.

### 1.4 The one part that does not add up

The reported set is {title, subtitle, rows} blank while the footer is fine. Under a single font ratio that is impossible: pre-fix the footer hint had a *smaller* ratio than the subtitle, so any ratio that blanked the subtitle also blanked the hint. Two readings are consistent with the code:

* the footer *hint label* was blank too and "页脚正常" refers to the buttons/layout, or
* the subtitle was blank for a second reason (it is the only header label that is re-written from `RefreshChrome` with a dynamic string, `HistoryPageController.cs:621-646`).

**UNPROVEN** — settles with R1 (§6). It does not change the fix.

---

## 2. Same-class risk scan — every TMP label in the panel

Rule from §1.2. `Overflow` is set centrally in `UiKit.Label` (`UiKit.cs:72`) and `enableAutoSizing` is explicitly off (`:73`), so auto-shrink cannot mask the problem either.

| label | created at | rect height | font | ratio | clipped by | verdict |
|---|---|---|---|---|---|---|
| Title | `HistoryPageController.cs:384-390` | `TitleHeight` | `TitleSize` | 1.50 | **nothing** | safe from culling; can spill (R2-03) |
| Subtitle | `:392-399` | `SubtitleHeight` | `SubtitleSize` | 1.56 | **nothing** | same; dynamic text (host/query) |
| Stat chip text | `UiChip.cs:34-36` | `ChipHeight` 34 | `ChipSize` 16 | 2.13 | nothing | safe; spills out of the capsule when long (R2-12) |
| Tab / button labels — native path | `UiButton.TryCreateNative` (`UiButton.cs:79-109`) | game template | game template | n/a | game template | **not** a `UiKit.Label` and `HistoryUiFont` is never applied, so it keeps the game's own font metrics — this is why buttons/tabs stayed visible and is the control for R1. Note it also forces `Overflow` on the clone (`:94`) |
| Tab / button labels — drawn fallback | `UiButton.CreateDrawn` (`UiButton.cs:123-162`) | `ButtonHeight` 67 | `ButtonTextSize` | ~2.8 | nothing | a `UiKit.Label` (so `Overflow` + borrowed font) inside a 67-px button — safe |
| Row line 1 | `Rows.cs:94-97` | `Line1Height` | `Line1Size` | 1.42 | `RectMask2D` on TextColumn (`Rows.cs:88-89`) | safe + clipped (added during this audit) |
| Row line 2 | `Rows.cs:99-105` | `Line2Height` | `Line2Size` | 1.44 | same mask | safe + clipped |
| Row avatar initial | `UiAvatar.cs:43-45` | `AvatarSize` | `AvatarSize × AvatarInitialRatio` | ~2.6 | `Mask` on the avatar | safe |
| Footer hint | `HistoryPageController.cs:565-567` | `HintHeight` | `HintSize` | 1.50 | **nothing** | safe; dynamic (hotkey names) |
| Delete banner | `:459-464` | `BannerHeight` (stretched) | `BannerSize` | 3.33 | **nothing** | safe |
| Rail title | `:525-531` | `RailTitleHeight` | `RailTitleSize` | 1.64 | **nothing** | safe |
| Rail empty | `:543-548` | `RailHintHeight`-ish, `wrap: true` | `RailEmptySize` | ~1.3 if 24 | **nothing** | at risk: multi-line + no clip (R2-02) |
| Empty title / hint | `:484-499` | `EmptyTitleHeight` / `EmptyHintHeight`, both `wrap: true` | 24 / 18 | 1.42 / 1.44 | **nothing** | at risk: multi-line + no clip (R2-02) |
| Search text + placeholder | `UiTextInput.cs:62-69` | `SearchHeight` 60 (stretched) | `SearchSize` 18 | 3.33 | `RectMask2D` on "Text Area" (`:55-60`) | safe + clipped |

No other code path creates a `TextMeshProUGUI` (grep for `TextMeshProUGUI` over `src/RoomHub`: `UiKit.Label`, `UiTextInput`, `UiAvatar`, `Rows` fields, `UiButton.Text`), so the scan is complete.

---

## 3. Round-1 findings — re-checked against the new code

| # | Claim | Verdict | Evidence |
|---|---|---|---|
| UI-01 | gamepad | **fixed** | `RoomHubPage.objectToSelectOnOpen` override (`RoomHubPage.cs:26-34`), `SelectOnOpen = true` (`HistoryPageController.cs:133`), `FocusInitial` + `UIInputHandler.SetSelectedObject` (`:983-1002`), explicit graph in `WireNavigation` (`:883-969`) |
| UI-02 | ESC | **fixed (with a new side effect)** | ESC is unconditional (`HistoryUi.cs:127`) and the field is deactivated first (`HistoryPageController.cs:253-258`). Side effect → R2-01 |
| UI-04 | 4:3 overlap | **fixed** | the rail is a sibling column (`offsetMax.x = −(RailWidth + RailGap)`, `:447-451`) and the tabs/search cluster is placed from the rail inset (`:666-696`) — no overlay possible. Residual: below a ~1100-px card the computed `available` goes negative and the field clamps to 120 px, which overlaps the tabs; that needs aspect < 1.15, i.e. not reachable in practice |
| UI-05 | rebuild churn | **fixed** | `RowPool` key reuse (`Rows.cs:527-587`), detach-then-destroy (`:117-120`), rows repositioned in place (`UiScrollView.cs:52-69`), VLG/fitter disabled (`:34-39`) |
| UI-06 | long names | **partially** | the rect is now narrowed (`Rows.cs:227-252`) and the column is masked (`:88-89`) — but the header/subtitle/footer/banner labels are not (R2-03) |
| UI-07 | title fallback | **fixed** | `SkipStartClosed` (`RoomHubPage.cs:46-61`) + `SetInputActive(true)` (`HistoryPageController.cs:145-148`) |
| UI-12 | avatar sprites | **fixed** | `UiAvatar` owns and destroys its sprite (`UiAvatar.cs:58-97`), `RefreshAvatar` re-reads the texture every 45 frames (`Rows.cs:155-160`, `HistoryPageController.cs:276-287`) |
| UI-13 | search box | **fixed** | `UiTextInput` is built from scratch, no PEAKLib prefab (`UiTextInput.cs:34-88`) |
| UI-14 | per-row services | **mostly fixed** | `RowContext` hoists `CanJoin`/`CanInvite` (`Rows.cs:10-20`, `HistoryPageController.cs:601-603`). Residual: `SteamFriends.HasFriend` per player row (`Rows.cs:421` → `SteamAvatarService.cs:185-194`) and `JoinGate.IsJoinAllowed` per room row (`Rows.cs:352`) — dictionary lookups, acceptable |
| UI-15 | cursor | **fixed** | the manual capture/restore is gone; `TickInputBlock` is now empty (`HistoryPageController.cs:289-292`) and the game's `CursorHandler` owns it. Dead method (R2-08) |
| UI-16 | re-entrancy | **fixed** | `if (IsPageOpen) return;` (`HistoryPageController.cs:110-111`) |
| UI-03/08/09/10/11 | (from round 1) | 03 fixed (`PauseMenuHook.cs:205-235` holds `timeScale` 0 and releases it from `Plugin.cs:124-125`), 11 fixed (`Plugin.IsFullscreenUiActive` `:160-178`), 08/09/10 unchanged (R2-09) |

---

## 4. New defects

### R2-01 — MEDIUM — touching the search box kills the K/J hotkeys for good
`Plugin.cs:133` (`if (HistoryUi.IsTextInputFocused()) return;`), `HistoryUi.cs:175-203`, `UiTextInput.cs:96-100`, `HistoryPageController.cs:253-258`.

**Scenario.** Click the search field once, press ESC (panel closes), press K → nothing. The panel reopens from the ribbon, but K/J stay dead until the user clicks some non-input UI element.

**Evidence.** `TMP_InputField.DeactivateInputField` (`TMP_InputField.cs:3872-3907`) only clears TMP's internal edit state — it never calls `EventSystem.SetSelectedGameObject(null)` (the only such call is on pointer-down, `:2023`). `NavigationContainerHandler.LateUpdate` deliberately skips deselection for a `TMP_InputField` (`NavigationContainerHandler.cs:71-77`), and the mod's own `FocusInitial`/`SetSelectedObject` is a no-op outside gamepad (`UIInputHandler.cs:78-84`). So `EventSystem.current.currentSelectedGameObject` stays on the field and `IsTextInputFocused()` stays true forever.

**Minimal fix.** In `UiTextInput.Deactivate`, after `DeactivateInputField()`, call `EventSystem.current?.SetSelectedGameObject(null)`; or drop the `IsTextInputFocused` gate and test `_searchInput.IsFocused` (`UiTextInput.cs:32`) instead, which follows TMP's real focus state.

### R2-02 — MEDIUM — the empty-state and rail-empty labels are `wrap: true` in fixed-height rects with no clip
`HistoryPageController.cs:484-499` (`EmptyTitleHeight` / `EmptyHintHeight`), `:543-548` (rail empty).

**Scenario.** With `Overflow`, a two-line empty hint (CN strings are long, e.g. `Loc.EmptyRoomsHint`) draws past its 26-px rect and over the neighbouring label (they are 22 px above / 24 px below the centre). `wrap: true` is the only place the panel asks for multi-line text, and none of those rects can grow (no `ContentSizeFitter`, no `preferredHeight`).

**Minimal fix.** Either size those rects from `label.GetPreferredValues(width, 0).y` after the text is set in `SetEmptyState` (`:864-881`), or add a `RectMask2D` and keep the text to one line (`wrap: false`).

### R2-03 — MEDIUM — header / footer / banner labels are unmasked `Overflow` labels fed by dynamic strings
`UiKit.cs:72` + `HistoryPageController.cs:384-399` (title/subtitle), `:459-464` (banner), `:565-567` (hint), `:525-531` (rail title).

**Scenario.** `_subtitleText` is `Loc.Tf(Loc.RoomTitle, host)` with a host name up to 32 chars, and `Loc.Tf(Loc.SearchQuery, query)` with a query up to `characterLimit = 48` (`UiTextInput.cs:77`). Its rect stops 420 px short of the right edge to clear the stat chip, but `Overflow` + `NoWrap` ignores the rect, so the text runs into the chip and then off the card. Same for the footer hint and the banner text. The row column got a `RectMask2D` (`Rows.cs:88-89`); these did not.

**Minimal fix.** Add `RectMask2D` to the subtitle/title/hint/banner rects (the `UiKit` comment already states this is the intended policy, `UiKit.cs:71`), or truncate the strings at the call site.

### R2-04 — LOW — `Hide()` runs the whole-canvas sorting scan for nothing
`RoomHubPage.cs:91-135` (`ApplySorting`), called with `apply: false` from `HistoryPageController.cs:183`.

**Evidence.** `ApplySorting` computes `modalOrder` and `best` (including `Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, …)`, `:112-113`) *before* `if (apply)` at `:126`. Every close therefore pays a full scene scan whose result is discarded, on top of the same scan on open.

**Minimal fix.** `if (!apply) { … only restore overrideSorting/sortingOrder … return; }` at the top.

### R2-05 — LOW — `DropPage` destroys the shared sprite cache while the page is still pending deferred destruction
`HistoryUi.cs:97-103` (`DropPage` → `UiSprites.Shutdown()`), `HistoryPageController.cs:205` (`Object.Destroy(page)`), `UiSprites.cs:38-53`.

**Evidence.** `Discard` destroys the page GameObject with `Object.Destroy` (deferred to the end of the frame) and `DropPage` then immediately destroys every `Sprite`/`Texture2D` in `UiSprites`. For the remainder of that frame the page's `Image` components still reference destroyed sprites. Also every scene load pays a full regeneration (~6 textures × 48×48 × 16 samples) on the next open.

**Minimal fix.** Destroy the page with `DestroyImmediate` in `Discard` before `Shutdown`, or call `UiSprites.Shutdown()` only from `Plugin.OnDestroy`/`OnSceneLoaded` (not from the `Tick` recovery path).

### R2-06 — LOW — every refresh re-shapes every button label twice
`UiButton.cs:169-181` (`SetLabel` → `_text.preferredWidth`), `Rows.cs:344-356` (`Bind` → `SetLabel`), `UiChip.cs:43-51`.

**Evidence.** `SetLabel` calls `TMP_Text.preferredWidth`, which runs a full `CalculatePreferredValues` shaping pass (`TMP_Text.cs:3321-3343`), and `Bind` calls `SetLabel` on every visible button of every row on every refresh — plus one pass per `UiChip.SetText`. With 10 rooms that is ~25 extra shaping passes per `FriendRoomsService.Changed` event, each of which can re-trigger a canvas layout.

**Minimal fix.** Cache `width` per label string in a static dictionary (the label set is closed: Join/View/Delete/Invite/Add Friend/Invited) or only call `SetLabel` when the string actually changed.

### R2-07 — LOW — `EnsureVisible` can scroll past the content bounds
`UiScrollView.cs:72-91`.

**Evidence.** It writes `Content.anchoredPosition` directly. `ScrollRect` with `MovementType.Clamped` only re-clamps inside `SetContentAnchoredPosition` during drag/inertia (`PEAKLib PeakScrollableContent.cs:1918` sets Clamped), so a code-set position is not normalised: selecting the last row when the content is shorter than the viewport leaves a visible gap. **UNPROVEN** magnitude (R5).

### R2-08 — LOW — `TickInputBlock()` is dead code
`HistoryPageController.cs:289-292`. Nothing calls it (grep: only the declaration). It is documented as "kept for the frozen public surface", but the type is `internal` and the caller was removed in this rewrite.

### R2-09 — LOW — the default button path still does not clear the template's `onClick` listeners
`UiButtonFactory.cs:92-112` vs `:189-191` (`PrepareTemplate` clears them, `CreateMenuButtonSafe` does not). Unchanged from round 1; now every tab, row action and footer button goes through it (`UiButton.cs:86`). If the cloned scene button carries persistent listeners, every panel button fires them. **UNPROVEN** (needs a scene dump of the template button).

### R2-10 — LOW — `RowPool.Take` drops a destroyed row without disposing it
`Rows.cs:541-549`.

**Evidence.** `if (_live.TryGetValue(key, out var row) && row.Root != null) return row;` — when `Root` is a destroyed object the method creates a new row and overwrites `_live[key]`, so the old `RowView`'s avatar `Sprite` (`UiAvatar._sprite`) is never destroyed. Only reachable if a row GameObject is destroyed externally (nothing in the current code does that), so it is a latent leak, not an active one.

### R2-11 — INFO — per-frame cost is clean; `Layout()` is not per-frame
Answering the explicit question: `RoomHubPage.Update` (`RoomHubPage.cs:63-69`) calls `Layout()` only when `rect.size` changed, and `Layout` writes `_card.sizeDelta` which cannot feed back into the page rect, so there is no oscillation. `Layout()` → `Resized` → `RefreshChrome` (`HistoryPageController.cs:1034-1046`) runs once per real resize. The remaining per-frame work while the page is open is `base.Update()` (game), `TickSelection` (a reference compare, `:294-317`) and the 45-frame avatar poll (`:276-287`). No per-frame allocation found in the mod's own code.

---

## 5. Verified correct

| # | Claim | Evidence |
|---|---|---|
| V1 | `Overflow` is set in exactly one place and covers every label the panel builds; `enableAutoSizing = false` is explicit, so the auto-shrink escape hatch cannot hide an overflow | `UiKit.cs:72-73`; all label creation goes through it |
| V2 | In the normal (template-clone) path the button/tab text is *not* a `UiKit.Label` and never gets `HistoryUiFont.Apply`, so it keeps the game's own font metrics — the reason buttons/tabs survived the bug, and the control for R1. The drawn fallback does use `UiKit.Label` and is therefore protected by `Overflow` like the rest | `UiButton.cs:79-109` vs `:123-162`; `UiKit.cs:72` |
| V3 | Row text is clipped by a `RectMask2D` on the text column, and the column's right inset is recomputed from the visible action widths | `Rows.cs:86-89,227-252` |
| V4 | Search field text/placeholder are clipped by a `RectMask2D` and the caret is bounded by `textViewport` | `UiTextInput.cs:55-75` |
| V5 | The pool is key-stable, disposes vanished keys, detaches before `Destroy`, and is cleared when the view kind changes (rooms ↔ players) so a row type can never be reused across kinds | `Rows.cs:527-587`, `HistoryPageController.cs:744-749` |
| V6 | Avatar sprites are owned by `UiAvatar` and destroyed on change/dispose; a destroyed cached texture is never returned (`GetAvatar` re-requests on the Unity fake-null check) | `UiAvatar.cs:58-97`; `SteamAvatarService.cs:57` |
| V7 | `UiSprites` caches by radius, tracks every generated texture and destroys sprites before textures; a generation failure degrades to a square and logs once | `UiSprites.cs:22-53,115-121` |
| V8 | `RoomHubPage.Layout` is guarded by a size compare and cannot oscillate (§R2-11) | `RoomHubPage.cs:63-85` |
| V9 | Solo pause is held at `timeScale 0` across the pause-menu close and released only when the panel is gone | `PauseMenuHook.cs:205-235`; `Plugin.cs:124-125` |
| V10 | The page cannot be opened during loading / end cutscene / end screen | `Plugin.cs:160-178`; `PauseMenuHook.cs:171-182` |
| V11 | `Open()` is re-entrancy safe and `Discard()` unsubscribes both static events and the language hook | `HistoryPageController.cs:110-111,199-246,363-370` |
| V12 | All `Loc` keys are declared and registered (checked mechanically over all 35 source files: 85 declared / 85 registered). The three keys that were missing mid-edit (`HintHotkeyShort`, `EmptyAllPlayers`, `EmptyPlayersHint`) were added at 15:48, so for part of this session the tree did not compile | `Loc.cs`; `build/out/RoomHub.dll` (15:39) is older than every edited source |
| V13 | Shared-sprite ownership is clean (the round-1 UI-05 regression class): the only `Object.Destroy(sprite)` in the new code is `UiAvatar.Dispose`/`SetTexture`, and it only ever destroys a `Sprite.Create`d from the avatar's own texture. Nothing destroys an `Image.sprite` that came from `UiSprites` (those are owned by the `UiSprites` cache) or from the cloned game button template (owned by the prefab) | `UiAvatar.cs:58-97`; `UiSprites.cs:38-59`; grep `Destroy(` over `src/RoomHub/Ui` |

---

## 6. Runtime checklist (what would settle the open items)

| # | Test | Proves |
|---|---|---|
| R1 | Log once per label after the first refresh: `label.name`, `rect.height`, `fontSize`, `textInfo.lineInfo[0].lineHeight`, `textInfo.characterCount`. `lineHeight > rect.height` ⇒ that label was culled pre-fix; `characterCount == 0` with non-empty text ⇒ the `ClearMesh` path | §1.2 threshold for the real font, and §1.4 (was the footer hint blank too?) |
| R2 | Click the search box, close, press K | R2-01 |
| R3 | Empty state with a long CN hint; rail with no friends | R2-02 |
| R4 | 32-char host name → players view; 48-char search query | R2-03 |
| R5 | Gamepad: select the last row in a 10-row list | R2-07 |
| R6 | Resize the window while open; watch for a frame hitch | R2-04/R2-11 |
| R7 | Scene change with the panel open, then reopen immediately | R2-05 |
