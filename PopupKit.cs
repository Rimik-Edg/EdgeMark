using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace EdgeMark
{
    /// <summary>Откуда открыли окно — от этого зависит, где оно появится.</summary>
    public enum Anchor
    {
        /// <summary>Рядом с язычком у края экрана.</summary>
        Tab,
        /// <summary>Над значком в трее (у курсора, прижато к панели задач).</summary>
        Tray,
        /// <summary>По центру экрана (оверлей), курсор переносится на окно.</summary>
        Center,
    }

    /// <summary>Общее для всплывающих окон у края экрана (список папок, заметки).</summary>
    public static class PopupKit
    {
        /// <summary>Размер окна в физических пикселях (до показа).</summary>
        private static (int W, int H, DpiScale Dpi) Measure(Border card, Visual dpiRef)
        {
            var dpi = VisualTreeHelper.GetDpi(dpiRef);
            card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = card.DesiredSize;
            return ((int)Math.Ceiling(size.Width * dpi.DpiScaleX), (int)Math.Ceiling(size.Height * dpi.DpiScaleY), dpi);
        }

        private static void SetPos(Window w, int x, int y, DpiScale dpi)
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = x / dpi.DpiScaleX;
            w.Top = y / dpi.DpiScaleY;
        }

        /// <summary>Разместить окно в зависимости от того, откуда его открыли.</summary>
        public static void Place(Window w, Border card, TabWindow tab, Anchor at, int tabAnchorTop)
        {
            if (at == Anchor.Tab) { PlaceNear(w, card, tab, tabAnchorTop); return; }

            var (width, height, dpi) = Measure(card, tab);
            Native.GetCursorPos(out var cur);
            var mon = Native.MonitorFromPoint(cur, Native.MONITOR_DEFAULTTONEAREST);
            var mi = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
            Native.GetMonitorInfo(mon, ref mi);
            var wa = mi.rcWork;
            int pad = (int)(10 * dpi.DpiScaleX);
            int x, y;

            if (at == Anchor.Center)
            {
                x = wa.Left + ((wa.Right - wa.Left) - width) / 2;
                y = wa.Top + ((wa.Bottom - wa.Top) - height) / 2;
            }
            else
            {
                // Трей: окно у курсора, прижато к панели задач (где бы она ни стояла).
                x = cur.X - width / 2;
                y = cur.Y - height / 2;
                if (cur.Y >= wa.Bottom) y = wa.Bottom - height - pad;      // панель снизу
                else if (cur.Y < wa.Top) y = wa.Top + pad;                  // сверху
                else if (cur.X >= wa.Right) x = wa.Right - width - pad;     // справа
                else if (cur.X < wa.Left) x = wa.Left + pad;                // слева
            }

            x = Math.Clamp(x, wa.Left + pad, Math.Max(wa.Left + pad, wa.Right - width - pad));
            y = Math.Clamp(y, wa.Top + pad, Math.Max(wa.Top + pad, wa.Bottom - height - pad));
            SetPos(w, x, y, dpi);
        }

        /// <summary>Перенести курсор в середину окна (оверлей: «окно сразу под рукой»).</summary>
        public static void CursorToCenter(Window w)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var r)) return;
            Native.SetCursorPos((r.Left + r.Right) / 2, r.Top + Math.Min(60, (r.Bottom - r.Top) / 3));
        }

        /// <summary>Оформление: системный акрил (Win11 22H2+) или своё окно со скруглением и тенью.</summary>
        public static void Setup(Window w, Border card, Action<IntPtr> onHandle)
        {
            double scale = App.Cfg.Scale;
            if (Math.Abs(scale - 1) > 0.001) card.LayoutTransform = new ScaleTransform(scale, scale);

            bool acrylic = Theme.HasAcrylic;
            if (acrylic)
            {
                Backdrop.PrepareAcrylic(w);
            }
            else
            {
                w.WindowStyle = WindowStyle.None;
                w.AllowsTransparency = true;
                w.ResizeMode = ResizeMode.NoResize;
                w.Background = Brushes.Transparent;
                card.Margin = new Thickness(14);
                card.BorderThickness = new Thickness(1);
                card.SetResourceReference(Border.BorderBrushProperty, "M.Border");
                card.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.35 };
            }

            w.SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW);
                if (acrylic) Backdrop.EnableAcrylic(w);
                onHandle?.Invoke(hwnd);
            };
        }

        /// <summary>Ставит окно рядом с язычком: сбоку от него, верх — на уровне anchorTop (физ. пиксели).</summary>
        public static void PlaceNear(Window w, Border card, TabWindow tab, int anchorTop)
        {
            var dpi = VisualTreeHelper.GetDpi(tab);
            card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = card.DesiredSize;
            int width = (int)Math.Ceiling(size.Width * dpi.DpiScaleX);
            int height = (int)Math.Ceiling(size.Height * dpi.DpiScaleY);

            var tr = tab.ScreenRect;
            var wa = Native.WorkAreaOf(tab.Handle);
            int pad = (int)(8 * dpi.DpiScaleX);
            bool right = App.Cfg.Edge == Edge.Right;

            int x = right ? tr.Left - width : tr.Right;
            int y = anchorTop - (int)(6 * dpi.DpiScaleY);
            x = Math.Clamp(x, wa.Left + pad, Math.Max(wa.Left + pad, wa.Right - width - pad));
            y = Math.Clamp(y, wa.Top + pad, Math.Max(wa.Top + pad, wa.Bottom - height - pad));

            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = x / dpi.DpiScaleX;
            w.Top = y / dpi.DpiScaleY;
        }

        /// <summary>
        /// Только что открылось (меньше 0,4 с назад)? В это время потеря фокуса окно не закрывает:
        /// её может вызвать закрытие соседнего окна, а не действие пользователя.
        /// </summary>
        public static bool JustShown(Window w) =>
            w.Tag is DateTime t && (DateTime.Now - t).TotalMilliseconds < 400;

        public static void ShowAnimated(Window w, IntPtr hwnd, FrameworkElement body, TranslateTransform slide)
        {
            w.Tag = DateTime.Now;
            bool right = App.Cfg.Edge == Edge.Right;
            body.Opacity = 0;
            w.Show();
            w.Activate();
            hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd != IntPtr.Zero) Native.SetForegroundWindow(hwnd);
            w.Focus();

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var d = TimeSpan.FromMilliseconds(160);
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(right ? 14 : -14, 0, d) { EasingFunction = ease });
            body.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, d));
        }

        /// <summary>Курсор над язычками у края (там клики обрабатывает сам язычок).</summary>
        public static bool CursorOverTab()
        {
            var tab = App.Current.Tab;
            if (tab == null || tab.Handle == IntPtr.Zero) return false;
            Native.GetCursorPos(out var p);
            return Native.Contains(tab.ScreenRect, p);
        }

        /// <summary>Нажата кнопка мыши за пределами окна и язычков.</summary>
        public static bool ClickedOutside(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            bool down = Native.IsKeyDown(Native.VK_LBUTTON) || Native.IsKeyDown(Native.VK_RBUTTON) ||
                        Native.IsKeyDown(Native.VK_MBUTTON);
            if (!down) return false;
            Native.GetCursorPos(out var p);
            Native.GetWindowRect(hwnd, out var me);
            var tab = App.Current.Tab?.ScreenRect ?? default;
            return !Native.Contains(me, p) && !Native.Contains(tab, p);
        }

        /// <summary>Положить что-то в буфер обмена и на секунду показать на кнопке ✓ (или ошибку).</summary>
        public static void CopyWithFeedback(Button btn, Action put)
        {
            if (btn.Content is string c && (c == "\uE73E" || c == "\uE783")) return; // ещё показывается прошлый результат
            var normal = btn.Content;
            bool ok = false;
            // Буфер обмена иногда занят другой программой — пробуем несколько раз.
            for (int i = 0; i < 5 && !ok; i++)
            {
                try { put(); ok = true; }
                catch { System.Threading.Thread.Sleep(30); }
            }

            btn.Content = ok ? "" : ""; // галочка / ошибка
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            t.Tick += (s, e) => { t.Stop(); btn.Content = normal; };
            t.Start();
        }
    }
}
