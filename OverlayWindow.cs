using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace EdgeMark
{
    /// <summary>
    /// Затемнение поверх всех окон (как оверлей Steam). Закрывает собой все экраны,
    /// поэтому остальные программы под ним недоступны, пока оно открыто.
    /// Закрывается по Esc, по клику в затемнение или когда закрылось окно закладок/заметок.
    /// </summary>
    public class OverlayWindow : Window
    {
        private bool _closing;

        public OverlayWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = true;
            Title = "EdgeMark overlay";
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00));
            Opacity = 0;
            Left = -30000; Top = -30000; Width = 10; Height = 10; // настоящий размер задаётся ниже, в пикселях

            SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW);
                // На все мониторы сразу, в физических пикселях (при разном масштабе мониторов DIP не годятся).
                int x = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
                int y = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
                int w = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
                int h = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_SHOWWINDOW);
            };

            Loaded += (s, e) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

            MouseDown += (s, e) => App.Current.CloseOverlay();
            KeyDown += (s, e) => { if (e.Key == Key.Escape) App.Current.CloseOverlay(); };
        }

        public void FadeOutAndClose()
        {
            if (_closing) return;
            _closing = true;
            var a = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(120));
            a.Completed += (s, e) => Close();
            BeginAnimation(OpacityProperty, a);
        }
    }
}
