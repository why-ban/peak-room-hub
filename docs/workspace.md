# 工作区说明

## 路径

| 用途 | 路径 |
|------|------|
| 工作目录 | `D:\zhuanban\Quick Rejoin` |
| 游戏目录 | `D:\SteamLibrary\steamapps\common\PEAK` |
| 反编译工具 | `ilspycmd`（dotnet tool） |

## 目录约定

- **所有工作文件只放工作目录**（含反编译产物）
- `refs/`：程序集副本，便于离线反编译与后续 csproj 引用
- `decompiled/`：可读 C# 源，**以反编译为准，禁止凭记忆写 API**
- `src/`：Quick Rejoin 模组源码
- 用 **git** 管理；大改前 commit，保证可回退

## 常用反编译命令

在 `refs/` 下执行（路径含空格时更稳）：

```powershell
ilspycmd .\PEAKLobbyBrowser.dll -p -o ..\decompiled\PEAKLobbyBrowser
ilspycmd .\PEAKLib.UI.dll -p -o ..\decompiled\PEAKLib.UI
ilspycmd .\Assembly-CSharp.dll -t SteamLobbyHandler -o ..\decompiled\game
```

## Git

- 仓库根：`D:\zhuanban\Quick Rejoin`
- 不要提交密钥；游戏本体不进仓库（只提交 refs 中需要的 dll 副本若体积可接受）
