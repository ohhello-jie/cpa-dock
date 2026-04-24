# CPA Launcher

[![Release](https://img.shields.io/github/v/release/ohhellojie/cpa-dock?label=release)](https://github.com/ohhellojie/cpa-dock/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows-blue)](#)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

面向 [CLIProxyAPI (CPA)](https://github.com/router-for-me/CLIProxyAPI) 的原生 Windows 桌面启动器。它把 CPA 的安装、配置、启动、停止、状态监控、日志诊断和更新集中到一个轻量窗口里。

![CPALauncher screenshot](docs/screenshot.png)

## 为什么需要它

CPA 很适合作为本地后台服务长期运行，但手动维护路径、配置、进程、日志和更新并不顺手。CPA Launcher 做的事情很克制：不替代 CPA，不重写复杂配置编辑器，只把常用生命周期操作变成清晰的桌面入口。

## 亮点

- **开箱即用**：首次运行可自动下载 CPA，并进入配置向导。
- **状态清楚**：区分运行中、启动中、已停止和外部运行，避免误操作。
- **配置精简**：只需要关注 `cli-proxy-api.exe` 和 `config.yaml`，服务地址、日志目录、认证目录自动推导。
- **凭证导入**：将 `.json` 凭证拖入窗口即可导入当前 `auth-dir`。
- **更新集中**：在同一个版本区检查 CPALauncher 和 CPA 更新。
- **桌面友好**：支持托盘、开机启动、自动拉起服务、关闭到托盘和深色模式。

## 快速开始

1. 从 [Releases](https://github.com/ohhellojie/cpa-dock/releases/latest) 下载 `CPALauncher-win-x64-self-contained.zip`。
2. 解压到任意目录，运行 `CPALauncher.exe`。
3. 首次启动时选择是否自动安装 CPA。
4. 在配置向导中确认监听地址、端口、代理和管理密钥。
5. 回到主界面，点击「启动」即可运行 CPA，并可直接打开管理页。

发布包为自包含版本，无需额外安装 .NET 运行时。

## 功能一览

### 服务控制

- 一键启动 / 停止 CPA。
- 启动成功后可自动打开管理页。
- 支持启动器打开后自动拉起 CPA，并设置延迟。
- 可检测外部运行的 CPA 实例，避免误判为启动器托管进程。

### 配置与凭证

- 持久化保存 CPA 程序路径和 `config.yaml` 路径。
- 自动展示服务地址、管理页、日志目录和认证目录。
- 管理密钥可保存并一键复制。
- 拖拽 `.json` 凭证导入当前 `auth-dir`。
- 导入凭证后按运行状态处理生效逻辑：托管运行中自动重启，外部运行提示手动重启，已停止提示下次启动生效。

### 诊断与更新

- 实时输出启动器和 CPA 运行日志。
- 支持复制、导出和清空诊断日志。
- 支持分别检查 CPA Launcher 与 CPA 版本。
- CPA 可一键升级；启动器更新会下载更新包并自动重启完成替换。

### 桌面体验

- 系统托盘常驻，双击恢复，右键可快速启动、停止和退出。
- 关闭窗口时可选择最小化到托盘。
- 支持 Windows 登录时自动启动启动器。
- 支持浅色 / 深色主题。
- 自绘窗口最大化时会避开 Windows 任务栏。

## 文件位置

| 内容 | 位置 |
|------|------|
| 启动器设置 | `%AppData%\CPALauncher\settings.json` |
| 默认 CPA 安装目录 | 启动器同级 `cpa\` 目录 |
| 配置文件 | 用户选择的 `config.yaml` |
| 认证目录 | 由 CPA 配置中的 `auth-dir` 决定 |
| 日志目录 | 由 CPA 运行环境和配置自动推导 |

## 技术栈

| 项目 | 选型 |
|------|------|
| 平台 | Windows |
| 框架 | .NET 8 + WPF |
| UI 库 | [HandyControl](https://github.com/HandyOrg/HandyControl) |
| 托盘 | [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) |
| 架构 | MVVM |

## 从源码构建

```powershell
git clone https://github.com/ohhellojie/cpa-dock.git
cd CPALauncher

dotnet build CPALauncher.slnx
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1
```

发布产物位于：

```text
artifacts\release\CPALauncher-win-x64-self-contained.zip
```

## 说明

CPA Launcher 不是 CPA 本体，也不是 Web 管理页。CPA 的核心能力、配置语义和运行行为仍以 [CLIProxyAPI](https://github.com/router-for-me/CLIProxyAPI) 为准。

## 许可证

[MIT](LICENSE)
