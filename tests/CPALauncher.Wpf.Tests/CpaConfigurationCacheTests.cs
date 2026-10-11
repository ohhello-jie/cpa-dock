using CPALauncher.Models;
using CPALauncher.Services;
using Xunit;

namespace CPALauncher.Wpf.Tests;

public sealed class CpaConfigurationCacheTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "cpa-cache-" + Guid.NewGuid().ToString("N"));
    private readonly ManualClock clock = new();
    private int calls;

    public CpaConfigurationCacheTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void UnchangedPolling_ReusesInspection_AndRechecksAtExpiry()
    {
        var (exe, config) = CreateFiles();
        var cache = new CpaConfigurationCache(Inspect, clock);
        var first = cache.Inspect(exe, config);
        for (var i = 1; i < 12; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(2.5));
            Assert.Same(first, cache.Inspect(exe, config));
        }
        Assert.Equal(1, calls);
        clock.Advance(TimeSpan.FromSeconds(2.5));
        Assert.NotSame(first, cache.Inspect(exe, config));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ConfigChange_Deletion_AndManualRefresh_InvalidateCachedResult()
    {
        var (exe, config) = CreateFiles();
        var cache = new CpaConfigurationCache(Inspect, clock);
        cache.Inspect(exe, config);
        File.AppendAllText(config, "\nport: 8318");
        cache.Inspect(exe, config);
        File.Delete(exe);
        cache.Inspect(exe, config);
        cache.Inspect(exe, config, force: true);
        Assert.Equal(4, calls);
    }

    [Fact]
    public void InspectionFailure_IsRetried_AndCanRecover()
    {
        var (exe, config) = CreateFiles();
        var fail = true;
        var cache = new CpaConfigurationCache((e, c) =>
        {
            calls++;
            if (fail) throw new IOException("not ready");
            return Runtime(e, c);
        }, clock);
        Assert.Throws<IOException>(() => cache.Inspect(exe, config));
        Assert.Throws<IOException>(() => cache.Inspect(exe, config));
        fail = false;
        cache.Inspect(exe, config);
        Assert.Equal(3, calls);
    }

    private (string, string) CreateFiles()
    {
        var exe = Path.Combine(directory, "cpa.exe");
        var config = Path.Combine(directory, "config.yaml");
        File.WriteAllText(exe, "fixture"); File.WriteAllText(config, "port: 8317");
        return (exe, config);
    }

    private CpaRuntimeInfo Inspect(string exe, string config) { calls++; return Runtime(exe, config); }
    private static CpaRuntimeInfo Runtime(string exe, string config) => new()
    {
        ExecutablePath = exe, ExecutableDirectory = Path.GetDirectoryName(exe)!,
        ConfigPath = config, ConfigDirectory = Path.GetDirectoryName(config)!,
        LogDirectory = Path.GetDirectoryName(config)!, BaseUrl = "http://127.0.0.1:8317",
        ManagementUrl = "http://127.0.0.1:8317/management.html", ServiceProbeUrl = "http://127.0.0.1:8317",
    };

    public void Dispose() => Directory.Delete(directory, recursive: true);
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
