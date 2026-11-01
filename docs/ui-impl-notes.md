# Room Hub 面板实现说明（WP-UI / task-10）

规格来源：`docs/ui-spec.md`（实现规格）、`docs/audit/ui-palette.md`（颜色实测）、`docs/audit/audit-ui.md`（16 条旧缺陷）、
`docs/audit/friend-modes.md`（好友状态映射）、设计稿 `tools/uimock/panel.html`（variant B）。
行号为本文件写就时的快照。

## 1. 模块职责

| 文件 | 职责 |
|---|---|
| `src/RoomHub/Ui/UiTheme.cs` | 设计令牌：9 个基础色 + 6 组按钮角色色（逐值与 ui-spec §1 对齐，脚本比对过）、全部几何常量、Chip 配色 |
| `src/RoomHub/Ui/UiSprites.cs` | 运行时圆角底图。48×48 白图，4×4 子采样覆盖率抗锯齿，border = 半径，九宫格 Sliced；按半径缓存，失败降级为直角 |
| `src/RoomHub/Ui/UiKit.cs` | 构件工厂：Rect / Panel / Card（外层描边 + 内缩实心）/ Label（NoWrap + Ellipsis）/ Divider / Dot |
| `src/RoomHub/Ui/UiButton.cs` | 按钮解剖（Shadow → Panel → 上下亮边 → Text），角色色、禁用态、Tab 变体、ColorBlock 乘算 |
| `src/RoomHub/Ui/UiChip.cs` | 胶囊徽章，宽度随文案自适应 |
| `src/RoomHub/Ui/UiAvatar.cs` | 头像：圆角底 + Mask + 真实贴图 + 首字母兜底；自己拥有 Sprite 生命周期 |
| `src/RoomHub/Ui/UiTextInput.cs` | 自建 TMP_InputField 搜索框（不依赖 PEAKLib PeakTextInput） |
| `src/RoomHub/Ui/UiScrollView.cs` | 包一层 PeakScrollableContent，关掉 VLG + ContentSizeFitter，改手工定位；负责 EnsureVisible |
| `src/RoomHub/Ui/UiNav.cs` | 显式 Navigation 接线原语（Link / Row / Column） |
| `src/RoomHub/Ui/RoomHubPage.cs` | `PeakCustomPage` 子类：`objectToSelectOnOpen`、响应式卡片尺寸、自适应 Canvas 排序、StartClosed 兜底 |
| `src/RoomHub/Ui/Rows.cs` | RowView 基类 + RoomRow / PlayerRow / FriendRow + 按 key 复用的 RowPool |
| `src/RoomHub/Ui/FriendLabels.cs` | `FriendGameMode` / `FriendArea` → Loc 文案（friend-modes.md §3 映射表） |
| `src/RoomHub/HistoryPageController.cs` | 页面壳：chrome、三种视图、搜索、删除模式、行池刷新、导航图、MenuWindow 生命周期 |
| `src/RoomHub/HistoryUi.cs` | 入口与每帧 Tick（ESC、选中项滚动、头像轮询）；`PauseInputSuppressor` 语义未变 |
| `src/RoomHub/HistoryUiFont.cs` | 字体解析服务：`FontFallbackSwapper.mainBaseFont` → `TMP_Settings.defaultFontAsset` → 场景借用 |

删除：`src/RoomHub/HistoryRowFactory.cs`（重写后无任何引用，grep 确认）。
保留：`src/RoomHub/UiButtonFactory.cs` —— `PauseMenuHook.cs:86` 与 `TitleMenuController.cs:68,173` 仍在用，删除会打断他人的文件。
`HistoryUi.Open` 里的 `UiButtonFactory.EnsureTemplate()` 调用已移除（面板不再用模板按钮）。

## 2. 旧缺陷 → 新实现

| 缺陷 | 处置位置 |
|---|---|
| **UI-01 HIGH 手柄完全不可用** | `Ui/RoomHubPage.cs:11` 覆写 `objectToSelectOnOpen`（首个可交互元素，兜底关闭按钮）；`HistoryPageController.cs:110` 起 `Open` 里 `SelectOnOpen=true` 且在 `MenuWindow.Open` 之前 `SetInitialSelection`；`HistoryPageController.cs:879` `WireNavigation` 建上下左右显式图（标签行 / 行内按钮 / 行间 / 好友栏 / 页脚）；`HistoryPageController.cs:1000` 之后的 `FocusInitial` 用 `UIInputHandler.SetSelectedObject`（该 API 只在手柄模式生效，正是需要的语义） |
| **UI-02 MEDIUM 搜索框聚焦后 ESC 失效** | `HistoryPageController.cs:248` `HandleBackOrClose` 先 `DeactivateInputField` + 交回焦点，再走返回/关闭；`HistoryUi.cs:109` 的 Tick 不再用 `IsTextInputFocused()` 拦截 ESC |
| **UI-03 MEDIUM 单人 timeScale** | 不在本工作包：`PauseMenuHook.ReleaseHeldTimeScale`（WP-ENTRY）。本实现不再写 `Time.timeScale`，也不碰 Cursor |
| **UI-04 MEDIUM 4:3 左右分栏重叠** | 卡片尺寸 `Ui/RoomHubPage.cs:72`；好友栏 `HistoryPageController.cs:510` 锚点 (1,0)-(1,1) + pivot(1,0.5) + sizeDelta(392,0)；主列 `HistoryPageController.cs:450` 只收缩 `offsetMax.x`；标签行的搜索簇 `HistoryPageController.cs:665` 以 `Pad+392+22` 为右边界，并按主列可用宽度收缩输入框（140…320） |
| **UI-05 MEDIUM 全量重建导致滚动跳变** | `Ui/Rows.cs:527` `RowPool`（按 key 复用，`Commit` 只销毁消失的 key，`Ui/Rows.cs:552`）；`HistoryPageController.cs:735/781` 主列表与好友栏各自刷新，好友回调只走 `RefreshRail`（`HistoryPageController.cs:359`）；行销毁前先 `SetParent(null)`（`Ui/Rows.cs:112`）避免延迟销毁参与本帧布局；滚动位置由 `Ui/UiScrollView.cs:52` 手工定位后保持 |
| **UI-06 MEDIUM 长名字压到按钮下面** | `Ui/UiKit.cs:49` Label 固定 `NoWrap + Ellipsis`，绝不再用首选宽度当 sizeDelta；`Ui/Rows.cs:227` `ApplyLayout` 把可见按钮总宽从文本列右边界扣掉（`offsetMax.x`） |
| **UI-07 LOW Title 场景 Show 兜底被 StartClosed 撤销** | `Ui/RoomHubPage.cs:47,52`：`SkipStartClosed()` + `Start()` 覆写；`HistoryPageController.cs` 的 `Open` 在 title 兜底路径里先置位再 `Show()` |
| **UI-08/09 LOW 模板按钮** | 面板不再克隆任何游戏按钮（零模板依赖），两条缺陷随实现消失；`UiButtonFactory` 仅为暂停菜单入口保留 |
| **UI-10 LOW 拖动条出屏** | 不在本工作包（`RibbonDragHandle`，WP-ENTRY） |
| **UI-11 LOW 结算界面可开面板** | 不在本工作包（`Plugin.IsFullscreenUiActive`，WP-ENTRY） |
| **UI-12 LOW 头像 Sprite 活过贴图** | `Ui/UiAvatar.cs:58` `SetTexture` 比较当前贴图并自毁旧 Sprite；`Ui/Rows.cs:155` `RefreshAvatar` 由 `HistoryPageController.TickAvatarRefresh` 每 45 帧重读；不再靠 GameObject 名字解析 SteamId |
| **UI-13 LOW 搜索框建失败变哑按钮** | `Ui/UiTextInput.cs:34` 自建输入框，不再依赖 `PeakTextInput`（它会在 FloatSettingCell 未加载时抛异常） |
| **UI-14 LOW 每行重复查服务** | `Ui/Rows.cs:12` `RowContext`（CanJoin / CanInvite）每次刷新只算一次，行内不再调 `HistoryUi.IsInLiveLobby` |
| **UI-15 LOW 光标强制写死** | 全部 `Cursor.lockState` 写入已删除；`HistoryPageController.cs:290` `TickInputBlock` 仅为冻结的 public 面保留为空实现，光标交给游戏 `CursorHandler` |
| **UI-16 LOW Open 无重入保护** | `HistoryPageController.cs:110` `if (IsPageOpen) return;` |

附加（Lead 追加）：Canvas 排序自适应 `Ui/RoomHubPage.cs:91` `ApplySorting` —— 枚举 `ScreenSpaceOverlay` 根 Canvas，
参照取 `Modal` 单例画布 / `GUIManager` 画布 / 全场景最大值，自身 = 参照 - 1，且绝不低于 Modal；找不到参照时 100。
开/关各打一条 Debug（自身 order + 参照 order + 参照对象名），并设 `overrideSorting = true`。

## 3. 设计取舍（与规格字面不同之处，均给出理由）

1. **按钮/行的根节点不带 Image**：投影必须画在面板之后。游戏自己的按钮就是 `Shadow → Panel → Border → Glow → Text` 子物体顺序
   （`docs/audit/ui-palette.md` §1），所以这里照抄：根节点挂 `Button`，`targetGraphic` 指向 Panel 子物体。
2. **不用 `MenuAPI.CreatePageWithBackground`**：该工厂硬编码 `AddComponent<PeakCustomPage>()`，无法得到子类。
   改为 `new GameObject + AddComponent<RoomHubPage>()`（与工厂内部完全等价），背景改为自建 `Scrim` 用 `Scrim` 令牌（0,0,0,0.8667）。
3. **关掉 PEAKLib 滚动内容的 VLG + ContentSizeFitter**：行是池化 + 手工定位的，布局组只会带来每帧重建和不可控的子宽度策略
   （这正是 UI-06 的机制）。`PeakScrollableContent` 的 ScrollRect / RectMask2D 仍然使用。
4. **"玩家"标签页的扁平列表在 UI 侧去重**：`HistoryStore.SearchPlayers("")` 返回空，仓库里没有"全部遇见过的人"接口，
   而 `HistoryStore.cs` 不归本工作包。UI 用 `GetRooms()` 按 SteamId 去重（保留最近一次），数据形状仍是 `PlayerSearchHit`。
   后续若要挪进 store，只需替换 `AllMetPlayers()`。
5. **`HistoryUiFont` 只保留 `Reset / Resolve / Apply`**：`CaptureFromMenuButton` 与 `ForceCenteredLabel` 已无调用点（全在被重写的文件里）；
   `Reset()` 被 `TitleMenuController.cs:36` 使用，必须保留。
6. **`Loc` 只增不改**：新增 27 个键（标签、统计、页脚提示、空态、机场模式、群系 12 个、`JOIN_BLOCKED`），
   现有常量一个没动；`RoomJoiner` 用的 `ROOMHISTORY_JOIN_BLOCKED` 已按 friend-modes.md §4 文案补齐。
7. **好友栏在所有视图常驻**（规格"常驻栏"），删除模式只影响主列表；好友回调只刷新好友栏 + 统计文案。

## 4. 必须进游戏验证（无法离线证明）

| # | 项 | 验证方式 | 期望 |
|---|---|---|---|
| R1 | `FontFallbackSwapper.instance` 在 Title 场景是否已存在 | 标题界面打开面板看日志 `UI font from …` | 打印 `FontFallbackSwapper.mainBaseFont`；若打印 `TMP_Settings.defaultFontAsset` 则标题界面中文会缺字（需改为延迟解析/重试） |
| R2 | `mainBaseFont` 的 CJK 回退是否生效 | 面板里看中文标题/房间名/群系名 | 无方框；`fallbackFontAssetTable[1]` 由 `LocalizedText.SetLanguage` 切换 |
| R3 | 手柄焦点链 | 只用手柄：打开面板 → 上下左右走遍 标签/搜索/行按钮/好友栏/页脚 → A 触发 → B 返回 | 打开即有高亮；不会卡死在任何元素；行内左右在 加入/查看 之间 |
| R4 | Canvas 排序 vs 游戏 Modal | 面板打开时触发一次加入失败弹窗（进已满房间） | 弹窗盖在面板之上；日志 `Room Hub canvas order …` 的 order 小于 modal |
| R5 | 好友 `steam_display` 是否原样到达 | 看 WP-STEAM 的 `Friend rich presence token is raw` Info 日志 | 有该行 → 群系后缀（`在游戏中 · 海岸`）会出现 |
| R6 | 滚动内容手工定位 + ScrollRect 夹取 | 20+ 房间，滚到底再触发好友状态变化 | 位置不跳；内容高度正确；不能滚出上边界 |
| R7 | Mask 圆角裁剪 | 有真实 Steam 头像的好友/玩家行 | 头像四角被裁成圆角，不出现方角 |
| R8 | 搜索框 | 点击输入、输入中文、Enter 搜索、ESC | 有光标；Enter 触发搜索；ESC 一次即关面板（UI-02 语义） |
| R9 | 4:3 / 21:9 | 窗口改成 4:3、5:4、21:9 各截一张 | 左右分栏不重叠；搜索框不压好友栏；长名字省略号 |
| R10 | 语言切换 | 面板打开时切语言 | 全部文案（含好友栏、页脚、空态）跟着变，不重建页面、不丢滚动位置 |
| R11 | 删除模式 | 进删除模式 | 横幅出现、行变红、只剩删除按钮、列表整体下移 70 |
| R12 | 头像热更新 | 等好友换头像（或触发 `AvatarImageLoaded_t`） | 头像刷新且不残留旧 Sprite（UI-12） |

## 5. 构建

`dotnet build "src\RoomHub\RoomHub.csproj"` → 0 错误 0 警告（本文件写就时）。

---

# 第二轮：实机回归 + 设计复审（docs/audit/ui-round2.md / ui-round2-defects.md）

## 6. 文字整块消失的根因（已用游戏自带 TMP 源码坐实）

`TextMeshProUGUI.cs:1343` 把可用高写成 `rect.height`（margin 为 0），`:1582` 钳到 ≥0 记作 `num11`；
`:2002` 算出当前行占用的高度 `num44`（首行即 `(ascentLine − descentLine) × fontScale`），`:2004` 一旦 `num44 > num11`
就进入溢出分支；`:2034-2042` 的 Ellipsis 分支在候选栈为空时把 `i = -1; m_characterCount = 0`，随后 `:2681-2686`
`ClearMesh()` → **整个标签被清空，不是裁一行**。候选栈只在 `:2424-2425` 记录，而单行标签在首行溢出前没有换行点，
所以“矩形矮于一行”必然空白。Truncate 用同一个高度判据（`:2029-2033`），且 TMP 在字体缺 U+2026 时会自己
回退到 Truncate（`:985`）；只有 Overflow / Masking / ScrollRect（`:2208-2214`）不裁。

结论：**Overflow 是唯一不会让文字消失的模式**，这条不能回退（断言注释写在 `UiKit.cs:71-78`）。

## 7. 本轮修复 → 落地位置

| 项 | 处置 | 位置 |
|---|---|---|
| P0-1 分隔线压副标题 | 头部重排：12 顶距 + 标题 64 + 副标题 32 + 6 间隔，Rule 移到 `HeaderHeight` 114 | `UiTheme.cs:67-70`、`HistoryPageController.cs:391-407` |
| P0-2 说明文字 3.26:1 | `TextFaint` 0.42 → 0.55（4.71:1）；`TextDim` → 0.68（6.6:1）；Mute chip 0.72（6.6:1） | `UiTheme.cs:50-51,168` |
| P0-3 按钮被压扁 | 删掉 `ButtonHeightSmall`，所有按钮统一模板原生 67；搜索框同步 67，标签行三元素底边对齐 | `UiTheme.cs:116`、`UiButton.cs:177-186`、`HistoryPageController.cs:436-447` |
| P0-4 小字号加粗 | `UiKit.Label` 的 `bold` 改为 `FontStyles`；全面板不再用 Bold，强调改 UpperCase（标题/好友栏标题） | `UiKit.cs:55-80`、`UiButton.cs:96` |
| P1 字号阶梯 | 标题 36 / 副标题 18 / 行名 24 / 行副 18 / 徽章 16 / 页脚 16 / 好友栏 22 / 空态 24·18 / 横幅 18 / 搜索 18，全部落在实测档位 | `UiTheme.cs:69-149` |
| P1 行高余量 | 所有标签矩形 ≥ 字号 × 1.75（标题 64/36=1.78、行名 42/24=1.75、行副 32/18=1.78、页脚 28/16=1.75、空态 42·32） | `UiTheme.cs:67-149` |
| P2 空态/说明文案 | 新增 `EMPTY_ALL_PLAYERS` / `EMPTY_PLAYERS_HINT` / `SEARCH_EMPTY_HINT` / `RAIL_HINT` / `DELETE_CONFIRM` / `STAT_PLAYERS` / `HINT_HOTKEY_SHORT`；副标题去掉重复品牌名；页脚按视图拼 Esc / 双击 / 热键 / 删除不可恢复 | `Loc.cs`、`HistoryPageController.cs:733-745,547` |
| P3 死令牌 | 删除 `TabIdleText/TabPanel/TabLine/TabText/TabSize/TabRadius/DarkPanel/ButtonHeightSmall/*Small` 等；`ChipLineColor` 接线成 chip 描边 | `UiTheme.cs`、`UiChip.cs:30-36` |
| P3 行密度 | 行高 122 → 96、头像 80 → 64、行内边距 24 → 32、行间 12 → 10，1440×840 可见 5 行 | `UiTheme.cs:85-99` |
| P3 按钮宽度 | clamp(88…240) → 吸附官方档位 {102.7, 150, 182, 187.8, 277} | `UiTheme.cs:190-200`、`UiButton.cs:182-196` |
| R2-01 热键被搜索框废掉 | `IsTextInputFocused` 改判 TMP 真实焦点；`Hide`/`Discard` 归还 EventSystem 选中项 | `HistoryUi.cs:187-203`、`HistoryPageController.cs:298,1027` |
| R2-02 空态多行溢出 | 空态标题/说明、好友栏空态各自套 `ClipBox`（RectMask2D 容器） | `HistoryPageController.cs:494-513,564`、`UiKit.cs:86-94` |
| R2-03 头/页脚/横幅未裁剪 | 标题、副标题、页脚提示、删除横幅全部套 `ClipBox` | `HistoryPageController.cs:391,401,469,583` |
| R2-04 关闭时白扫场景 | `ApplySorting(false)` 只打日志后返回 | `RoomHubPage.cs:97-101` |
| R2-05 精灵缓存早销毁 | `DropPage` 仅在页面已不在时释放精灵缓存 | `HistoryUi.cs:97-110` |
| R2-06 每刷新重复排版 | 按钮宽度按“路径+最小宽+内缩+文案”缓存；chip 文案未变直接返回 | `UiButton.cs:182-196`、`UiChip.cs:56` |
| R2-07 滚动越界 | `EnsureVisible` 自己把 `anchoredPosition.y` 夹到 [0, content−viewport] | `UiScrollView.cs:90-97` |
| R2-08 死方法 | 删除 `TickInputBlock`，改为 `IsSearchFocused` 探针 | `HistoryPageController.cs:292-298` |
| R2-10 行池覆盖泄漏 | `Take` 遇到已销毁的行先 `Dispose` 再新建 | `Rows.cs:543-557` |
| R2-12 徽章文字溢出胶囊 | chip 根节点加 RectMask2D | `UiChip.cs:34` |

## 8. 未处理（明确延后）

| 项 | 原因 |
|---|---|
| R2-09 克隆按钮不清 `onClick` | `UiButtonFactory.cs` 不在本工作包；且需先证伪模板是否带 persistent listener |
| §3.3 房间行加房间码 chip | 96 高两行行内无横向余量，加了会挤掉行副信息；需先改行结构 |
| T-5 好友“多久前在线” | `FriendRoomInfo` 没有时间字段，`FriendRoomsService` 不归本工作包 |
| T-6/T-7 筛选空态 / 搜索中 | 面板没有房间筛选，搜索是内存同步查询，没有可见的中间态 |

## 9. 第二轮仍需实机验证

| # | 项 | 期望 |
|---|---|---|
| S1 | 面板打开后标题/副标题/行名/行副信息/页脚提示/空态说明全部可见 | 本次事故的验收项 |
| S2 | 日志 `UI font: button=… label=…` | 两者同名则字形统一；不同名说明按钮与标签两套字体（评审 §5.5） |
| S3 | 点搜索框 → ESC 关面板 → 按 K | 面板重新打开（R2-01 修复点） |
| S4 | 32 字符房主名 / 48 字符查询词 | 副标题在统计 chip 前被裁掉，不压 chip、不出卡片 |
| S5 | 空态长中文说明、无好友时的好友栏 | 不溢出到页脚/列表 |
| S6 | 行数：1440×840 | 可见 5 行（96+10 节奏） |
| S7 | 按钮外观 | 与游戏暂停菜单按钮同高同边框（67），同行两个按钮宽度取自官方档位 |
