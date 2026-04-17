using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CPALauncher.Services;

public static class CpaConfigGenerator
{
    public static string WriteDefaultConfig(string configPath, string host, int port, string? proxyUrl, string? secretKey)
    {
        var effectiveSecretKey = ResolveManagementSecretKey(secretKey);
        var sb = new StringBuilder();

        sb.AppendLine("# Server host/interface to bind to.");
        sb.AppendLine("# Use \"127.0.0.1\" or \"localhost\" to restrict access to local machine only.");
        sb.AppendLine("# Use \"0.0.0.0\" to bind all interfaces.");
        sb.AppendLine($"host: \"{host}\"");

        sb.AppendLine("# Server port");
        sb.AppendLine($"port: {port}");

        sb.AppendLine("# TLS settings for HTTPS");
        sb.AppendLine("tls:");
        sb.AppendLine("  enable: false");
        sb.AppendLine("  cert: \"\"");
        sb.AppendLine("  key: \"\"");

        sb.AppendLine("# Management API settings");
        sb.AppendLine("remote-management:");
        sb.AppendLine("  allow-remote: false");
        sb.AppendLine($"  secret-key: \"{effectiveSecretKey}\"");
        sb.AppendLine("  disable-control-panel: false");
        sb.AppendLine($"  panel-github-repository: \"{LauncherSetupDefaults.DefaultPanelGitHubRepository}\"");

        sb.AppendLine("# Authentication directory");
        var authDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cli-proxy-api").Replace("\\", "\\\\");
        sb.AppendLine($"auth-dir: \"{authDir}\"");

        sb.AppendLine("# API keys for authentication");
        sb.AppendLine("api-keys: []");

        sb.AppendLine("# Enable debug logging");
        sb.AppendLine("debug: false");

        sb.AppendLine("# pprof HTTP debug server");
        sb.AppendLine("pprof:");
        sb.AppendLine("  enable: false");
        sb.AppendLine("  addr: \"127.0.0.1:8316\"");

        sb.AppendLine("commercial-mode: false");

        sb.AppendLine("# When true, write application logs to rotating files instead of stdout");
        sb.AppendLine("logging-to-file: true");
        sb.AppendLine("logs-max-total-size-mb: 512");
        sb.AppendLine("error-logs-max-files: 10");

        sb.AppendLine("# In-memory usage statistics aggregation");
        sb.AppendLine("usage-statistics-enabled: true");

        sb.AppendLine("# Proxy URL. Supports socks5/http/https protocols.");
        if (!string.IsNullOrWhiteSpace(proxyUrl))
            sb.AppendLine($"proxy-url: \"{proxyUrl}\"");
        else
            sb.AppendLine($"proxy-url: \"{LauncherSetupDefaults.DefaultProxyUrl}\"");

        sb.AppendLine("force-model-prefix: false");
        sb.AppendLine("passthrough-headers: false");

        sb.AppendLine("# Number of times to retry a request");
        sb.AppendLine("request-retry: 3");
        sb.AppendLine("max-retry-credentials: 0");
        sb.AppendLine("max-retry-interval: 30");

        sb.AppendLine("# Quota exceeded behavior");
        sb.AppendLine("quota-exceeded:");
        sb.AppendLine("  switch-project: true");
        sb.AppendLine("  switch-preview-model: true");

        sb.AppendLine("# Routing strategy: round-robin, fill-first");
        sb.AppendLine("routing:");
        sb.AppendLine("  strategy: \"round-robin\"");

        sb.AppendLine("ws-auth: false");
        sb.AppendLine("nonstream-keepalive-interval: 0");

        sb.AppendLine("codex-api-key: []");
        sb.AppendLine("openai-compatibility: []");

        var dir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(configPath, sb.ToString(), new UTF8Encoding(false));
        return effectiveSecretKey;
    }

    private static string ResolveManagementSecretKey(string? secretKey)
    {
        if (!string.IsNullOrWhiteSpace(secretKey))
        {
            return secretKey.Trim();
        }

        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }
}
