using CPALauncher.Services;
using Xunit;

namespace CPALauncher.Wpf.Tests;

public sealed class CpaConfigGeneratorTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), "cpa-launcher-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void WriteDefaultConfig_WhenProxyIsNotProvided_WritesBlankProxyAndPanelRepository()
    {
        Directory.CreateDirectory(tempDirectory);
        var configPath = Path.Combine(tempDirectory, "config.yaml");

        var secretKey = CpaConfigGenerator.WriteDefaultConfig(
            configPath,
            host: "127.0.0.1",
            port: 8317,
            proxyUrl: null,
            secretKey: null);

        var content = File.ReadAllText(configPath);

        Assert.Contains("panel-github-repository: \"https://github.com/router-for-me/Cli-Proxy-API-Management-Center\"", content);
        Assert.Contains("proxy-url: \"\"", content);
        Assert.DoesNotContain("proxy-url: \"http://127.0.0.1:7897\"", content);
        Assert.Contains($"secret-key: \"{secretKey}\"", content);
    }

    [Fact]
    public void WriteDefaultConfig_WhenSecretKeyIsNotProvided_GeneratesUsableManagementKey()
    {
        Directory.CreateDirectory(tempDirectory);
        var configPath = Path.Combine(tempDirectory, "generated-secret.yaml");

        var secretKey = CpaConfigGenerator.WriteDefaultConfig(
            configPath,
            host: "127.0.0.1",
            port: 8317,
            proxyUrl: null,
            secretKey: null);

        var content = File.ReadAllText(configPath);

        Assert.False(string.IsNullOrWhiteSpace(secretKey));
        Assert.Matches("^[a-f0-9]{32}$", secretKey);
        Assert.Contains($"secret-key: \"{secretKey}\"", content);
    }

    [Fact]
    public void WriteDefaultConfig_WhenSecretKeyIsProvided_PreservesProvidedValue()
    {
        Directory.CreateDirectory(tempDirectory);
        var configPath = Path.Combine(tempDirectory, "provided-secret.yaml");

        var secretKey = CpaConfigGenerator.WriteDefaultConfig(
            configPath,
            host: "127.0.0.1",
            port: 8317,
            proxyUrl: null,
            secretKey: "launcher-test-key");

        var content = File.ReadAllText(configPath);

        Assert.Equal("launcher-test-key", secretKey);
        Assert.Contains("secret-key: \"launcher-test-key\"", content);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
