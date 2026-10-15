using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using CPALauncher.Models;

namespace CPALauncher.Services;

public sealed class LauncherHotUpdateService
{
    private static readonly string[] LauncherExecutableNames = ["CPA Dock.exe", "CPALauncher.exe"];
    private readonly HttpClient httpClient;

    public LauncherHotUpdateService(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<LauncherHotUpdatePreparationResult> PrepareHotUpdateAsync(
        CpaUpdateInfo info,
        string currentExecutablePath,
        IProgress<(long downloaded, long total)>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(currentExecutablePath) || !File.Exists(currentExecutablePath))
        {
            return LauncherHotUpdatePreparationResult.Failed("找不到当前启动器 exe，无法执行热更新。");
        }

        var updateRoot = Path.Combine(Path.GetTempPath(), $"cpa-launcher-hot-update-{Guid.NewGuid():N}");
        var zipPath = Path.Combine(updateRoot, "package.zip");
        var extractRoot = Path.Combine(updateRoot, "package");
        var applyScriptPath = Path.Combine(updateRoot, "apply-launcher-update.ps1");

        try
        {
            Directory.CreateDirectory(updateRoot);
            Directory.CreateDirectory(extractRoot);

            await UpdatePackageDownloader.DownloadAsync(httpClient, info, zipPath, progress, ct);
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true), ct);

            var packageExecutablePath = LauncherExecutableNames
                .SelectMany(name => Directory.EnumerateFiles(extractRoot, name, SearchOption.AllDirectories))
                .FirstOrDefault();

            if (packageExecutablePath is null)
            {
                TryDeleteDirectory(updateRoot);
                return LauncherHotUpdatePreparationResult.Failed("启动器更新包中未找到 CPA Dock.exe 或 CPALauncher.exe。");
            }

            var packageDirectory = Path.GetDirectoryName(packageExecutablePath)!;
            var installDirectory = Path.GetDirectoryName(Path.GetFullPath(currentExecutablePath))!;

            // Fail before the running launcher exits if its directory is read-only.
            var writeProbe = Path.Combine(installDirectory, $".cpa-write-{Guid.NewGuid():N}");
            using (new FileStream(writeProbe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1, FileOptions.DeleteOnClose)) { }

            await File.WriteAllTextAsync(applyScriptPath, BuildApplyScriptContent(), Encoding.UTF8, ct);

            return LauncherHotUpdatePreparationResult.Succeeded(new LauncherHotUpdatePackage(
                info.TagName,
                packageDirectory,
                installDirectory,
                Path.GetFullPath(currentExecutablePath),
                applyScriptPath));
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(updateRoot);
            return LauncherHotUpdatePreparationResult.Failed(ct.IsCancellationRequested
                ? "启动器热更新已取消。" : "启动器更新下载超时，请重试。");
        }
        catch (Exception ex)
        {
            TryDeleteDirectory(updateRoot);
            return LauncherHotUpdatePreparationResult.Failed($"启动器热更新准备失败：{ex.Message}");
        }
    }

    public LauncherHotUpdateStartResult StartHotUpdate(LauncherHotUpdatePackage package)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = package.InstallDirectory,
            };

            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(package.ApplyScriptPath);
            startInfo.ArgumentList.Add("-ProcessId");
            startInfo.ArgumentList.Add(process.Id.ToString());
            startInfo.ArgumentList.Add("-PackageDirectory");
            startInfo.ArgumentList.Add(package.PackageDirectory);
            startInfo.ArgumentList.Add("-InstallDirectory");
            startInfo.ArgumentList.Add(package.InstallDirectory);
            startInfo.ArgumentList.Add("-LauncherPath");
            startInfo.ArgumentList.Add(package.LauncherExecutablePath);

            using var updaterProcess = Process.Start(startInfo);
            return updaterProcess is null
                ? LauncherHotUpdateStartResult.Failed("热更新辅助进程启动失败。")
                : LauncherHotUpdateStartResult.Succeeded();
        }
        catch (Exception ex)
        {
            return LauncherHotUpdateStartResult.Failed($"热更新辅助进程启动失败：{ex.Message}");
        }
    }

    internal static string BuildApplyScriptContent()
    {
        using var stream = typeof(LauncherHotUpdateService).Assembly.GetManifestResourceStream(
            "CPALauncher.Services.ApplyLauncherUpdate.ps1")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }
}

public sealed record LauncherHotUpdatePackage(
    string VersionTag,
    string PackageDirectory,
    string InstallDirectory,
    string LauncherExecutablePath,
    string ApplyScriptPath);

public sealed record LauncherHotUpdatePreparationResult(
    bool Success,
    string Message,
    LauncherHotUpdatePackage? Package)
{
    public static LauncherHotUpdatePreparationResult Succeeded(LauncherHotUpdatePackage package)
        => new(true, "启动器热更新包已准备完成。", package);

    public static LauncherHotUpdatePreparationResult Failed(string message)
        => new(false, message, null);
}

public sealed record LauncherHotUpdateStartResult(bool Success, string Message)
{
    public static LauncherHotUpdateStartResult Succeeded()
        => new(true, "启动器热更新辅助进程已启动。");

    public static LauncherHotUpdateStartResult Failed(string message)
        => new(false, message);
}
