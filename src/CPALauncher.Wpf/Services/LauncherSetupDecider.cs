using CPALauncher.Models;

namespace CPALauncher.Services;

public static class LauncherSetupDecider
{
    public static bool ShouldRunFirstTimeSetup(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return !HasInstalledExecutable(settings);
    }

    public static bool HasInstalledExecutable(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return !string.IsNullOrWhiteSpace(settings.ExecutablePath)
            && File.Exists(settings.ExecutablePath);
    }
}
