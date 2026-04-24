using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CPALauncher.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;

namespace CPALauncher.Views;

public partial class MainWindow
{
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
        SetupTrayIcon();
        SetupAutoScroll();
        SetupLocalThemeSync();

        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
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

        Resources["WindowBackgroundBrush"] = CreateBackgroundBrush(isDark);
    }

    private void SetSolidResource(string key, string color)
    {
        Resources[key] = new SolidColorBrush(ParseColor(color));
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
        if (e.Action == NotifyCollectionChangedAction.Add && DiagnosticListBox.Items.Count > 0)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(DiagnosticListBox);
            if (scrollViewer != null)
            {
                var isAtBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 20;
                if (isAtBottom)
                {
                    scrollViewer.ScrollToBottom();
                    scrollViewer.ScrollToHorizontalOffset(0);
                }
            }
            else
            {
                // Fallback: 如果还没渲染出 ScrollViewer
                DiagnosticListBox.ScrollIntoView(DiagnosticListBox.Items[^1]);
            }
        }
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

        _trayIcon?.Dispose();
        base.OnClosing(e);
    }

    internal void MarkExiting() => _isExiting = true;
}
