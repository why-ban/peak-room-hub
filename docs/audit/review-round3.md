# 3.0.0 第三轮复审（UI 二轮修复 / review-round3）

复核：逐行读当前工作区代码 + 反编译 `refs/PEAKLib.UI.dll`（PeakMenuButton / PeakLocalizableElement / MenuAPI）+ 游戏 3.0.1 反编译 + 自跑构建。
构建：`dotnet build "src\RoomHub\RoomHub.csproj"` → **0 错误 0 警告**，`build/plugins/RoomHub.dll` = **144,384 字节**（与 Lead 所述一致）。

## 0. 结论

7 项声明**全部在代码里成立**（下表）。文字消失的根因修复方向正确且覆盖完整：全仓已无 `TextOverflowModes != Overflow`（grep 仅 `UiKit.cs:74`、`UiButton.cs:98` 两处，都是 Overflow），所有自建标签矩形 ≥ 字号 × 1.75。
**没有 blocker。** 新引入 1 条 high（好友栏名字被压到 0–56 px）+ 3 条 medium（富文本注入可再次"清空"文字、手柄焦点在事件刷新路径仍会丢、按钮宽度取值依赖模板 Text 布局）。

## 1. 逐条验证（声明 → 证据 → 判定）

| # | 声明 | 证据 | 判定 |
|---|---|---|---|
| 1 | `UiKit.Label` 固定 Overflow；行高 ≥1.75×；新增 ClipBox | `UiKit.cs:74`（Overflow，注释写明 Ellipsis/Truncate 的 culling 路径）、`:75`（关自动缩放）、`:86-91`（ClipBox=RectMask2D）；行高：Line1 24/42、Line2 18/32、Title 36/64、Subtitle 18/32、Hint 16/28、Empty 24/42+18/32、RailTitle 22/40、Chip 16/34、Banner 18/60（`UiTheme.cs:67-143`）| ✅ 成立 |
| 2 | 按钮改为 `PeakMenuButton`，只用 SetColor/SetBorderColor，高 67 | `UiButton.cs:73-113`（`UiButtonFactory.CreateMenuButtonSafe` → 克隆真实按钮）、`:239-240`（`SetColor(..., automaticBorderColor:false)` + `SetBorderColor`）、`:182`（`sizeDelta=(w, ButtonHeight=67)`）；反编译确认 `PeakMenuButton.SetColor` 只改 Panel、`SetBorderColor` 只改 BorderTop/Bottom，不碰 Text | ✅ 成立 |
| 3 | 字号阶梯 36/18/24/18/16/16/22，<24 不 Bold | `UiTheme.cs:68-112`；行文本 `Rows.cs:96,104`（FontStyles.Normal）；标题/按钮 `UpperCase` 非 Bold；Chip `UiChip.cs:44` Normal | ✅ 成立 |
| 4 | 行 122→96、头像 80→64、1440×840 见 5 行 | `UiTheme.cs:85,97`；body 高 = 840−96−(114+3+92)=535，5×96+4×10=520 ≤ 535 < 6×96+5×10 | ✅ 成立 |
| 5 | IsTextInputFocused 改判 TMP 真焦点；Hide/Discard 归还选中项 | `HistoryUi.cs:184-194` → `HistoryPageController.cs:298` `IsSearchFocused => _searchInput.IsFocused`（TMP `isFocused`）；`HistoryPageController.cs:1027-1044` `ReleaseFocus` 在 `Hide:178`/`Discard:205` 调用 | ✅ 成立 |
| 6 | 对比度 TextFaint .55 / TextDim .68 / CardFill / RowBg .72 | `UiTheme.cs:45-51` | ✅ 成立 |
| 7 | 手柄 B 关面板 + 刷新后焦点恢复 | `HistoryUi.cs:130`（`Input.GetKeyDown(Escape) || ConsumeGamepadBack()`）、`:145-176`（读并清 `UIInputHandler.cancelWasPressed` 与 `Character.input.pauseWasPressed`）；`HistoryPageController.cs:637` `RefreshAll` 末尾 `RefreshInitialSelection()`、`:1072-1095` | ✅ 成立（但见 R3-03 覆盖不全） |

## 2. 缺陷

### R3-01 — HIGH（视觉/布局，非崩溃）— 好友栏名字被压到 0–56 px
- 证据：行宽 = `RailWidth 392 − RailInset 22` = **370**（`HistoryPageController.cs:527,558`）；文本列左内缩 = `RowPad 32 + Avatar 64 + AvatarGap 24` = **120**（`Rows.cs:86`）；右内缩 = `RowPad 32 + 动作槽宽 + ActionGap 16`（`Rows.cs:241-243`）。`FriendRow` 只有一个动作槽：可操作时是 `Join/Invite` 按钮（`MeasureWidth` 量化档最小可用档 **150**，`UiTheme.cs:128` + `UiButton.cs:194-197`），不可操作时是状态胶囊（宽 `preferred+26`，上限 260，`UiChip.cs:65`）。
- 结论数字：名字宽 = **202 − 动作槽宽**。动作槽 150 → **52 px**；胶囊 171（≈"Invite only · Shore"）→ **31 px**；胶囊 201（≈"Status unknown · Citadel"）→ **1 px**；胶囊 ≥202 → **负宽，RectMask2D 把名字整条裁掉**（`Rows.cs:89`）。24 px 字号下 52 px ≈ 2 个字形。
- 触发：任何"好友在局内/有群系后缀"的行（即好友栏最常见状态）。第二版把 RowPad 24→32、AvatarGap→24、按钮 88→150，把上一版的 106 px 压到 52 px。
- 最小修法：好友栏宽度 392→460（名字回到 120 px）；或把 `FriendRow` 的状态胶囊删掉（`Rows.cs:490` 已经把 status 写在 Line2，`:521` 又重复显示一次），并把动作按钮移到第二行/改小字号；至少给 `UiChip` 在 rail 内加一个 ≤120 的宽度上限。

### R3-02 — MEDIUM — `richText = true` + 未消毒好友名：好友可再次"清空"自己的名字
- 证据：`UiKit.cs:77` 把 `richText` 从 false 改成 true；`Rows.cs:486-489` 把 `FriendRoomInfo.Name` 原样写进 Line1；该名字来自 `SteamFriends.GetFriendPersonaName`（`FriendRoomsService.cs:240`），**没有**经过 `HistoryStore.Sanitize`（只有房间/玩家记录过消毒）。
- 触发：好友把 Steam 昵称设成 `<size=0>x` 或 `<color=#00000000>x` → 该行名字不可见（或整行变色）。这正是"文字全部不显示"的同类症状，且是文本注入面。
- 最小修法：`UiKit.Label` 改回 `richText = false`（面板没有任何富文本用法；`Loc.Tf` 只转义花括号），或在 `FriendRow.Bind` 里对名字跑一遍 `<[^>]+>` 剥离。

### R3-03 — MEDIUM — 事件刷新路径没有调用 `RefreshInitialSelection`，R-02 只修了一半
- 证据：`RefreshInitialSelection` 只在 `RefreshAll`（`HistoryPageController.cs:637`）里调用。`OnFriendsChanged`（`:1139-1156`，`RefreshRail`+`WireNavigation`）、`OnJoinGateChanged`（`:1124-1137`，`RefreshMainList`+`WireNavigation`）、`OnPlayerInvite`（`:1312-1313`）都会 `Commit` 掉消失 key 的行（`Rows.cs:575-582`），但都不刷新 `_initial`。
- 触发（纯手柄）：焦点停在某个好友行的 Join/Invite 上 → 该好友退出游戏 → `FriendRoomsService.Changed` → 行被销毁 → `EventSystem` 选中项失效 → `NavigationContainerHandler.cs:59-69` 取 `objectToSelectOnOpen`，若它指向刚被销毁的行则返回 null（`RoomHubPage.cs:30-32`）→ 焦点再次丢失，只能靠下一次 `RefreshAll`（切页/删行）恢复。
- 最小修法：这三个处理函数在 `WireNavigation()` 之后各补一行 `RefreshInitialSelection();`。

### R3-04 — MEDIUM（条件性，UNPROVEN）— `UiButton` 的 `inset` 取自模板 Text 的 `sizeDelta.x`
- 证据：`UiButton.cs:176-178` `float inset = Mathf.Abs(_text.rectTransform.sizeDelta.x); if (inset < 8f) inset = ButtonPadX*2;`，随后 `:194` `required = max(preferredWidth + inset + 8, _minWidth)` 再 `:195` 量化到档位 {102.7,150,182,187.8,277}。
- 风险：若游戏按钮模板的 Text 是**定宽**（sizeDelta.x ≈ 150）而不是拉伸（0），则 `inset≈150`，任何标签都会量化到 **277**：行内两按钮 = 566，5:4 下行文本列 = 730−120−(32+566+16) = **−4**（名字消失），4:3 = 86。若模板 Text 是拉伸（sizeDelta.x=0，最可能），inset=52，一切正常。
- 最小修法：`inset = Mathf.Clamp(inset, 52f, 96f)`（一行），并在 `LogFontComparison` 里顺手打一次 `_text.rectTransform.rect.size`，进游戏即可定论。

## 3. 低（一句话）

- **R3-05** 4:3/5:4 下 Clear 按钮静默消失：`HistoryPageController.cs:699` `clearFits` 需要 `available ≥ 280`，只在宽高比 ≥1.35 成立；搜索视图仍可用 Esc/B/房间标签退出，属有意降级。
- **R3-06** 宽高比 <1.204 时搜索框开始压住标签：`fieldWidth` 下限 120（`:700`）大于可用宽度（1.1 → 重叠 88 px，1.0 → 196 px）；主列表行文本列在 <0.97 时变负（`Rows.cs:243`）。16:9/16:10/4:3/5:4/21:9 实测余量 24 px，**上一轮 R-03 已修好**。
- **R3-07** `SetRole` 会清 `_tab`（`UiButton.cs:203`），任何在 `SetTabActive` 之后调用 `SetRole` 的地方都会静默丢掉 tab 配色；当前调用点没有这个顺序。
- **R3-08** 头像仍用 `Mask`（`UiAvatar.cs:35`）：Mask 按矩形写模板、不按 sprite alpha，头像四角不会变圆（上一轮 R-05 仍未修）。
- **R3-09** 无模板兜底路径每个按钮做 3 次 `Resources.FindObjectsOfTypeAll` 全扫（`UiButtonFactory.cs:101,122-147`），面板约 15 个按钮 → ~45 次全扫；建议缓存"无模板"结果。

## 4. 对 Lead 指定问题的直接回答

- **ClipBox 会不会连带裁掉子元素**：不会。7 个 ClipBox（`HistoryPageController.cs:391,401,469,494,504,564,583`）下面只有 TMP 标签，没有按钮/徽章。唯一包住行内容的是 `TextColumn` 的 RectMask2D（`Rows.cs:89`），其子节点只有 Line1/Line2 与 Line1 上的徽章；动作按钮是 `Root` 的子节点（`Rows.cs:182-195,263-269`），在遮罩之外。唯一副作用：TextColumn 过窄时 Line1 徽章会一起被裁（同 R3-01/R3-06 的窄宽场景）。
- **`SetLabel` 宽度会不会在模板 TMP 自动缩放下失控**：不会。`UiButton.cs:96` 在第一次测量之前就把 `enableAutoSizing` 关掉，`preferredWidth` 稳定；唯一的变量是 R3-04 的 `inset` 取值。
- **`SetTabActive`/`SetRole`/`SetInteractable` 是否互相覆盖**：当前调用点的最终状态都正确，因为 `SetInteractable` 永远最后调用（`Rows.cs:446-447` 等），而 `Repaint` 每次从三者重算；唯一坑是 `SetRole` 会清 `_tab`（R3-07，当前无调用点触发）。
- **`RefreshInitialSelection` 与 `UiNav` 的交互**：它先重算 `FirstInteractive()` 并写回 `_initial`，只在"当前无有效选中"时补选（`HistoryPageController.cs:1080-1094`）；`TickSelection`→`EnsureVisible` 每帧把新选中项滚进视口（`:319-322`，`UiScrollView.cs:92-96` 本轮已加夹取），所以**不会**把焦点留在屏幕外。缺口只在 R3-03 的三个事件路径。
- **"文字仍可能消失"的残余路径**：① R3-02 富文本标记；② R3-01 好友栏负宽文本列（RectMask2D 全裁）；③ 宽高比 <0.97 时主列表文本列负宽。除此之外，全仓已无 Truncate/Ellipsis/Masking，行高全部 ≥1.75×，定高文本框全部套了 ClipBox 或 RectMask2D。

## 5. 未证明 / 只能进游戏看

- R3-04 的模板 Text 布局（一次日志即可定论）。
- Canvas 排序 vs 游戏 Modal 的实际叠放。
- CJK 字体回退（`FontFallbackSwapper.instance` 在 Title 场景是否存在）。
- `steam_display` 是否原样 token。
- 手柄在真实手柄下的焦点链（B/Start 已在代码层闭环）。
