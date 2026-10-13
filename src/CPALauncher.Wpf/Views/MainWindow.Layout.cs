using System.Windows;
using System.Windows.Threading;

namespace CPALauncher.Views;

public partial class MainWindow
{
    private const double MinimumWidth = 860;
    private const double MinimumHeight = 560;
    private const double MaximumWidth = 1100;
    private const double MaximumHeight = 720;
    private bool _fitPending;

    private void OnRecentLogSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is CPALauncher.ViewModels.MainViewModel vm)
            vm.SetRecentLogCapacity((int)Math.Floor(Math.Max(0, e.NewSize.Height - 20) / 26));
    }

    private void RequestWindowFit()
    {
        if (!IsLoaded || _fitPending) return;
        _fitPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _fitPending = false;
            if (WindowState != WindowState.Normal) return;
            var workArea = WindowPlacement.GetWorkArea(this);
            var bounds = WindowPlacement.FitAroundCenter(
                new Rect(Left, Top, ActualWidth, ActualHeight),
                new Size(Math.Clamp(Width, MinimumWidth, MaximumWidth),
                    Math.Clamp(Height, MinimumHeight, MaximumHeight)), workArea);

            // Recalculate only for monitor/work-area changes, never for page content.
            MinWidth = MinHeight = 0;
            MaxWidth = Math.Min(MaximumWidth, workArea.Width);
            MaxHeight = Math.Min(MaximumHeight, workArea.Height);
            MinWidth = Math.Min(MinimumWidth, workArea.Width);
            MinHeight = Math.Min(MinimumHeight, workArea.Height);
            Width = bounds.Width;
            Height = bounds.Height;
            Left = bounds.Left;
            Top = bounds.Top;
        }, DispatcherPriority.Loaded);
    }
}
