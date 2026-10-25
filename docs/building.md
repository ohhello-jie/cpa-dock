# 源码构建

在 Windows 上安装 .NET 8 SDK。界面使用 WPF 和 [HandyControl](https://github.com/HandyOrg/HandyControl)。

```powershell
git clone https://github.com/ohhellojie/cpa-dock.git
cd cpa-dock
dotnet test tests/CPALauncher.Wpf.Tests/CPALauncher.Wpf.Tests.csproj -c Release
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1
```

发布包生成在 `artifacts\release\`：

- `CPA-Dock-win-x64-self-contained.zip`：Windows x64 下载包，运行 `CPA Dock.exe`。
- `CPALauncher-win-x64-self-contained.zip`：旧版自动更新兼容包，保留 `CPALauncher.exe` 文件名。
- `SHA256SUMS.txt`：两个下载包各自的校验值。

两个包使用同一程序，均包含运行时。

图标源文件位于 `src/CPALauncher.Wpf/Assets/logo.svg`，运行 `scripts/build-icon.ps1` 可生成 PNG 和 ICO。

GitHub Actions 在 Windows 上运行测试，通过后生成下载包和校验文件，构建产物保留 14 天。Release 由维护者手动发布。

## 公开内容检查

检查脚本需要 Python 3.12 或更高版本，发布脚本也会调用它。

提交或发布前执行：

```powershell
python scripts/privacy_guard.py --install-hooks
python scripts/privacy_guard.py --history
```

本地钩子检查暂存文件、提交署名和可达的 Git 历史，CI 会重复检查。提交使用公开署名和 GitHub 提供的 noreply 邮箱；新的公开署名需加入 `.privacy-policy.json` 的允许名单。

发布脚本还会检查程序和 ZIP 内部内容，无法读取或不支持的容器会中止打包。调试信息中的源码路径统一映射到 `/_/`；替换程序前先退出所有旧版实例。

图片需先检查画面，再将最终文件的 SHA-256 加入 `.privacy-policy.json`。图片改变后，摘要未经审核就不能通过检查。凭证、运行数据、客户日志和内部开发计划保存在仓库外。
