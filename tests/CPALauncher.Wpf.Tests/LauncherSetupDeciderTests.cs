using CPALauncher.Models;
using CPALauncher.Services;
using Xunit;

namespace CPALauncher.Wpf.Tests;

public sealed class LauncherSetupDeciderTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), "cpa-launcher-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ShouldRunFirstTimeSetup_WhenExecutablePathIsMissingOnDisk()
    {
        Directory.CreateDirectory(tempDirectory);

        var settings = new LauncherSettings
        {
            ExecutablePath = Path.Combine(tempDirectory, "cli-proxy-api.exe"),
        };

        var result = LauncherSetupDecider.ShouldRunFirstTimeSetup(settings);

        Assert.True(result);
    }

    [Fact]
    public void ShouldRunFirstTimeSetup_WhenExecutablePathIsBlank()
    {
        var settings = new LauncherSettings
        {
            ExecutablePath = "   ",
        };

        var result = LauncherSetupDecider.ShouldRunFirstTimeSetup(settings);

        Assert.True(result);
    }

    [Fact]
    public void ShouldNotRunFirstTimeSetup_WhenExecutableExists()
    {
        Directory.CreateDirectory(tempDirectory);
        var executablePath = Path.Combine(tempDirectory, "cli-proxy-api.exe");
        File.WriteAllText(executablePath, "stub");

        var settings = new LauncherSettings
        {
            ExecutablePath = executablePath,
        };

        var result = LauncherSetupDecider.ShouldRunFirstTimeSetup(settings);

        Assert.False(result);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
