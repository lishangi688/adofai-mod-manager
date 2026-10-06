# ADOFAI Mod Manager

《冰与火之舞》（A Dance of Fire and Ice）的 Windows Mod 管理器。

AMM 可以连接实现统一接口的 Mod 资源站，用于浏览、安装、更新和管理 Mod，也可以处理 UnityModManager 加载器的安装与维护。

> 本项目是第三方工具，与 7th Beat Games、UnityModManager 官方及各 Mod 资源站没有隶属关系。

## 功能

### 在线 Mod

- 浏览、搜索、排序和筛选资源站中的 Mod
- 查看版本、依赖和游戏版本兼容信息
- 一键安装指定版本
- 自动识别已安装和可更新的 Mod
- 支持从资源站或 GitHub 获取更新

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

- 收藏常用 Mod
- 启动时检查已安装 Mod 的更新
- 检查 AMM 自身更新
- 浅色、深色和跟随系统主题
- 首次使用向导
- 自定义 Mod 资源站

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
3. 在“设置 → 资源站”中配置资源站地址和 API key。
4. 打开“UMM 环境”，安装或修复加载器。
5. 回到“在线 Mod”，选择需要的 Mod 并安装。

## 资源站

AMM 不绑定某一个固定资源站。只要站点实现了项目规定的接口，就可以在“设置 → 资源站”中配置使用。

默认配置使用 [ADOFAI Tools](https://adofaitools.top/)。它提供在线 Mod 的列表、详情和下载服务，也是目前 AMM 开箱即用的数据来源。

资源站接口规范和接入示例见：

- [资源站接口规范](docs/RESOURCE-SITE-API.md)
- [资源站接入说明](docs/SITE-LISTING.md)

API key 由各资源站自行管理。AMM 不内置任何 API key，使用需要鉴权的资源站时，请在对应站点创建自己的 key。

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

