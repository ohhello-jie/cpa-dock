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
