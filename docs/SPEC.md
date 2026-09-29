# ADOFAI Mod Manager（暂定名）— 需求与技术方案

> 版本：v0.1（2026-09-28）
> 说明：本文档是我（AI）根据你的决策 + 实际机器环境调研整理的，用来对齐目标。你随时可以改。

---

## 1. 一句话目标

做一个 **Windows 桌面软件**，用来替代 Unity Mod Manager（UMM），为《冰与火之舞》(A Dance of Fire and Ice, ADOFAI) 提供
**在线浏览/下载/安装/卸载/更新 mod** 的能力，并且界面现代化、全中文。

---

## 2. 需求清单（已确认）

### 2.1 核心
| 编号 | 需求 | 说明 |
|---|---|---|
| R1 | 独立软件 | 不是 UMM 的插件，是一个自己的 exe |
| R2 | 完全取代 UMM | 自动接管机器上已有的 UMM 安装；用户以后只开这一个软件 |
| R3 | 在线 mod 站 | 对接 `adofaitools.top`，**用户自己填 API key** |
| R4 | 在线操作 | 浏览、搜索、下载、安装、卸载、更新 |
| R5 | GitHub 更新通道 | 每个 mod 可手动绑定一个 GitHub 仓库；资源站没及时更新时，从 GitHub 拿最新版 |
| R6 | 本地 zip 安装 | 支持用户手动选一个本地 zip 安装（资源站可能不全） |
| R7 | 收藏 | 收藏 mod，单独查看 |
| R8 | 更新提醒 | **打开软件时**检查一次并提示 |
| R9 | 现代 UI | 替代 UMM 老旧的 WinForms 界面 |
| R10 | 中文界面 | 仅中文 |
| R11 | 游戏版本兼容提示 | 读取 mod 要求的游戏版本，提示可能不兼容 |

### 2.2 明确**不做**
- ❌ 一键更新全部（最新版不一定稳定，且 mod 常绑定游戏版本，交给用户自己判断）
- ❌ 支持其它游戏（只服务 ADOFAI）
- ❌ 后台常驻定时检查更新（只在打开时检查）

---

## 3. 实际环境事实（已在你机器上核实）

| 项 | 值 |
|---|---|
| 游戏安装路径 | `D:\SteamLibrary\steamapps\common\A Dance of Fire and Ice` |
| Steam AppID | `977950` |
| 另一份残缺副本 | `C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice`（只有一个 Mods 文件夹，忽略） |
| 已装 UMM | **0.32.5（newman55 官方，MIT）** |
| UMM 注入方式 | DoorstopProxy：游戏根目录 `winhttp.dll` + `doorstop_config.ini` |
| 注入目标 | `A Dance of Fire and Ice_Data\Managed\UnityModManager\UnityModManager.dll` |
| loader 目录内容 | `UnityModManager.dll`、`0Harmony.dll`、`dnlib.dll`、`UnityModManager.xml`、`Config.xml`、`Params.xml`、`Log.txt` |
| mod 存放 | `<游戏目录>\Mods\<ModId>\`，里面有 `Info.json` |
| mod 安装本质 | 把 UMM 格式 zip 解压到 `Mods\<ModId>\`；卸载 = 删文件夹 |
| 现有在线仓库 | `https://ummrepo.ych.yqloss.net/`（YCH），**目前报 TLS 错误，实际是坏的** |

### Info.json 关键字段（决定功能可行性）
```jsonc
{
  "Id": "AdofaiTweaks",
  "DisplayName": "ADOFAI Tweaks",
  "Author": "PizzaLovers007 & CrackThrough",
  "Version": "2.9.3",
  "ManagerVersion": "0.22.14.0",     // 需要的 UMM 最低版本
  "GameVersion": "...",              // 适配的游戏版本  ← 用于兼容提示
  "Requirements": [ ... ],           // 依赖            ← 用于依赖提示
  "LoadAfter": [ ... ],              // 加载顺序
  "AssemblyName": "AdofaiTweaks.dll",
  "EntryMethod": "AdofaiTweaks.Startup.Load",
  "HomePage": "https://github.com/PizzaLovers007/AdofaiTweaks",
  "Repository": "https://raw.githubusercontent.com/PizzaLovers007/AdofaiTweaks/master/Repository.json"
}
```

> 💡 重要发现：`Repository` 字段本身就是"从 GitHub 更新"的标准机制。例如 AdofaiTweaks、CreplayMod 都已自带 GitHub 的 `Repository.json`。我们直接复用这个，比重新发明更可靠。

### GitHub 更新设计（两层）
1. 优先读 mod 自带的 `Repository`（一个 `Repository.json`，格式 `{"Releases":[{"Id","Version","DownloadUrl"}]}`）
2. 没有 `Repository` 时，退回 **GitHub Releases API**：取最新 Release → 自动挑最像 mod 的 zip（识别不准时允许用户手动选，并记住选择）

---

## 4. 技术方案

### 4.1 技术栈
- **语言/框架**：C# / **.NET（WPF 桌面）**，Windows 11 Fluent 风格
- **UI 库**：WPF-UI（Fluent 主题，Mica 材质、现代控件）
- **MVVM**：CommunityToolkit.Mvvm
- **HTTP**：`HttpClient`（天然支持现代 TLS，顺便修复 UMM 的 TLS 报错）
- **JSON**：`System.Text.Json`
- **压缩**：`System.IO.Compression`
- **本地数据**：`%AppData%\AdofaiModManager\`（配置、收藏、API key、GitHub 绑定）

### 4.2 "完全取代 UMM" 怎么实现
不重写 loader，而是**复用 UMM 官方 MIT 的现成组件**：
- 打包官方 0.32.5 的 `UnityModManager.dll`(net35) + `0Harmony.dll` + `dnlib.dll` + `winhttp_x64.dll/x86.dll` + `doorstop_config.ini` 模板
- 部署 = 把 loader 复制到 `_Data\Managed\UnityModManager\`，把 `winhttp.dll` + `doorstop_config.ini` 放到游戏根目录
- 卸载 = 还原以上文件（并处理 UMM 曾经"Assembly 方式"留下的 `.original_` 备份，做干净还原）
- 首次运行：检测已有 UMM 安装并"接管"，不重复注入

### 4.3 主要页面（初稿）
1. **首页/在线 mod**：搜索 + 分类/排序 + mod 卡片（图标、名称、作者、版本、下载量）
2. **mod 详情**：介绍、更新日志、依赖、兼容性、下载/安装/更新按钮、绑定 GitHub、收藏
3. **已安装**：启用/禁用、卸载、更新、打开文件夹、版本回退
4. **收藏**
5. **设置**：游戏路径、API key、GitHub Token（可选，提升限额）、更新检查、关于/开源许可

---

## 5. 许可证与发布

| 组件 | 许可证 | 我们的义务 |
|---|---|---|
| UMM 0.32.5 | MIT（© 2018 newman55） | 保留版权 + 许可文本，注明"基于 UnityModManager" |
| dnlib / Harmony | MIT | 保留声明 |
| Ionic.Zip (DotNetZip) | MS-PL | 保留声明 |
| UnityDoorstop (winhttp) | MIT | 保留声明 |
| 本软件 | 由你决定（建议 MIT 或 GPL） | — |

- 发布前建议**换一个自己的名字**，避免与官方 UMM 混淆
- ⚠️ **必须提前拿到 `adofaitools.top` 站长对"第三方客户端调用接口 + 公开分发"的许可**，否则接口随时可能被封
- ⚠️ 公开软件里**绝不内置你的 API key**，改为让每个用户自己填（当前方案已如此）

---

## 6. 分阶段计划

| 阶段 | 内容 | 状态 |
|---|---|---|
| P0 | 环境搭建（.NET SDK 10.0.401 + Git 2.55） | ✅ 完成 |
| P1 | 项目骨架 + 现代 UI 外壳 + 游戏路径自动识别 | ✅ 完成 |
| P2 | mod 本地管理（列出已装 mod、本地 zip 安装、卸载、启用/禁用） | ✅ 完成 |
| P3 | loader 接管（检测/部署/卸载 UMM loader）+ 内核独立更新通道 + 回滚 | ✅ 完成 |
| P4 | GitHub 更新通道（Repository.json + Releases API + 手动绑定） | ✅ 完成 |
| P5 | 资源站对接（API key、浏览、搜索、下载安装、更新提醒） | ✅ 完成（更新提醒在 P6） |
| P6 | 收藏、兼容性提示、打磨、打包安装器、许可页 | ✅ 完成 |
| P6.5 | 首次使用向导（游戏目录 / 加载器 / 资源站，每步可跳过） | ✅ 完成 |
| P7 | 发布（含站长授权确认） | ⬜ 待做 |

### 发布形式（决策）

- **安装包（默认）**：单个 `ADOFAI-Mod-Manager-Setup-x.y.z.exe`（Inno Setup 制作）。
  - 安装向导第一步可选：**为所有用户安装**（默认，`Program Files`，需要管理员）/ **仅为我安装**（用户目录，免管理员），两种都支持**自定义路径**。
  - 自带开始菜单快捷方式、可选桌面快捷方式、卸载程序、许可协议页。
- **绿色版**：`ADOFAI-Mod-Manager-vx.y.z-win-x64.zip`，解压即用（给不想安装的人）。
- 一键打包：`installer/build-installer.ps1`（发布 → 绿色版 zip → 安装包）。
- ⚠️ 因为默认可能装到 `Program Files`（只读），**程序不向安装目录写任何文件**：
  配置/收藏/更新源在 `%AppData%\AdofaiModManager`，**日志在 `%LocalAppData%\AdofaiModManager\logs`**。
- 安装包里的中文界面使用社区翻译 `ChineseSimplified.isl`（随安装器分发）。

> P1/P2 完成后已具备：现代 Fluent 界面、导航、设置页（游戏目录自动/手动、API 地址与 key）、
> 已安装页（扫描 `Mods`、启用/禁用走 `Params.xml`、本地 zip 安装、卸载、打开文件夹）。

### 关于「内核」与「外壳」（P3 设计决策）

| | 说明 | 更新方式 |
|---|---|---|
| 外壳 | 本软件（界面 / 在线站 / GitHub 更新 / mod 管理） | 自身版本更新：打开时检查 GitHub 新版本，提示并打开发布页 |
| 内核 | UMM loader（`UnityModManager.dll` + doorstop），负责游戏内注入与加载 mod | **独立更新**：内置稳定内核兜底 + 自建清单更新通道 + 支持回滚 |

- 内核更新清单（`manifest.json`）曾计划由我们自建托管；**现已移除**（设置项与「检查内核更新」按钮一并删除），
  内核更新改为两条路：① 从资源站「工具库」获取最新 UMM；② 用户导入本地下载的 UMM 压缩包。
- 升级内核前**自动备份当前内核**，支持一键回滚（应对游戏更新后内核出问题的场景）。
- 软件内置一个已验证的稳定内核，保证离线可用、随时可"安装/修复"。

#### 注入方式（决策）
- **只实现 DoorstopProxy**（放 `winhttp.dll` + `doorstop_config.ini`，由 UnityDoorstop 调用加载器），这是 UMM 官方推荐方式。
- **不实现 Assembly 方式**（用 dnlib 直接修改游戏本体托管 DLL）。原因：需要移植官方 `Console/Main.cs` 中数百行补丁逻辑，会改动游戏本体、有破坏风险，且 UMM 官方也说明该方式在游戏更新后会失效。
- 界面上「UMM 环境」页展示当前注入方式，不提供方式选择。

> P5 之前的所有工作都**不依赖**资源站，可以现在就开工。

---

## 7. 未决事项

1. ✅ 资源站 API 已实测通过，详见 [`docs/API.md`](API.md)（base URL：`https://www.adofaitools.top`，鉴权：`Authorization: Bearer`）
2. ⬜ 站长对第三方调用/公开分发的授权
3. ⬜ 软件正式名字（暂定 ADOFAI Mod Manager）
4. ⬜ 本软件自己的开源许可证与是否公开源码
5. ⬜ 分发方式（NSIS 安装包 / 绿色版 zip）
6. ⬜ 是否处理 BepInEx 遗留（游戏目录里有个空的 `BepInEx\plugins\CheryTools`）

---

## 8. 风险

| 风险 | 影响 | 应对 |
|---|---|---|
| 资源站接口没有文档 | 无法对接 | 用 key 实测接口 + 参考 ADOFAI-Tools 源码反推 |
| GitHub Release 命名不统一 | 自动识别挑错文件 | 允许手动指定并记住 |
| UMM 曾用 Assembly 方式**改过**游戏托管 DLL | 卸载不干净可能崩游戏 | 严格备份/还原，先只做 Doorstop 接管 |
| 游戏更新导致 mod 失效 | 用户困惑 | 兼容性提示 + 保留旧版本回退 |
| 公开发布未授权调用接口 | 被站长封禁 | 发布前取得授权 |
