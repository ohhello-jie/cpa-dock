using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using CPALauncher.Models;
using CPALauncher.Services;
using MessageBox = CPALauncher.Views.LauncherDialog;

namespace CPALauncher.ViewModels;

public partial class MainViewModel
{
    private int _selectedPage;
    private int _logScope;
    private string _logSearchText = "";
    private string _lastCheckedText = "尚未检查";
    private string _latestActivityText = "无";
    private string _credentialsSummary = "未配置";
    private string? _credentialDirectorySnapshot;
    private DateTime _lastCredentialCheck;
    private readonly ConcurrentQueue<string> _pendingOutput = new();
    private int _outputFlushScheduled;

    public int SelectedPage { get => _selectedPage; set => SetProperty(ref _selectedPage, value); }
    public bool AutoScrollLogs { get; set; } = true;
    public string LastCheckedText { get => _lastCheckedText; private set => SetProperty(ref _lastCheckedText, value); }
    public string LatestActivityText { get => _latestActivityText; private set => SetProperty(ref _latestActivityText, value); }
    public ICollectionView DiagnosticView { get; private set; } = null!;
    public string LogSearchText
    {
        get => _logSearchText;
        set { if (SetProperty(ref _logSearchText, value)) DiagnosticView.Refresh(); }
    }
    public int LogScope
    {
        get => _logScope;
        set { if (SetProperty(ref _logScope, value)) DiagnosticView.Refresh(); }
    }

    public Visibility WelcomeVisibility => _runtimeInfo is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ServiceVisibility => _runtimeInfo is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility StartActionVisibility => CanStart() ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StopActionVisibility => CanStop() ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReadyActionVisibility => _serviceReachable ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ExternalNoticeVisibility => _statusPalette == StatusPalette.External ? Visibility.Visible : Visibility.Collapsed;
    public string EndpointDisplay => FormatConfigurationValue(_runtimeInfo?.BaseUrl ?? "尚未配置");
    public string CredentialsSummary => _credentialsSummary;
    public string CredentialActivationHint => _processManager.IsManagedProcessRunning
        ? "导入后会自动重启 CPA，请避开正在使用的请求。"
        : _statusPalette == StatusPalette.External ? "导入后请在原程序中重启 CPA。"
        : "导入到当前认证目录，启动 CPA 后生效。";
    public string ProcessCheckText => _processManager.IsManagedProcessRunning ? $"运行中 · PID {ManagedProcessId}"
        : _statusPalette == StatusPalette.External ? "外部运行 · 不由启动器管理" : "无托管进程";
    public string ConfigurationCheckText => _runtimeInfo is null ? _lastConfigurationError ?? "未配置" : "可读取";
    public string HttpCheckText => _runtimeInfo is null ? "未配置" : _serviceReachable ? "响应正常" : "未响应";
    public string ListenerDisplay => FormatConfigurationValue(_runtimeInfo is null ? "等待配置" : $"{_runtimeInfo.BindHost}:{_runtimeInfo.Port}");

    public ICommand GoOverviewCommand { get; private set; } = null!;
    public ICommand GoDiagnosticsCommand { get; private set; } = null!;
    public ICommand GoSettingsCommand { get; private set; } = null!;
    public ICommand InstallCpaCommand { get; private set; } = null!;
    public ICommand ImportCredentialsCommand { get; private set; } = null!;
    public ICommand CopyServiceUrlCommand { get; private set; } = null!;
    public ICommand CopyVisibleLogsCommand { get; private set; } = null!;
    public ICommand OpenAuthDirectoryCommand { get; private set; } = null!;
    public ICommand OpenConfigurationFileCommand { get; private set; } = null!;

    private void InitializeExperience()
    {
        DiagnosticView = CollectionViewSource.GetDefaultView(DiagnosticLines);
        DiagnosticView.Filter = item => item is string line && DiagnosticLogFilter.Matches(line, LogSearchText, LogScope);
        GoOverviewCommand = new RelayCommand(() => SelectedPage = 0);
        GoDiagnosticsCommand = new RelayCommand(() => SelectedPage = 1);
        GoSettingsCommand = new RelayCommand(() => SelectedPage = 2);
        InstallCpaCommand = new RelayCommand(async () =>
        {
            try { await RunFirstTimeSetupAsync(); await RefreshAsync(true); }
            catch (Exception ex)
            {
                AddDiagnosticLine($"[launcher] 安装失败：{ex.Message}");
                MessageBox.Show(ex.Message, "安装失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { IsUpdateInProgress = false; }
        }, () => CanRunUpdateCommand());
        ImportCredentialsCommand = new RelayCommand(async () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "导入 CPA 凭证", Filter = "JSON 凭证 (*.json)|*.json", Multiselect = true,
            };
            if (dialog.ShowDialog() == true) await ImportDroppedTokensAsync(dialog.FileNames);
        }, () => !string.IsNullOrWhiteSpace(_authDirectory));
        CopyServiceUrlCommand = new RelayCommand(() => CopyText(_runtimeInfo?.BaseUrl ?? "", "服务地址已复制。"), () => _runtimeInfo is not null);
        CopyVisibleLogsCommand = new RelayCommand(() => CopyText(string.Join(Environment.NewLine, DiagnosticView.Cast<string>()), "当前日志已复制。"));
        OpenAuthDirectoryCommand = new RelayCommand(() => OpenExistingPath(_authDirectory), () => Directory.Exists(_authDirectory));
        OpenConfigurationFileCommand = new RelayCommand(() =>
        {
            try
            {
                var editor = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
                editor.ArgumentList.Add(_configPath);
                Process.Start(editor);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "无法打开配置文件", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }, () => File.Exists(_configPath));
    }

    private static void CopyText(string text, string notice)
    {
        try
        {
            if (string.IsNullOrEmpty(text)) return;
            Clipboard.SetText(text);
            MessageBox.Show(notice, "已复制", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "复制失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private static void OpenExistingPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "无法打开", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void NotifyExperienceState()
    {
        foreach (var name in new[] { nameof(WelcomeVisibility), nameof(ServiceVisibility), nameof(StartActionVisibility),
                     nameof(StopActionVisibility), nameof(ReadyActionVisibility), nameof(ExternalNoticeVisibility), nameof(EndpointDisplay),
                     nameof(CredentialsSummary), nameof(CredentialActivationHint), nameof(ProcessCheckText),
                     nameof(ConfigurationCheckText), nameof(HttpCheckText), nameof(ListenerDisplay) })
            OnPropertyChanged(name);
    }

    private void RefreshCredentialSummary(bool force = false)
    {
        if (!force && _credentialDirectorySnapshot == _authDirectory
            && DateTime.UtcNow - _lastCredentialCheck < TimeSpan.FromSeconds(30)) return;
        _credentialDirectorySnapshot = _authDirectory;
        _lastCredentialCheck = DateTime.UtcNow;
        try
        {
            _credentialsSummary = string.IsNullOrEmpty(_authDirectory) ? "未配置"
                : $"{(Directory.Exists(_authDirectory) ? Directory.EnumerateFiles(_authDirectory, "*.json").Count() : 0)} 个文件";
        }
        catch { _credentialsSummary = "无法读取"; }
        OnPropertyChanged(nameof(CredentialsSummary));
    }

    private void QueueProcessOutput(string line)
    {
        _pendingOutput.Enqueue(line);
        // Bound the producer queue as well as the visible history during bursts.
        while (_pendingOutput.Count > 400) _pendingOutput.TryDequeue(out _);
        ScheduleOutputFlush();
    }

    private void ScheduleOutputFlush()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted ||
            Interlocked.CompareExchange(ref _outputFlushScheduled, 1, 0) != 0) return;
        dispatcher.BeginInvoke(() =>
        {
            try
            {
                for (var i = 0; i < 200 && _pendingOutput.TryDequeue(out var line); i++) AddDiagnosticLine(line);
            }
            finally
            {
                Interlocked.Exchange(ref _outputFlushScheduled, 0);
                if (!_pendingOutput.IsEmpty) ScheduleOutputFlush();
            }
        }, DispatcherPriority.Background);
    }
}
