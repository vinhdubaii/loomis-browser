using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RemiBrowser.Interop
{
    /// <summary>
    /// Fixes the classic WindowChrome + WindowStyle="None" + Maximized bug.
    ///
    /// Without this, WPF simply stretches a borderless maximized window to the
    /// *full monitor bounds*, which overlaps the taskbar and overflows past the
    /// screen edges by the amount the OS silently reserves for window borders —
    /// that's what produced the black gaps above the toolbar, the sliver on the
    /// left edge, and owned dialogs (Settings, Customize Background) rendering
    /// in the wrong place. A previous attempt patched this by adding a margin
    /// to the content (SystemParameters.WindowResizeBorderThickness +
    /// WindowNonClientFrameThickness), which is a known-fragile technique —
    /// it over-compensates on many DPI/monitor configurations, which is exactly
    /// what caused the black gaps to appear in the first place.
    ///
    /// This is the actually-correct, standard fix: hook WM_GETMINMAXINFO — the
    /// same Windows message native maximized windows use — so the OS itself
    /// sizes/positions the window to exactly the monitor's *work area* (screen
    /// minus taskbar). With this in place, no margin or padding hack is needed
    /// anywhere in XAML or code-behind; RootGrid.Margin can just stay 0 always.
    ///
    /// Usage: call WindowMaximizeFix.Apply(this) once from any
    /// WindowStyle="None" + WindowChrome window's constructor, right after
    /// InitializeComponent().
    ///
    /// Optional second argument (useFullMonitorBounds): a callback the window
    /// can use to opt into true F11-style full screen. When it returns true,
    /// WM_GETMINMAXINFO is answered with the monitor's *full* bounds (rcMonitor)
    /// instead of the work area (rcWork) — i.e. the window is allowed to cover
    /// the taskbar too, exactly like Chrome/Edge full screen. It's re-checked
    /// on every maximize, so the same window can toggle between "maximized
    /// (work area only)" and "full screen (whole monitor)" just by changing
    /// what the callback returns and re-triggering WindowState = Maximized.
    /// </summary>
    public static class WindowMaximizeFix
    {
        public static void Apply(Window window, Func<bool>? useFullMonitorBounds = null)
        {
            window.SourceInitialized += (_, _) =>
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (HwndSource.FromHwnd(handle) is { } hwndSource)
                {
                    hwndSource.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                    {
                        if (msg == WM_GETMINMAXINFO)
                        {
                            ApplyAreaToMinMaxInfo(hwnd, lParam, useFullMonitorBounds?.Invoke() ?? false);
                            handled = true;
                        }
                        return IntPtr.Zero;
                    });
                }
            };
        }

        private const int WM_GETMINMAXINFO = 0x0024;
        private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        private static void ApplyAreaToMinMaxInfo(IntPtr hwnd, IntPtr lParam, bool useFullMonitorBounds)
        {
            var minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                GetMonitorInfo(monitor, ref monitorInfo);

                var monitorArea = monitorInfo.rcMonitor;
                var targetArea = useFullMonitorBounds ? monitorArea : monitorInfo.rcWork;

                // Position is relative to the monitor's own top-left, not the
                // virtual desktop's — this is what makes multi-monitor setups
                // maximize onto the correct screen instead of monitor 0.
                minMaxInfo.ptMaxPosition.X = targetArea.Left - monitorArea.Left;
                minMaxInfo.ptMaxPosition.Y = targetArea.Top - monitorArea.Top;
                minMaxInfo.ptMaxSize.X = targetArea.Right - targetArea.Left;
                minMaxInfo.ptMaxSize.Y = targetArea.Bottom - targetArea.Top;
            }

            Marshal.StructureToPtr(minMaxInfo, lParam, true);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }
    }
}
