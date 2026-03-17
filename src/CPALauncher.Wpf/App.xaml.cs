using System.Windows;
using CPALauncher.Views;

namespace CPALauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var startMinimized = e.Args.Any(arg => string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));
        var mainWindow = new MainWindow(startMinimized);
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
