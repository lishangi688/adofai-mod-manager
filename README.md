# ADOFAI Mod Manager

《冰与火之舞》（A Dance of Fire and Ice）的 Windows Mod 管理器。

AMM 可以连接实现统一接口的 Mod 资源站，也能使用 TUF、modlist 等公开来源，用于浏览、安装、更新和管理 Mod，也可以处理 UnityModManager 加载器的安装与维护。

> 本项目是第三方工具，与 7th Beat Games、UnityModManager 官方及各 Mod 资源站没有隶属关系。

## 功能

### 在线 Mod

- 支持多个来源：ADOFAITools、TUF、modlist，以及自定义站点，可随时切换
- 浏览、搜索、排序和分类筛选各来源中的 Mod
- 查看版本和游戏版本兼容信息
- 一键安装指定版本
- 自动识别已安装和可更新的 Mod
- 需要 MelonLoader 的 Mod 会被明确标注（AMM 暂不支持自动安装）
- 安装前会校验下载内容，避免把项目主页当成压缩包
- 支持从来源站或 GitHub 获取更新

### 已安装 Mod

- 启用或禁用 Mod
- 更新、卸载和打开 Mod 文件夹
- 从本地 ZIP 文件安装 Mod
- 查看依赖关系和兼容性提示
- 检测重复安装，减少更新后留下旧副本的情况

### UMM 环境

- 检查游戏是否安装 UnityModManager 加载器
- 安装、修复和卸载加载器
- 单独更新 UMM loader
- 在不同 loader 版本之间回滚
- 支持内置文件、资源站下载和本地导入

### 其他功能

- 收藏常用 Mod（不限来源）
- 启动时检查已安装 Mod 的更新（同时查询所有来源）
- 检查 AMM 自身更新
- 浅色、深色和跟随系统主题
- 首次使用向导
- 默认 Mod 站点与自定义站点

## 系统要求

- Windows 10 / 11（x64）
- Steam 版《冰与火之舞》
- 发布包为自包含版本，无需另行安装 .NET 运行时

## 安装

前往 [Releases](../../releases) 页面下载：

| 版本 | 说明 |
| --- | --- |
| 安装包 | 可选择安装范围和安装目录，并自动创建快捷方式 |
| 绿色版 ZIP | 解压后直接运行，不写入注册表 |

程序不会把配置、日志和缓存写入安装目录。安装到 `Program Files` 后也可以正常使用。

## 第一次使用

1. 运行 `AdofaiModManager.exe`。
2. 在“设置”中确认游戏目录。
3. 在“设置 → 来源”里查看可用的 Mod 来源（默认 ADOFAITools，需要时填 API key）；具体用哪个来源在“在线 Mod”页右上角切换。
4. 打开“UMM 环境”，安装或修复加载器。
5. 回到“在线 Mod”，选择需要的 Mod 并安装。

## 来源

AMM 内置多个 Mod 来源：默认站点与自定义站点在“设置”里配置，日常使用在「在线 Mod」页右上角切换。

| 来源 | 说明 |
| --- | --- |
| [ADOFAI Tools](https://adofaitools.top/) | 默认来源；需要 API key（在站点「个人中心」创建） |
| [TUF](https://tuforums.com/mods) | 免 key，Mod 数量最多 |
| [modlist](https://modlist.org) | 免 key，公开 API |
| 自定义站点 | 任何实现相同接口的站点，在「设置 → 自定义站点」里填地址即可 |

AMM 不绑定某一个固定资源站。只要站点实现了项目规定的接口，就可以在“设置”中配置使用。

资源站接口规范和接入示例见：

- [资源站接口规范](docs/RESOURCE-SITE-API.md)

站点里需要鉴权的部分，API key 由各资源站自行管理。AMM 不内置任何 API key，请在上表中对应的站点创建自己的 key。Mod 下载一律通过各来源自己的下载端点完成，并带有本地缓存。

## 开发

项目使用 .NET 10。

调试运行：

```powershell
dotnet run --project src/AdofaiModManager
```

构建发布包：

```powershell
powershell -ExecutionPolicy Bypass `
  -File installer/build-installer.ps1 `
  -Version 0.1.5
```

默认生成绿色版 ZIP。需要生成安装包时，加上 `-Installer` 参数。

## 致谢

感谢 [ADOFAI Tools](https://adofaitools.top/) 站长 [small-lizi](https://github.com/small-lizi/) 允许 AMM 接入资源站，并提供在线 Mod 数据和下载服务。

ADOFAI Tools 的项目代码和相关实现见其
[GitHub 仓库](https://github.com/small-lizi/ADOFAI-Tools)。

感谢 [TUF（The Universal Forums）](https://tuforums.com/mods) 提供 Mod 目录与下载服务。

感谢 [modlist.org](https://modlist.org)（作者 [square3ang](https://github.com/modlist-org)）提供公开 API 并允许 AMM 接入。

感谢 [UnityModManager](https://github.com/newman55/unity-mod-manager) 项目及其作者 [newman55](https://github.com/newman55/) 提供的加载器基础。

## 许可证

本项目以 MIT 许可证发布。

随程序分发的第三方组件及其许可证，见：

```text
src/AdofaiModManager/Resources/UmmLoader/
```

## 免责声明

- 使用本软件的风险由用户自行承担。
- 在线功能依赖第三方资源站，其可用性和内容由对应站点负责。
- 安装 Mod 前请遵守 Mod 作者和资源站的许可与使用规定。
- 使用前建议备份游戏文件、存档和 Mod。

