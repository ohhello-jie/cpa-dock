using CPALauncher.Services;
using Xunit;

namespace CPALauncher.Wpf.Tests;

public sealed class LauncherExecutablePathResolverTests
{
    [Fact]
    public void Resolve_UsesProcessPath_WhenAvailable()
    {
        var result = LauncherExecutablePathResolver.Resolve(
            processPath: @"C:\Apps\CPALauncher.exe",
            mainModulePath: @"C:\Fallback\CPALauncher.exe",
            baseDirectory: @"C:\Base");

        Assert.Equal(@"C:\Apps\CPALauncher.exe", result);
    }

    [Fact]
    public void Resolve_FallsBackToMainModulePath_WhenProcessPathIsMissing()
    {
        var result = LauncherExecutablePathResolver.Resolve(
            processPath: null,
            mainModulePath: @"C:\Fallback\CPALauncher.exe",
            baseDirectory: @"C:\Base");

        Assert.Equal(@"C:\Fallback\CPALauncher.exe", result);
    }

    [Fact]
    public void Resolve_FallsBackToBaseDirectoryExecutable_WhenOtherPathsAreMissing()
    {
        var result = LauncherExecutablePathResolver.Resolve(
            processPath: "",
            mainModulePath: "",
            baseDirectory: @"C:\Base");

        Assert.Equal(Path.Combine(@"C:\Base", "CPALauncher.exe"), result);
    }
}
