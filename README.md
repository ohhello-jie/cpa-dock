# CPALauncher

`CPALauncher` 是一个原生 Windows 启动器项目，目标是把 `CLIProxyAPI` 封装成一个适合日常鼠标操作的本地工具。

## 产品定位

- 它不是 `CLIProxyAPI` 的替代品。
- 它不是 Web 管理页的替代品。
- 它不是完整配置编辑器。
- 它的职责只有一个：稳定管理 `CLIProxyAPI` 的本地运行生命周期。

一句话定义：

> 第一次配置 `cli-proxy-api.exe` 与 `config.yaml` 路径，之后用户只需要点击按钮，就能启动、停止、最小化到托盘并打开 CPA 管理页。

## 当前技术选型

- 平台：Windows
- UI：`.NET 8 + WinForms`
- 目标场景：本地单机、自用优先、原生轻量启动器

## 当前已实现

- 保存启动器自身配置到 `%AppData%\CPALauncher\settings.json`
- 选择 `cli-proxy-api.exe` 路径
- 自动尝试识别同目录 `config.yaml`
- 手动重选 `config.yaml`
- 一键启动 / 停止由启动器托管的 CPA 进程
- 托盘菜单：显示主窗口、启动、停止、打开管理页、退出
- 自动推导管理页地址、探测地址、配置目录、鉴权目录、日志目录
- 实时显示运行状态、托管 PID、最近诊断输出
- 检测“外部实例已运行但不是本启动器拉起”的场景
- 可选：启动器启动时自动拉起服务
- 可选：Windows 登录后自动启动启动器（托盘方式）
- 可选：自动拉起服务前延迟若干秒
- 可选：启动成功后自动打开管理页
- 可选：关闭窗口时最小化到托盘
- 配置向导内置帮助提示与官方文档入口
- 诊断输出支持复制与导出

## 已确认的 MVP 范围

- 首次启动向导
- 选择 `cli-proxy-api.exe` 路径
- 选择 `config.yaml` 路径
- 保存启动器自己的配置文件
- 一键启动 CPA
- 一键停止 CPA
- 最小化到系统托盘
- 恢复主窗口
- 一键打开管理页
- 一键打开配置目录
- 一键打开日志目录
- 显示运行状态

## 明确不在 v1 做的事

- 内嵌 Web 管理页
- 可视化 YAML 编辑器
- 自动下载 GitHub Release
- OAuth 登录流程内嵌
- 使用统计自动持久化
- 多实例复杂编排

## 建议目录结构

```text
CPALauncher/
├─ CPALauncher.slnx
├─ README.md
├─ docs/
│  └─ MVP.md
└─ src/
   └─ CPALauncher/
```

## 当前界面设计参考

- Fluent 2 Layout：<https://fluent2.microsoft.design/layout>
- Windows Spacing：<https://learn.microsoft.com/en-us/windows/apps/design/style/spacing>
- App Settings Guidelines：<https://learn.microsoft.com/en-us/windows/apps/design/app-settings/guidelines-for-app-settings>
- Notification Area / Tray：<https://learn.microsoft.com/en-us/windows/win32/uxguide/winenv-notification>

## 下一步建议

1. 增加“首次配置向导”而不是仅靠弹窗引导。
2. 增加“启动器退出时是否记住最近一次诊断输出”。
3. 增加“检测端口占用并给出更明确提示”。
4. 评估是否补一个“开机自启启动器”选项。

## 发布与交付

- 发布脚本：`scripts\publish-win-x64.ps1`
- 发布配置：`src\CPALauncher\Properties\PublishProfiles\SingleFile.pubxml`
- 默认模式：`win-x64` 单文件、自包含发布
- 一键发布命令：`powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1`
- 发布产物目录：`artifacts\publish\win-x64\self-contained`
- 可交付目录：`artifacts\release\CPALauncher-win-x64-self-contained`
- 压缩包：`artifacts\release\CPALauncher-win-x64-self-contained.zip`
- `bin\Debug` / `bin\Release` 属于开发构建输出，不等于最终交付；真正可分发的单文件 exe 以 `artifacts\release` 下的产物为准
