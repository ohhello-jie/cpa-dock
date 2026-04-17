using Microsoft.Win32;

namespace CPALauncher.Services;

public sealed class WindowsStartupManager
{
    private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CPALauncher";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunRegistryPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户的开机启动注册表项。");

        if (!enabled)
        {
            if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return;
        }

        key.SetValue(ValueName, BuildLaunchCommand(), RegistryValueKind.String);
    }

    public string BuildLaunchCommand()
    {
        var executablePath = LauncherExecutablePathResolver.ResolveCurrentProcessPath();
        return $"\"{executablePath}\" --minimized";
    }
}
