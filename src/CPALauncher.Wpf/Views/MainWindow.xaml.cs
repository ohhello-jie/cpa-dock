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

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            Icon = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Assets/app.ico"));
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

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo)
        {
            handled = TryApplyMaximizedBounds(hwnd, lParam);
        }

        return IntPtr.Zero;
    }

    private static bool TryApplyMaximizedBounds(IntPtr hwnd, IntPtr lParam)
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
        minMaxInfo.MaxSize.X = workArea.Right - workArea.Left;
        minMaxInfo.MaxSize.Y = workArea.Bottom - workArea.Top;

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
        SetSolidResource("InkBrush", isDark ? "#EAF1F8" : "#142233");
        SetSolidResource("MutedBrush", isDark ? "#B3C0CF" : "#536376");
        SetSolidResource("SoftMutedBrush", isDark ? "#8FA0B3" : "#708092");
        SetSolidResource("LineBrush", isDark ? "#304155" : "#D9E2EC");
        SetSolidResource("CardBrush", isDark ? "#182638" : "#EEF4FA");
        SetSolidResource("CardStrongBrush", isDark ? "#1D2D42" : "#F6FAFE");
        SetSolidResource("PanelBrush", isDark ? "#142131" : "#EAF1F8");
        SetSolidResource("WindowEdgeBrush", isDark ? "#203148" : "#E8F0F8");
        SetSolidResource("CardBorderBrush", isDark ? "#2A3D55" : "#F7FBFF");
        SetSolidResource("ActionBrush", isDark ? "#1B2B3F" : "#F8FBFE");
        SetSolidResource("ActionHoverBrush", isDark ? "#24364D" : "#FFFFFF");
        SetSolidResource("ActionPressedBrush", isDark ? "#152236" : "#EDF4FA");
        SetSolidResource("ChromeHoverBrush", isDark ? "#24364D" : "#DDE7F0");
        SetSolidResource("ChromePressedBrush", isDark ? "#2A405A" : "#CEDCE8");
        SetSolidResource("SwitchTrackBrush", isDark ? "#506175" : "#CBD5DF");
        SetSolidResource("SwitchThumbBrush", isDark ? "#EAF1F8" : "#F8FBFE");
        SetSolidResource("SwitchCheckedBrush", isDark ? "#5AAE82" : "#59A778");
        SetSolidResource("StatusHaloBrush", isDark ? "#203F34" : "#CFEADF");
        SetSolidResource("DecorativeStrokeBrush", isDark ? "#2A3C52" : "#D9E3EE");
        SetSolidResource("DecorativeStrokeAltBrush", isDark ? "#24364D" : "#E2EAF2");
        SetSolidResource("VersionCardBrush", isDark ? "#17283B" : "#F7FBFE");
        SetSolidResource("WarningCardBrush", isDark ? "#342A20" : "#F3EADF");
        SetSolidResource("WarningTextBrush", isDark ? "#F1B35F" : "#8C530F");
        SetSolidResource("WarningStrongTextBrush", isDark ? "#F5A94F" : "#9A4D08");
        SetSolidResource("OverlayPanelBrush", isDark ? "#172638" : "#F8FBFE");
        SetSolidResource("DropOverlayScrimBrush", isDark ? "#99060D16" : "#660D1722");

        Resources["WindowBackgroundBrush"] = CreateBackgroundBrush(isDark);
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

    private static LinearGradientBrush CreateBackgroundBrush(bool isDark)
    {
        var colors = isDark
            ? new[] { "#101B29", "#152336", "#0F1926" }
            : new[] { "#F7FAFD", "#E8F0F8", "#F4F8FC" };

        return new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1),
            GradientStops =
            [
                new GradientStop(ParseColor(colors[0]), 0),
                new GradientStop(ParseColor(colors[1]), 0.62),
                new GradientStop(ParseColor(colors[2]), 1),
            ],
        };
    }

    private static System.Windows.Media.Color ParseColor(string color)
        => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
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

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
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

        Dispatcher.BeginInvoke(() =>
        {
            if (DiagnosticListBox.Items.Count <= 0)
            {
                return;
            }

            var lastItem = DiagnosticListBox.Items[DiagnosticListBox.Items.Count - 1];
            DiagnosticListBox.ScrollIntoView(lastItem);

            var scrollViewer = FindVisualChild<ScrollViewer>(DiagnosticListBox);
            scrollViewer?.ScrollToBottom();
            scrollViewer?.ScrollToHorizontalOffset(0);
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
        var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        var icon = iconStream != null ? new Icon(iconStream.Stream) : SystemIcons.Application;

        _trayIcon = new TaskbarIcon
        {
            Icon = icon,
            ToolTipText = "CPA Launcher",
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

        var startItem = new MenuItem { Header = "启动 CPA" };
        startItem.Click += (_, _) => (DataContext as MainViewModel)?.StartCommand.Execute(null);
        menu.Items.Add(startItem);

        var stopItem = new MenuItem { Header = "停止 CPA" };
        stopItem.Click += (_, _) => (DataContext as MainViewModel)?.StopCommand.Execute(null);
        menu.Items.Add(stopItem);

        var mgmtItem = new MenuItem { Header = "打开管理页" };
        mgmtItem.Click += (_, _) => (DataContext as MainViewModel)?.OpenManagementCommand.Execute(null);
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
