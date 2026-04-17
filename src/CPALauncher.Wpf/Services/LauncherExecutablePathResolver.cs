using System.Diagnostics;

namespace CPALauncher.Services;

public static class LauncherExecutablePathResolver
{
    public static string Resolve(
        string? processPath = null,
        string? mainModulePath = null,
        string? baseDirectory = null,
        string executableName = "CPALauncher.exe")
    {
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return processPath;
        }

        if (!string.IsNullOrWhiteSpace(mainModulePath))
        {
            return mainModulePath;
        }

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            return Path.Combine(baseDirectory, executableName);
        }

        throw new InvalidOperationException("无法确定当前启动器 exe 路径，因此不能注册开机自启。");
    }

    public static string ResolveCurrentProcessPath()
    {
        var mainModulePath = default(string);

        try
        {
            mainModulePath = Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            // Ignore and continue to the AppContext.BaseDirectory fallback.
        }

        return Resolve(
            processPath: Environment.ProcessPath,
            mainModulePath: mainModulePath,
            baseDirectory: AppContext.BaseDirectory);
    }
}
