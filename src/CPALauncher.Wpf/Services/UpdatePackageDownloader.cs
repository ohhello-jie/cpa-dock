using System.Net.Http;
using CPALauncher.Models;

namespace CPALauncher.Services;

internal static class UpdatePackageDownloader
{
    internal static async Task DownloadAsync(HttpClient client, CpaUpdateInfo info, string destination,
        IProgress<(long downloaded, long total)>? progress, CancellationToken ct)
    {
        // ResponseHeadersRead only applies HttpClient.Timeout through receipt of the headers.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var token = timeout.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, info.AssetDownloadUrl);
        request.Headers.Add("User-Agent", "CPALauncher");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var expectedLength = response.Content.Headers.ContentLength;
        var total = expectedLength ?? info.AssetSize;
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        long downloaded = 0;
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            downloaded += count;
            progress?.Report((downloaded, total));
        }
        token.ThrowIfCancellationRequested();
        if ((expectedLength.HasValue && downloaded != expectedLength.Value) ||
            (info.AssetSize > 0 && downloaded != info.AssetSize))
            throw new InvalidDataException("更新包下载不完整，请重试。");
    }
}
