# 3.0.0 实机回归修复（2026-11-01）

## 1. 面板文字大面积不显示（严重）

**现象**：标题、副标题、房间行名称与副信息、玩家行文字全部不可见；按钮、标签页、页脚提示、搜索占位符、状态徽章正常。

**证据链**
1. 用户截图放大后确认标题区与行文本区**像素级为空**，不是颜色接近背景。
2. 不可见的四类标签与可见标签的唯一差别是**行高与字号的比值**：
   - 标题 42pt 放在 50px 高（比值 1.19）
   - 副标题 17pt 放在 22px 高（1.29）
   - 行名 28pt 放在 34px 高（1.21）
   - 行副信息 19pt 放在 24px 高（1.26）
   而可见的标签全部比值 ≥ 2（按钮 24/66、标签页 24/60、页脚 18/96、徽章 17/34、占位符 20/60）。
3. `UiKit.Label` 对非换行标签设置 `overflowMode = TextOverflowModes.Ellipsis`。TMP 在该模式下，
   当矩形高度小于一行行高（Daruma Drop One 行高约 1.3x 字号）时会整行裁掉，表现为**完全空白**。

**修复**
- `UiKit.Label` 的溢出模式改为 `Overflow`，任何情况下都绘制已有文本（`UiKit.cs`）。
- 行高同步放宽到安全值：`Line1Height` 34→40、`Line2Height` 24→30、标题框 50→58、副标题框 22→30。
- 好友栏标题 21→26，行高 40→46（用户反馈"太粗太小"）。

## 2. 日志中的模组相关错误

`Failed to load text: ROOMHISTORY_UNKNOWN_HOST / ROOMHISTORY_UNKNOWN_PLAYER`

**根因**：`Plugin.Awake` 先执行 `Store.Load()`，再执行 `Loc.Register()`；而 `HistoryStore` 在加载时用
`Loc.T(Loc.UnknownHost)` 判断占位名，此时本地化表尚未注册。

**修复**：把 `Loc.Register()` 提到 `Store.Load()` 之前。

（日志里的 `Failed load bundles Object reference not set` 来自游戏自身的 Addressables 初始化，与本模组无关。）

## 3. 按钮改回游戏自身的按钮组件（用户要求）

原实现自绘按钮（自建 Image + 两条内嵌亮边）。用户明确要求使用 PEAKLib 依赖库里的、游戏自身的那种按钮。

**修复**：`UiButton` 改为包装 `PEAKLib.UI.Elements.PeakMenuButton`，经 `UiButtonFactory.CreateMenuButtonSafe`
克隆场景真实按钮（上一版已验证可用的路径），角色配色只改 `Panel` 与上下两条 `Border` 的颜色
（`PeakMenuButton.SetColor(automaticBorderColor:false)` + `SetBorderColor`），文字度量与内边距全部沿用游戏模板。
按钮高度对齐模板：`ButtonHeight` 67、`ButtonHeightSmall` 60、`TabHeight` 67。

## 4. 面板过透导致可读性差

实测令牌 `DarkPanel`（0.1792,0.1253,0.0905,0.7294）是游戏给小型 HUD 芯片用的，用作全屏卡片时会让场景透上来。
新增 `CardFill`（0.106,0.075,0.055,0.96）用于卡片底，行底由 0.32 黑提高到 0.72 暖黑。

## 5. 仍需实机确认

- 标题/行文本是否恢复显示（本次核心修复）。
- 按钮外观是否回到上一版那种官方按钮。
- 好友栏标题字号是否合适。
