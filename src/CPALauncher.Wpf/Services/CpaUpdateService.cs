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

    public async Task<CpaUpdateCheckResult> CheckForUpdateAsync(
        string repo,
        string? currentVersion,
        CancellationToken ct = default,
        bool requireWindowsAsset = true,
        string productName = "CPA",
        Func<string, bool>? assetNamePredicate = null,
        string assetDescription = "Windows amd64 安装包")
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{repo}/releases/latest");
            request.Headers.Add("User-Agent", "CPALauncher");
            request.Headers.Add("Accept", "application/vnd.github+json");

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return CpaUpdateCheckResult.CheckFailed(
                    $"获取最新发布信息失败（HTTP {(int)response.StatusCode} {response.ReasonPhrase ?? "Unknown"}）。");
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagNameElement))
                return CpaUpdateCheckResult.CheckFailed("最新发布信息缺少 tag_name，无法判断版本。");

            var tagName = tagNameElement.GetString();
            if (string.IsNullOrEmpty(tagName))
                return CpaUpdateCheckResult.CheckFailed("最新发布信息中的 tag_name 为空，无法判断版本。");

            var versionStr = tagName.TrimStart('v');
            if (!Version.TryParse(versionStr, out var newVersion))
                return CpaUpdateCheckResult.CheckFailed($"最新发布版本号格式无效：{tagName}。");

            if (!string.IsNullOrWhiteSpace(currentVersion))
            {
                var curVersionStr = currentVersion.TrimStart('v');
                if (!Version.TryParse(curVersionStr, out var curVersion))
                    return CpaUpdateCheckResult.CheckFailed($"当前 {productName} 版本号格式无效：{currentVersion}。");

                if (newVersion <= curVersion)
                    return CpaUpdateCheckResult.UpToDate(tagName, newVersion);
            }

            var releaseUrl = root.TryGetProperty("html_url", out var releaseUrlElement)
                ? releaseUrlElement.GetString() ?? string.Empty
                : string.Empty;

            var assetUrl = releaseUrl;
            long assetSize = 0;

            if (requireWindowsAsset)
            {
                assetUrl = null;
                assetNamePredicate ??= IsDefaultWindowsAsset;

                if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
                    return CpaUpdateCheckResult.CheckFailed($"最新发布信息缺少 assets 列表，无法定位 {assetDescription}。");

                foreach (var asset in assetsElement.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out var nameElement))
                        continue;

                    var name = nameElement.GetString();
                    if (name is not null && assetNamePredicate(name))
                    {
                        assetUrl = asset.TryGetProperty("browser_download_url", out var assetUrlElement)
                            ? assetUrlElement.GetString()
                            : null;
                        assetSize = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var size)
                            ? size
                            : 0;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(assetUrl))
                    return CpaUpdateCheckResult.CheckFailed($"最新发布中未找到可用的 {assetDescription}。");
            }

            return CpaUpdateCheckResult.UpdateAvailable(new CpaUpdateInfo
            {
                TagName = tagName,
                NewVersion = newVersion,
                AssetDownloadUrl = assetUrl,
                AssetSize = assetSize,
                ReleaseUrl = releaseUrl,
            });
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return CpaUpdateCheckResult.CheckFailed("检查更新时发生异常，无法获取最新发布信息。");
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

    private static bool IsDefaultWindowsAsset(string name)
    {
        return name.Contains("windows", StringComparison.OrdinalIgnoreCase)
               && name.Contains("amd64", StringComparison.OrdinalIgnoreCase)
               && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }
}
