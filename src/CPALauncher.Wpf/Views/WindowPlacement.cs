using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CPALauncher.Views;

internal static class WindowPlacement
{
    internal static Rect GetWorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            return SystemParameters.WorkArea;

        var transform = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return new Rect(transform.Transform(new Point(info.Work.Left, info.Work.Top)),
            transform.Transform(new Point(info.Work.Right, info.Work.Bottom)));
    }

    internal static Rect FitAroundCenter(Rect current, Size size, Rect workArea)
    {
        var width = Math.Min(size.Width, workArea.Width);
        var height = Math.Min(size.Height, workArea.Height);
        var left = Math.Clamp(current.Left + (current.Width - width) / 2, workArea.Left, workArea.Right - width);
        var top = Math.Clamp(current.Top + (current.Height - height) / 2, workArea.Top, workArea.Bottom - height);
        return new Rect(left, top, width, height);
    }

    internal static void CenterOverOwner(Window window)
    {
        if (window.Owner is not { IsVisible: true } owner) return;
        var bounds = FitAroundCenter(new Rect(owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight),
            new Size(window.ActualWidth, window.ActualHeight), GetWorkArea(owner));
        window.Left = bounds.Left;
        window.Top = bounds.Top;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
}
