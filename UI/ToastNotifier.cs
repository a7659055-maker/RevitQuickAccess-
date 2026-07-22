using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace RevitQuickAccess.UI
{
    /// <summary>
    /// Small transient label shown at the mouse cursor (e.g. "Бинды включены" when F8 is pressed
    /// while the Revit canvas is focused). A borderless, click-through, top-most window positioned
    /// with Win32 physical coordinates so it lands exactly under the cursor at any DPI.
    /// </summary>
    public static class ToastNotifier
    {
        private static Dispatcher _dispatcher;
        private static Window _win;
        private static Border _border;
        private static TextBlock _text;
        private static DispatcherTimer _timer;

        /// <summary>Call once from the UI thread (App.OnStartup) to capture the dispatcher.</summary>
        public static void Init() => _dispatcher = Dispatcher.CurrentDispatcher;

        public static void Show(string message, bool positive)
        {
            var d = _dispatcher;
            if (d == null) return;
            // marshal off the keyboard-hook call stack
            d.BeginInvoke(new Action(() => ShowCore(message, positive)), DispatcherPriority.Normal);
        }

        private static void ShowCore(string message, bool positive)
        {
            try
            {
                EnsureWindow();
                _text.Text = message;
                _border.Background = new SolidColorBrush(positive
                    ? Color.FromRgb(0x2E, 0x8B, 0x57)    // green
                    : Color.FromRgb(0xC2, 0x44, 0x36));  // red

                _win.Opacity = 1;
                _win.Show();

                GetCursorPos(out POINT p);
                var hwnd = new WindowInteropHelper(_win).Handle;
                SetWindowPos(hwnd, HWND_TOPMOST, p.X + 16, p.Y + 20, 0, 0,
                    SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

                RestartTimer();
            }
            catch { }
        }

        private static void EnsureWindow()
        {
            if (_win != null) return;
            _text = new TextBlock
            {
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12
            };
            _border = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 7, 12, 7),
                Child = _text
            };
            _win = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                IsHitTestVisible = false,
                Content = _border
            };
            _win.SourceInitialized += (s, e) =>
            {
                // make the window click-through
                var hwnd = new WindowInteropHelper(_win).Handle;
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            };
        }

        private static void RestartTimer()
        {
            _timer?.Stop();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
            _timer.Tick += (s, e) =>
            {
                _timer.Stop();
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280));
                fade.Completed += (a, b) => { try { _win.Hide(); } catch { } };
                _win.BeginAnimation(UIElement.OpacityProperty, fade);
            };
            _timer.Start();
        }

        // ---- Win32 ----

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
