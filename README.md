<img src="src/CPALauncher.Wpf/Assets/logo.svg" width="56" alt="CPA Dock">

# CPA Dock

A Windows desktop companion for CLIProxyAPI.

[![Release](https://img.shields.io/github/v/release/ohhello-jie/cpa-dock?label=release)](https://github.com/ohhello-jie/cpa-dock/releases/latest)
[![Windows](https://img.shields.io/badge/platform-Windows-5265CE)](https://github.com/ohhello-jie/cpa-dock/releases/latest)
[![MIT](https://img.shields.io/badge/license-MIT-6B7280)](LICENSE)

[CLIProxyAPI](https://github.com/router-for-me/CLIProxyAPI) 的 Windows 桌面伴侣。启动、管理、更新，一处完成。

![CPA Dock](docs/screenshot.png)

## 日常使用

- **服务**：安装、启动、停止，打开管理页；区分托管进程和外部运行。
- **凭证**：选择或拖入 JSON 文件；覆盖同名文件、重启服务前确认。
- **诊断**：检查进程、配置和 HTTP；搜索、筛选、复制、导出日志。
- **桌面**：托盘、开机启动、自动启动 CPA、深色模式；分别更新 CPA 和启动器。

## 开始

1. 从 [Releases](https://github.com/ohhello-jie/cpa-dock/releases/latest) 下载 Windows x64 自包含包。
2. 解压，运行 `CPALauncher.exe`。无需安装 .NET。
3. 点击「安装 CPA」，或在设置中选择已有程序与 `config.yaml`。
4. 启动 CPA，打开管理页。

CPA Dock 原名 CPA Launcher。程序文件名与设置目录沿用旧名称，原有配置可继续使用。

## 文件

设置保存在 `%AppData%\CPALauncher\settings.json`；默认安装目录为程序同级 `cpa\`。凭证目录读取 CPA 的 `auth-dir`，日志目录由运行配置推导。

退出时可停止 CPA，也可保留服务。外部启动的 CPA 需要在原程序中停止或重启。

## 构建

.NET 8 · WPF · [HandyControl](https://github.com/HandyOrg/HandyControl)

```powershell
git clone https://github.com/ohhello-jie/cpa-dock.git
cd cpa-dock
dotnet test CPALauncher.slnx
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1
```

发布包位于 `artifacts\release\`。图标源文件为 `Assets/logo.svg`，运行 `scripts/build-icon.ps1` 可重新生成 PNG 与 ICO。

CPA 的 API、认证与代理能力由 [CLIProxyAPI](https://github.com/router-for-me/CLIProxyAPI) 提供。

[MIT License](LICENSE)
