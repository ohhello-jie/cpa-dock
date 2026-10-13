using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CPALauncher.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;

namespace CPALauncher.Views;

public partial class MainWindow
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private TaskbarIcon? _trayIcon;
    private bool _isExiting;
    private bool _isExitPromptOpen;
    private DispatcherTimer? _dragLeaveClearTimer;
    private bool _isLogScrollPending;
    private System.Windows.Size? _sizeBeforeMinimize;
    private System.Windows.Size _lastNormalSize = new(900, 560);

    public MainWindow() : this(new MainViewModel()) { }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SizeChanged += (_, e) =>
        {
            if (WindowState == WindowState.Normal && e.NewSize.Width > 0 && e.NewSize.Height > 0)
                _lastNormalSize = e.NewSize;
        };
        try
        {
            Icon = new System.Windows.Media.Imaging.BitmapImage(
                new Uri($"pack://application:,,,/{typeof(MainWindow).Assembly.GetName().Name};component/Assets/app.ico"));
        }
        catch { /* 图标加载失败不阻塞启动 */ }
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    public MainWindow(bool startMinimized) : this()
    {
        if (startMinimized)
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        HandyControl.Controls.Growl.Register(LauncherDialog.NotificationToken, MainGrowlPanel);
        SetupTrayIcon();
        SetupAutoScroll();
        SetupLocalThemeSync();
        RequestWindowFit();

        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0112 && (wParam.ToInt64() & 0xFFF0) == 0xF030)
        {
            handled = true; // Keep maximization disabled while allowing bounded resizing.
            return IntPtr.Zero;
        }
        if (msg is 0x02E0 or 0x007E or 0x001A or 0x0232)
            RequestWindowFit(); // DPI, display, work-area changes, or move to another monitor.
        if (msg == WmGetMinMaxInfo)
        {
            handled = TryApplyWindowBounds(hwnd, lParam);
        }

        return IntPtr.Zero;
    }

    private bool TryApplyWindowBounds(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>(),
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var workArea = monitorInfo.WorkArea;
        var monitorArea = monitorInfo.MonitorArea;

        minMaxInfo.MaxPosition.X = workArea.Left - monitorArea.Left;
        minMaxInfo.MaxPosition.Y = workArea.Top - monitorArea.Top;
        var toDevice = HwndSource.FromHwnd(hwnd)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var width = Math.Min(workArea.Right - workArea.Left, (int)Math.Round(MaximumWidth * toDevice.M11));
        var height = Math.Min(workArea.Bottom - workArea.Top, (int)Math.Round(MaximumHeight * toDevice.M22));
        minMaxInfo.MaxSize = new NativePoint { X = width, Y = height };
        minMaxInfo.MaxTrackSize = minMaxInfo.MaxSize;
        minMaxInfo.MinTrackSize = new NativePoint
        {
            X = Math.Min(width, (int)Math.Round(MinimumWidth * toDevice.M11)),
            Y = Math.Min(height, (int)Math.Round(MinimumHeight * toDevice.M22)),
        };

        Marshal.StructureToPtr(minMaxInfo, lParam, fDeleteOld: false);
        return true;
    }

    private void SetupLocalThemeSync()
    {
        if (DataContext is not MainViewModel vm)
            return;

        ApplyLocalChromeTheme(vm.IsDarkMode);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsDarkMode))
            {
                ApplyLocalChromeTheme(vm.IsDarkMode);
            }
        };
    }

    private void ApplyLocalChromeTheme(bool isDark)
    {
        SetSolidResource("SidebarBrush", isDark ? "#24262A" : "#FAFAFA");
        SetSolidResource("InkBrush", isDark ? "#E8EAED" : "#23262C");
        SetSolidResource("MutedBrush", isDark ? "#9DA3AE" : "#777D86");
        SetSolidResource("SoftMutedBrush", isDark ? "#7D8595" : "#A0A6B1");
        SetSolidResource("LineBrush", isDark ? "#343840" : "#E5E7EB");
        SetSolidResource("CardBrush", isDark ? "#1C1E23" : "#FFFFFF");
        SetSolidResource("PanelBrush", isDark ? "#24272D" : "#F5F6F7");
        SetSolidResource("ActionBrush", isDark ? "#1C1E23" : "#FFFFFF");
        SetSolidResource("ActionHoverBrush", isDark ? "#30343C" : "#EFF0F2");
        SetSolidResource("DockPrimaryBackgroundBrush", isDark ? "#E8EAED" : "#292C31");
        SetSolidResource("DockPrimaryForegroundBrush", isDark ? "#1C1E23" : "#FFFFFF");
        SetSolidResource("AccentBrush", isDark ? "#E8EAED" : "#292C31");
        SetSolidResource("LinkBrush", isDark ? "#E8EAED" : "#23262C");
        SetSolidResource("AccentSoftBrush", isDark ? "#24272D" : "#F5F6F7");
        SetSolidResource("SwitchBrush", isDark ? "#454B55" : "#CBCED3");
        SetSolidResource("WindowBackgroundBrush", isDark ? "#1C1E23" : "#FFFFFF");
    }

    private void SetSolidResource(string key, string color)
    {
        var nextColor = ParseColor(color);
        if (Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = nextColor;
            return;
        }

        Resources[key] = new SolidColorBrush(nextColor);
    }

    private static System.Windows.Media.Color ParseColor(string color)
        => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        for (var source = e.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase or TabItem)
                return;
        }
        if (e.ClickCount == 2)
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void SetupAutoScroll()
    {
        if (DataContext is MainViewModel vm)
        {
            vm.DiagnosticLines.CollectionChanged += OnDiagnosticLinesChanged;
        }
    }

    private void OnDiagnosticLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
        {
            return;
        }

        ScrollToLatestDiagnostic();
    }

    private void OnDiagnosticVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) ScrollToLatestDiagnostic();
    }

    private void ScrollToLatestDiagnostic()
    {
        if (_isLogScrollPending || DiagnosticListBox is null || !DiagnosticListBox.IsVisible
            || DataContext is not MainViewModel { AutoScrollLogs: true }) return;
        _isLogScrollPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _isLogScrollPending = false;
            if (!DiagnosticListBox.IsVisible || DiagnosticListBox.Items.Count == 0
                || DataContext is not MainViewModel { AutoScrollLogs: true }) return;
            DiagnosticListBox.ScrollIntoView(DiagnosticListBox.Items[^1]);
        }, DispatcherPriority.Background);
    }

    private void OnDragEnter(object sender, DragEventArgs e) => HandleTokenDragPreview(e);

    private void OnDragOver(object sender, DragEventArgs e) => HandleTokenDragPreview(e);

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        ScheduleTokenDropPreviewClear();
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        CancelScheduledTokenDropPreviewClear();

        if (DataContext is MainViewModel vm)
        {
            await vm.ImportDroppedTokensAsync(GetDroppedFilePaths(e));
        }

        e.Handled = true;
    }

    private void HandleTokenDragPreview(DragEventArgs e)
    {
        CancelScheduledTokenDropPreviewClear();

        if (DataContext is MainViewModel vm)
        {
            var filePaths = GetDroppedFilePaths(e);
            vm.PreviewTokenDrop(filePaths);
            e.Effects = vm.IsTokenDropValid ? DragDropEffects.Copy : DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void ScheduleTokenDropPreviewClear()
    {
        if (_dragLeaveClearTimer == null)
        {
            _dragLeaveClearTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(80),
            };
            _dragLeaveClearTimer.Tick += OnDragLeaveClearTimerTick;
        }

        _dragLeaveClearTimer.Stop();
        _dragLeaveClearTimer.Start();
    }

    private void CancelScheduledTokenDropPreviewClear()
    {
        _dragLeaveClearTimer?.Stop();
    }

    private void OnDragLeaveClearTimerTick(object? sender, EventArgs e)
    {
        _dragLeaveClearTimer?.Stop();
        (DataContext as MainViewModel)?.ClearTokenDropPreview();
    }

    private static IReadOnlyList<string> GetDroppedFilePaths(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return Array.Empty<string>();
        }

        return e.Data.GetData(DataFormats.FileDrop) is string[] filePaths
            ? filePaths
            : Array.Empty<string>();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private void SetupTrayIcon()
    {
        var iconStream = Application.GetResourceStream(new Uri($"pack://application:,,,/{typeof(MainWindow).Assembly.GetName().Name};component/Assets/app.ico"));
        var icon = iconStream != null ? new Icon(iconStream.Stream) : SystemIcons.Application;

        _trayIcon = new TaskbarIcon
        {
            Icon = icon,
            ToolTipText = "CPA Dock",
        };

        _trayIcon.TrayMouseDoubleClick += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };

        var menu = new ContextMenu();

        var showItem = new MenuItem { Header = "显示主窗口", FontWeight = FontWeights.SemiBold };
        showItem.Click += (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); };
        menu.Items.Add(showItem);

        menu.Items.Add(new Separator());

        var startItem = new MenuItem { Header = "启动 CPA", Command = (DataContext as MainViewModel)?.StartCommand };
        menu.Items.Add(startItem);

        var stopItem = new MenuItem { Header = "停止 CPA", Command = (DataContext as MainViewModel)?.StopCommand };
        menu.Items.Add(stopItem);

        var mgmtItem = new MenuItem { Header = "打开管理页", Command = (DataContext as MainViewModel)?.OpenManagementCommand };
        menu.Items.Add(mgmtItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "退出启动器" };
        exitItem.Click += (_, _) => (DataContext as MainViewModel)?.ExitApplicationCommand.Execute(null);
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenu = menu;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        SizeToContent = SizeToContent.Manual;
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            return;
        }
        if (WindowState == WindowState.Minimized)
            _sizeBeforeMinimize = _lastNormalSize;
        if (WindowState == WindowState.Normal)
        {
            if (_sizeBeforeMinimize is { } restoreSize)
            {
                _sizeBeforeMinimize = null;
                // Apply after native restore, which may retain an older normal rectangle.
                Dispatcher.BeginInvoke(() =>
                {
                    if (WindowState != WindowState.Normal) return;
                    Width = restoreSize.Width;
                    Height = restoreSize.Height;
                    RequestWindowFit();
                }, DispatcherPriority.Loaded);
            }
            else RequestWindowFit();
        }

        if (WindowState == WindowState.Minimized && DataContext is MainViewModel { MinimizeToTrayOnClose: true })
        {
            Hide();
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_isExiting)
        {
            HandyControl.Controls.Growl.Unregister(LauncherDialog.NotificationToken, MainGrowlPanel);
            _trayIcon?.Dispose();
            base.OnClosing(e);
            return;
        }

        if (!_isExiting && DataContext is MainViewModel vm && vm.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        if (DataContext is MainViewModel closeVm)
        {
            e.Cancel = true;
            if (_isExitPromptOpen)
                return;

            _isExitPromptOpen = true;
            try
            {
                await closeVm.ExitFromMainWindowCloseAsync();
            }
            finally
            {
                _isExitPromptOpen = false;
            }

            return;
        }

        HandyControl.Controls.Growl.Unregister(LauncherDialog.NotificationToken, MainGrowlPanel);
        _trayIcon?.Dispose();
        base.OnClosing(e);
    }

    internal void ActivateFromSingleInstanceRequest()
    {
        ShowInTaskbar = true;
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    internal void MarkExiting() => _isExiting = true;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
