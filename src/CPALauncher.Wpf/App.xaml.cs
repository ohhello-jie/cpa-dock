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

        var startMinimized = e.Args.Any(arg => string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));
        var mainWindow = new MainWindow(startMinimized);
        MainWindow = mainWindow;
        mainWindow.Show();
        _singleInstanceCoordinator.StartListening(HandleSingleInstanceActivation);
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
