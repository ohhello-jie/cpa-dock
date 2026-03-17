using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CPALauncher.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;

namespace CPALauncher.Views;

public partial class MainWindow
{
    private TaskbarIcon? _trayIcon;
    private bool _isExiting;

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

        if (WindowState == WindowState.Minimized)
        {
            Hide();
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
        if (e.Action == NotifyCollectionChangedAction.Add && DiagnosticListBox.Items.Count > 0)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(DiagnosticListBox);
            if (scrollViewer != null)
            {
                var isAtBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 20;
                if (isAtBottom)
                {
                    DiagnosticListBox.ScrollIntoView(DiagnosticListBox.Items[^1]);
                }
            }
            else
            {
                // Fallback: 如果还没渲染出 ScrollViewer
                DiagnosticListBox.ScrollIntoView(DiagnosticListBox.Items[^1]);
            }
        }
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

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExiting && DataContext is MainViewModel vm && vm.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _trayIcon?.Dispose();
        base.OnClosing(e);
    }

    internal void MarkExiting() => _isExiting = true;
}
