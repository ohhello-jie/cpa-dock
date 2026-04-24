using System.Windows;
using HandyControl.Data;

namespace CPALauncher.Views;

public static class LauncherDialog
{
    public const string NotificationToken = "MainWindowNotification";

    public static MessageBoxResult Show(
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon)
    {
        if (button == MessageBoxButton.OK)
        {
            ShowNotification(messageBoxText, caption, icon);
            return MessageBoxResult.OK;
        }

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

    private static void ShowNotification(string message, string caption, MessageBoxImage icon)
    {
        var app = Application.Current;
        if (app?.Dispatcher == null)
            return;

        app.Dispatcher.Invoke(() =>
        {
            var growlInfo = new GrowlInfo
            {
                Message = string.IsNullOrWhiteSpace(caption) ? message : $"{caption}\n{message}",
                Token = NotificationToken,
                WaitTime = ResolveWaitTime(icon),
                ShowDateTime = false,
                ShowCloseButton = false,
            };

            switch (icon)
            {
                case MessageBoxImage.Error:
                    HandyControl.Controls.Growl.Error(growlInfo);
                    break;
                case MessageBoxImage.Warning:
                    HandyControl.Controls.Growl.Warning(growlInfo);
                    break;
                case MessageBoxImage.Information:
                    HandyControl.Controls.Growl.Info(growlInfo);
                    break;
                default:
                    HandyControl.Controls.Growl.Info(growlInfo);
                    break;
            }
        });
    }

    private static int ResolveWaitTime(MessageBoxImage icon)
    {
        return icon switch
        {
            MessageBoxImage.Error => 8,
            MessageBoxImage.Warning => 6,
            _ => 4,
        };
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
