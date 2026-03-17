namespace CPALauncher.Models;

public sealed class LauncherSettings
{
    public string? ExecutablePath { get; set; }

    public string? ConfigPath { get; set; }

    public bool MinimizeToTrayOnClose { get; set; } = true;

    public bool AutoStartService { get; set; }

    public bool LaunchLauncherOnWindowsStartup { get; set; }

    public int AutoStartDelaySeconds { get; set; }

    public bool OpenManagementPageAfterStart { get; set; } = true;

    public bool UseDarkTheme { get; set; }
}
