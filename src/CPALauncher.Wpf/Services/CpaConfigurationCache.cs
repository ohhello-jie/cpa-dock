using CPALauncher.Models;

namespace CPALauncher.Services;

/// <summary>Avoid repeated YAML reads and directory write probes on each status tick.</summary>
public sealed class CpaConfigurationCache(
    Func<string, string, CpaRuntimeInfo> inspect,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private ConfigurationStamp? stamp;
    private CpaRuntimeInfo? cached;
    private DateTimeOffset expiresAt;

    public CpaRuntimeInfo Inspect(string executablePath, string configPath, bool force = false)
    {
        var file = new FileInfo(configPath);
        var next = new ConfigurationStamp(
            Path.GetFullPath(executablePath), Path.GetFullPath(configPath),
            File.Exists(executablePath), file.Exists,
            file.Exists ? file.LastWriteTimeUtc.Ticks : 0,
            file.Exists ? file.Length : 0,
            Environment.GetEnvironmentVariable("WRITABLE_PATH"));

        if (!force && cached is not null && next == stamp && clock.GetUtcNow() < expiresAt)
            return cached;

        // Failed inspections are never cached; a repaired configuration can recover immediately.
        var result = inspect(executablePath, configPath);
        cached = result;
        stamp = next;
        expiresAt = clock.GetUtcNow().AddSeconds(30);
        return result;
    }

    private sealed record ConfigurationStamp(string Executable, string Config, bool ExecutableExists,
        bool ConfigExists, long WriteTicks, long Length, string? WritablePath);
}
