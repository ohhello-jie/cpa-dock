namespace CPALauncher;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var startMinimized = args.Any(arg => string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(startMinimized));
    }
}
