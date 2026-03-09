namespace CPALauncher.Models;

public sealed class CpaRuntimeInfo
{
    public required string ExecutablePath { get; init; }

    public required string ExecutableDirectory { get; init; }

    public required string ConfigPath { get; init; }

    public required string ConfigDirectory { get; init; }

    public string BindHost { get; init; } = string.Empty;

    public string AccessHost { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 8317;

    public bool UseTls { get; init; }

    public bool LoggingToFile { get; init; }

    public bool UsageStatisticsEnabled { get; init; }

    public bool ControlPanelDisabled { get; init; }

    public bool ManagementSecretConfigured { get; init; }

    public string? AuthDirectory { get; init; }

    public required string LogDirectory { get; init; }

    public required string BaseUrl { get; init; }

    public required string ManagementUrl { get; init; }

    public required string ServiceProbeUrl { get; init; }
}
