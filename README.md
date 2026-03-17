# CPA Launcher

原生 Windows 桌面启动器，用于管理 [CLIProxyAPI](https://github.com/user/CLIProxyAPI) 的本地运行生命周期。

> 配置一次 `cli-proxy-api.exe` 与 `config.yaml` 路径，之后只需点击按钮即可启动、停止、最小化到托盘并打开管理页。

## 定位

CPA Launcher **不是** CPA 的替代品、Web 管理页的替代品或配置编辑器。
它的职责只有一个：**让 CPA 在本地像一个普通桌面应用一样运行**。

## 技术栈

| 项目 | 选型 |
|------|------|
| 平台 | Windows |
| 框架 | .NET 8 + WPF |
| UI 库 | [HandyControl](https://github.com/HandyOrg/HandyControl) |
| 托盘 | [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) |
| 架构 | MVVM |

## 功能

**核心**

- 一键启动 / 停止 CPA 进程
- 系统托盘常驻（双击恢复、右键菜单操作）
- 实时状态监控（运行中 / 启动中 / 已停止 / 外部运行）
- 诊断输出面板（复制、导出、清空）

**配置管理**

- 选择 `cli-proxy-api.exe` 和 `config.yaml` 路径
- 自动推导管理页 URL、探针 URL、配置 / 日志 / 鉴权目录
- 设置持久化到 `%AppData%\CPALauncher\settings.json`

**自动化选项**

- 启动器打开时自动拉起服务（可设延迟）
- Windows 登录时自动启动启动器
- 服务就绪后自动打开管理页
- 关闭窗口时最小化到托盘

**外观**

- 深色 / 浅色主题切换

## 发布

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1
```

产物为 `win-x64` 自包含单文件 exe，输出到 `artifacts\release\` 目录。

## 项目结构

```text
CPALauncher/
├─ scripts/                  # 发布脚本
├─ src/
│  └─ CPALauncher.Wpf/       # WPF 主项目
│     ├─ Models/              # 数据模型
│     ├─ Services/            # 进程管理、配置检查等服务
│     ├─ ViewModels/          # MVVM ViewModel
│     └─ Views/               # XAML 视图
└─ README.md
```

## 不在当前版本范围

- 内嵌 Web 管理页
- 可视化 YAML 编辑器
- 自动下载 GitHub Release
- 多实例编排
