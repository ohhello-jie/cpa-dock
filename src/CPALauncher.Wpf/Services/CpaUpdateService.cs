using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using CPALauncher.Models;

namespace CPALauncher.Services;

public sealed class CpaUpdateService
{
    private readonly HttpClient _httpClient;

    public CpaUpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CpaUpdateInfo?> CheckForUpdateAsync(string repo, string? currentVersion, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{repo}/releases/latest");
            request.Headers.Add("User-Agent", "CPALauncher");
            request.Headers.Add("Accept", "application/vnd.github+json");

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString();
            if (string.IsNullOrEmpty(tagName))
                return null;

            var versionStr = tagName.TrimStart('v');
            if (!Version.TryParse(versionStr, out var newVersion))
                return null;

            if (currentVersion is not null)
            {
                var curVersionStr = currentVersion.TrimStart('v');
                if (Version.TryParse(curVersionStr, out var curVersion) && newVersion <= curVersion)
                    return null;
            }

            var releaseUrl = root.GetProperty("html_url").GetString() ?? "";

            string? assetUrl = null;
            long assetSize = 0;

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (name is not null && name.Contains("windows") && name.Contains("amd64") && name.EndsWith(".zip"))
                {
                    assetUrl = asset.GetProperty("browser_download_url").GetString();
                    assetSize = asset.GetProperty("size").GetInt64();
                    break;
                }
            }

            if (string.IsNullOrEmpty(assetUrl))
                return null;

            return new CpaUpdateInfo
            {
                TagName = tagName,
                NewVersion = newVersion,
                AssetDownloadUrl = assetUrl,
                AssetSize = assetSize,
                ReleaseUrl = releaseUrl,
            };
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<(bool Success, string Message, string? ExePath)> InstallLatestAsync(
        string targetDirectory, CpaUpdateInfo info,
        IProgress<(long downloaded, long total)>? progress = null, CancellationToken ct = default)
    {
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"cpa-install-{Guid.NewGuid():N}.zip");

        try
        {
            Directory.CreateDirectory(targetDirectory);

            // Download zip with progress
            using var request = new HttpRequestMessage(HttpMethod.Get, info.AssetDownloadUrl);
            request.Headers.Add("User-Agent", "CPALauncher");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? info.AssetSize;
            long downloadedBytes = 0;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    downloadedBytes += bytesRead;
                    progress?.Report((downloadedBytes, totalBytes));
                }
            }

            // Extract all contents to target directory
            ZipFile.ExtractToDirectory(tempZipPath, targetDirectory, overwriteFiles: true);

            // Locate cli-proxy-api.exe
            var exePath = Directory.EnumerateFiles(targetDirectory, "cli-proxy-api.exe", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (exePath is null)
                return (false, "zip 中未找到 cli-proxy-api.exe。", null);

            return (true, $"CPA {info.TagName} 安装成功。", exePath);
        }
        catch (OperationCanceledException)
        {
            return (false, "安装已取消。", null);
        }
        catch (Exception ex)
        {
            return (false, $"安装失败：{ex.Message}", null);
        }
        finally
        {
            TryDeleteFile(tempZipPath);
        }
    }

    public async Task<(bool Success, string Message)> ApplyUpdateAsync(
        CpaUpdateInfo info, string targetExePath, IProgress<(long downloaded, long total)>? progress = null, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(targetExePath)!;
        var tempZipPath = Path.Combine(dir, $"cpa-update-{Guid.NewGuid():N}.zip");
        var backupPath = targetExePath + ".bak";

        try
        {
            // Download zip with progress
            using var request = new HttpRequestMessage(HttpMethod.Get, info.AssetDownloadUrl);
            request.Headers.Add("User-Agent", "CPALauncher");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? info.AssetSize;
            long downloadedBytes = 0;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    downloadedBytes += bytesRead;
                    progress?.Report((downloadedBytes, totalBytes));
                }
            }

            // Extract cli-proxy-api.exe from zip
            string? extractedExePath = null;
            using (var archive = ZipFile.OpenRead(tempZipPath))
            {
                var entry = archive.Entries.FirstOrDefault(e =>
                    e.Name.Equals("cli-proxy-api.exe", StringComparison.OrdinalIgnoreCase));

                if (entry is null)
                    return (false, "zip 中未找到 cli-proxy-api.exe。");

                extractedExePath = Path.Combine(dir, $"cpa-new-{Guid.NewGuid():N}.exe");
                entry.ExtractToFile(extractedExePath, overwrite: true);
            }

            // Backup old exe → replace with new
            if (File.Exists(targetExePath))
            {
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
                File.Move(targetExePath, backupPath);
            }

            try
            {
                File.Move(extractedExePath, targetExePath);
            }
            catch
            {
                // Rollback: restore from backup
                if (File.Exists(backupPath) && !File.Exists(targetExePath))
                    File.Move(backupPath, targetExePath);
                throw;
            }

            // Clean up backup and extracted temp
            TryDeleteFile(backupPath);

            return (true, $"CPA 已更新到 {info.TagName}。");
        }
        catch (OperationCanceledException)
        {
            return (false, "更新已取消。");
        }
        catch (Exception ex)
        {
            return (false, $"更新失败：{ex.Message}");
        }
        finally
        {
            TryDeleteFile(tempZipPath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }
}
