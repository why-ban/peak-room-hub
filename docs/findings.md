# Quick Rejoin — 反编译发现（基于真实代码，非猜测）

## 工作区布局

```
Quick Rejoin/
  src/              # 模组源码（待实现）
  decompiled/       # ilspycmd 反编译产物
    PEAKLobbyBrowser/
    PEAKLib.UI/
    PEAKLib.Core/
    game/           # 定向反编译的游戏类型
  refs/             # 研究/编译用程序集副本
  docs/             # 笔记
  build/            # 构建产物
```

## 游戏联机架构（关键）

PEAK 使用 **Steam Lobby + Photon PUN** 双层：

1. 玩家加入 **Steam Lobby**（`CSteamID`）
2. 进入后通过 Lobby Chat 向主机请求 **Photon Room Name**
3. 再 `PhotonNetwork.JoinRoom` 进入实际游戏房间

主入口：`SteamLobbyHandler.TryJoinLobby(CSteamID)`  
参考模组 `PEAKLobbyBrowser` 也是走这条路径。

## 加入失败会发生什么？（回答用户问题）

### 结论

**游戏原生已经有明确的 Modal 弹窗提示**，不是静默失败。  
只要我们调用 `SteamLobbyHandler.TryJoinLobby`，无效/已解散房间会触发官方提示。

### 路径明细（`SteamLobbyHandler`）

| 场景 | 代码路径 | 玩家看到的提示（本地化 key） |
|------|----------|------------------------------|
| `RequestLobbyData` 立刻失败 | `TryJoinLobby` / `RequestLobbyData` | `MODAL_CANTJOINLOBBY_TITLE` + `MODAL_FAILEDTOFETCH_BODY` |
| Lobby 不存在 / 已解散 / 拉数据失败 | `OnLobbyDataUpdate` 且 `m_bSuccess != 1` | `MODAL_CANTFINDLOBBY_TITLE` + `MODAL_CANTFINDLOBBY_BODY` |
| 版本不一致 | `OnLobbyDataUpdate` PeakVersion 不匹配 | `MODAL_MISMATCH_TITLE` + `MODAL_MISMATCH_BODY`（含双方版本） |
| 进了 Lobby 但拿不到 PhotonRegion（重试 5 次后） | `OnLobbyEnter` | `MODAL_CANTJOINLOBBY_TITLE` + `MODAL_INVALIDLOBBY_BODY` |
| Photon 房间加入失败 | `NetworkConnector.OnJoinRoomFailed` | `MODAL_FAILEDPHOTON_TITLE` + `[returnCode] message`，点 OK 回 Title |
| 被踢 | message == `"KICKED"` | 切到 `KickedState`，主菜单显示踢出 Modal |

**已解散房间** 最常见落到：`MODAL_CANTFINDLOBBY_*`（找不到 Lobby）。

### 实现建议

- **优先复用** `GameHandler.GetService<SteamLobbyHandler>().TryJoinLobby(lobbyId)`，不要自己写一套 Steam Join，避免丢官方错误处理。
- 若还要额外提示（例如本地历史记录里标记“可能已过期”），可在调用前/后叠加，但**不能替代**原生 Modal。

## 参考模组 PEAKLobbyBrowser 可复用点

| 能力 | 实现方式 |
|------|----------|
| 主菜单按钮 | 等 `Title` 场景 → 找 `MainMenu/Canvas/MainPage/Menu/Buttons` → 克隆 `Button_PlaySolo` 改文案/位置/点击 |
| UI 页面 | `PEAKLib.UI`：`MenuAPI.CreatePageWithBackground`、`CreateMenuButton`、`CreateButton`、`CreateText`、`CreateScrollableContent` |
| 加入房间 | `SteamLobbyHandler.TryJoinLobby(new CSteamID(ulong))` |
| 依赖 | Hard: `PEAKLib.Core` + `PEAKLib.UI` |

注意：参考模组主菜单按钮是**克隆原生按钮**定位到右侧区域，不是 `MenuAPI.AddToMainMenu`。  
`MenuAPI.AddToMainMenu` 存在，但参考模组未用它做主入口按钮。

## 本地化

- 游戏：`LocalizedText.CURRENT_LANGUAGE` + `LocalizedText.Language`（含 `SimplifiedChinese` / `TraditionalChinese` / `English`）
- PEAKLib.UI：`MenuAPI.CreateLocalization(index).AddLocalization(text, language)` 可向 `LocalizedText.mainTable` 注册词条
- 模组应注册中英（至少 SimplifiedChinese + English；TraditionalChinese 可跟简中或单独）

## 需要记录的房间信息（实现时）

加入成功时建议持久化（JSON，本地文件）：

- `LobbyId`（ulong / string）— 加入必需
- `HostName` — 显示 “XXX创建的房间 / XXX's Room”
- `JoinedAt` (UTC) — 相对时间 + 24h 清理
- 可选：`LobbyName`、`MemberCount`、`MaxMembers`、`Scene`、`PhotonRegion`、`LastSeen`

上限 5 条；超过 24 小时自动清除；按最近加入排序。

## 主菜单交互需求映射

1. 主界面**右侧**按钮 → 打开最近房间列表页  
2. 列表项显示：房主房间名 + 多久前加入 + 可选详情  
3. **双击**房间条目 → `TryJoinLobby`  
4. 快捷键 **J** → 加入最近一条  
5. 中英随 `LocalizedText.CURRENT_LANGUAGE`  
6. UI 必须用 PEAKLib.UI  

## 尚未完全确认（实现前再反编译）

- 加入成功的最佳 Hook 点（`OnLobbyEnter` / Photon `OnJoinedRoom` / 其他）以便写入历史  
- 主菜单右侧精确布局是否沿用 LobbyBrowser 的 NextLevelUI 锚点，还是另找锚点  
- 双击检测用 Unity Event 还是自定义时间窗口  

---

生成方式：`ilspycmd` 反编译  
关键源：`Assembly-CSharp.dll` → `SteamLobbyHandler`、`NetworkConnector`；`PEAKLobbyBrowser.dll`；`PEAKLib.UI.dll`
