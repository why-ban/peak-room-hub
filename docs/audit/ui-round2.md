# UI-ROUND2 — 已实现面板的视觉 / 排版复审

复审对象：`src/RoomHub/Ui/`（12 个文件）+ 重写后的 `HistoryPageController.cs`。
对照基准：`docs/audit/ui-palette.md`（官方令牌）、`docs/audit/ui-design.md`（第一轮规格）、游戏 v3.0.1 反编译与 `PEAK_Data` 资源实测。
本轮只写文档，未改任何代码。

| 证据等级 | 含义 |
|---|---|
| **实测** | 从代码 / 资源文件读出，附 `file:line` 或资源对象名 |
| **推导** | 由实测值算出的结论（公式给出） |
| **待证** | 现有材料无法确定，需运行时验证 |

---

## 0. 先纠正一条会误导实现的事实

第一轮文档里我把 PEAKLib 的 `fontStyle = (FontStyles)16` 写成了 **Bold**，这是错的。

| 事实 | 证据 |
|---|---|
| `TMPro.FontStyles`：`Normal=0, Bold=1, Italic=2, LowerCase=8, UpperCase=0x10, …` | `Unity.TextMeshPro\TMPro\FontStyles.cs:8-18` |
| 所以 `(FontStyles)16` = **UpperCase**，不是 Bold | 同上 |
| PEAKLib 的按钮 / 标签页 / 输入框都是 **全大写**，不加粗 | `PEAKLib.UI.decompiled.cs:1237, 1759, 2065` |

这条错误被实现继承了：`UiKit.cs:66` 把所有 `bold: true` 的标签设成 `FontStyles.Bold`（7 处），而游戏与 PEAKLib 的实际强调手段是「大写 + 字号」，不是加粗。§1.4 给出后果。

---

## 1. 字号与字重阶梯

### 1.1 PEAK 自身的字号分布（实测）

对 `level1`（主菜单）/ `level3`、`level4`（暂停菜单、局内 UI）做 TMP 组件扫描，命中 447 个文本组件。方法：在 MonoBehaviour 原始字节里定位 `m_fontSize / m_fontSizeBase / m_fontWeight` 三元组（float, float, int，两个 float 相等且 weight ∈ {100…900}），再用紧随其后的 `m_enableAutoSizing / m_fontSizeMin / m_fontSizeMax / m_fontStyle` 做校验。**对象名无法还原**（MonoBehaviour 无类型树），所以这是分布统计，不是逐控件归属。

| 维度 | 结果 |
|---|---|
| 字号众数 | **24**（120 次）· 18（48）· 32（38）· 30（32）· 36（28）· 28（24）· 10（18）· 20（18）· 22（12）· 14（11）· 16（7） |
| 稀有字号 | 48（5）· 50（5）· 46（4）· 40.7（4） |
| `fontWeight` | **447/447 全部 400** |
| `m_fontStyle` | Normal **390（87.2%）** · UpperCase 29（6.5%） · **Bold 仅 11（2.5%）** · LowerCase 10 · Italic 7 |
| 自动缩放 | 关闭 250 / 开启 197 |

结论：**PEAK 的正文是 24，辅助是 18，标题是 28/30/32/36，加粗几乎不用。** 这直接对上用户的"字太粗太小"——实现里 7 处 Bold、且小字号（17/19）也在加粗。

### 1.2 当前实现逐项清单

| 层级 | 当前值 | 位置 | 字重 |
|---|---|---|---|
| 面板标题 | **42** | `UiTheme.cs:62` → `HistoryPageController.cs:384` | Bold |
| 副标题 | **17** | `UiTheme.cs:63` → `HistoryPageController.cs:392` | Normal |
| 标签页文字 | 未设（继承克隆模板的 TMP） | `UiTheme.cs:71` `TabSize=24` **从未被应用** | 继承模板 |
| 搜索框文字 / 占位 | 20 | `UiTheme.cs:77` → `UiTextInput.cs:62,67` | Normal |
| 行主信息（房名/玩家名） | **28** | `UiTheme.cs:84` → `Rows.cs:94` | **Bold** |
| 行副信息 | **19** | `UiTheme.cs:85` → `Rows.cs:102` | Normal |
| 徽章（Host/Friends/Live） | **17** | `UiTheme.cs:123` → `UiChip.cs:34` | **Bold** |
| 按钮文字 | 未设（继承克隆模板） | `UiTheme.cs:106-107` `ButtonTextSize(24/21)` **从未被应用** | 继承模板 |
| 页脚提示 | 18 | `UiTheme.cs:94` → `HistoryPageController.cs:560` | Normal |
| 好友栏标题 | **26**（金色） | `UiTheme.cs:99` → `HistoryPageController.cs:525` | **Bold** |
| 好友栏空态 | 19 | `UiTheme.cs:85` 复用 → `HistoryPageController.cs:542` | Normal |
| 空态标题 / 说明 | 28 / 19 | `UiTheme.cs:131-132` → `:483, :491` | Bold / Normal |
| 删除横幅 | 20 | `UiTheme.cs:129` → `HistoryPageController.cs:459` | Bold |
| 头像首字母 | `size*0.42` ≈ 33.6 | `UiAvatar.cs:43` | Bold |
| 统计 chip | 17 | `UiTheme.cs:123` → `HistoryPageController.cs:648` | Bold |

### 1.3 建议值表

原则：**字号量化到 1.1 的实测档位 {10, 14, 16, 18, 20, 22, 24, 28, 30, 32, 36}**；**Bold 只保留 1 处**（面板标题），其余靠字号 + 颜色 + 大写建立层级。

| 层级 | 当前 → 建议 | 依据 |
|---|---|---|
| 面板标题 | 42 Bold → **36 Normal + UpperCase** | 42 不在实测档位；36 出现 28 次且是实测最大档的主流；PEAK 的强调是 UpperCase（29 次）而非 Bold（11 次） |
| 副标题 | 17 → **18**，色 `TextDim` | 18 是实测第二众数（48 次）；17 不在档位 |
| 标签页文字 | 继承模板 → **保持继承**，但显式设 `fontStyle = UpperCase` | 字号由克隆模板决定（等于游戏自身），不要覆盖；`(FontStyles)16` 是 PEAKLib 的做法（`PEAKLib.UI.decompiled.cs:1759`） |
| 搜索框 | 20 → **18** | 20 可用，但输入框在 60 高的容器里 18 更稳；实测 18 次众数第二 |
| 行主信息 | 28 Bold → **24 Normal** | 24 是实测第一众数（120 次）；Bold 在小字号会糊（§1.4）。若必须保留强调，改 28 Normal |
| 行副信息 | 19 → **18** | 19 不在档位；18 是辅助文本的标准档 |
| 徽章 | 17 Bold → **16 Normal** | 17 不在档位；16 实测 7 次；胶囊高 34 装 16px 文字比例正确（≈47%） |
| 按钮文字 | 继承 → **保持继承** | 克隆体自带游戏字号，这是目前唯一"完全正确"的层级 |
| 页脚提示 | 18 → **16** | 16 是实测档位；提示属于最低层级，18 与行副信息同级会抢层级 |
| 好友栏标题 | 26 Bold 金色 → **22 Normal + UpperCase**，色保留 Gold | 26 实测仅 2 次；22 在档位（12 次）。Gold 本身对比度 12.2:1（§2），问题只在字号/字重 |
| 好友栏空态 | 19 → **18** | 同辅助文本 |
| 空态标题 / 说明 | 28/19 → **24 / 18** | 空态标题应与行主信息同级（24），说明与行副信息同级（18） |
| 删除横幅 | 20 Bold → **18 Normal** | 横幅是警示不是标题；18 足够，Bold 去掉 |
| 头像首字母 | 33.6 Bold → **`size*0.38`（80→30）Normal** | 80px 头像里 33.6 的粗体字母会糊，30 Normal 更接近游戏中玩家名的观感 |
| 统计 chip | 17 Bold → **16 Normal** | 同徽章 |

### 1.4 为什么小字号不能加粗（Daruma Drop One 的实际问题）

TMP 在字体资产没有对应粗体字面时，用 **SDF 膨胀（fake bold）** 模拟加粗：把字形 alpha 向外扩张若干像素。这个扩张量按字号比例给，所以 17–20px 的字母被扩张后，笔画间的空隙（counter）会被填掉，中文字形的笔画直接粘连——这就是"糊"。

- 当前实现里 17px Bold 的徽章、19px Bold 的空态标题、33.6px Bold 的头像字母都属于高风险区（`UiChip.cs:34`、`HistoryPageController.cs:483`、`UiAvatar.cs:43`）。
- Daruma Drop One 是否自带粗体字面**待证**（`TMP_FontAsset` 是 MonoBehaviour，读不到 `fontWeightTable`）。但不论有没有，结论相同：**24px 以下不用 Bold**；要强调就用字号或大写。
- 中文更要小心：`LocalizedText.languageSupportsAllCaps` 对中日韩返回 false（`LocalizedText.cs:131-141`），说明游戏本身就不指望中文靠"大写"做强调——中文的层级只能靠字号、颜色、间距。

---

## 2. 对比度

### 2.1 等效底色的合成链

面板不是纯色，必须按 alpha 逐层合成后算。合成顺序（从后到前）：

`场景 → Scrim(0,0,0,0.8667) → CardFill(0.106,0.075,0.055,0.96) → [RowBg(0.055,0.04,0.03,0.72)]`

| 层 | 结果 | 说明 |
|---|---|---|
| 卡片等效色（场景黑） | `0.102, 0.072, 0.053` | 推导 |
| 卡片等效色（场景白） | `0.107, 0.077, 0.058` | 推导 |
| 行等效色（场景黑） | `0.068, 0.049, 0.036` | 推导 |

**卡片 0.96 的 alpha 把场景几乎完全隔断**：场景从纯黑到纯白，卡片等效色只差 0.005，所以下面所有比值对场景不敏感（差异 < 0.2）。这是当前实现做对的地方，不用改。

WCAG 相对亮度：`L = 0.2126R' + 0.7152G' + 0.0722B'`，其中 `C' = C/12.92 (C≤0.04045)` 否则 `((C+0.055)/1.055)^2.4`；比值 `(Lmax+0.05)/(Lmin+0.05)`。

### 2.2 全量比值表

| 文字 | 底色 | 比值 | 判定（AA 正文 4.5 / 大字 3.0） |
|---|---|---|---|
| `Paper` 1.00（行主信息、标题） | 卡片 | **13.17 : 1** | ✅ AAA |
| `Paper` 1.00 | 行 | **13.84 : 1** | ✅ AAA |
| `TextDim` 0.58（副标题、行副信息） | 卡片 | **5.11 : 1** | ✅ AA |
| `TextDim` 0.58 | 行 | **5.18 : 1** | ✅ AA |
| **`TextFaint` 0.42**（页脚提示、空态说明、好友栏空态、搜索占位） | 卡片 | **3.26 : 1** | ❌ 不达 AA |
| `Gold` 1.00（好友栏标题） | 卡片 | **12.22 : 1** | ✅ AAA |
| 按钮白字 1.00 | Primary 橙 | **5.87 : 1** | ✅ AA |
| 按钮白字 1.00 | Success 绿 | **10.05 : 1** | ✅ AAA |
| 按钮白字 1.00 | Info 蓝 | **6.76 : 1** | ✅ AA |
| 按钮白字 1.00 | Danger 红 | **9.37 : 1** | ✅ AAA |
| 按钮白字 1.00 | Ghost / WoodDark | **6.43 : 1** | ✅ AA |
| **按钮白字 0.45（禁用）** | 灰 `0.30,0.27,0.24` | **3.32 : 1** | ⚠️ 禁用态豁免，但偏低 |
| 标签页白字 1.00（未选中） | WoodDark | **6.43 : 1** | ✅ AA（见 §2.4 的坑） |
| 标签页白字 1.00（选中） | Primary 橙 | **5.87 : 1** | ✅ AA |
| Host chip 金 `1,0.8009,0` | 金@0.15 叠卡片 | **8.73 : 1** | ✅ AAA |
| Friend chip 蓝 `0.4784,0.7765,1` | 蓝@0.15 叠卡片 | **8.61 : 1** | ✅ AAA |
| Live chip 绿 `0.5098,0.8706,0.4863` | 绿@0.2 叠卡片 | **9.70 : 1** | ✅ AAA |
| **Mute chip `Paper` 0.52** | tan@0.09 叠卡片 | **4.12 : 1** | ❌ 差 0.38 |
| 删除横幅文字 `1,0.7059,0.5882` | danger@0.3 叠卡片 | **9.32 : 1** | ✅ AAA |

### 2.3 不达标项与替换值（只用官方色或官方色的 alpha 变体）

| # | 位置 | 当前 | 替换 | 替换后比值 |
|---|---|---|---|---|
| C-1 | 页脚提示 `HistoryPageController.cs:560`、空态说明 `:491`、好友栏空态 `:542`、搜索占位 `UiTextInput.cs:67` | `TextFaint` alpha **0.42** → 3.26:1 | `TextFaint` alpha **0.55**（仍是 Paper 色，只改 alpha） | **4.71 : 1** ✅ |
| C-2 | 好友状态 chip `UiTheme.cs:167` | Paper alpha **0.52** → 4.12:1 | alpha **0.72** | **6.57 : 1** ✅ |
| C-3 | 禁用按钮文字 `UiButton.cs:101` | 白 alpha **0.45** → 3.32:1 | 白 alpha **0.65** | **5.09 : 1** ✅ |

> 如果只想改一处：C-1 是用户实际抱怨"说明文字看不见"的最可能来源——搜索占位和页脚提示是 3.26:1，在 16–20px 下确实吃力。

### 2.4 三个"看起来该管、其实不管"的死令牌

| 令牌 | 状态 | 后果 |
|---|---|---|
| `TabIdleText`（`UiTheme.cs:54`，alpha 0.72） | **声明但从未被读取** | 未选中标签页实际用 `UiButton.Repaint` 的纯白（`UiButton.cs:101`），所以 3.21:1 那个坑**没有发生**；但令牌留在那里，谁下次接上就会踩 |
| `TabPanel` / `TabLine` / `TabText`（`UiTheme.cs:178-183`） | **全部未被引用** | 同上，是"以为在用"的假配置 |
| `ChipLineColor`（`UiTheme.cs:170-176`） | **未被引用** | chip 的描边色定义了但没画，chip 实际只有底 + 字 |
| `TabSize` 24 / `ButtonTextSize` 24 / `ButtonTextSizeSmall` 21（`UiTheme.cs:71,106,107`） | **未被应用** | 标签/按钮字号由克隆模板决定。这是**好事**（原生字号），但这三个常量应删除，否则下一个人会以为改它有用 |

---

## 3. 信息密度

### 3.1 缺什么文本（具体到键名与三语）

| # | 位置 | 现状 | 建议新增 | 三语文案 |
|---|---|---|---|---|
| T-1 | Players 视图空态 | `_emptyHint.text = ""`（`HistoryPageController.cs:873`）——**只有一行标题，没有任何引导** | `ROOMHISTORY_EMPTY_PLAYERS_HINT` | en: `Players show up here after you share a room with them.` · zh-CN: `与你同房过的玩家会出现在这里。` · zh-TW: `與你同房過的玩家會出現在這裡。` |
| T-2 | 搜索无结果 | 同样 `""`（`:873`），用户不知道下一步做什么 | `ROOMHISTORY_SEARCH_EMPTY_HINT` | en: `Check the spelling, or search the full Steam name.` · zh-CN: `检查拼写，或改用完整 Steam 昵称搜索。` · zh-TW: `檢查拼寫，或改用完整 Steam 暱稱搜尋。` |
| T-3 | 好友栏（有内容时） | 只有标题 `Steam friends · N`，没有说明能做什么 | `ROOMHISTORY_RAIL_HINT` | en: `Join a friend's lobby from here.` · zh-CN: `可以直接从这里加入好友的房间。` · zh-TW: `可以直接從這裡加入好友的房間。` |
| T-4 | 删除模式 | 有横幅与 `HintEscDelete`，但没说"删除后不可恢复" | `ROOMHISTORY_DELETE_CONFIRM` | en: `Deleted rooms cannot be restored.` · zh-CN: `删除后无法恢复。` · zh-TW: `刪除後無法恢復。` |
| T-5 | 好友行 | 只有状态，没有"多久之前" | 复用 `ROOMHISTORY_MET_AGO` 的 `Loc.Relative` | en: `Seen {0}` · zh-CN: `{0}前在线` · zh-TW: `{0}前在線` |
| T-6 | 列表为空 vs 列表被过滤 | 两种空态共用 `EmptyRoomsTitle`，用户分不清"没记录"还是"筛选掉了" | `ROOMHISTORY_EMPTY_FILTERED` | en: `No rooms match this filter.` · zh-CN: `没有符合筛选条件的房间。` · zh-TW: `沒有符合篩選條件的房間。` |
| T-7 | 搜索进行中 | 无任何"正在搜索"反馈 | `ROOMHISTORY_SEARCHING` | en: `Searching…` · zh-CN: `正在搜索…` · zh-TW: `正在搜尋…` |

> T-1/T-2 是**代码里明确写死为空串**的两处，不是"忘了加"，是最直接的"文字消失"来源。用户那句"大量说明文字完全消失"至少有这两个实例可对。

### 3.2 冗余什么

| # | 冗余点 | 现状 | 建议 |
|---|---|---|---|
| R-1 | 标题与副标题重复品牌名 | 标题 `Room Hub`（`Loc.cs:97`）+ 副标题 `ROOM HUB · rooms and players you have met`（`Loc.cs:171`） | 副标题去掉 `ROOM HUB · ` 前缀，只留一句随视图变化的说明 |
| R-2 | 房间数出现三次 | 标签页 `Rooms (N)`（`:652`）+ 统计 chip `N rooms · M friends`（`:648`）+ 好友栏 `Steam friends · M`（`:736`） | 统计 chip 改为视图上下文（如"最近 7 天"或"共 N 条"），不再重复房间数 |
| R-3 | 好友数出现两次 | 统计 chip + 好友栏标题 | 同上，只保留好友栏标题里的数字 |
| R-4 | 行副信息用 `│` 分隔两条元数据 | `Rows.cs:333-334, 426`：`Joined 2h ago  │  4 players met` 在 19px 下横线会抢注意力 | 改 `·`（与副标题一致），或拆成两个右对齐小标签 |

### 3.3 行内信息结构建议

当前每行只有两行文字 + 右侧按钮，信息量对"房间历史"这个场景偏少：

- 行主信息：`{host}的房间`（`Loc.cs:118`）
- 行副信息：`{时间}  ·  {N} 人`（当前 `│`）
- **建议补第三块**：房间码（`RoomRecord.LobbyId` 的历史值）——用户重进靠的是它，但现在要 hover/点击之后才看得到。建议在行副信息右侧加一枚 `chipBg` 底的小号码片，16px。

---

## 4. 层级与呼吸感

### 4.1 垂直节奏实测（卡片 1440×840）

| 区块 | 高度 | 占卡片 | 位置 |
|---|---|---|---|
| Header | 108 | 12.9% | `UiTheme.cs:60` |
| Rule（分隔线） | 3 | — | `UiTheme.cs:64`，锚在 y=−108 |
| Tabs 区 | 92 | 11.0% | `UiTheme.cs:66`（tab 67 + 上 16 + 余 9） |
| Body | **541** | 64.4% | `HistoryPageController.cs:443-444` 算出 |
| Footer | 96 | 11.4% | `UiTheme.cs:93` |
| 合计 | 840 | 100% | ✅ 闭合 |

行：122 高 + 12 间距 → **可见 4.03 行**。1440 宽的面板只露 4 行，密度偏低；PEAK 自己的按钮行是 67。

### 4.2 三个确定的排版缺陷

**P-1（高）分隔线穿过副标题。**
- 副标题矩形：`offsetMin(36, −118)` / `offsetMax(−420, −88)`（`HistoryPageController.cs:397-398`）→ 竖直区间 **−88 … −118**。
- 分隔线：锚 `y = −HeaderHeight = −108`，高 3（`HistoryPageController.cs:403-409`）→ 占据 **−108 … −111**。
- 副标题 17px 行高约 20px，垂直居中于区间中心 −103 → 实际占 **−93 … −113**。
- 结论：**3px 分隔线正好切过副标题下半部**。这既是"文字被吃掉"的观感来源之一，也是层级信号错乱（线在字中间）。
- 建议：副标题整体下移 6（`offsetMin y = −124`、`offsetMax y = −94`），或把 Rule 下移到 `y = −(HeaderHeight + 6)`。二者取一，别都改。

**P-2（高）同一行里 67 与 60 混排。**
- 标签页 67 高（`HistoryPageController.cs:418`，`UiTheme.TabHeight`），同一条基线上的搜索按钮 60 高（`:426`，`ButtonHeightSmall`），搜索框 60 高（`:434`，`SearchHeight`）。
- 结果：标签条右端三个元素底边不齐，差 7px；页脚里 Close/Delete 也是 60（`:569, :577`）夹在 96 高的页脚中间。
- 建议：**统一 67**（撤掉这三处 `ButtonHeightSmall`），或按 §5.2 走官方 182×50 档并同步改边框配方。混用是最差的选项。

**P-3（中）四套边距并存。**
- 卡片内边距 `Pad = 36`（`UiTheme.cs:61`）、行内边距 `RowPad = 24`（`:82`）、头像到文字列 `RowGapInner = 24`（`:83`）、行间距 `RowGap = 12`（`:80`）、主列与右栏 `RailGap = 22`（`HistoryPageController.cs:22`）。
- 36 / 24 / 22 / 12 之间没有倍数关系，视觉上"外松内紧"但紧得不一致。
- 建议节奏：**外 32 / 内 24 / 元素间 16 / 行间 10**（全部 2 的倍数，且 32 与 24 相差 1.33 倍，比 36:24=1.5 更收敛）。改动只在 `UiTheme.cs:61,80,82,83` 与 `HistoryPageController.cs:22` 五处。

### 4.3 行内横向留白

行宽 954（卡片 1440 − Pad 72 − 右栏 414）：

| 区段 | 当前 | 评价 |
|---|---|---|
| 左 → 头像 | 24 | 偏小；与卡片 36 的内边距不成比例，行看起来"贴边" |
| 头像 80 | — | 122 高的行里占 66%，合理 |
| 头像 → 文字列 | 24 | 合理 |
| 文字列宽（无按钮时） | 802 | 富余 |
| 文字列宽（两个按钮时） | ≈539 | 按钮宽随文案浮动（88…240），文字列宽跟着跳，**同一列表里每行文字起点相同但终点不同**，观感散 |
| 文字 → 右侧按钮 | 24（`RowGapInner`） | 合理 |

建议：行高 122 → **96**，头像 80 → **64**，左内边距 24 → **32**（与卡片 32 对齐），按钮宽度量化（§5.3）后文字列宽就不再跳。

---

## 5. 按钮外观

### 5.1 模板实测：150×67，边框按 67 高调过

克隆源是主菜单的 `UI_MainMenuButton_LeaveGame (2)`（`PEAKLib.UI.decompiled.cs:1138`），在 `level1` 里实测：

| 子物体 | sizeDelta | anchoredPosition |
|---|---|---|
| 根 | **150 × 67** | — |
| `Shadow` | (0, **+10**) | (0, **−3.3**) |
| `Panel` | (0, **+10**) | (0, 0) |
| `Border`（上） | (**−20**, **7**) | (0, **−15**) |
| `Border`（下） | (−20, 7) | (0, **+14**) |
| `Glow` | (0, +10) | (0, 0) |
| `Text` | (**−60**, **−20**) | (0, 0) |

同场景里 182×50 那一档的边框配方**完全不同**（`Border` sizeDelta **(159, 7)**，位置 −10 / −15）。这说明 **PEAK 是逐档重调边框的，不是等比缩放**。

### 5.2 把 67 压到 60 会发生什么（数值）

`ButtonHeightSmall = 60`（`UiTheme.cs:103`）被用在 `HistoryPageController.cs:426, 569, 577`。克隆体的子物体是按 67 高写的：

| 量 | 67 高（原生） | 60 高（当前实现） | 差异 |
|---|---|---|---|
| 上边框到顶边 | 15 | 15（锚定，不变） | 0 |
| 下边框到底边 | 14 | 14（锚定，不变） | 0 |
| **两条糖果条之间的净空** | 67−15−7−7−14 = **24** | 60−15−7−7−14 = **17** | **−29%** |
| Panel 出血 | +10（上下各 5） | +10 | 不变，但相对更厚 |
| Text 可用高 | 67−20 = **47** | 60−20 = **40** | −15% |

后果：两条亮条被挤到中间，按钮显得"扁且闷"；文字可用高从 47 掉到 40，模板字号下中文的下伸部会贴到亮条上。**这就是"不像游戏按钮"的量化原因**——不是配色错，是几何被压了。

- 建议：**所有按钮统一 67 高**（撤掉 `ButtonHeightSmall` 的三处使用）。
- 若确实需要更矮：用官方的 **182×50** 档，并且**同时**把边框改成该档配方（`Border` sizeDelta 159×7，位置 −10/−15）。只改高度不改边框，任何档位都会错位。

### 5.3 宽度：当前不是官方档位

`UiButton.SetLabel`（`UiButton.cs:54-57`）：`width = clamp(preferredWidth + pad*2, 88, 240)`。

- 88 比官方最窄的 102.7 还窄；240 又落在 187.8 与 277 之间。
- 结果是每个按钮宽度随文案浮动，"Join"≈107、"Add friend"≈172，同一行两个按钮宽度不一致，右侧边缘参差。
- 建议：**量化到官方档位 {102.7, 150, 182, 187.8, 277}**，取"能装下 preferredWidth 的最小档"。这样一行里两个按钮的宽度关系是游戏本身有的比例，且 §4.3 的文字列宽不再跳。

### 5.4 配色：这一层是对的，只有一处不是官方色

| 项 | 状态 |
|---|---|
| Panel 用 `PanelColor(role)`、Border 用 `BorderColor(role)`，成对取自官方色板 | ✅ 正确（`UiButton.cs:97-98`） |
| `SetColor(..., automaticBorderColor: false)` 后单独 `SetBorderColor` | ✅ 正确，避免 PEAKLib 用 HSV±0.2 覆盖官方边框色 |
| Glow / Shadow 保持模板原值（1,0.7046,0 与 0,0,0,0.2667） | ✅ 正确，hover 表现与游戏一致 |
| 文字白色 | ✅ 与实测 `ConfirmPage/UI_OK`、`UI_X` 的 Text=(1,1,1,1) 一致 |
| `UiRole.Disabled` = `(0.30, 0.27, 0.24, 0.9)` | ❌ **不是官方色**。官方"不可用"语义色是 `locked (0.5294,0.4784,0.4)`。但白字压 locked 只有 4.4:1，所以要么保留当前灰 + 文字提到 0.65（5.09:1），要么 locked + 纯白（4.4:1，仍差一点）。建议前者。 |

### 5.5 一个待证的一致性风险

按钮文字用的是克隆模板自带的 `TMP_FontAsset`（`UiButtonFactory.PrepareTemplate` 只删 `LocalizedText`，不动字体），而 `UiKit.Label` 的标签走 `HistoryUiFont.Resolve()`（优先 `FontFallbackSwapper.mainBaseFont`）。两者**大概率是同一个字体资产**，但没有代码保证。若不同，面板里会同时出现两套字形。建议运行时打一行日志比对 `font.name`（**待证**）。

---

## 6. 缺陷清单（按严重度）

| # | 严重度 | 缺陷 | 证据 | 修复 |
|---|---|---|---|---|
| D-01 | **高** | 分隔线切过副标题 | `HistoryPageController.cs:397-398` vs `:403-409` | 副标题下移 6 或 Rule 下移 6（§4.2 P-1） |
| D-02 | **高** | 说明类文字 3.26:1，达不到 AA | `UiTheme.cs:46` + 4 处引用 | `TextFaint` alpha 0.42 → 0.55（4.71:1） |
| D-03 | **高** | 按钮 67 高被压到 60，原生边框配方错位 | `UiTheme.cs:103`；`HistoryPageController.cs:426,569,577`；模板实测 150×67 | 统一 67，或换 182×50 档并同步改边框 |
| D-04 | **高** | 小字号 + Bold（17/19/33.6）导致糊 | `UiChip.cs:34`、`HistoryPageController.cs:483`、`UiAvatar.cs:43`、`UiKit.cs:66` | 24px 以下不用 Bold；强调改 UpperCase |
| D-05 | 中 | Players / 搜索视图空态说明被写死为空串 | `HistoryPageController.cs:873` | 补 T-1 / T-2 两条文案 |
| D-06 | 中 | 行高 122 导致 1440×840 面板只显示 4 行 | `UiTheme.cs:79,90` | 行高 96、头像 64 → 5.5 行 |
| D-07 | 中 | 标签/搜索/页脚按钮 60 与 67 混排 | `:418` vs `:426`；`:569,577` | 见 D-03 |
| D-08 | 中 | 按钮宽度 clamp(88…240) 不对齐官方档位 | `UiButton.cs:55` | 量化到 {102.7,150,182,187.8,277} |
| D-09 | 中 | 好友状态 chip 4.12:1 | `UiTheme.cs:167` | alpha 0.52 → 0.72（6.57:1） |
| D-10 | 低 | 6 个令牌声明但从未使用（TabIdleText/TabPanel/TabLine/TabText/ChipLineColor/TabSize/ButtonTextSize*） | 见 §2.4 | 删除或接线，别留着误导 |
| D-11 | 低 | 禁用按钮文字 3.32:1 | `UiButton.cs:101` | 白 0.45 → 0.65 |
| D-12 | 低 | 标题 42 / 副标题 17 / 行 19 / 徽章 17 不在 PEAK 实测档位 | §1.1 vs §1.2 | 按 §1.3 量化 |

---

## 附录 A — 证据索引

| 内容 | 位置 |
|---|---|
| 主题令牌与常量 | `src/RoomHub/Ui/UiTheme.cs` |
| 标签构造与字重 | `src/RoomHub/Ui/UiKit.cs:49-76` |
| 按钮包装与重绘 | `src/RoomHub/Ui/UiButton.cs:50-102` |
| 行解剖 | `src/RoomHub/Ui/Rows.cs:66-104, 227-259` |
| 头部 / 标签 / 页脚 / 空态 | `src/RoomHub/HistoryPageController.cs:379-579` |
| 文案键与三语 | `src/RoomHub/Loc.cs` |
| 官方色板 | `docs/audit/ui-palette.md` |
| TMP 字重枚举 | `decompiled-latest/Unity.TextMeshPro/TMPro/FontStyles.cs:8-18` |
| PEAKLib 的 UpperCase | `PEAKLib.UI.decompiled.cs:1237, 1759, 2065` |
| 按钮模板实测（150×67 及子物体） | `level1` → `UI_MainMenuButton_LeaveGame (2)`；对照 `UI_MainMenuButton (1..4)` = 182×50 的另一套边框 |

## 附录 B — 复算方法（可复现）

1. 字号分布：对 `level1/level3/level4` 的每个 MonoBehaviour 原始字节做滑窗，匹配 `(float f, float f, int w)` 且 `8 ≤ f ≤ 96`、`|f1−f2| < 0.01`、`w ∈ {100…900}`，再用紧随的 `m_enableAutoSizing∈{0,1}`、`m_fontSizeMin/Max` 合理区间、`m_fontStyle ∈ [0,0x3FF]` 三重校验。命中 447 个组件。**对象名不可还原**，故只作分布统计。
2. 对比度：按 §2.1 的合成链逐层 alpha 合成得等效底色，再套 WCAG 相对亮度公式。场景取纯黑与纯白两端，结果差异 < 0.2，说明卡片 0.96 alpha 已隔离场景。
3. 按钮几何：`level1` 的 `Transform` 类型树直接读 `m_SizeDelta` / `m_AnchoredPosition`（原生类型，无类型树缺失问题）。
