using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CPALauncher.Views;

public partial class LauncherDialogWindow
{
    private MessageBoxResult _result = MessageBoxResult.None;

    public LauncherDialogWindow(string caption, string message, MessageBoxButton button, MessageBoxImage image)
    {
        InitializeComponent();
        WindowThemeResources.ApplyDialogResources(Resources, WindowThemeResources.IsDarkModeEnabled());
        DataContext = new LauncherDialogViewModel(caption, message, image);
        BuildButtons(button);
    }

    public MessageBoxResult Result => _result;

    private void BuildButtons(MessageBoxButton button)
    {
        switch (button)
        {
            case MessageBoxButton.OKCancel:
                AddButton("取消", MessageBoxResult.Cancel, isPrimary: false);
                AddButton("确定", MessageBoxResult.OK, isPrimary: true);
                break;
            case MessageBoxButton.YesNo:
                AddButton("否", MessageBoxResult.No, isPrimary: false);
                AddButton("是", MessageBoxResult.Yes, isPrimary: true);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("取消", MessageBoxResult.Cancel, isPrimary: false);
                AddButton("否", MessageBoxResult.No, isPrimary: false);
                AddButton("是", MessageBoxResult.Yes, isPrimary: true);
                break;
            default:
                AddButton("确定", MessageBoxResult.OK, isPrimary: true);
                break;
        }
    }

    private void AddButton(string text, MessageBoxResult result, bool isPrimary)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource(isPrimary ? "DialogPrimaryButtonStyle" : "DialogButtonStyle"),
            Margin = new Thickness(ButtonPanel.Children.Count == 0 ? 0 : 10, 0, 0, 0),
            IsDefault = isPrimary,
            IsCancel = result == MessageBoxResult.Cancel || result == MessageBoxResult.No,
        };

        button.Click += (_, _) =>
        {
            _result = result;
            DialogResult = true;
            Close();
        };

        ButtonPanel.Children.Add(button);
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
            if (source is ButtonBase)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        _result = MessageBoxResult.Cancel;
        DialogResult = true;
        Close();
    }

    private sealed class LauncherDialogViewModel
    {
        public LauncherDialogViewModel(string caption, string message, MessageBoxImage image)
        {
            Caption = caption;
            Message = message;
            (IconGlyph, ToneText, ToneBrush) = ResolveTone(image);
        }

        public string Caption { get; }
        public string Message { get; }
        public string IconGlyph { get; }
        public string ToneText { get; }
        public Brush ToneBrush { get; }

        private static (string glyph, string toneText, Brush brush) ResolveTone(MessageBoxImage image)
        {
            return image switch
            {
                MessageBoxImage.Error => ("\uE783", "操作未完成", new SolidColorBrush(Color.FromRgb(210, 75, 75))),
                MessageBoxImage.Warning => ("\uE7BA", "需要确认", new SolidColorBrush(Color.FromRgb(221, 138, 31))),
                MessageBoxImage.Question => ("\uE9CE", "请选择下一步", new SolidColorBrush(Color.FromRgb(64, 132, 196))),
                MessageBoxImage.Information => ("\uE946", "状态提示", new SolidColorBrush(Color.FromRgb(68, 163, 110))),
                _ => ("\uE946", "状态提示", new SolidColorBrush(Color.FromRgb(64, 132, 196))),
            };
        }
    }
}
