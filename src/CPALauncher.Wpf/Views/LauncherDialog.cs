using System.Windows;
using HandyControl.Data;

namespace CPALauncher.Views;

public static class LauncherDialog
{
    public static MessageBoxResult Show(
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon)
    {
        var dialog = new LauncherDialogWindow(caption, messageBoxText, button, icon);
        var owner = Application.Current?.MainWindow;
        if (owner != null && owner.IsLoaded)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
        return NormalizeResult(dialog.Result, button);
    }

    public static MessageBoxResult Show(MessageBoxInfo info)
    {
        return Show(
            info.Message,
            info.Caption,
            info.Button,
            ResolveImage(info));
    }

    private static MessageBoxResult NormalizeResult(MessageBoxResult result, MessageBoxButton button)
    {
        if (result != MessageBoxResult.None)
        {
            return result;
        }

        return button switch
        {
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.OK,
        };
    }

    private static MessageBoxImage ResolveImage(MessageBoxInfo info)
    {
        if (info.IconKey == ResourceToken.ErrorGeometry)
        {
            return MessageBoxImage.Error;
        }

        if (info.IconKey == ResourceToken.WarningGeometry)
        {
            return MessageBoxImage.Warning;
        }

        if (info.IconKey == ResourceToken.AskGeometry)
        {
            return MessageBoxImage.Question;
        }

        return MessageBoxImage.Information;
    }
}
