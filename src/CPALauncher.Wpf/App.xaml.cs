using System.Windows;
using CPALauncher.Services;
using CPALauncher.Views;

namespace CPALauncher;

public partial class App : Application
{
    private SingleInstanceCoordinator? _singleInstanceCoordinator;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceCoordinator = SingleInstanceCoordinator.Create();
        if (!_singleInstanceCoordinator.IsPrimary)
        {
            _singleInstanceCoordinator.RequestActivation(e.Args);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var applyIndex = Array.IndexOf(e.Args, "--update-apply");
        var transaction = applyIndex >= 0 && applyIndex + 1 < e.Args.Length ? e.Args[applyIndex + 1] : null;
        try
        {
            if (transaction is null && LauncherUpdateRecovery.StartPendingRecovery(Environment.ProcessPath!))
            {
                Shutdown();
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"更新恢复未完成，已保留原文件。\n{ex.Message}", "CPA Dock", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        var startMinimized = e.Args.Any(arg => string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));
        var mainWindow = new MainWindow(startMinimized);
        MainWindow = mainWindow;
        if (transaction is not null)
            mainWindow.ContentRendered += (_, _) =>
            {
                try { LauncherUpdateRecovery.AcknowledgeStartup(Environment.ProcessPath!, transaction); }
                catch { Shutdown(1); }
            };
        mainWindow.Show();
        _singleInstanceCoordinator.StartListening(HandleSingleInstanceActivation);
        if (e.Args.Contains("--update-failed", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(() => LauncherDialog.Show(
                "更新未完成，已恢复旧版。请稍后重试。", "更新失败",
                MessageBoxButton.OK, MessageBoxImage.Warning));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceCoordinator?.Dispose();
        base.OnExit(e);
    }

    private void HandleSingleInstanceActivation(string[] args)
    {
        var keepMinimized = args.Any(arg => string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));
        if (keepMinimized)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            if (MainWindow is Views.MainWindow mainWindow)
            {
                mainWindow.ActivateFromSingleInstanceRequest();
            }
        });
    }
}
