using CPALauncher.Services;
using CPALauncher.ViewModels;
using Xunit;
using System.Reflection;
using CPALauncher.Models;

namespace CPALauncher.Wpf.Tests;

public sealed class MainViewModelTokenImportActivationTests
{
    [Fact]
    public async Task ImportDroppedTokensAsync_WhenManagedServiceIsRunning_RestartsService()
    {
        var harness = MainViewModelTokenImportHarness.CreateManagedRunning();
        var result = new TokenImportResult
        {
            Status = TokenImportStatus.Succeeded,
            ImportedCount = 1,
            SummaryMessage = "已导入 1 个凭证文件。"
        };

        await harness.ViewModel.HandleTokenImportActivationForTestAsync(result);

        Assert.Equal(["stop", "start"], harness.ServiceActions);
        Assert.Equal("CPA 已重启，凭证已生效", harness.ViewModel.ImportNoticeText);
    }

    [Fact]
    public async Task ImportDroppedTokensAsync_WhenServiceIsExternalOnly_ShowsManualRestartNotice()
    {
        var harness = MainViewModelTokenImportHarness.CreateExternallyRunning();
        var result = new TokenImportResult
        {
            Status = TokenImportStatus.Succeeded,
            ImportedCount = 1,
            SummaryMessage = "已导入 1 个凭证文件。"
        };

        await harness.ViewModel.HandleTokenImportActivationForTestAsync(result);

        Assert.Empty(harness.ServiceActions);
        Assert.Equal("凭证已导入，重启外部 CPA 后生效", harness.ViewModel.ImportNoticeText);
    }

    [Fact]
    public async Task ImportDroppedTokensAsync_WhenServiceIsStopped_ShowsNextStartNotice()
    {
        var harness = MainViewModelTokenImportHarness.CreateStopped();
        var result = new TokenImportResult
        {
            Status = TokenImportStatus.Succeeded,
            ImportedCount = 1,
            SummaryMessage = "已导入 1 个凭证文件。"
        };

        await harness.ViewModel.HandleTokenImportActivationForTestAsync(result);

        Assert.Empty(harness.ServiceActions);
        Assert.Equal("凭证已导入，下次启动 CPA 时生效", harness.ViewModel.ImportNoticeText);
    }

    [Fact]
    public async Task ImportDroppedTokensAsync_WhenImportFailed_DoesNothing()
    {
        var harness = MainViewModelTokenImportHarness.CreateManagedRunning();
        var result = new TokenImportResult
        {
            Status = TokenImportStatus.Failed,
            ImportedCount = 0,
            FailedCount = 1,
            Errors = ["导入 one.json 失败：权限不足"],
            SummaryMessage = "有 1 个文件导入失败。"
        };

        await harness.ViewModel.HandleTokenImportActivationForTestAsync(result);

        Assert.Empty(harness.ServiceActions);
        Assert.Equal(string.Empty, harness.ViewModel.ImportNoticeText);
    }

    [Fact]
    public async Task ImportDroppedTokensAsync_WhenRestartFails_ShowsFallbackNoticeAndFailureLog()
    {
        var harness = MainViewModelTokenImportHarness.CreateManagedRunning();
        harness.RestartShouldSucceed = false;
        var result = new TokenImportResult
        {
            Status = TokenImportStatus.PartiallySucceeded,
            ImportedCount = 1,
            FailedCount = 1,
            Errors = ["导入 other.json 失败：权限不足"],
            SummaryMessage = "已导入 1 个凭证文件，1 个文件失败。"
        };

        await harness.ViewModel.HandleTokenImportActivationForTestAsync(result);

        Assert.Equal("凭证已导入。自动重启失败，请手动重启 CPA", harness.ViewModel.ImportNoticeText);
        Assert.Contains(harness.DiagnosticLines, line => line.Contains("自动重启失败", StringComparison.Ordinal));
    }
}

internal sealed class MainViewModelTokenImportHarness : MainViewModel
{
    private static readonly FieldInfo CurrentStatusField = typeof(MainViewModel)
        .GetField("_currentStatus", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("无法定位 MainViewModel._currentStatus 字段。");

    private readonly bool isManagedRunning;

    private MainViewModelTokenImportHarness(LauncherStatus currentStatus, bool isManagedRunning)
        : base(skipInitialization: true, settingsStore: new CPALauncher.Services.LauncherSettingsStore(
            Path.Combine(Path.GetTempPath(), "cpa-test-settings-" + Guid.NewGuid().ToString("N"))))
    {
        this.isManagedRunning = isManagedRunning;
        CurrentStatusField.SetValue(this, currentStatus);
    }

    public MainViewModelTokenImportHarness ViewModel => this;

    public List<string> ServiceActions { get; } = [];

    public bool RestartShouldSucceed { get; set; } = true;

    public static MainViewModelTokenImportHarness CreateManagedRunning()
    {
        return new MainViewModelTokenImportHarness(LauncherStatus.Running, isManagedRunning: true);
    }

    public static MainViewModelTokenImportHarness CreateExternallyRunning()
    {
        return new MainViewModelTokenImportHarness(LauncherStatus.Running, isManagedRunning: false);
    }

    public static MainViewModelTokenImportHarness CreateStopped()
    {
        return new MainViewModelTokenImportHarness(LauncherStatus.Stopped, isManagedRunning: false);
    }

    public Task HandleTokenImportActivationForTestAsync(TokenImportResult result)
    {
        return HandleTokenImportActivationAsync(result);
    }

    protected override bool IsManagedProcessRunningForActivation()
    {
        return isManagedRunning;
    }

    protected override Task<bool> RestartManagedServiceAfterTokenImportAsync()
    {
        ServiceActions.Add("stop");
        ServiceActions.Add("start");
        return Task.FromResult(RestartShouldSucceed);
    }
}
