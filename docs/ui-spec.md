# Room Hub 3.0.0 — UI 重建实现规格

设计稿：`tools/uimock/panel.html`（渲染：`node render.mjs "panel.html?variant=B&state=rooms" out.png`）。
已定方案：**B 布局** —— 顶部「房间 / 玩家」标签页 + 右侧常驻 Steam 好友栏；版本 **3.0.0**；新增「好友所在生物群系」。
参考分辨率 1920×1080（`PeakCustomPage` 的 CanvasScaler 强制此值，matchWidthOrHeight=1）。

## 0. 硬约束

1. **零游戏素材**：运行时不得 `Resources.Load` 任何游戏美术。所有圆角/边框底图由代码在运行时生成（见 §2）。
2. **不依赖 `Templates.ButtonTemplate`**：新 UI 不使用 `MenuAPI.CreateMenuButton`，也不使用 `PeakText`/`PeakButton`
   （`PeakText.Awake` 会把字体强制换成 Daruma Drop One 并把 sizeDelta 设成不换行首选宽度，这正是长名字溢出的根因）。
   只用 `PeakCustomPage` 作为页面壳（MenuWindow 集成需要），其余一律自建 GameObject。
3. **字体**：优先 `FontFallbackSwapper.instance.mainBaseFont`（游戏基础字体，`fallbackFontAssetTable[1]` 是简中/繁中回退，
   `FontFallbackSwapper.cs:31-41`）。取不到时退回 `TMP_Settings.defaultFontAsset`，再退回现有 `HistoryUiFont` 的借用逻辑。
   每个 TextMeshProUGUI 都要显式设 font，不要依赖 TMP 默认。
4. **公共 API 冻结**：`HistoryUi` 的全部 public 成员、`HistoryPageController` 的 `Open/Hide/Discard/Tick*` 等被 Plugin 调用，
   签名不变。`PauseInputSuppressor`（`[HarmonyPatch(typeof(GUIManager), nameof(GUIManager.UpdatePaused))]`）必须保留且语义不变。

## 1. 令牌（`UiTheme`）

颜色为游戏 3.0.1 实测值（`docs/audit/ui-palette.md`），Unity Color 直接构造：

| 名称 | RGBA |
|---|---|
| Paper 纸白（主文字/浅面板） | 0.8742, 0.8567, 0.7615, 1 |
| TanBorder 浅描边 | 0.7529, 0.6745, 0.5529, 1 |
| Divider 分隔线 | 0.3396, 0.2996, 0.2419, 1 |
| DarkPanel 深色面板底 | 0.1792, 0.1253, 0.0905, 0.7294 |
| WoodDark 未选中标签底 | 0.4528, 0.3527, 0.2456, 1 |
| ChipBg 头像/徽章底 | 0.3585, 0.271, 0.2452, 1 |
| Gold 金色强调 | 1.0, 0.8009, 0.0, 1 |
| Shadow 投影 | 0, 0, 0, 0.2667 |
| Scrim 全屏遮罩 | 0, 0, 0, 0.8667 |

按钮角色（Panel / Border 成对）：

| 角色 | Panel | Border | 用途 |
|---|---|---|---|
| Primary | 0.6604, 0.2775, 0.0592 | 0.8774, 0.4371, 0.0 | 关闭、主操作 |
| Success | 0.1416, 0.2925, 0.1173 | 0.0512, 0.434, 0.115 | 加入、邀请 |
| Info | 0.0588, 0.3598, 0.6588 | 0.0, 0.5177, 0.8784 | 查看 |
| Danger | 0.5189, 0.1297, 0.1718 | 0.7453, 0.2058, 0.0 | 删除 |
| Ghost | 0.4528, 0.3527, 0.2456 | 白 0.10 alpha | 次级操作 |
| Disabled | 0.30, 0.27, 0.24, 0.9 | 白 0.05 alpha | 不可用 |

尺寸（画布单位）：

| 项 | 值 |
|---|---|
| 卡片最大尺寸 | 1440 × 840，四周至少留 70；实际 = min(1440, 画布宽-140) × min(840, 画布高-140) |
| 卡片圆角 / 边框 | 13 / 2px `TanBorder` 0.28 alpha |
| 头部高 / 内边距 | 108 / 36 |
| 标题 / 副标题字号 | 42 粗体 / 17 |
| 分隔线 | 高 3，左右缩进 36 |
| 标签行高 / 上内边距 | 92 / 16 |
| 标签 | 高 60，最小宽 180，间距 12，字号 24 粗体，圆角 10 |
| 搜索框 | 320 × 60，圆角 10 |
| 列表区上内边距 / 左右 | 20 / 36 |
| 行 | 高 122，间距 12，圆角 13，字号 l1 28 粗体 / l2 19 |
| 头像 | 80 × 80，圆角 11 |
| 页脚高 / 内边距 | 96 / 36 |
| 好友栏宽 / 左边线 | 392 / 3px `Divider` 0.5 alpha |
| 按钮 | 高 66（小号 58），圆角 11（10），字号 24（21），左右内边距 26（20） |
| 按钮投影 | 下移 5，色 `Shadow` |
| 按钮内嵌亮边 | 上/下各一条，高 7（小号 6），左右内缩 12（10），距边 12（10），色 = Border |

## 2. 运行时圆角底图（`UiSprites`）

生成一次并缓存，供所有 Image 复用（九宫格 `Image.Type.Sliced`）：

- `Rounded(float radiusPx, float sizePx = 48)`：在 `Texture2D` 上画白色圆角矩形（抗锯齿用 4x 超采样后降采样），
  `Sprite.Create(tex, rect, pivot, 100f, 0, SpriteMeshType.FullRect, border)`，border = 各边 radiusPx。
  `tex.wrapMode = Clamp`，`filterMode = Bilinear`，`hideFlags = HideFlags.DontSave`。
- 缓存 key = 半径。半径集合：3（分隔线/亮边）、10、11、13。
- 全部 sprite 在 `Shutdown` 时 `Object.Destroy`。

若 `Texture2D` 生成抛异常，降级为 `sprite = null`（直角），不得让面板打不开。

## 3. 构件（`UiKit`）

每个构件返回根 GameObject，并暴露设置方法。所有构件：
`RectTransform + CanvasRenderer + (Image | TextMeshProUGUI)`，`raycastTarget` 按需。

- `Panel(parent, name)` → Image（圆角 13，`DarkPanel`）
- `Card(parent, name)` → Panel + 2px `TanBorder` 描边（用外层 Image 做描边色 + 内层 inset 2px 的实心）
- `Label(parent, text, size, color, bold)` → TextMeshProUGUI，`NoWrap`，`Ellipsis`，`raycastTarget=false`
- `Button(parent, label, role, small)` → 根 Image（Panel 色，圆角）+ 上下两条亮边 Image（Border 色）+ 居中 Text；
  挂 `Button` 组件，`targetGraphic` = 根 Image；`ColorBlock` 由 Panel 色派生 pressed/disabled（乘算，不引入新色）；
  `Navigation.mode = Explicit`，由调用方接线。
- `Chip(parent, text, ChipKind)` → 圆角 9 的胶囊 + 17 号字；ChipKind: Host(金)/Friend(蓝)/Live(绿)/Mute(灰)
- `Divider(parent)` → 高 3 的 `Divider` 色条
- `Avatar(parent, size)` → 根 Image（`ChipBg`，圆角）+ `Mask` + 子 Image（真实头像，`preserveAspect`）；
  无头像时子节点显示首字母 Label
- `StatusDot(parent)` → 10×10 绿点

## 4. 页面骨架（`RoomHubPage : PeakCustomPage`）

```
RoomHubPage (PeakCustomPage, sortingOrder 由现有 EnsurePageBlocksRaycasts 逻辑保证)
└ Scrim            (全屏 Image, Scrim 色, raycastTarget=true)
└ Card             (居中, §1 尺寸, 圆角 13, DarkPanel + TanBorder 描边)
   ├ Header        (108)  [标题 42][副标题 17]          右侧：[统计 Chip]
   ├ Rule          (3)
   ├ TabsRow       (92)   [房间 n][玩家]                 右侧：[搜索框 320×60][搜索按钮]
   ├ Body          (flex) [列表滚动区] | [好友栏 392]
   └ Footer        (96)   左：提示(Esc/双击)             右：[删除模式][关闭]
```

**响应式**：Card 用 `anchorMin=anchorMax=(0.5,0.5)`，在 `OnOpen` 与画布尺寸变化时重算 `sizeDelta`（§1 公式）。
Body 内部用 `anchorMin=(0,0) anchorMax=(1,1)` + `offsetMin/offsetMax` 拉伸，**不要**用固定像素宽度做左右分栏
（这正是旧版 4:3 重叠的原因）。好友栏用 `anchorMin=(1,0) anchorMax=(1,1)`、`pivot=(1,0.5)`、`sizeDelta=(392, 0)`、
`offsetMin/Max` 留出上下边距；主列表用 `anchorMax.x` 收缩到好友栏左侧减 22。

**视图**：`Rooms` / `Players` / `Search` 三种。标签只有「房间 / 玩家」两个；好友是右侧常驻栏，不是标签。
搜索命中时「玩家」标签保持高亮，副标题显示查询词，并提供「清除」回到房间视图。

## 5. 行（`RoomRow` / `PlayerRow` / `FriendRow`）

统一结构（自左向右）：头像 80 → 文本列（flex）→ 操作区（右对齐，按钮间距 12）。

- **房间行**：l1 = `{房主}的房间`（NoWrap + Ellipsis）；l2 = `{相对时间}加入 │ 遇见 {n} 人`；
  操作 = `加入`(Success，仅 JoinGate 允许且不在房间中时) + `查看`(Info)。
  删除模式：整行底色转 Danger 0.42 alpha、描边 Danger、操作区只留 `删除`(Danger)。
- **玩家行**：l1 = 名字 + `房主` Chip(金) + `好友` Chip(蓝)；l2 = `{相对时间}遇见 │ 房间：{房主名}`；
  操作 = `邀请`(Success，仅 `CanShowInvite` 时) + `加好友`(Ghost，已是好友则 Disabled 且不可点)。
- **好友行**：l1 = 名字；l2 = 状态点 + `在游戏中 · {生物群系}` / `在主菜单中` / `仅邀请` / `单人游戏中` / `状态未知`；
  操作 = `加入`(Success，可加入时) 或 `邀请`(Success，主菜单且我们在房间时) 或 状态 Chip(Mute)。

**行池化（必须）**：列表以 key（房间 = LobbyId，玩家 = SteamId，好友 = SteamId）维护
`Dictionary<string, RowView>`。刷新时复用同 key 的行、原地更新文本/按钮可见性/颜色，只对新增 key 创建、对消失 key 销毁。
**禁止**每次事件全量销毁重建（旧版每个好友回调都重建整个列表，导致滚动跳变与头像贴图反复重建）。
刷新后如行数变化，只在必要时重算 Content 高度。

## 6. 交互与导航

- **手柄必须可用**（旧版 `SelectOnOpen=false` 且从不设选中项，手柄完全不可操作）：
  `RoomHubPage` 覆写 `objectToSelectOnOpen` 返回首个可交互元素（房间视图 = 第一行的「加入/查看」按钮，
  无房间时 = 「关闭」）；`SelectOnOpen = true`。
- 每行内按钮、行间、标签、搜索框、页脚按钮之间建立**显式 Navigation**（上下左右）。
  行本身不参与选中，只有按钮参与（行点击 = 双击加入，保留）。
- 列表滚动：选中项变化时确保可见。`PeakScrollableContent` 不创建 Scrollbar，也**不**挂
  `ScrollRectAutoScroller`（已核实：该类只在存在时由外部调用），因此不需要额外 Scrollbar。
- **ESC**：保留现有 `HandleBackOrClose`（嵌套视图 → 返回；删除模式 → 退出；否则关闭）与
  `HistoryUi.PageOpenThisFrame` 一帧闩锁 + `PauseInputSuppressor`。
- **搜索框聚焦后 ESC 必须仍能关面板**：每帧检查 `TMP_InputField.isFocused`，聚焦时先 `DeactivateInputField()`
  并把焦点交回页面，再处理关闭。不要依赖 `HistoryUi.IsTextInputFocused()` 阻止 ESC（那正是 bug 来源）。
- 光标：交给游戏的 `CursorHandler`，删除手写的 `Cursor.lockState` 反复写入（`audit-ui.md` #15）。

## 7. 本地化

新增文案一律走 `Loc`（新增常量 + `L.Register()` 里补英/简中/繁中三条）。需要的新键至少包括：
标签名、统计文案、好友生物群系（13 个 `RichPresenceState` 的中英文名，可由 `FriendRoomsService` 提供枚举名，
UI 侧 `Loc` 映射）、空态、提示语、搜索清除。**不要**在 UI 里硬编码中文字符串。
语言切换：现有 `LocalizationSync` 回调里重建可见文本（含页壳与好友栏），不要整页销毁。

## 8. 验收

1. `dotnet build` 0 错误 0 警告。
2. 四个视图（房间/玩家/搜索/删除模式）与三种行在 16:9、16:10、4:3、21:9 下不重叠、不溢出。
3. 手柄：打开面板即有选中项，方向键可在行间与页脚间移动，A 触发、B 返回。
4. 长名字（32 字符）与 CJK 不遮挡右侧按钮。
5. 好友列表刷新不改变滚动位置、不重建未变化的行。
6. 用 `tools/uimock` 的渲染脚本产出对照图，与设计稿逐项比对。
