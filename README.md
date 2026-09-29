# ADOFAI Mod Manager

一个面向《冰与火之舞》(A Dance of Fire and Ice) 的 Windows mod 管理器。
目标是**替代 Unity Mod Manager (UMM)**，把「找 mod → 下载 → 安装 → 更新 → 卸载」变成一站式操作，并提供现代化界面。

> ⚠️ 本项目是非官方第三方工具，与 7th Beat Games、UnityModManager 官方、ADOFAI Tools 资源站均无隶属关系。

---

## 功能

- **在线 Mod**：浏览 / 搜索 / 排序资源站上的 mod，一键下载安装；自动识别已安装与可更新。
- **已安装**：扫描 `Mods` 文件夹，启用 / 禁用、卸载、打开文件夹、本地 zip 安装、游戏版本兼容提示、依赖展示。
- **更新来源（双通道）**：资源站 + GitHub **都查，取版本更高的那个**（版本相同时优先资源站）。
  GitHub 上作者通常首发、更及时；国内连不上时会自动只用资源站，并提供下载兜底与二次降级。
  也可以为单个 mod **手动绑定** GitHub 仓库。
- **收藏**：把喜欢的 mod 收藏起来，随时安装 / 打开页面。
- **UMM 环境**：检测 / 安装 / 修复 / 卸载游戏加载器；内核（UMM loader）可**独立更新**并支持**回滚**。
- **更新提醒**：打开软件时自动检查已安装 mod 的更新，「已安装」导航上显示角标数量。
- **AMM 自身更新**：`设置 → 关于` 显示当前版本并可「检查更新」；同时从 **GitHub Releases** 与 **资源站工具库**
  两条通道检查，有新版本时软件顶部会提示（可「忽略此版本」），点一下直接打开下载页。
- **浅色 / 深色 / 跟随系统**三套主题。

---

## 系统要求

- Windows 10 / 11 (x64)
- 《冰与火之舞》Steam 版
- 发布包为**自包含**版本，**无需**另行安装 .NET 运行时

---

## 下载与安装

提供两种形式，任选其一：

| 形式 | 说明 |
|---|---|
| **安装包**（推荐） | 单个 `ADOFAI-Mod-Manager-Setup-x.y.z.exe`。安装时可选择：**为所有用户安装**（默认，装到 `Program Files`，需要管理员）/ **仅为我安装**（装到用户目录，无需管理员）/ **自定义路径**；自动创建开始菜单与（可选）桌面快捷方式，自带卸载程序 |
| **绿色版** | `ADOFAI-Mod-Manager-vx.y.z-win-x64.zip`，解压即用，不写注册表 |

> 程序不会向安装目录写入任何文件（配置/日志都在用户目录），因此装在 `Program Files` 下也完全正常。

本地缓存位于 `%LocalAppData%\AdofaiModManager\cache`：
`icons`（图标，7 天刷新）、`api`（接口响应，Mod 列表 10 分钟 / 详情 30 分钟）、`mods`（下载过的安装包，最多 15 个或 600MB）。
缓存只用于**减少对资源站的重复请求**（不给站长添压力）并支持**离线重装**，删除该目录不影响使用。

---

## 使用方法

1. 解压发布包，运行 `AdofaiModManager.exe`。
2. 打开**设置**：
   - 确认「游戏目录」（一般会自动识别，识别不到可手动选择）。
   - 填写资源站的 **API key**（在资源站「个人中心」创建）。
3. 打开「UMM 环境」→ 点「安装 / 修复加载器」，为游戏装上 mod 支持。
4. 回到**在线 Mod**，搜索并安装想要的 mod。
5. 在**已安装**里启用 / 禁用 / 卸载 / 更新 mod。

> 界面底部导航顺序：在线 Mod · 已安装 · 收藏 · UMM 环境 · 设置。

---

## 接入你自己的资源站

本软件**不绑定任何单一资源站**：在「设置 → 资源站」里填写**任意实现了相同接口**的站点地址即可使用。

- 接口规范（必需/可选接口、字段说明、下载流程）：[`docs/RESOURCE-SITE-API.md`](docs/RESOURCE-SITE-API.md)
- 文档里包含**最小实现示例**（Node/Express 示意，其它语言同理），站长照着实现即可提供一个新资源站。
- API key **允许留空**，方便不需要鉴权的站点。

---

## 开发 / 构建

需要 .NET SDK 10。

### 调试运行

```powershell
dotnet run --project src/AdofaiModManager
```

### 一键打包（发布版 + 安装包）

```powershell
# 会依次：发布 → 生成绿色版 zip → 用 Inno Setup 生成安装包
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 0.1.9
```

> 安装包需要 [Inno Setup](https://jrsoftware.org/isdl.php)（`winget install JRSoftware.InnoSetup`）。
> 没装也能跑，脚本会跳过安装包、只生成绿色版。

### 单独发布（绿色版）

```powershell
dotnet publish src/AdofaiModManager/AdofaiModManager.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:DebugType=none -o dist/ADOFAI-Mod-Manager
```

### 目录结构

```
src/AdofaiModManager/
  Models/      数据模型（Info.json、在线 mod、内核、更新等）
  Services/    核心服务（资源站客户端、mod 管理、loader 接管、内核更新、GitHub 更新…）
  Views/       页面与对话框
  Resources/UmmLoader/   随程序分发的 UMM 加载器组件与许可证
docs/
  SPEC.md      需求与技术方案
  API.md       资源站接口实测文档
tools/
  LoaderSmokeTest/   加载器部署 / 卸载的隔离冒烟测试
  UpdateSmokeTest/   更新源解析与检查的真实测试
```

---

## 致谢与数据来源

- **UnityModManager** —— 本项目的加载器（内核）复用自 newman55 的 [UnityModManager](https://github.com/newman55/unity-mod-manager)（MIT）。
- **ADOFAI Tools**（[adofaitools.top](https://www.adofaitools.top)）—— 默认资源站。在线 Mod 数据与下载均由该站提供，**已获得站长授权**接入第三方客户端。
- 本软件不绑定单一资源站：任何实现 [`docs/RESOURCE-SITE-API.md`](docs/RESOURCE-SITE-API.md) 接口的站点都可以被用户配置使用。

---

## 许可证

本项目以 **MIT** 许可发布，见 [`LICENSE`](LICENSE)。

### 随程序分发的第三方组件

| 组件 | 许可证 | 说明 |
|---|---|---|
| UnityModManager (`UnityModManager.dll`) | MIT © newman55 | 游戏内 mod 加载器 |
| UnityDoorstop (`winhttp_*.dll`) | **LGPL-2.1** © NeighTools | 注入代理，以未修改的独立 DLL 分发 |
| Harmony (`0Harmony.dll`) | MIT © Andreas Pardeike | |
| dnlib (`dnlib.dll`) | MIT © de4dot | |

完整许可证原文见 `src/AdofaiModManager/Resources/UmmLoader/`。

---

## 免责声明

- 使用本软件即表示你自行承担风险；请自行备份游戏存档与 mod。
- 在线功能依赖第三方资源站，其可用性与内容不由本项目控制。
- 安装 mod 前请遵守对应 mod 作者与资源站的许可与规定。
