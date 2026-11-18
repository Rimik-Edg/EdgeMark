using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace EdgeMark
{
    /// <summary>Эффекты окон: системный акрил (Windows 11 22H2+), скругления DWM, тёмная рамка.</summary>
    public static class Backdrop
    {
        /// <summary>Готовит окно к системному акрилу. Вызывать в конструкторе окна (до показа).</summary>
        public static void PrepareAcrylic(Window w)
        {
            w.WindowStyle = WindowStyle.None;
            w.AllowsTransparency = false;
            w.ResizeMode = ResizeMode.NoResize;
            w.Background = Brushes.Transparent;
            WindowChrome.SetWindowChrome(w, new WindowChrome
            {
                CaptionHeight = 0,
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
            });
        }

        /// <summary>Включает акрил. Вызывать в SourceInitialized.</summary>
        public static void EnableAcrylic(Window w)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            var src = HwndSource.FromHwnd(hwnd);
            if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;

            var m = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            Native.DwmExtendFrameIntoClientArea(hwnd, ref m);

            SetDark(hwnd, Theme.MenuDark);

            int corner = Native.DWMWCP_ROUND;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            int backdrop = Native.DWMSBT_TRANSIENTWINDOW;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }

        /// <summary>
        /// Оформление обычного окна (настройки, «Изменить папку») под текущие цвета:
        /// тёмная/светлая рамка, а при своём цвете окна — заголовок того же цвета (Windows 11).
        /// </summary>
        public static void StyleWindow(Window w)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetDark(hwnd, Theme.UiDark);
            if (!Theme.IsWin11) return;

            const int DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;
            const uint DEFAULT = 0xFFFFFFFF;
            int caption = unchecked((int)DEFAULT), text = unchecked((int)DEFAULT);
            if (Theme.CustomWindow)
            {
                var c = Theme.WindowColor;
                caption = c.R | (c.G << 8) | (c.B << 16);
                text = Theme.UiDark ? 0xF2F2F2 : 0x1A1A1A;
            }
            Native.DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            Native.DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
        }

        public static void SetDark(Window w, bool dark) => SetDark(new WindowInteropHelper(w).Handle, dark);

        public static void SetDark(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero) return;
            int v = dark ? 1 : 0;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));
        }
    }
}
