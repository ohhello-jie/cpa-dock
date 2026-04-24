using CPALauncher.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

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
        WindowThemeResources.ApplyWizardResources(Resources, WindowThemeResources.IsDarkModeEnabled());
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
        if (e.ButtonState == MouseButtonState.Pressed && !IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            DragMove();
        }
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase or TextBoxBase or PasswordBox)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
