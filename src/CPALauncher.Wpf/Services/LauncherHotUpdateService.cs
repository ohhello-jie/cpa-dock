using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using CPALauncher.Models;

namespace CPALauncher.Services;

public sealed class LauncherHotUpdateService
{
    private const string LauncherExecutableName = "CPALauncher.exe";
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

            await DownloadPackageAsync(info, zipPath, progress, ct);
            ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);

            var packageExecutablePath = Directory
                .EnumerateFiles(extractRoot, LauncherExecutableName, SearchOption.AllDirectories)
                .FirstOrDefault();

            if (packageExecutablePath is null)
            {
                TryDeleteDirectory(updateRoot);
                return LauncherHotUpdatePreparationResult.Failed("启动器更新包中未找到 CPALauncher.exe。");
            }

            var packageDirectory = Path.GetDirectoryName(packageExecutablePath)!;
            var installDirectory = Path.GetDirectoryName(Path.GetFullPath(currentExecutablePath))!;

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
            return LauncherHotUpdatePreparationResult.Failed("启动器热更新已取消。");
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

            var updaterProcess = Process.Start(startInfo);
            return updaterProcess is null
                ? LauncherHotUpdateStartResult.Failed("热更新辅助进程启动失败。")
                : LauncherHotUpdateStartResult.Succeeded();
        }
        catch (Exception ex)
        {
            return LauncherHotUpdateStartResult.Failed($"热更新辅助进程启动失败：{ex.Message}");
        }
    }

    private async Task DownloadPackageAsync(
        CpaUpdateInfo info,
        string zipPath,
        IProgress<(long downloaded, long total)>? progress,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, info.AssetDownloadUrl);
        request.Headers.Add("User-Agent", "CPALauncher");

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? info.AssetSize;
        long downloadedBytes = 0;

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloadedBytes += bytesRead;
            progress?.Report((downloadedBytes, totalBytes));
        }
    }

    internal static string BuildApplyScriptContent()
    {
        return """
            param(
                [Parameter(Mandatory = $true)][int]$ProcessId,
                [Parameter(Mandatory = $true)][string]$PackageDirectory,
                [Parameter(Mandatory = $true)][string]$InstallDirectory,
                [Parameter(Mandatory = $true)][string]$LauncherPath
            )

            $ErrorActionPreference = "Stop"
            $LogPath = Join-Path (Split-Path -Parent $PackageDirectory) "apply-launcher-update.log"

            function Write-UpdateLog {
                param([string]$Message)
                $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
                Add-Content -LiteralPath $LogPath -Value "[$timestamp] $Message" -Encoding UTF8
            }

            try {
                Write-UpdateLog "Waiting for CPALauncher process $ProcessId to exit."
                try {
                    Wait-Process -Id $ProcessId -Timeout 90 -ErrorAction SilentlyContinue
                }
                catch {
                    Write-UpdateLog "Wait-Process finished with warning: $($_.Exception.Message)"
                }

                Start-Sleep -Milliseconds 600

                $packageExe = Join-Path $PackageDirectory "CPALauncher.exe"
                if (-not (Test-Path -LiteralPath $packageExe)) {
                    throw "CPALauncher.exe was not found in update package."
                }

                Write-UpdateLog "Copying update payload to $InstallDirectory."
                Get-ChildItem -LiteralPath $PackageDirectory -Force | ForEach-Object {
                    Copy-Item -LiteralPath $_.FullName -Destination $InstallDirectory -Recurse -Force
                }

                Write-UpdateLog "Restarting CPALauncher from $LauncherPath."
                Start-Process -FilePath $LauncherPath -WorkingDirectory $InstallDirectory

                try {
                    Remove-Item -LiteralPath $PackageDirectory -Recurse -Force -ErrorAction SilentlyContinue
                    Remove-Item -LiteralPath (Join-Path (Split-Path -Parent $PackageDirectory) "package.zip") -Force -ErrorAction SilentlyContinue
                }
                catch {
                    Write-UpdateLog "Cleanup skipped: $($_.Exception.Message)"
                }
            }
            catch {
                Write-UpdateLog "Update failed: $($_.Exception.Message)"
                exit 1
            }
            """;
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
