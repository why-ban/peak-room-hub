# PEAK 3.0.1 官方 UI 设计令牌（实测提取）

数据来源：`D:\SteamLibrary\steamapps\common\PEAK\PEAK_Data` 的 level1（主菜单）、level3/level4（暂停菜单/局内 UI）。
方法：UnityPy 1.25 读取 GameObject/RectTransform 类型树；`UnityEngine.UI.Image` 在 Mono 序列化里是 MonoBehaviour 且无类型树，
因此按 `m_GameObject(12) + m_Enabled(4) + m_Script(12) + m_Material(12) => m_Color @ offset 44` 的固定布局直接解 4 个 float。
提取脚本：`D:\zhuanban\youhua\scratch-assets\dump_palette.py`、`dump_pause_btns.py`、`dump_all_colors.py`。

## 1. 按钮解剖（所有 PEAK 按钮共用）

以 `UI_MainMenuButton_Resume`（277×67）为例，子物体顺序与几何完全固定：

| 子物体 | 锚点 | anchoredPosition | sizeDelta | 颜色 |
|---|---|---|---|---|
| Shadow | 全拉伸 | (0, -3.3) | (0, +10) | (0, 0, 0, 0.2667) |
| Panel | 全拉伸 | (0, 0) | (0, +10) | 角色主色 |
| Border（上） | 顶部拉伸 | (0, -15) | (-20, 7) | 角色亮色 |
| Border（下） | 底部拉伸 | (0, +14) | (-20, 7) | 角色亮色 |
| Glow | 全拉伸 | (0, 0) | (0, +10) | (1, 0.7046, 0, 0)，默认 alpha 0，hover 时淡入 |
| Text | 全拉伸 | (0, 0) | (-60, -20) | (1, 1, 1, 1) |

要点：
- Panel 比父物体高 10px（上下各出血 5px），Shadow 再下移 3.3px 形成投影。
- Border 是**内嵌亮边**，不是描边：距顶 15px、高 7px、左右各内缩 10px，上下各一条，构成"糖果条"高光。
- 277 宽按钮 Text 内缩为 (-50, -20)；150 宽按钮为 (-60, -20)。
- `UI_PauseMenuButton` 的 Border 有 -6.6 的横向偏移与 -28.5 的宽度修正，属于旧版遗留，不必复制。

## 2. 按钮角色色板（全部实测）

| 角色 | 用例 | Panel | Border |
|---|---|---|---|
| 主橙 primary | Resume / 通用主操作 | 0.6604, 0.2775, 0.0592 | 0.8774, 0.4371, 0.0 |
| 危险红 danger | LeaveGame | 0.5189, 0.1297, 0.1718 | 0.7453, 0.2058, 0.0 |
| 成功绿 success | Invite | 0.1416, 0.2925, 0.1173 | 0.0512, 0.434, 0.115 |
| 信息蓝 info | PauseMenuButton | 0.0588, 0.3598, 0.6588 | 0.0, 0.5177, 0.8784 |
| 强调紫 accent | Controls / ResetDefaults | 0.3919, 0.1843, 0.6235 | 0.5569, 0.1255, 0.8491 |
| 青 teal | Options | 0.185, 0.394, 0.6226 | 0.0, 0.5612, 0.5943 |
| 金 gold | Badges | 0.7736, 0.5567, 0.0 | 0.9451, 0.7576, 0.0 |
| 深红 darkred | 主菜单 4 号按钮 | 0.5, 0.0637, 0.0992 | 0.6509, 0.1851, 0.0 |

## 3. 表面与文字色

| 令牌 | 值 | 证据 |
|---|---|---|
| 纸白 paper | 0.8742, 0.8567, 0.7615 | `Box` / `FirstTimeSetupPage` / `Button`(PlayPage) |
| 纸白描边 tanBorder | 0.7529, 0.6745, 0.5529 | `Box/Border`、`Window_STATS/DotGrid` |
| 分隔线 divider | 0.3396, 0.2996, 0.2419 | `Title (1)/Line (1)` |
| 深色面板 darkPanel | 0.1792, 0.1253, 0.0905, 0.7294 | `Controls/Image`、`UI_Control (9)/Image` |
| 木色中 woodMid | 0.566, 0.4503, 0.3284 | `Menu/Panel (2)` |
| 木色深 woodDark | 0.4528, 0.3527, 0.2456 | `Menu/Panel (3)` |
| 木色浅 woodLight | 0.783, 0.6374, 0.4765 | `NextLevelUI/Panel` |
| 面板投影 panelDrop | 0.4497, 0.359, 0.3045 | `NextLevelUI/PanelDrop` |
| 全屏遮罩 scrim | 0, 0, 0, 0.8667 | `SettingsPage/Background` |
| 图标底 chipBg | 0.3585, 0.271, 0.2452 | `Icons/Image` |
| 金色强调 gold | 1.0, 0.8009, 0.0 | `SELECT` |
| 锁定灰 locked | 0.5294, 0.4784, 0.4 | `Box/LockedIcon` |

## 4. 按钮尺寸档位（实测）

| 场景 | 尺寸 |
|---|---|
| 主菜单主操作 | 420 × 67 |
| 主菜单次操作 | 353 × 60 |
| 暂停菜单标准 | 277 × 67 |
| 暂停菜单窄 | 187.8 × 67 |
| 主菜单小 | 182 × 50 |
| 主菜单更小 | 191.9 × 44.2 / 125.8 × 44.2 |
| 主菜单底部窄条 | 150 × 67 |

## 5. 可直接复用的 Sprite

从 `resources.assets` / `sharedassets1.assets` 导出（已存 PNG 于 `scratch-assets\sprites\`）：

- `UISprite`（32×32，白色圆角矩形，圆角约 8px）——通用面板/卡片底
- `InputFieldBackground`（32×32，同上）——输入框底
- `UIElement8px`（8×8）——极简圆角底
- `UIMask`（32×32）
- `UI_Blur`（202×202）、`UI_BlurCircle`、`UI_Blur_Outlne`、`UI_Banner`、`UI_Ring`、`UI_OutlineMask`
- 图标：`Crown`（房主冠冕）、`Check`、`UICheckMark`、`PeakIcon`、`ScoutIcon`、`Guidebook_*` 系列
- 手柄字形：`ButtonSpriteSheetXbox/PS4/PS5/Switch_0..31`

**运行时无需加载这些资源**：模组已经克隆了场景里的真实按钮模板（`UiButtonFactory`），
可以直接从克隆体的 `Panel`/`Border`/`Shadow`/`Glow` 子物体上取 `Image.sprite` 与 `Image.color`，
得到与官方完全一致的外观，且不需要打包任何游戏素材。

## 6. 未取到的项（需运行时确认）

- TMP 字号/字体资源：`TextMeshProUGUI` 同样是 MonoBehaviour，raw 偏移不可靠，未解析。
  现状模组用 `HistoryUiFont` 借用游戏字体，字号是自定的（44/24/20/18）。
- Glow 的 hover 动画曲线：`Glow` 默认 alpha 0，推测由 `SelectableEvents`/`Animator` 驱动，未验证。
- 按钮按下/禁用态颜色：场景里只有常态，禁用态由运行时脚本改色，未找到常量。
