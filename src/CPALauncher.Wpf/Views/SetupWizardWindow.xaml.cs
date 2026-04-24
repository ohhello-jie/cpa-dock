using CPALauncher.Services;
using System.Windows.Input;

namespace CPALauncher.Views;

public partial class SetupWizardWindow
{
    public string Host => HostTextBox.Text.Trim();
    public int Port => (int)PortNumericUpDown.Value;
    public string? ProxyUrl
    {
        get
        {
            var url = ProxyUrlTextBox.Text.Trim();
            return string.IsNullOrWhiteSpace(url) ? null : url;
        }
    }
    public string? SecretKey
    {
        get
        {
            var pw = SecretKeyPasswordBox.Password;
            return string.IsNullOrWhiteSpace(pw) ? null : pw;
        }
    }

    public SetupWizardWindow()
    {
        InitializeComponent();
        HostTextBox.Text = LauncherSetupDefaults.DefaultHost;
        PortNumericUpDown.Value = LauncherSetupDefaults.DefaultPort;
        ProxyUrlTextBox.Text = LauncherSetupDefaults.DefaultProxyUrl;
    }

    private void OnFinishClick(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnSkipClick(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnWindowMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
