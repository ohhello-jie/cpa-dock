using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CPALauncher.Models;
using CPALauncher.Services;
using HandyControl.Data;
using MessageBox = HandyControl.Controls.MessageBox;

namespace CPALauncher.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly CpaProcessManager _processManager = new();
    private readonly CpaConfigInspector _configInspector = new();
    private readonly LauncherSettingsStore _settingsStore = new();
    private readonly WindowsStartupManager _startupManager = new();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly HttpClient _downloadHttpClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly CpaUpdateService _updateService;
    private readonly DispatcherTimer _refreshTimer;

    private LauncherStatus _currentStatus = LauncherStatus.Unconfigured;
    private LauncherSettings _settings = new();
    private CpaRuntimeInfo? _runtimeInfo;

    private string _statusText = "未配置";
    private string _statusDetail = "请先配置 CPA 可执行文件和配置文件路径。";
    private Brush _statusBackgroundBrush = new SolidColorBrush(Color.FromRgb(232, 236, 241));
    private Brush _statusForegroundBrush = new SolidColorBrush(Color.FromRgb(32, 31, 30));
    private string _managedProcessId = "无";

    private string _executablePath = "";
    private string _configPath = "";
    private string _managementUrl = "";
    private string _probeUrl = "";
    private string _configDirectory = "";
    private string _logDirectory = "";
    private string _authDirectory = "";

    private bool _minimizeToTrayOnClose = true;
    private bool _autoStartService;
    private bool _launchOnWindowsStartup;
    private bool _openManagementPageAfterStart = true;
    private int _autoStartDelaySeconds;
    private bool _isDarkMode;
    private bool _checkForUpdatesOnStartup = true;

    private string _updateStatusText = "";
    private int _updateProgress;
    private bool _isUpdateInProgress;

    public ObservableCollection<string> DiagnosticLines { get; } = new();

    // Status Properties
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string StatusDetail
    {
        get => _statusDetail;
        set => SetProperty(ref _statusDetail, value);
    }

    public Brush StatusBackgroundBrush
    {
        get => _statusBackgroundBrush;
        set => SetProperty(ref _statusBackgroundBrush, value);
    }

    public Brush StatusForegroundBrush
    {
        get => _statusForegroundBrush;
        set => SetProperty(ref _statusForegroundBrush, value);
    }

    public string ManagedProcessId
    {
        get => _managedProcessId;
        set => SetProperty(ref _managedProcessId, value);
    }

    // Path Properties
    public string ExecutablePath
    {
        get => _executablePath;
        set
        {
            if (SetProperty(ref _executablePath, value))
            {
                _settings.ExecutablePath = value;
                SaveSettings();
            }
        }
    }

    public string ConfigPath
    {
        get => _configPath;
        set
        {
            if (SetProperty(ref _configPath, value))
            {
                _settings.ConfigPath = value;
                SaveSettings();
            }
        }
    }

    // Info Properties
    public string ManagementUrl
    {
        get => _managementUrl;
        set => SetProperty(ref _managementUrl, value);
    }

    public string ProbeUrl
    {
        get => _probeUrl;
        set => SetProperty(ref _probeUrl, value);
    }

    public string ConfigDirectory
    {
        get => _configDirectory;
        set => SetProperty(ref _configDirectory, value);
    }

    public string LogDirectory
    {
        get => _logDirectory;
        set => SetProperty(ref _logDirectory, value);
    }

    public string AuthDirectory
    {
        get => _authDirectory;
        set => SetProperty(ref _authDirectory, value);
    }

    public string ManagementSecretKeyDisplay =>
        string.IsNullOrWhiteSpace(_settings.ManagementSecretKey)
            ? "未保存"
            : MaskManagementSecretKey(_settings.ManagementSecretKey);

    public string ManagementSecretKeyToolTip =>
        string.IsNullOrWhiteSpace(_settings.ManagementSecretKey)
            ? "当前没有已保存的管理密钥。"
            : _settings.ManagementSecretKey;

    public bool HasManagementSecretKey => !string.IsNullOrWhiteSpace(_settings.ManagementSecretKey);

    // Settings Properties
    public bool MinimizeToTrayOnClose
    {
        get => _minimizeToTrayOnClose;
        set
        {
            if (SetProperty(ref _minimizeToTrayOnClose, value))
            {
                _settings.MinimizeToTrayOnClose = value;
                SaveSettings();
            }
        }
    }

    public bool AutoStartService
    {
        get => _autoStartService;
        set
        {
            if (SetProperty(ref _autoStartService, value))
            {
                _settings.AutoStartService = value;
                SaveSettings();
            }
        }
    }

    public bool LaunchOnWindowsStartup
    {
        get => _launchOnWindowsStartup;
        set
        {
            if (SetProperty(ref _launchOnWindowsStartup, value))
            {
                _settings.LaunchLauncherOnWindowsStartup = value;
                _startupManager.SetEnabled(value);
                SaveSettings();
            }
        }
    }

    public bool OpenManagementPageAfterStart
    {
        get => _openManagementPageAfterStart;
        set
        {
            if (SetProperty(ref _openManagementPageAfterStart, value))
            {
                _settings.OpenManagementPageAfterStart = value;
                SaveSettings();
            }
        }
    }

    public int AutoStartDelaySeconds
    {
        get => _autoStartDelaySeconds;
        set
        {
            if (SetProperty(ref _autoStartDelaySeconds, value))
            {
                _settings.AutoStartDelaySeconds = value;
                SaveSettings();
            }
        }
    }

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (SetProperty(ref _isDarkMode, value))
            {
                _settings.UseDarkTheme = value;
                SaveSettings();
                ApplyTheme(value);
            }
        }
    }

    public bool CheckForUpdatesOnStartup
    {
        get => _checkForUpdatesOnStartup;
        set
        {
            if (SetProperty(ref _checkForUpdatesOnStartup, value))
            {
                _settings.CheckForUpdatesOnStartup = value;
                SaveSettings();
            }
        }
    }

    // Update Properties
    public string UpdateStatusText
    {
        get => _updateStatusText;
        set
        {
            if (SetProperty(ref _updateStatusText, value))
                OnPropertyChanged(nameof(UpdateStatusVisibility));
        }
    }

    public int UpdateProgress
    {
        get => _updateProgress;
        set => SetProperty(ref _updateProgress, value);
    }

    public bool IsUpdateInProgress
    {
        get => _isUpdateInProgress;
        set => SetProperty(ref _isUpdateInProgress, value);
    }

    public Visibility UpdateProgressVisibility =>
        _isUpdateInProgress ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UpdateStatusVisibility =>
        string.IsNullOrEmpty(_updateStatusText) ? Visibility.Collapsed : Visibility.Visible;

    // Commands
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenManagementCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand CheckForUpdateCommand { get; }
    public ICommand BrowseExecutableCommand { get; }
    public ICommand BrowseConfigCommand { get; }
    public ICommand OpenExecutableDirCommand { get; }
    public ICommand OpenConfigDirCommand { get; }
    public ICommand CopyManagementSecretKeyCommand { get; }
    public ICommand CopyLogsCommand { get; }
    public ICommand ExportLogsCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand ShowWindowCommand { get; }
    public ICommand ExitApplicationCommand { get; }

    public MainViewModel()
    {
        _updateService = new CpaUpdateService(_downloadHttpClient);

        // Initialize commands
        StartCommand = new RelayCommand(StartServiceAsync, () => CanStart());
        StopCommand = new RelayCommand(StopServiceAsync, () => CanStop());
        RefreshCommand = new RelayCommand(RefreshAsync);
        OpenManagementCommand = new RelayCommand(OpenManagementPage, () => !string.IsNullOrEmpty(_managementUrl));
        OpenLogsCommand = new RelayCommand(OpenLogsDirectory, () => !string.IsNullOrEmpty(_logDirectory));
        CheckForUpdateCommand = new RelayCommand(CheckForUpdateAsync, () => !_isUpdateInProgress);
        BrowseExecutableCommand = new RelayCommand(BrowseExecutable);
        BrowseConfigCommand = new RelayCommand(BrowseConfig);
        OpenExecutableDirCommand = new RelayCommand(OpenExecutableDirectory, () => !string.IsNullOrEmpty(_executablePath));
        OpenConfigDirCommand = new RelayCommand(OpenConfigDirectory, () => !string.IsNullOrEmpty(_configPath));
        CopyManagementSecretKeyCommand = new RelayCommand(CopyManagementSecretKey, () => HasManagementSecretKey);
        CopyLogsCommand = new RelayCommand(CopyLogs);
        ExportLogsCommand = new RelayCommand(ExportLogs);
        ClearLogsCommand = new RelayCommand(ClearLogs);
        ShowWindowCommand = new RelayCommand(ShowWindow);
        ExitApplicationCommand = new RelayCommand(ExitApplication);

        // Wire process manager events
        _processManager.OutputReceived += OnProcessOutput;
        _processManager.ProcessExited += OnProcessExited;

        // Setup refresh timer
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
        _refreshTimer.Tick += async (s, e) => await RefreshAsync();

        // Load settings and initialize
        LoadSettings();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (LauncherSetupDecider.ShouldRunFirstTimeSetup(_settings))
        {
            await RunFirstTimeSetupAsync();
        }

        await RefreshAsync();
        _refreshTimer.Start();

        if (_settings.AutoStartService && _currentStatus == LauncherStatus.Stopped)
        {
            if (_settings.AutoStartDelaySeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(_settings.AutoStartDelaySeconds));
            }
            await StartServiceAsync();
        }

        if (_settings.CheckForUpdatesOnStartup && LauncherSetupDecider.HasInstalledExecutable(_settings))
        {
            _ = CheckForUpdateSilentAsync();
        }
    }

    private async Task RunFirstTimeSetupAsync()
    {
        var answer = MessageBox.Show(
            "检测到当前未安装 CPA，或原有安装路径已失效，是否自动下载 CPA？\n\n选择「是」将自动下载最新版本到程序目录。",
            "安装 CPA",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        // Check for latest version
        UpdateStatusText = "正在获取最新版本信息...";
        AddDiagnosticLine("[launcher] 首次安装：正在获取最新版本信息...");

        var updateCheck = await _updateService.CheckForUpdateAsync(_settings.CpaGitHubRepo, currentVersion: null);
        if (updateCheck.Status != CpaUpdateCheckStatus.UpdateAvailable || updateCheck.UpdateInfo is null)
        {
            UpdateStatusText = "";
            AddDiagnosticLine($"[launcher] {updateCheck.FailureReason ?? "无法获取 CPA 版本信息，请稍后手动配置。"}");
            MessageBox.Show(
                updateCheck.FailureReason ?? "无法获取 CPA 版本信息，请检查网络后重试，或手动下载配置。",
                "获取版本失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var info = updateCheck.UpdateInfo;

        // Determine install directory
        var launcherDir = AppContext.BaseDirectory;
        var cpaDir = Path.Combine(launcherDir, "cpa");

        AddDiagnosticLine($"[launcher] 发现最新版本 {info.TagName}（{info.AssetSize / 1024.0 / 1024.0:F1} MB），开始下载...");

        // Download with progress
        IsUpdateInProgress = true;
        OnPropertyChanged(nameof(UpdateProgressVisibility));
        UpdateProgress = 0;
        UpdateStatusText = "正在下载 CPA...";

        var progress = new Progress<(long downloaded, long total)>(p =>
        {
            if (p.total > 0)
                UpdateProgress = (int)(p.downloaded * 100 / p.total);
            UpdateStatusText = $"正在下载 CPA... {p.downloaded / 1024.0 / 1024.0:F1} / {p.total / 1024.0 / 1024.0:F1} MB";
        });

        var result = await _updateService.InstallLatestAsync(cpaDir, info, progress);

        IsUpdateInProgress = false;
        OnPropertyChanged(nameof(UpdateProgressVisibility));

        if (!result.Success || result.ExePath is null)
        {
            UpdateStatusText = "安装失败";
            AddDiagnosticLine($"[launcher] {result.Message}");
            MessageBox.Show(result.Message, "安装失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Set executable path
        ExecutablePath = result.ExePath;
        _settings.LastInstalledCpaVersion = info.TagName;
        SaveSettings();

        UpdateStatusText = $"CPA {info.TagName} 安装完成";
        AddDiagnosticLine($"[launcher] {result.Message}");

        // Show setup wizard
        var wizard = new Views.SetupWizardWindow
        {
            Owner = Application.Current.MainWindow
        };

        if (wizard.ShowDialog() == true)
        {
            var configFilePath = Path.Combine(cpaDir, "config.yaml");
            var effectiveSecretKey = Services.CpaConfigGenerator.WriteDefaultConfig(
                configFilePath,
                wizard.Host,
                wizard.Port,
                wizard.ProxyUrl,
                wizard.SecretKey);
            ConfigPath = configFilePath;
            UpdateStoredManagementSecretKey(effectiveSecretKey);
            AddDiagnosticLine($"[launcher] 配置文件已生成：{configFilePath}");

            if (string.IsNullOrWhiteSpace(wizard.SecretKey))
            {
                AddDiagnosticLine("[launcher] 已自动生成管理密钥，并保存到启动器。");
                TryCopyManagementSecretKeyToClipboard(
                    effectiveSecretKey,
                    "已自动生成管理密钥，并复制到剪贴板。\n\n后续也可以在启动器首页点击“复制管理密钥”再次获取。");
            }
            else
            {
                AddDiagnosticLine("[launcher] 已保存当前管理密钥，可在启动器首页再次复制。");
            }
        }
        else
        {
            AddDiagnosticLine("[launcher] 用户跳过了配置向导，请手动配置 config.yaml。");
            MessageBox.Show("你可以稍后手动创建 config.yaml 并在路径设置中指定。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void LoadSettings()
    {
        _settings = _settingsStore.Load();
        ExecutablePath = _settings.ExecutablePath ?? "";
        ConfigPath = _settings.ConfigPath ?? "";
        MinimizeToTrayOnClose = _settings.MinimizeToTrayOnClose;
        AutoStartService = _settings.AutoStartService;
        LaunchOnWindowsStartup = _settings.LaunchLauncherOnWindowsStartup;
        OpenManagementPageAfterStart = _settings.OpenManagementPageAfterStart;
        AutoStartDelaySeconds = _settings.AutoStartDelaySeconds;

        // 不触发 setter 以避免重复保存，直接赋值并应用主题
        _isDarkMode = _settings.UseDarkTheme;
        OnPropertyChanged(nameof(IsDarkMode));
        ApplyTheme(_isDarkMode);

        _checkForUpdatesOnStartup = _settings.CheckForUpdatesOnStartup;
        OnPropertyChanged(nameof(CheckForUpdatesOnStartup));
        NotifyManagementSecretKeyChanged();
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            AddDiagnosticLine($"[launcher] 保存设置失败：{ex.Message}");
        }
    }

    private void UpdateStoredManagementSecretKey(string? secretKey)
    {
        _settings.ManagementSecretKey = string.IsNullOrWhiteSpace(secretKey) ? null : secretKey.Trim();
        SaveSettings();
        NotifyManagementSecretKeyChanged();
    }

    private void NotifyManagementSecretKeyChanged()
    {
        OnPropertyChanged(nameof(ManagementSecretKeyDisplay));
        OnPropertyChanged(nameof(ManagementSecretKeyToolTip));
        OnPropertyChanged(nameof(HasManagementSecretKey));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task RefreshAsync()
    {
        try
        {
            InspectConfiguration();
            UpdateProcessInfo();
            var serviceReachable = await ProbeServiceAsync();
            UpdateStatusDisplay(serviceReachable);
        }
        catch (Exception ex)
        {
            AddDiagnosticLine($"[launcher] 刷新状态失败：{ex.Message}");
        }
    }

    private void InspectConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_settings.ExecutablePath) || string.IsNullOrWhiteSpace(_settings.ConfigPath))
        {
            _runtimeInfo = null;
            ManagementUrl = "";
            ProbeUrl = "";
            ConfigDirectory = "";
            LogDirectory = "";
            AuthDirectory = "";
            return;
        }

        try
        {
            _runtimeInfo = _configInspector.Inspect(_settings.ExecutablePath, _settings.ConfigPath);
            ManagementUrl = _runtimeInfo.ManagementUrl;
            ProbeUrl = _runtimeInfo.ServiceProbeUrl;
            ConfigDirectory = _runtimeInfo.ConfigDirectory;
            LogDirectory = _runtimeInfo.LogDirectory;
            AuthDirectory = _runtimeInfo.AuthDirectory ?? "";
        }
        catch (Exception ex)
        {
            _runtimeInfo = null;
            AddDiagnosticLine($"[launcher] 配置检查失败：{ex.Message}");
        }
    }

    private void UpdateProcessInfo()
    {
        ManagedProcessId = _processManager.ManagedProcessId?.ToString() ?? "无";
    }

    private async Task<bool> ProbeServiceAsync()
    {
        if (_runtimeInfo == null) return false;

        try
        {
            var response = await _httpClient.GetAsync(_runtimeInfo.ServiceProbeUrl);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateStatusDisplay(bool serviceReachable)
    {
        if (_runtimeInfo == null)
        {
            _currentStatus = LauncherStatus.Unconfigured;
            StatusText = "未配置";
            StatusDetail = GetUnconfiguredStatusDetail();
            StatusBackgroundBrush = new SolidColorBrush(Color.FromRgb(232, 236, 241));
            StatusForegroundBrush = new SolidColorBrush(Color.FromRgb(32, 31, 30));
            return;
        }

        var isManaged = _processManager.IsManagedProcessRunning;

        if (isManaged && serviceReachable)
        {
            _currentStatus = LauncherStatus.Running;
            StatusText = "运行中";
            StatusDetail = "CPA 已运行，当前由启动器托管。";
            StatusBackgroundBrush = new SolidColorBrush(Color.FromRgb(209, 241, 224));
            StatusForegroundBrush = new SolidColorBrush(Color.FromRgb(15, 85, 43));
        }
        else if (isManaged && !serviceReachable)
        {
            _currentStatus = LauncherStatus.Starting;
            StatusText = "启动中";
            StatusDetail = "CPA 进程已启动，正在等待服务就绪...";
            StatusBackgroundBrush = new SolidColorBrush(Color.FromRgb(255, 242, 204));
            StatusForegroundBrush = new SolidColorBrush(Color.FromRgb(120, 79, 0));
        }
        else if (!isManaged && serviceReachable)
        {
            _currentStatus = LauncherStatus.Running;
            StatusText = "外部运行";
            StatusDetail = "CPA 正在运行，但不由启动器托管（可能由其他方式启动）。";
            StatusBackgroundBrush = new SolidColorBrush(Color.FromRgb(222, 236, 255));
            StatusForegroundBrush = new SolidColorBrush(Color.FromRgb(0, 74, 173));
        }
        else
        {
            _currentStatus = LauncherStatus.Stopped;
            StatusText = "已停止";
            StatusDetail = "CPA 当前未运行。";
            StatusBackgroundBrush = new SolidColorBrush(Color.FromRgb(238, 238, 238));
            StatusForegroundBrush = new SolidColorBrush(Color.FromRgb(66, 66, 66));
        }
    }

    private bool CanStart() => _currentStatus == LauncherStatus.Stopped || _currentStatus == LauncherStatus.StartFailed;
    private bool CanStop() => _currentStatus == LauncherStatus.Running || _currentStatus == LauncherStatus.Starting;

    private async Task StartServiceAsync()
    {
        if (_runtimeInfo == null)
        {
            MessageBox.Show("请先配置 CPA 可执行文件和配置文件路径。", "无法启动", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _currentStatus = LauncherStatus.Starting;
        UpdateStatusDisplay(false);

        var result = await _processManager.StartAsync(_settings.ExecutablePath!, _settings.ConfigPath!);

        if (!result.Success)
        {
            _currentStatus = LauncherStatus.StartFailed;
            StatusText = "启动失败";
            StatusDetail = result.Message;
            StatusBackgroundBrush = new SolidColorBrush(Color.FromRgb(255, 226, 226));
            StatusForegroundBrush = new SolidColorBrush(Color.FromRgb(163, 0, 0));
            MessageBox.Show(result.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        AddDiagnosticLine($"[launcher] {result.Message}");
        await WaitForServiceReadyAsync();

        if (_settings.OpenManagementPageAfterStart && !string.IsNullOrEmpty(_managementUrl))
        {
            OpenManagementPage();
        }
    }

    private async Task WaitForServiceReadyAsync()
    {
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(500);
            if (await ProbeServiceAsync())
            {
                await RefreshAsync();
                return;
            }
        }
    }

    private async Task StopServiceAsync()
    {
        _currentStatus = LauncherStatus.Stopping;
        StatusText = "停止中";
        StatusDetail = "正在停止 CPA 进程...";
        StatusBackgroundBrush = new SolidColorBrush(Color.FromRgb(255, 242, 204));
        StatusForegroundBrush = new SolidColorBrush(Color.FromRgb(120, 79, 0));

        var result = await _processManager.StopAsync();
        AddDiagnosticLine($"[launcher] {result.Message}");
        await RefreshAsync();
    }

    private void OpenManagementPage()
    {
        if (string.IsNullOrEmpty(_managementUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(_managementUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开管理页：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenLogsDirectory()
    {
        if (string.IsNullOrEmpty(_logDirectory)) return;
        try
        {
            Process.Start(new ProcessStartInfo(_logDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开日志目录：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // --- Update logic ---

    private async Task CheckForUpdateSilentAsync()
    {
        UpdateStatusText = "正在检查 CPA 更新...";
        AddDiagnosticLine("[launcher] 正在检查 CPA 更新...");

        var currentVersion = ResolveCurrentCpaVersionForUpdateCheck();
        if (currentVersion is null)
        {
            await HandleUnknownCurrentVersionAsync(showMessageBox: false);
            return;
        }

        var updateCheck = await _updateService.CheckForUpdateAsync(_settings.CpaGitHubRepo, currentVersion);
        if (updateCheck.Status == CpaUpdateCheckStatus.UpToDate)
        {
            UpdateStatusText = "";
            AddDiagnosticLine($"[launcher] CPA 已是最新版本：{updateCheck.LatestTagName}。");
            return;
        }

        if (updateCheck.Status == CpaUpdateCheckStatus.CheckFailed || updateCheck.UpdateInfo is null)
        {
            UpdateStatusText = "";
            AddDiagnosticLine($"[launcher] {updateCheck.FailureReason ?? "检查更新失败。"}");
            return;
        }

        var info = updateCheck.UpdateInfo;

        UpdateStatusText = $"发现新版本：{info.TagName}";
        AddDiagnosticLine($"[launcher] 发现 CPA 新版本：{info.TagName}（{info.AssetSize / 1024.0 / 1024.0:F1} MB）");

        var answer = MessageBox.Show(
            $"发现 CPA 新版本 {info.TagName}（{info.AssetSize / 1024.0 / 1024.0:F1} MB）。\n\n是否立即下载并更新？",
            "CPA 更新可用",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
        {
            await PerformUpdateAsync(info);
        }
        else
        {
            UpdateStatusText = "";
        }
    }

    private async Task CheckForUpdateAsync()
    {
        if (!LauncherSetupDecider.HasInstalledExecutable(_settings))
        {
            MessageBox.Show("当前未检测到有效的 CPA 可执行文件，请先下载或重新配置路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        UpdateStatusText = "正在检查 CPA 更新...";
        AddDiagnosticLine("[launcher] 正在检查 CPA 更新...");

        var currentVersion = ResolveCurrentCpaVersionForUpdateCheck();
        if (currentVersion is null)
        {
            await HandleUnknownCurrentVersionAsync(showMessageBox: true);
            return;
        }

        var updateCheck = await _updateService.CheckForUpdateAsync(_settings.CpaGitHubRepo, currentVersion);
        if (updateCheck.Status == CpaUpdateCheckStatus.UpToDate)
        {
            UpdateStatusText = "";
            AddDiagnosticLine($"[launcher] CPA 已是最新版本：{updateCheck.LatestTagName}。");
            MessageBox.Show(
                $"CPA 已是最新版本：{updateCheck.LatestTagName}。",
                "检查更新",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (updateCheck.Status == CpaUpdateCheckStatus.CheckFailed || updateCheck.UpdateInfo is null)
        {
            UpdateStatusText = "";
            AddDiagnosticLine($"[launcher] {updateCheck.FailureReason ?? "检查更新失败。"}");
            MessageBox.Show(
                updateCheck.FailureReason ?? "检查更新失败。",
                "检查更新失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var info = updateCheck.UpdateInfo;

        UpdateStatusText = $"发现新版本：{info.TagName}";
        AddDiagnosticLine($"[launcher] 发现 CPA 新版本：{info.TagName}（{info.AssetSize / 1024.0 / 1024.0:F1} MB）");

        var answer = MessageBox.Show(
            $"发现 CPA 新版本 {info.TagName}（{info.AssetSize / 1024.0 / 1024.0:F1} MB）。\n\n是否立即下载并更新？",
            "CPA 更新可用",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
        {
            await PerformUpdateAsync(info);
        }
        else
        {
            UpdateStatusText = "";
        }
    }

    private async Task PerformUpdateAsync(CpaUpdateInfo info)
    {
        IsUpdateInProgress = true;
        OnPropertyChanged(nameof(UpdateProgressVisibility));
        UpdateProgress = 0;
        UpdateStatusText = "正在下载更新...";

        // Stop CPA if running
        if (_processManager.IsManagedProcessRunning)
        {
            AddDiagnosticLine("[launcher] 更新前停止 CPA 进程...");
            await StopServiceAsync();
        }

        var progress = new Progress<(long downloaded, long total)>(p =>
        {
            if (p.total > 0)
                UpdateProgress = (int)(p.downloaded * 100 / p.total);
            UpdateStatusText = $"正在下载更新... {p.downloaded / 1024.0 / 1024.0:F1} / {p.total / 1024.0 / 1024.0:F1} MB";
        });

        var result = await _updateService.ApplyUpdateAsync(info, _settings.ExecutablePath!, progress);

        IsUpdateInProgress = false;
        OnPropertyChanged(nameof(UpdateProgressVisibility));

        if (result.Success)
        {
            _settings.LastInstalledCpaVersion = info.TagName;
            SaveSettings();
            UpdateStatusText = $"已更新到 {info.TagName}";
            AddDiagnosticLine($"[launcher] {result.Message}");

            var restart = MessageBox.Show(
                $"{result.Message}\n\n是否立即启动 CPA？",
                "更新成功",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (restart == MessageBoxResult.Yes)
            {
                await StartServiceAsync();
            }
        }
        else
        {
            UpdateStatusText = "更新失败";
            AddDiagnosticLine($"[launcher] {result.Message}");
            MessageBox.Show(result.Message, "更新失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task HandleUnknownCurrentVersionAsync(bool showMessageBox)
    {
        var latestCheck = await _updateService.CheckForUpdateAsync(_settings.CpaGitHubRepo, currentVersion: null);
        UpdateStatusText = "";

        if (latestCheck.Status == CpaUpdateCheckStatus.UpdateAvailable && latestCheck.UpdateInfo is not null)
        {
            var message = $"已获取到最新版本 {latestCheck.UpdateInfo.TagName}，但当前已配置的 CPA 没有可识别版本号，无法自动判断是否需要更新。";
            AddDiagnosticLine($"[launcher] {message}");

            if (showMessageBox)
            {
                MessageBox.Show(
                    $"{message}\n\n建议先通过启动器重新安装一次，或手动确认当前 CPA 版本后再更新。",
                    "检查更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return;
        }

        var failureMessage = latestCheck.FailureReason ?? "当前 CPA 版本号无法识别，且获取最新版本信息失败。";
        AddDiagnosticLine($"[launcher] {failureMessage}");

        if (showMessageBox)
        {
            MessageBox.Show(
                failureMessage,
                "检查更新失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private string? ResolveCurrentCpaVersionForUpdateCheck()
    {
        var configuredVersion = NormalizeVersionString(_settings.LastInstalledCpaVersion);
        if (!string.IsNullOrWhiteSpace(configuredVersion))
        {
            return configuredVersion;
        }

        if (string.IsNullOrWhiteSpace(_settings.ExecutablePath) || !File.Exists(_settings.ExecutablePath))
        {
            return null;
        }

        try
        {
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(_settings.ExecutablePath);
            return NormalizeVersionString(fileVersionInfo.ProductVersion)
                ?? NormalizeVersionString(fileVersionInfo.FileVersion);
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeVersionString(string? rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return null;
        }

        var trimmed = rawVersion.Trim();
        if (Version.TryParse(trimmed.TrimStart('v'), out _))
        {
            return trimmed;
        }

        var simplified = trimmed.Split(['+', '-', ' '], 2, StringSplitOptions.RemoveEmptyEntries)[0];
        return Version.TryParse(simplified.TrimStart('v'), out _) ? simplified : null;
    }

    // --- File browsing ---

    private Task BrowseExecutable()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            Title = "选择 cli-proxy-api.exe"
        };

        if (dialog.ShowDialog() == true)
        {
            ExecutablePath = dialog.FileName;
            _ = RefreshAsync();
        }

        return Task.CompletedTask;
    }

    private Task BrowseConfig()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "YAML 配置文件 (*.yaml;*.yml)|*.yaml;*.yml|所有文件 (*.*)|*.*",
            Title = "选择 config.yaml"
        };

        if (dialog.ShowDialog() == true)
        {
            ConfigPath = dialog.FileName;
            _ = RefreshAsync();
        }

        return Task.CompletedTask;
    }

    private Task OpenExecutableDirectory()
    {
        if (string.IsNullOrEmpty(_executablePath)) return Task.CompletedTask;
        try
        {
            var dir = Path.GetDirectoryName(_executablePath);
            if (!string.IsNullOrEmpty(dir))
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开目录：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return Task.CompletedTask;
    }

    private Task OpenConfigDirectory()
    {
        if (string.IsNullOrEmpty(_configPath)) return Task.CompletedTask;
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir))
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开目录：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return Task.CompletedTask;
    }

    // --- Log operations ---

    private Task CopyLogs()
    {
        try
        {
            var logs = string.Join(Environment.NewLine, DiagnosticLines);
            Clipboard.SetText(logs);
            MessageBox.Show("日志已复制到剪贴板。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"复制失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return Task.CompletedTask;
    }

    private Task CopyManagementSecretKey()
    {
        if (string.IsNullOrWhiteSpace(_settings.ManagementSecretKey))
        {
            MessageBox.Show("当前没有已保存的管理密钥。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.CompletedTask;
        }

        TryCopyManagementSecretKeyToClipboard(_settings.ManagementSecretKey, "管理密钥已复制到剪贴板。");
        return Task.CompletedTask;
    }

    private Task ExportLogs()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = $"cpa-launcher-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                File.WriteAllLines(dialog.FileName, DiagnosticLines);
                MessageBox.Show("日志已导出。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        return Task.CompletedTask;
    }

    private Task ClearLogs()
    {
        DiagnosticLines.Clear();
        _processManager.ClearRecentOutput();
        return Task.CompletedTask;
    }

    // --- Events ---

    private void OnProcessOutput(object? sender, string line)
    {
        Application.Current.Dispatcher.InvokeAsync(() => AddDiagnosticLine(line), DispatcherPriority.Background);
    }

    private void OnProcessExited(object? sender, int exitCode)
    {
        Application.Current.Dispatcher.InvokeAsync(async () => await RefreshAsync());
    }

    private void AddDiagnosticLine(string line)
    {
        DiagnosticLines.Add(line);
        while (DiagnosticLines.Count > 400)
            DiagnosticLines.RemoveAt(0);
    }

    // --- Window management ---

    private Task ShowWindow()
    {
        var window = Application.Current.MainWindow;
        if (window != null)
        {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        }
        return Task.CompletedTask;
    }

    private async Task ExitApplication()
    {
        if (_processManager.IsManagedProcessRunning)
        {
            var result = MessageBox.Show(new MessageBoxInfo
            {
                Message = "当前 CPA 仍由启动器托管。\n\n" +
                    "「是」= 停止 CPA 后退出\n" +
                    "「否」= 保留 CPA 运行，仅退出启动器\n" +
                    "「取消」= 不退出",
                Caption = "退出前确认",
                Button = MessageBoxButton.YesNoCancel,
                IconBrushKey = ResourceToken.AccentBrush,
                IconKey = ResourceToken.AskGeometry,
            });

            if (result == MessageBoxResult.Cancel)
                return;

            if (result == MessageBoxResult.Yes)
            {
                await _processManager.StopAsync();
            }
        }

        _refreshTimer.Stop();
        _processManager.Dispose();

        if (Application.Current.MainWindow is Views.MainWindow mainWindow)
        {
            mainWindow.MarkExiting();
        }

        Application.Current.Shutdown();
    }

    private static void ApplyTheme(bool isDark)
    {
        var app = Application.Current;
        if (app == null) return;

        var mergedDicts = app.Resources.MergedDictionaries;

        var skinUri = isDark
            ? new Uri("pack://application:,,,/HandyControl;component/Themes/SkinDark.xaml")
            : new Uri("pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml");
        var themeUri = new Uri("pack://application:,,,/HandyControl;component/Themes/Theme.xaml");

        mergedDicts.Clear();
        mergedDicts.Add(new ResourceDictionary { Source = skinUri });
        mergedDicts.Add(new ResourceDictionary { Source = themeUri });
    }

    private static string MaskManagementSecretKey(string secretKey)
    {
        if (secretKey.Length <= 8)
        {
            return secretKey;
        }

        return $"{secretKey[..4]}...{secretKey[^4..]}";
    }

    private static void TryCopyManagementSecretKeyToClipboard(string secretKey, string successMessage)
    {
        try
        {
            Clipboard.SetText(secretKey);
            MessageBox.Show(successMessage, "管理密钥", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"管理密钥已生成，但复制到剪贴板失败：{ex.Message}", "管理密钥", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string GetUnconfiguredStatusDetail()
    {
        if (!LauncherSetupDecider.HasInstalledExecutable(_settings))
        {
            return string.IsNullOrWhiteSpace(_settings.ExecutablePath)
                ? "请先下载或配置 CPA 可执行文件路径。"
                : "当前配置的 CPA 可执行文件不存在，请重新下载或选择正确路径。";
        }

        if (string.IsNullOrWhiteSpace(_settings.ConfigPath))
        {
            return "请先配置 config.yaml 路径。";
        }

        if (!File.Exists(_settings.ConfigPath))
        {
            return "当前配置的 config.yaml 不存在，请重新生成或选择正确路径。";
        }

        return "请先配置 CPA 可执行文件和配置文件路径。";
    }
}
