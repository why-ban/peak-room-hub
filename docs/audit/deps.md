# 依赖审计：PEAKLib / Steamworks.NET / BepInEx·HarmonyX（对应游戏 3.0.1）

审计对象：`D:\zhuanban\Quick Rejoin`，RoomHub.dll。**只读审计，未改动 src/**。

## 0. 方法与证据链约定

三条独立证据源，报告里分别用不同锚点：

| 前缀 | 含义 | 可复现命令 |
|---|---|---|
| `build/decompiled-peaklib/<asm>/<file>.cs:LINE` | 用 ilspycmd 反编译**当前安装的 DLL** 得到 | 见下 |
| `D:\zhuanban\youhua\decompiled-latest\<asm>\<file>.cs:LINE` | 权威游戏 3.0.1 反编译 | 已有 |
| `<repo>/src/RoomHub/*.cs:LINE` | 本 mod 源码 | 已有 |

复现 PEAKLib 反编译（本报告全部 PEAKLib 行号都来自这次输出）：

```
C:/Users/Administrator/.dotnet/tools/ilspycmd.exe -p \
  -o "build/decompiled-peaklib/PEAKLib.UI" --nested-directories \
  "D:\SteamLibrary\steamapps\common\PEAK\BepInEx\plugins\com.github.PEAKModding.PEAKLib.UI.dll"
```

同法对 `PEAKLib.Core` / `PEAKLib.ModConfig` / `BepInEx\core\0Harmony.dll` 执行。

运行期证据：`D:\SteamLibrary\steamapps\common\PEAK\BepInEx\LogOutput.log`（791,089 B，末次写入 2026-10-31 01:01，单次会话，BepInEx 5.4.23.3，Unity v6000.3.15.12692100 — log:2-3）。

---

## 1. Installed vs latest

| 依赖 | 已安装 | 安装日期 | 上游最新 | 落后 | 证据 |
|---|---|---|---|---|---|
| PEAKLib.Core | **1.7.2** (`1.7.2+6baf03bb22b4894c96a4986a5f1662d1f6ae6780`) | 2025-12-23 | 1.7.2 | **无** | `build/decompiled-peaklib/PEAKLib.Core/Properties/AssemblyInfo.cs:13`；`CorePlugin.cs:10` |
| PEAKLib.UI | **1.7.2** (`1.7.2+0f112221a591a5c756d8ecb4c0405829f43935ff`) | 2026-09-17 | 1.7.2 | **无** | `PEAKLib.UI/Properties/AssemblyInfo.cs:13`；`UIPlugin.cs:8` |
| PEAKLib.ModConfig | **1.8.2** (`1.8.2+3a472f9fe544746bd7e121bcf944bc6b27379ab5`) | 2026-09-14 | 1.8.2 | **无** | `PEAKLib.ModConfig/Properties/AssemblyInfo.cs:12`；`ModConfigPlugin.cs:27` |
| BepInEx 5 | **5.4.23.3** (`+cd593c47…`) | 2025-04-22 | 5.4.23.6 (2026-10-23) | 3 patch | 文件版本元数据；BepInEx releases.atom |
| Harmony (0Harmony) | **2.9.0** — `ProductName = HarmonyX`, `CompanyName = BepInEx` | 2025-06-18 | HarmonyX 2.16.1 (2026-03-30) | 7 minor | 文件版本元数据；HarmonyX releases.atom |
| MonoDetour | **0.7.13** | 2026-03-02 | 未取到（MonoDetour 无 GitHub releases 源） | 未知 | `com.github.MonoDetour.dll` 版本元数据 |
| HarmonyXInterop | **1.0.0.0** (`+d4cdcb4c…`) | 2025-06-18 | 随 BepInEx 5 | — | `BepInEx\core\HarmonyXInterop.dll` |
| Steamworks.NET（游戏自带） | **2024.8.0** / Steamworks SDK **1.61** | 2026-10-23 | 2025.165.0 / SDK 1.65 (2026-10-30) | 5 次 SDK 升级 | `D:\zhuanban\youhua\decompiled-latest\com.rlabrecque.steamworks.net\Steamworks\Version.cs:5,7` |

**结论 1（重要，与任务前提相反）**：PEAKLib 三个包**不是"落后三个月"**——已安装的就是 Thunderstore 上的最新版。

Thunderstore 版本史（`https://thunderstore.io/c/peak/api/v1/package/` 全量索引，2587 个包）：

- `PEAKModding-PEAKLib_UI`：1.6.1 (2025-10-26) → 1.6.2 (2026-08-29) → 1.7.0 (2026-09-08) → 1.7.1 (2026-09-14) → **1.7.2 (2026-09-17)**
- `PEAKModding-PEAKLib_Core`：… → **1.7.2 (2025-12-23)**（此后未动）
- `PEAKModding-ModConfig`：… → **1.8.2 (2026-09-14)**（包名是 `ModConfig`，不是 `PEAKLib_ModConfig`）

**真正的风险点在这里**：`RoomHub.dll` 文件时间 2026-08-11，即编译时用的是 **PEAKLib.UI ≤ 1.6.1**；此后 UI 在 3 周内连发 4 版（1.6.2 → 1.7.2）。所以问题不是"要不要升级"，而是"mod 是照 1.6.1 写的，现在跑在 1.7.2 上"。

---

## 2. 1.6.1 → 1.7.2 到底破了什么（公共 API 逐成员比对）

用 Mono.Cecil 导出两个 DLL 的 public 表面后做集合差（脚本见 §9）。结果：

| 比对 | 移除 | 新增 |
|---|---|---|
| PEAKLib.UI 1.6.1 → 1.7.0 | **0** | 22 |
| PEAKLib.UI 1.7.0 → 1.7.2 | 2 | 0 |
| **PEAKLib.UI 1.6.1 → 1.7.2** | **0** | **20** |
| PEAKLib.Core 1.7.1 → 1.7.2 | **0** | **0** |

1.6.1 → 1.7.2 的全部变化（纯新增）：

```
T PEAKLib.UI.Elements.PeakDropdown : PeakLocalizableElement        (整类新增)
  Dropdown / Background / Arrow                                     (属性)
  SetOptions(List<string>) / SetPosition(Vector2) / SetInitialValue(int)
  SetBackgroundColor / SetArrowColor / SetLabelColor / SetOptionsColor / SetMultiSelect
  OnValueChanged(UnityAction<int>)
M PEAKLib.UI.MenuAPI.CreateDropdown(string, Transform)
M PEAKLib.UI.MenuAPI.AddToSettingsMenus(BuilderDelegate)
M PEAKLib.UI.Elements.PeakHorizontalTabs.TryGetTab(string, out GameObject)
```

1.7.0 → 1.7.2 唯一移除：`PeakDropdown.VerticalLayout`（属性 + getter）。**mod 没用它**（`grep -n VerticalLayout src/` 无命中）。

**结论 2**：**mod 调用的每一个 PEAKLib 公共 API 在 1.7.2 上仍然存在且签名未变**——`MenuAPI.CreateMenuButton`、`Templates.ButtonTemplate`、`PeakMenuButton`、`PeakText`、`PeakTextInput`、`PeakScrollableContent`、`MenuAPI.CreatePageWithBackground`、`MenuAPI.CreateLocalization`、`MenuAPI.AddToPauseMenu` 全部在。

这与 PEAKLib 的贡献者约定一致：*"Never break the public API. Public members must never be renamed (add the `[Obsolete]` attribute and create a new e.g. method with the new name instead)"* —— `https://raw.githubusercontent.com/PEAKModding/PEAKLib/main/README.md`。

所以 mod 的故障**不是 API 删除**，而是**运行期行为**（§4.1）和**游戏侧对象变化**（§4.2）。

**全 PEAKLib 只有一个 `[Obsolete]`**：`MenuAPI.AddToSettingsMenu`（`MenuAPI.cs:41`，指向 `AddToSettingsMenus`，`MenuAPI.cs:47`）。mod 没调用它（`grep -n "AddToSettingsMenu" src/` 无命中），所以**没有一处"用了已弃用 API"**。

---

## 3. PEAKLib.UI 1.7.2 可用 API 清单（UI 重建的地基）

命名空间：`PEAKLib.UI`（`MenuAPI` / `ElementExtensions` / `Utilities` / `UIAlignment` / `TranslationKey`），`PEAKLib.UI.Elements`（控件）。

### 3.1 `MenuAPI`（静态工厂，唯一入口）

| 成员 | 签名 | 行为要点 | 锚点 |
|---|---|---|---|
| `OPTIONS_WIDTH` | `const float = 277f` | 暂停菜单标准宽度 | `MenuAPI.cs:29` |
| `AddToMainMenu` | `void(BuilderDelegate)` | 委托参数是 `Transform` | `MenuAPI.cs:31` |
| `AddToPauseMenu` | `void(BuilderDelegate)` | 1.7.2 在 `PauseMenuMainPage.Start` 后触发 | `MenuAPI.cs:36`；`Hooks/PauseMenuMainPageHooks.cs:20` |
| `AddToSettingsMenus` | `void(BuilderDelegate)` | **推荐**（旧的 `AddToSettingsMenu` 已 Obsolete） | `MenuAPI.cs:47` |
| `AddToControlsMenu` | `void(BuilderDelegate)` | 在 `InitializeControlsPageTemplates` 之后 | `MenuAPI.cs:52`；`PauseMenuMainPageHooks.cs:25` |
| `CreatePage` | `PeakCustomPage(string pageName)` | `new GameObject(name).AddComponent<PeakCustomPage>()`，**无背景** | `MenuAPI.cs:57-62` |
| `CreatePageWithBackground` | `PeakCustomPage(string)` | = CreatePage + `CreateBackground()` | `MenuAPI.cs:72-75` |
| `CreateChildPage` | `PeakChildPage(string, UIPage parent)` | 父级必须是 `UIPageHandler` 下的页 | `MenuAPI.cs:64-70` |
| `CreateMenuButton` | `PeakMenuButton(string buttonName)` | **依赖 `Templates.ButtonTemplate`，为空即抛异常** | `MenuAPI.cs:77-88` |
| `CreatePauseMenuButton` | `PeakMenuButton(string)` | CreateMenuButton + `SetWidth(277f)` | `MenuAPI.cs:90-93` |
| `CreateText` | `PeakText(string displayText)` | 对象名固定 `UI_PeakText` → **不能本地化** | `MenuAPI.cs:95-101` |
| `CreateText` | `PeakText(string displayText, string objectName)` | 唯一可本地化重载 | `MenuAPI.cs:103-111` |
| `CreateButton` | `PeakButton(string buttonName)` | **不依赖任何模板**，纯代码构建 | `MenuAPI.cs:113-120` |
| `CreateScrollableContent` | `PeakScrollableContent(string)` | 自带 `Content`（VLG + ContentSizeFitter） | `MenuAPI.cs:122-126` |
| `CreateTextInput` | `PeakTextInput(string inputName)` | 依赖 `InputCellMapper.Instance.FloatSettingCell` | `MenuAPI.cs:128-133` |
| `CreateDropdown` | `PeakDropdown(string, Transform parent)` | 1.7.0 新增；依赖 `InputCellMapper.Instance.EnumSettingCell` | `MenuAPI.cs:207-218` |
| `CreateLocalization` | `TranslationKey(string index)` | 注册键，见 3.7 | `MenuAPI.cs:153-160` |
| `AddOnOffSetting` | `GenericBoolSetting(string displayName, bool defaultValue, SettingsCategory, bool currentValue, Action<bool>? saveCallback = null)` | 自动 `SettingsHandler.Instance.AddSetting` | `MenuAPI.cs:162-175` |
| `AddSliderSetting` | `GenericFloatSetting(string, float defaultValue, SettingsCategory, float currentValue, float minValue = 0f, float maxValue = 1000f, Action<float>? = null)` | 同上 | `MenuAPI.cs:177-190` |
| `AddEnumSetting<T>` | `GenericEnumSetting<T>(string, T currentValue, T defaultValue, SettingsCategory, Action<T>? = null) where T : unmanaged, Enum` | 同上 | `MenuAPI.cs:192-205` |

`BuilderDelegate` 定义：`public delegate void BuilderDelegate(Transform transform)`（`MenuAPI.cs:17`）。

### 3.2 `PeakElement`（所有控件的基类）

```csharp
[RequireComponent(typeof(RectTransform))]
public class PeakElement : MonoBehaviour {
    public RectTransform RectTransform { get; internal set; }   // Awake 里赋值
}
```
锚点：`Elements/PeakElement.cs:5-13`。

**派生链**：`PeakElement` → `PeakLocalizableElement`（`Text` 属性 + `SetTextInternal`，`Elements/PeakLocalizableElement.cs:7-20`）→ `PeakText` / `PeakMenuButton` / `PeakDropdown`。`PeakButton` / `PeakScrollableContent` / `PeakHorizontalTabs` / `PeakTextInput` 直接继承 `PeakElement`。

### 3.3 `PeakButton` — 现代行/按钮原语（**重建首选**）

```csharp
[RequireComponent(typeof(Button))]
public class PeakButton : PeakElement {
    public Button Button { get; set; }
    public PeakText Text { get; set; }
    public PeakButton SetWidth(float width);
    public PeakButton SetHeight(float height);
    public PeakButton OnClick(UnityAction onClickEvent);
}
```
- `Awake` 自建子对象 `Image`（底色 `RGBA(0.1792, 0.1253, 0.0905, 0.729)`）+ `Text (TMP)`，TMP 自适应 18–22、`fontStyle = 16`(Bold)、`alignment = 514`：`Elements/PeakButton.cs:23-37`
- **不需要 `Templates.ButtonTemplate`**，因此不受 §4.1 影响

### 3.4 `PeakMenuButton` — 主菜单/暂停菜单风格的"大按钮"

```csharp
public class PeakMenuButton : PeakLocalizableElement {
    public Button Button { get; private set; }
    public Image Panel { get; private set; }
    public Image BorderTop { get; private set; }
    public Image BorderBottom { get; private set; }
    public PeakMenuButton SetText(string text);
    public PeakMenuButton SetColor(Color color, bool automaticBorderColor = true);
    public PeakMenuButton SetBorderColor(Color color);
    public PeakMenuButton SetWidth(float width);
    public PeakMenuButton OnClick(UnityAction onClickEvent);
}
```
**硬约束**（克隆场景按钮时才会踩到）：`Awake` 直接 `transform.Find("Panel")`、`Find("Text")`、`Find("Border")`，并把 `Border` 的**下一个兄弟**当 `BorderBottom`——任一缺失即 `NullReferenceException`：`Elements/PeakMenuButton.cs:23-26`。`SetColor` 在 `automaticBorderColor=true` 时用 `Utilities.GetContrastingColor` 反推描边色（`:35-46`）。

### 3.5 `PeakText`

```csharp
public class PeakText : PeakLocalizableElement {
    public TextMeshProUGUI TextMesh { get; }
    public PeakText SetText(string text);
    public PeakText SetFontSize(float size);
    public PeakText SetColor(Color color);
}
```
两个必须知道的副作用：
1. `Awake` 把锚点强制成 `anchorMin = anchorMax = (0,1)`、`pivot = (0,1)`，并把字体换成 `"Daruma Drop One"`（`Resources.FindObjectsOfTypeAll<TMP_FontAsset>()` 按 `faceInfo.familyName` 找）：`Elements/PeakText.cs:54-63`。
2. `SetText` / `SetFontSize` 都会把 `sizeDelta` 重算成 `GetPreferredValues()`（`:70`、`:78`）→ **任何固定尺寸标签必须在 SetText 之后重新写 sizeDelta**（`HistoryUiFont.ForceCenteredLabel` 已经这么做了，`src/RoomHub/HistoryUiFont.cs:95`）。

### 3.6 `PeakScrollableContent` / `PeakHorizontalTabs` / `PeakTextInput` / `PeakDropdown`

```csharp
public class PeakScrollableContent : PeakElement {
    public RectTransform Content { get; private set; }
}
```
`Awake` 建 `Content`（`RectTransform + VerticalLayoutGroup + ContentSizeFitter`），`childControlHeight = false`、`childForceExpandHeight = false`、`verticalFit = PreferredSize`；ScrollRect：`scrollSensitivity 50`、`elasticity 0`、`movementType Clamped`：`Elements/PeakScrollableContent.cs:19-41`。→ **行必须自带高度**。

```csharp
public class PeakHorizontalTabs : PeakElement {
    public List<GameObject> Tabs;
    public GameObject AddTab(string tabName);
    public bool TryGetTab(string tabName, out GameObject Tab);   // 1.7.0 新增
    public void DeleteTab(string tabName);
    public void UpdateName(string currentName, string newName);
    public void SetBackgroundColor(Color color);
}
```
- 无 `MenuAPI` 工厂，需自己 `AddComponent`（`[RequireComponent(ScrollRect, RectMask2D)]`）
- `Awake` 先 `Tabs.Clear()`，并把自身尺寸固定为 `h=40`、顶部拉伸：`Elements/PeakHorizontalTabs.cs:30-36`
- `AddTab` 用字段 `backgroundColor` 上色（`:96`）→ **`SetBackgroundColor` 必须在 `AddTab` 之前调**
- `AddTab` 建 `LayoutElement{minWidth 220, flexibleWidth 1, preferredHeight 40}`（`:85-88`）

```csharp
public class PeakTextInput : PeakElement {
    public TMP_InputField InputField { get; private set; }
    public PeakTextInput OnValueChanged(UnityAction<string> onValueChanged);
    public PeakTextInput SetPlaceholder(string placeholder);
}
```
首次访问 `TextInputPrefab` 时若 `SingletonAsset<InputCellMapper>.Instance.FloatSettingCell` 为空会 **抛异常**（`Elements/PeakTextInput.cs:29-32`）——即"UI 创建得太早"。`Create()` 是 `internal`（`:73`），外部只能用 `MenuAPI.CreateTextInput`。

```csharp
public class PeakDropdown : PeakLocalizableElement {
    public TMP_Dropdown Dropdown { get; private set; }
    public Image Background { get; private set; }
    public Image Arrow { get; private set; }
    public PeakDropdown SetPosition(Vector2);
    public PeakDropdown SetOptions(List<string>);
    public PeakDropdown SetBackgroundColor(Color);
    public PeakDropdown SetArrowColor(Color);
    public PeakDropdown SetLabelColor(Color);
    public PeakDropdown SetOptionsColor(Color);
    public PeakDropdown SetMultiSelect(bool IsMulti);
    public PeakDropdown OnValueChanged(UnityAction<int>);
    public PeakDropdown SetInitialValue(int value);   // SetValueWithoutNotify
}
```
`Awake` 会 `RemoveAllListeners()` + `ClearOptions()`（`Elements/PeakDropdown.cs:45-46`）。

### 3.7 页面基类

```csharp
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(CanvasScaler))]
[RequireComponent(typeof(GraphicRaycaster))]
public class PeakCustomPage : MenuWindow {
    public static readonly Color DEFAULT_BACKGROUND_COLOR;   // (0,0,0,0.9569)
    public bool OpenOnStart { get; set; }          // 默认 false
    public bool SelectOnOpen { get; set; }         // 默认 true
    public bool CloseOnPause { get; set; }         // 默认 true
    public bool CloseOnUICancel { get; set; }      // 默认 true
    public bool AutoHideOnClose { get; set; }      // 默认 true
    public bool BlocksPlayerInput { get; set; }    // 默认 true
    public bool ShowCursorWhileOpen { get; set; }  // 默认 true
    public PeakCustomPage CreateBackground(Color? backgroundColor = null);
    public PeakCustomPage SetOnClose(UnityAction);
    public PeakCustomPage SetOnOpen(UnityAction);
}
```
`Awake`：`Canvas.renderMode = ScreenSpaceOverlay`、`additionalShaderChannels = 31`、**`sortingOrder = 1`**、`CanvasScaler` `ScaleWithScreenSize` 1920×1080 `matchWidthOrHeight = 1`：`Elements/PeakCustomPage.cs:54-65`。

```csharp
public class PeakChildPage : UIPage, IHaveParentPage {
    public static readonly Color DEFAULT_BACKGROUND_COLOR;
    public Button? BackButton { get; set; }
    public UIPage? ParentPage { get; set; }
    public PeakChildPage SetParentPage(UIPage parent);
    public PeakChildPage SetBackButton(Button button);
    public (UIPage, PageTransistion) GetParentPage();
    public PeakChildPage CreateBackground(Color? backgroundColor = null);
    public PeakChildPage SetOnClose(UnityAction);
    public PeakChildPage SetOnOpen(UnityAction);
}
```
`Awake` 必须能在父链上找到 `UIPageHandler`，否则抛异常（`Elements/PeakChildPage.cs:50`）。

### 3.8 布局/本地化扩展（`ElementExtensions`，全部 `this` 扩展、链式返回）

`ParentTo(Transform|Component)`（`:11/19/28/36`）、`SetSiblingIndex(int)`（`:45/52`）、`SetPosition(Vector2)`（`:59`）、`SetSize(Vector2)`（`:67`）、`SetOffsetMin/Max`（`:75/83`）、`SetAnchorMin/Max`（`:91/99`）、`SetAnchorMinMax(Vector2)`（`:107`）、`SetPivot(Vector2)`（`:117`）、`AlignToParent(UIAlignment)`（`:125`）、`ExpandToParent()`（`:176/191`）、`AddLocalization(string text, Language)`（`:211`）、`SetLocalizationIndex(string|TranslationKey)`（`:238/254`）。

`AddLocalization` 的坑：对象名等于 `"UI_PeakText"` 时**直接抛异常**，要求 `MenuAPI.CreateText(text, "mymod_unique_name")`（`ElementExtensions.cs:215-218`）。

`Utilities.GetContrastingColor(Color input, float shiftAmount = 0.2f)`：HSV 明度 ±0.2（`Utilities.cs:9-19`）。
`Utilities.ExpandToParent(RectTransform)`：`Utilities.cs:21-36`。

`TranslationKey` 是 `record`：`new TranslationKey(index).AddLocalization(text, language)`（`TranslationKey.cs:3-10`）。

---

## 4. mod 里已弃用 / 脆弱用法（含替代）

### 4.1 `Templates.ButtonTemplate` 反射注入 —— 可行，但方向错了

```
src/RoomHub/UiButtonFactory.cs:207   var prop = typeof(Templates).GetProperty("ButtonTemplate", BindingFlags.Public | BindingFlags.Static);
src/RoomHub/UiButtonFactory.cs:208   if (prop == null || !prop.CanWrite) { … return; }
src/RoomHub/UiButtonFactory.cs:213   prop.SetValue(null, template);
```

事实核对：
- `public static GameObject? ButtonTemplate { get; internal set; }`（`Elements/Templates.cs:9`）→ **setter 存在只是 `internal`，`PropertyInfo.CanWrite` 为 true**，`SetValue` 会成功。所以这段反射**当前有效**，不是 bug。
- 但整条链路本可以不存在：`MenuAPI.CreateButton`（`MenuAPI.cs:113-120`）**完全不读模板**。

**建议替代**：页面内的行/按钮改用 `MenuAPI.CreateButton` → `PeakButton`；只有确实需要"主菜单大按钮"外观时才走 `CreateMenuButton`，此时保留 `UiButtonFactory` 作为降级路径（它已经把 `DontDestroyOnLoad` 副本、`LocalizedText` 清理、`onClick` 清空都做对了，见 `UiButtonFactory.cs:178-196`）。

### 4.2 `Templates.ButtonTemplate` 为什么会是 null —— mod 的归因与证据不符

mod 的注释把锅甩给 PEAKLib（`UiButtonFactory.cs:16-20`：*"Newer PEAK builds renamed/removed that object, so the postfix throws"*）。逐行核对 PEAKLib 1.6.1 与 1.7.2 的 `Hooks/PauseMenuHooks.cs`：**两份文件只有 `using` 与特性写法不同，逻辑一字未改**（1.6.1 用 `using On.MainMenu`，1.7.2 用 `using Md.MainMenu`；这是 hookgen 生成代码的命名空间，属 PEAKLib 编译期产物，与游戏侧无关。是否由 MonoDetour 版本导致该改名 **UNPROVEN**，但不影响本结论：`First("UI_MainMenuButton_LeaveGame (2)")` 这一行两版完全相同）。

当前 1.7.2 的填充逻辑（`Hooks/PauseMenuHooks.cs:20-41`；为可读性省掉了 ilspy 的 `(Object)` 强制转换，语义等价，逐行锚点见注释）：

```csharp
private static void Postfix_Start(MainMenu self) {
    GameObject[] source = Resources.FindObjectsOfTypeAll<GameObject>();
    Templates.SettingsCellPrefab = source.First(n => n.name == "SettingsCell");            // :23
    GameObject val = source.First(n => n.name == "UI_MainMenuButton_LeaveGame (2)");        // :25
    if (Templates.SettingsCellPrefab == null || val == null) {                              // :27
        ThrowHelper.ThrowIfArgumentNull(val, "ButtonTemplate prefab is null! …");           // :29
        …
        return;
    }
    Templates.ButtonTemplate = Object.Instantiate(val);                                     // :33
}
```

三个可证事实：
1. 用的是 LINQ `First`（不是 `FirstOrDefault`）→ 名字对不上时**抛 `InvalidOperationException`**，`:27-31` 的空值保护是**死代码**，永远走不到。
2. 抛点在 MonoDetour 的 postfix 里 → `Templates.ButtonTemplate` 永远保持 `null`，之后所有 `MenuAPI.CreateMenuButton` 都撞 `MenuAPI.cs:82` 的 `throw new Exception("You're creating MenuButton too early! …")`。**这正是 mod 观察到的现象。**
3. 但触发条件是**游戏场景里是否存在名字恰为 `UI_MainMenuButton_LeaveGame (2)` 的 GameObject**——带 `(2)` 后缀说明它本来就依赖 Unity 的重名去重产物，极其脆弱。

**无法证明的部分**：`UI_MainMenuButton_LeaveGame` 在任何 3.0.1 代码里都不存在（`grep -rn UI_MainMenuButton D:\zhuanban\youhua\decompiled-latest` = 0 命中），因为 GameObject 名字活在场景/prefab 资产里，不在代码里。→ **UNPROVEN - needs runtime evidence**。

**可用运行期证据反而否证了它**：末次会话日志里 **没有任何** `UiButtonFactory` / `ButtonTemplate` 记录（`LogOutput.log` 全文检索，只有 3 条 `Room Hub` 行：log:86/88/252），说明那次会话模板是好的（`UiButtonFactory.EnsureTemplate` 在 `Templates.ButtonTemplate != null` 时静默返回，`UiButtonFactory.cs:66-67`）。**而且那次加载的是 PEAKLib.UI 1.7.1，不是磁盘上现在的 1.7.2**（log:31 `Loading [PEAKLib.UI 1.7.1]` vs `UIPlugin.cs:8` 声明 `1.7.2`）→ 环境在 2026-10-31 之后被换过，旧日志不能替 1.7.2 背书。

**替代方案（推荐）**：不要再依赖模板。见 §5.1。

### 4.3 `MenuWindow.Open/Close` 反射 —— 必需，不是坏味道

```
src/RoomHub/HistoryPageController.cs:55-58   typeof(MenuWindow).GetMethod("Open"/"Close", Instance|Public|NonPublic)
src/RoomHub/HistoryPageController.cs:216      MenuWindowOpen.Invoke((MenuWindow)_page!, null);
```

核对游戏侧：`internal virtual void Open()`（`Assembly-CSharp/MenuWindow.cs:80`）、`internal void Close()`（`:131`）——**确实是 internal**，公开 API 里没有等价物。`Open()` 是唯一会把实例塞进 `MenuWindow.AllActiveWindows`（`:9`、`:83-86`）的路径，也是唯一会 `SetInputActive(true)`（`:98`）和 `SelectStartingElement()`（`:96`）的路径。`Show()`（`:155`）只 `panel.SetActive(true)`。

→ 反射是**当前唯一正确做法**。更干净的替代：给 RoomHub 加
`[assembly: System.Runtime.CompilerServices.IgnoresAccessChecksTo("Assembly-CSharp")]`
然后直接调 `Open()` / `Close()`（PEAKLib 自己就是这么跨程序集访问游戏 internal 的：`PEAKLib.UI/Properties/AssemblyInfo.cs:8`）。这样编译期就能报错，而不是运行期 `GetMethod` 返回 null 静默降级（`HistoryPageController.cs:212-214`）。

### 4.4 `PauseMenuMainPage.m_quitButton` 反射 —— 仍有效

`src/RoomHub/PauseMenuHook.cs:19-20` 取 `typeof(PauseMenuMainPage).GetField("m_quitButton", Instance|NonPublic)`。
游戏侧仍存在：`private Button m_quitButton;`（`Assembly-CSharp/PauseMenuMainPage.cs:15`）。
可选的更稳锚点：同页已有 `public Button resumeButton => m_resumeButton;`（`:56`）与 `public Button copyRoomCodeButton;`（`:50`）——**Resume 有公开属性，Quit 没有**，所以 `m_quitButton` 仍只能反射。建议保留反射但加"取不到就 `SetAsLastSibling`"的降级（已有，`PauseMenuHook.cs:115/123`）。

### 4.5 其它 PEAKLib 相关脆弱点

| 位置 | 问题 | 替代 |
|---|---|---|
| `src/RoomHub/HistoryPageController.cs:405` | `CreatePageWithBackground` 建的是**根 Canvas**，`sortingOrder = 1`（`PeakCustomPage.cs:60`）。若游戏暂停菜单 Canvas 排序 ≥1，页面会被盖住 | 建完页面后显式 `GetComponent<Canvas>().sortingOrder = N`；具体安全值 **UNPROVEN - needs runtime evidence** |
| `src/RoomHub/HistoryPageController.cs:50-52,93-116` | 自己存 `_prevLockMode/_prevCursorVisible` 手工管光标 | `PeakCustomPage` 已有 `ShowCursorWhileOpen`/`BlocksPlayerInput`（`PeakCustomPage.cs:50-52`），交给 `MenuWindow` 驱动即可 |
| `src/RoomHub/PauseMenuHook.cs:57-58` | 每次切语言 `FindObjectsByType<PeakMenuButton>(Include, None)` 全场景扫 | 建按钮时缓存 `PeakMenuButton` 引用 |
| `src/RoomHub/HistoryUiFont.cs:60` | 全场景扫 `TextMeshProUGUI` 找字体 | 建 UI 时一次性抓 `PeakMenuButton.Text.font` 缓存（`HistoryUiFont.cs:35-39` 已有该路径） |
| `src/RoomHub/HistoryRowFactory.cs:51/133/165/233` | 用 `MenuAPI.CreateButton` 建行——**这条已经是现代的、无模板依赖的** | 保持 |
| `src/RoomHub/Loc.cs:143` | `MenuAPI.CreateLocalization(index)` | 保持（非 Obsolete） |

---

## 5. UI 重建应该采用什么（具体到 API 名）

### 5.1 按钮/行

- **行、图标按钮、次级操作** → `MenuAPI.CreateButton(name)` → `PeakButton`，`.SetHeight(h).SetWidth(w).OnClick(cb)`。无模板依赖，`Awake` 自建背景与 TMP 文本（`PeakButton.cs:23-37`）。
- **主菜单/暂停菜单同款大按钮** → `MenuAPI.CreateMenuButton(name)` → `PeakMenuButton`，`.SetText(t).SetColor(c, automaticBorderColor: true).SetWidth(MenuAPI.OPTIONS_WIDTH).OnClick(cb)`。用 `MenuAPI.OPTIONS_WIDTH`（277f）替代 `UiButtonFactory.cs:118` 的硬编码 277f。
- 需要和 PEAK 暂停菜单行完全同款时，克隆场景按钮仍是唯一途径——此时 `UiButtonFactory.IsSuitableTemplate`（`UiButtonFactory.cs:161-176`）的结构校验必须保留，因为 `PeakMenuButton.Awake` 硬依赖 `Panel`/`Text`/`Border`+下一兄弟（`PeakMenuButton.cs:23-26`）。

### 5.2 页面骨架

```csharp
var page = MenuAPI.CreatePageWithBackground("RoomHub Page");
page.OpenOnStart = false;
page.SelectOnOpen = true;
page.CloseOnPause = true;
page.CloseOnUICancel = true;
page.BlocksPlayerInput = true;
page.ShowCursorWhileOpen = true;
page.SetOnOpen(OnShown).SetOnClose(OnHidden);
```
全部来自 `PeakCustomPage.cs:40-52`、`:120-130`。打开仍走 `MenuWindow.Open`（§4.3）。

子页面（详情/设置）用 `MenuAPI.CreateChildPage(name, parentPage)` + `.SetBackButton(btn)`，返回按钮自动 `pageHandler.TransistionToPage(parent, SetActivePageTransistion)`（`PeakChildPage.cs:66-81`）。

### 5.3 列表

```csharp
var scroll = MenuAPI.CreateScrollableContent("RoomHubList");
scroll.ParentTo(page.transform).SetAnchorMinMax(new Vector2(0.5f, 1f)).SetSize(new Vector2(1200f, 620f)).SetPosition(new Vector2(0f, -160f));
// 行加进 scroll.Content
row.transform.SetParent(scroll.Content, false);
```
`Content` 已是 `VerticalLayoutGroup(childControlHeight=false, childForceExpandHeight=false)` + `ContentSizeFitter(verticalFit=PreferredSize)`（`PeakScrollableContent.cs:26-41`）→ **每行必须显式给高度**（`PeakButton.SetHeight`）。

### 5.4 标签页

```csharp
var tabs = new GameObject("RoomHubTabs", typeof(RectTransform), typeof(UnityEngine.UI.ScrollRect), typeof(UnityEngine.UI.RectMask2D))
    .AddComponent<PeakHorizontalTabs>();
tabs.SetBackgroundColor(panelBrown);   // 必须在 AddTab 之前
tabs.ParentTo(page.transform).ExpandToParent();
tabs.AddTab("Rooms").GetComponent<Button>().onClick.AddListener(ShowRooms);
```
`SetBackgroundColor` 先于 `AddTab` 是硬顺序（`PeakHorizontalTabs.cs:96` 读字段、`:159-164` 只写字段）。

### 5.5 文本

- 一律 `MenuAPI.CreateText(text, "roomhub_unique_name")` —— 传唯一名，否则 `AddLocalization` 抛异常（`ElementExtensions.cs:215-218`）。
- 本地化：`var key = MenuAPI.CreateLocalization("ROOMHUB_TITLE"); key.AddLocalization("房间历史", Language.ChineseSimplified);` 然后 `text.SetLocalizationIndex(key)`（`TranslationKey.cs:5-9`、`ElementExtensions.cs:254-259`）。这可以取代 `LocalizationSync` 的手工刷新（`PauseMenuHook.cs:33/45`）。
- **CJK 字体必须自己接管**：`PeakText.Awake` 固定用 `"Daruma Drop One"`（`PeakText.cs:61`）。保留 `HistoryUiFont.Apply` 的做法（`HistoryUiFont.cs:79-85`），但把字体抓取收敛到一次。

### 5.6 设置项

如果要把 `PluginConfig` 搬进游戏设置菜单：`MenuAPI.AddToSettingsMenus(builder)`（**不是** Obsolete 的 `AddToSettingsMenu`）+ `MenuAPI.AddOnOffSetting` / `AddSliderSetting` / `AddEnumSetting<T>`（`MenuAPI.cs:47,162-205`）。注意 `SettingsHandler.Instance` 为空时只会 `LogWarning` 不注册（`MenuAPI.cs:166-169`）。

### 5.7 调色板（可直接抄的常量）

| 值 | 用途 | 锚点 |
|---|---|---|
| `Color(0.1792453f, 0.1253449f, 0.09046815f, 62f/85f)` | Peak 按钮/标签底色（棕） | `PeakButton.cs:25`、`PeakHorizontalTabs.cs:171` |
| `Color(0, 0, 0, 0.9569f)` | 整页背景 | `PeakCustomPage.cs:136` |
| `Utilities.GetContrastingColor(c)` | 描边色 | `Utilities.cs:9` |
| TMP 字号 18–22 自适应 / `fontStyle 16` / `alignment 514` | 按钮文字规格 | `PeakButton.cs:31-36` |

---

## 6. Steamworks.NET

### 6.1 版本

游戏自带 `com.rlabrecque.steamworks.net.dll`（417,280 B）自报 **2024.8.0 / SDK 1.61**（`com.rlabrecque.steamworks.net/Steamworks/Version.cs:5,7`）。上游最新 **2025.165.0 / SDK 1.65**（2026-10-30）。

**这一项 mod 改不了**：Steamworks.NET 由游戏发布，mod 只能针对 2024.8.0 编译。升级路径只有游戏更新。2024.8.0 → 2025.165.0 的变更全是 SDK 1.61→1.65 的接口跟进（`ISteamNetworkingSockets` 参数化、SteamInput 事件大小、Android 支持、callback 异常处理器可覆盖），**没有** rich presence / lobby / invite / avatar 的破坏性改动——逐版 release notes 见 §9。

### 6.2 `RequestFriendRichPresence` 的官方节流语义（关键）

官方原文（`https://partner.steamgames.com/doc/api/ISteamFriends#RequestFriendRichPresence`）：

> Requests Rich Presence data from a specific user. **This is used to get the Rich Presence information from a user that is not a friend of the current user**, like someone in the same lobby or game server. **This function is rate limited, if you call this too frequently for a particular user then it will just immediately post a callback without requesting new data from the server.**
> Triggers a `FriendRichPresenceUpdate_t` callback.

mod 的用法（`src/RoomHub/FriendRoomsService.cs`）：
- 每 10 s 刷一次好友列表（`:58`），对每个"在玩 PEAK"的好友（`:130-137`）调 `RequestFriendRichPresence(sid)`（`:160`），单好友 30 s 节流（`:70`）。
- 把 `FriendRichPresenceUpdate_t` 当作"数据到了"的信号（`:84`、注释 `:142-143`）。

**三条与官方语义的冲突**：
1. 文档说这个接口的用途是**非好友**（同大厅/同服务器的人）；mod 拿它对**好友**用。好友的 rich presence 本应由客户端自动推送，不保证这个调用能拿到东西。
2. 文档说被节流时"**立刻回一个 callback 但不取新数据**"。而 mod 在 `OnFriendRichPresenceUpdate` 里作废缓存（`:84` + `:142-143` 注释）→ 节流回调会造成"作废→下次打开再请求→再被节流"的空转。30 s 闸门避免了热循环，但**回调不能当作数据新鲜的证据**。
3. mod 注释断言 `RequestFriendRichPresence` *"designed for lobby/game-server members … returns nothing"*（`:194-195`）——方向对，但结论过头：文档没说"returns nothing"，说的是"可能立刻回调且不带新数据"。

### 6.3 Enhanced Rich Presence 键

官方原文（`https://partner.steamgames.com/doc/features/enhancedrichpresence`）：

> To define the string to display in Steam friends list and chat, set the `steam_display` key/value in `SetRichPresence`.
> To define the grouping of players … set the `steam_player_group` and `steam_player_group_size` key/values in `SetRichPresence`.
> 示例：`SteamFriends()->SetRichPresence("steam_display", bDisplayScoreInRichPresence ? "#StatusWithScore" : "#StatusWithoutScore");`

即 `steam_display` 存的是**以 `#` 开头的本地化 token**，由 Steam 客户端渲染。

游戏侧实际写入（`Assembly-CSharp/SteamRichPresence.cs`）：

```csharp
if (state == RichPresenceState.Status_MainMenu) SteamFriends.ClearRichPresence();      // :17
else if (PhotonNetwork.OfflineMode) { num = 1; num2 = 1; }                             // :19-23
else if (PhotonNetwork.InRoom) {
    num = PhotonNetwork.PlayerList.Length;
    SteamFriends.SetRichPresence("steam_player_group", PhotonNetwork.CurrentRoom.Name); // :27
    num2 = PhotonNetwork.CurrentRoom.MaxPlayers;
}
SteamFriends.SetRichPresence("steam_display", "#" + state);                            // :30
SteamFriends.SetRichPresence("players", num.ToString());                               // :31
SteamFriends.SetRichPresence("steam_player_group_size", num.ToString());               // :32
SteamFriends.SetRichPresence("max_players", num2.ToString());                          // :33
```

对 mod 的直接影响：
- `steam_player_group` = **Photon 房间名**，且**主菜单分支不写**（`:17` 清空后只写 `:30-33`）。mod 把 `steam_player_group` 当"在房间里"的最强信号、且排在 `max_players=="1"` 之前判断（`FriendRoomsService.cs:233-234`）——**与游戏实现一致**。
- `steam_display` 游戏写的是 `"#" + state`（如 `#Status_Shore`），而 mod 注释说它"arrives LOCALIZED (e.g. "Main Menu")，不是 `#Status_MainMenu`"（`FriendRoomsService.cs:223-224`），并因此让 `string.Equals(display, "#Status_MainMenu")` 这条判断"rarely fires"（`:231`）。**代码与注释冲突**。按文档，`GetFriendRichPresence` 应返回 token；mod 观察到的本地化文本无法从代码解释 → **UNPROVEN - needs runtime evidence**（打一行 `LogDebug` 输出原始 `display` 即可定案）。
- `GetFriendRichPresenceKeyCount` 被 mod 当判别器用（`FriendRoomsService.cs:186`），官方文档对它的描述是 *"This is typically only ever used for debugging purposes."*（同 ISteamFriends 页）——能用，但不是设计用途。

### 6.4 邀请

mod 用 `SteamMatchmaking.InviteUserToLobby(liveLobby, target)`（`src/RoomHub/SteamInviteService.cs:48`）——大厅邀请的正确接口。
对照 `ISteamFriends::InviteUserToGame(CSteamID, const char* pchConnectString)`：其 `pchConnectString` 受 `k_cchMaxRichPresenceValueLength` 限制，且成功时触发 **`GameRichPresenceJoinRequested_t`**（不是 `GameLobbyJoinRequested_t`）。两条路径回调不同，若将来改用它必须同时换回调。

### 6.5 头像

mod：`SteamFriends.RequestUserInformation(steamId, false)`（`src/RoomHub/SteamAvatarService.cs:63`）+ `GetMediumFriendAvatar`（`:65`）。
官方对 `RequestUserInformation` 的警告：*"It's a lot slower to download avatars and churns the local cache, so if you don't need avatars, don't request them."* —— `https://partner.steamgames.com/doc/api/ISteamFriends#RequestUserInformation`。对陌生人批量拉头像时要限流/缓存（mod 已有 `SteamAvatarService` 缓存，需运行期确认命中率，**UNPROVEN**）。
`ActivateGameOverlayToUser("friendadd", id)`（`:148`）走 overlay，无节流文档约束。

---

## 7. BepInEx / HarmonyX / MonoDetour

### 7.1 Harmony 风味确认

`0Harmony.dll` 元数据：`FileVersion 2.9.0.0`、`ProductVersion 2.9.0`、`CompanyName BepInEx`、`ProductName HarmonyX` → **确认是 HarmonyX 2.9.0，不是上游 Harmony 2.x**。
MonoDetour 0.7.13 + `com.github.MonoDetour.Interop.HarmonyX.dll` 0.7.13 + `HarmonyXInterop.dll` 1.0.0.0 同时在 `BepInEx\core\`：**PEAKLib 走 MonoDetour，RoomHub 走 HarmonyX，两者通过 interop 共存**。PEAKLib 的钩子注册方式是 `[MonoDetourHookInitialize]` + `MonoDetourManager.InvokeHookInitializers(assembly)`（`CorePlugin.cs:28`、`UIPlugin.cs:21`、`ModConfigPlugin.cs:84`）——与 Harmony 的 `PatchAll` 是两套机制，互不干扰。

### 7.2 `Harmony.PatchAll` 的行为与 mod 的假设

mod：`_harmony = new Harmony(PluginGuid); _harmony.PatchAll(typeof(Plugin).Assembly);`（`src/RoomHub/Plugin.cs:150-151`）。

安装版实现（`build/decompiled-peaklib/0Harmony/HarmonyLib/Harmony.cs:131-137`）：

```csharp
public void PatchAll(Assembly assembly) {
    AccessTools.GetTypesFromAssembly(assembly).Do(delegate(Type type) {
        CreateClassProcessor(type).Patch();
    });
}
```

**没有任何 try/catch**。而 `PatchClassProcessor.PatchWithAttributes` 在字符串指定的目标方法解析不到时**抛异常**：

```csharp
lastOriginal = patchMethod.info.GetOriginalMethod();
if (lastOriginal == null)
    throw new ArgumentException("Undefined target method for patch method " + patchMethod.info.method.FullDescription());
```
—— `PatchClassProcessor.cs:181-185`。

**后果**：mod 里有两条**字符串命名**的补丁目标
- `src/RoomHub/LobbyRecorder.cs:10` — `[HarmonyPatch(typeof(SteamLobbyHandler), "OnLobbyEnter")]`
- `src/RoomHub/PlayerTracker.cs:256` — `[HarmonyPatch(typeof(SteamLobbyHandler), "OnLobbyDataUpdate")]`

只要游戏把这两个私有方法改名/加签名，`PatchAll` 会在**那一个类型**上抛出，**后续所有类型（含 `HistoryUi` 的 `GUIManager.UpdatePaused` 前缀）全部不再应用**，而且类型遍历顺序是程序集元数据顺序、不是源码顺序——故障表现会是"随机一部分功能静默失效"。

**当前是否已踩**：没有。游戏 3.0.1 里两者都在——`private void OnLobbyEnter(LobbyEnter_t param)`（`Assembly-CSharp/SteamLobbyHandler.cs:107`）、`private void OnLobbyDataUpdate(LobbyDataUpdate_t param)`（`:306`）。所以现在是**潜伏风险**，不是现存 bug。

**替代（推荐）**：改成显式逐条 `_harmony.Patch(AccessTools.Method(...), postfix: ...)` 并各自 try/catch，或对每条 `[HarmonyPatch]` 类单独 `CreateClassProcessor(t).Patch()` 包 try/catch，让一条失败不影响其余。

### 7.3 版本升级建议

- **BepInEx 5.4.23.3 → 5.4.23.6**：三个补丁版全是平台/日志修复（Doorstop 4.4.1→4.6.0、macOS arm64、Unity 6 日志写入）。BepInEx 5 已进入 LTS，**不建议为 mod 强行升级**；但 5.4.23.5 的 "Fix log writer errors for Unity 6" 对当前 Unity 6000.3 环境有实际意义（本机日志第 1 行就是 `[Error : BepInEx] Unable to start Unity log writer`，log:1）。
- **HarmonyX 2.9.0 → 2.16.1**：**不建议**。官方 v2.15.0 release note 明确警告 *"There may be some issues with IL manipulation on mono runtimes (see MonoMod/MonoMod#233). The v2.13 release is the last release without this problem."* —— PEAK 跑的是 Mono。2.9.0 早于该问题引入，反而更安全。且 Harmony 版本由 BepInEx 决定，mod 不应自带 `0Harmony.dll`。
- **MonoDetour 0.7.13**：PEAKLib 依赖它；上游无 GitHub releases 源，版本信息未取到 → **UNPROVEN**，不构成行动项。

---

## 8. 明确标注为未证实的结论

| 结论 | 状态 | 缺什么证据 |
|---|---|---|
| `Templates.ButtonTemplate` 在当前 3.0.1 上是否为空 | **UNPROVEN** | 需一次开页面时的运行日志（`UiButtonFactory` 会打 Info/Warning） |
| 游戏场景里是否还存在 `UI_MainMenuButton_LeaveGame (2)` | **UNPROVEN** | GameObject 名不在代码里；需 AssetStudio/运行时 dump |
| `PeakCustomPage` 的 `sortingOrder = 1` 是否会被游戏暂停菜单盖住 | **UNPROVEN** | 需运行时看游戏 PauseMenu Canvas 的 sortingOrder |
| `steam_display` 经 `GetFriendRichPresence` 返回 token 还是本地化文本 | **UNPROVEN**（代码与 mod 注释冲突） | 一行 `LogDebug(display)` |
| 末次会话日志（PEAKLib.UI 1.7.1）能否代表当前 1.7.2 环境 | **否** | log:31 与 `UIPlugin.cs:8` 版本不一致，环境已被替换 |

---

## 9. Sources（全部实际读过）

**本地**
- `D:\SteamLibrary\steamapps\common\PEAK\BepInEx\plugins\com.github.PEAKModding.PEAKLib.{Core,UI,ModConfig}.dll` — 反编译至 `build/decompiled-peaklib/`
- `D:\SteamLibrary\steamapps\common\PEAK\BepInEx\core\{0Harmony,BepInEx,HarmonyXInterop,com.github.MonoDetour*}.dll`
- `D:\SteamLibrary\steamapps\common\PEAK\BepInEx\LogOutput.log` — 运行期日志（791,089 B）
- `D:\zhuanban\youhua\decompiled-latest\Assembly-CSharp\{MenuWindow,PauseMenuMainPage,SteamRichPresence,SteamLobbyHandler}.cs`
- `D:\zhuanban\youhua\decompiled-latest\com.rlabrecque.steamworks.net\Steamworks\Version.cs`
- 旧版本比对：`build/pkgs/{UI-1.6.1,UI-1.7.0,Core-1.7.1}`（Thunderstore 下载）

**上游代码**
- https://raw.githubusercontent.com/PEAKModding/PEAKLib/main/README.md — "Never break the public API" 约定
- https://raw.githubusercontent.com/PEAKModding/PEAKLib/main/src/PEAKLib.UI/Elements/Templates.cs
- https://raw.githubusercontent.com/PEAKModding/PEAKLib/main/src/PEAKLib.UI/MenuAPI.cs
- https://raw.githubusercontent.com/PEAKModding/PEAKLib/main/src/PEAKLib.UI/Assets/README.md
- https://github.com/PEAKModding/PEAKLib/releases.atom — 仅 v1.0.0（2025-07-08）

**Thunderstore（真实发布渠道）**
- https://thunderstore.io/c/peak/api/v1/package/ — 全量索引，用于版本史与"已装即最新"的判定

**Release notes**
- https://github.com/rlabrecque/Steamworks.NET/releases.atom — 2024.8.0 … 2025.165.0
- https://github.com/BepInEx/BepInEx/releases.atom — 5.4.23.3 → 5.4.23.6
- https://github.com/BepInEx/HarmonyX/releases.atom — 2.9.0 → 2.16.1（含 mono IL 警告）

**复现脚本（§2 的公共 API 差集）**

用 BepInEx 自带的 `Mono.Cecil.dll` 导出 public 表面，再对两份文本做集合差：

```powershell
Add-Type -Path 'D:\SteamLibrary\steamapps\common\PEAK\BepInEx\core\Mono.Cecil.dll'
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
foreach ($t in $asm.MainModule.Types) {
  if (-not ($t.IsPublic -or $t.IsNestedPublic)) { continue }
  "T $($t.FullName) : $($t.BaseType)"
  foreach ($m in $t.Methods) { if ($m.IsPublic) { "  M $($t.FullName).$($m.Name)($($m.Parameters -join ', '))" } }
  foreach ($p in $t.Properties) { if (($p.GetMethod -and $p.GetMethod.IsPublic) -or ($p.SetMethod -and $p.SetMethod.IsPublic)) { "  P $($t.FullName).$($p.Name)" } }
  foreach ($f in $t.Fields) { if ($f.IsPublic) { "  F $($t.FullName).$($f.Name)" } }
}
```

**Steam 官方文档**
- https://partner.steamgames.com/doc/api/ISteamFriends#RequestFriendRichPresence — 节流语义原文
- https://partner.steamgames.com/doc/api/ISteamFriends#InviteUserToGame
- https://partner.steamgames.com/doc/api/ISteamFriends#RequestUserInformation — 头像缓存警告
- https://partner.steamgames.com/doc/features/enhancedrichpresence — `steam_display` / `steam_player_group` / `steam_player_group_size`
