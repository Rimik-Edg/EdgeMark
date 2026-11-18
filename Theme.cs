using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace EdgeMark
{
    /// <summary>Тёмная/светлая тема и акцентный цвет — берутся из Windows и обновляются на лету.</summary>
    public static class Theme
    {
        public static bool IsDark { get; private set; }
        public static Color Accent { get; private set; } = Color.FromRgb(0x00, 0x78, 0xD4);

        public static event Action Changed;

        public static bool IsWin11 => Environment.OSVersion.Version.Build >= 22000;
        public static bool HasAcrylic => Environment.OSVersion.Version.Build >= 22621;

        public static void Init()
        {
            Apply();
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.General ||
                    e.Category == UserPreferenceCategory.Color ||
                    e.Category == UserPreferenceCategory.VisualStyle)
                {
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Apply();
                        Changed?.Invoke();
                    }));
                }
            };
        }

        private static void Apply()
        {
            IsDark = ReadDark();
            Accent = ReadAccent();

            var r = Application.Current.Resources;
            var cfg = App.Cfg;

            // Свой цвет окна красит и меню, и окна настроек; иначе — тема Windows.
            CustomWindow = cfg != null && TryParse(cfg.MenuColor, out var custom);
            Color win = CustomWindow ? custom : (IsDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));
            UiDark = CustomWindow ? Luma(win) < 0.55 : IsDark;
            WindowColor = win;
            bool d = UiDark;

            Set(r, "WindowBg", win);
            Set(r, "CardBg", d ? Mix(win, Colors.White, 0.06) : Mix(win, Colors.White, 0.65));
            Set(r, "MenuBorder", d ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x24, 0x00, 0x00, 0x00));
            Set(r, "Fg", d ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x1A, 0x1A, 0x1A));
            Set(r, "SubFg", d ? Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x8F, 0x00, 0x00, 0x00));
            Set(r, "Hover", d ? Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0x00, 0x00, 0x00));
            Set(r, "Pressed", d ? Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x08, 0x00, 0x00, 0x00));
            Set(r, "Separator", d ? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x00, 0x00, 0x00));
            Set(r, "ActionHover", d ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0x00, 0x00, 0x00));
            Set(r, "Danger", d ? Color.FromRgb(0xFF, 0x99, 0xA4) : Color.FromRgb(0xC4, 0x2B, 0x1C));
            Set(r, "Success", d ? Color.FromRgb(0x6C, 0xCB, 0x5F) : Color.FromRgb(0x0F, 0x7B, 0x0F));
            Set(r, "PathBg", d ? Color.FromArgb(0x4D, 0x00, 0x00, 0x00) : Color.FromArgb(0x12, 0x00, 0x00, 0x00));
            Set(r, "InputBg", d ? Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));

            ApplyTab(r);
            ApplyMenu(r);
        }

        /// <summary>Пересчитать цвета (после смены настроек оформления).</summary>
        public static void Reapply()
        {
            Apply();
            Changed?.Invoke();
        }

        public static bool TryParse(string hex, out Color c)
        {
            c = default;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            try
            {
                c = (Color)ColorConverter.ConvertFromString(hex.Trim());
                c.A = 255;
                return true;
            }
            catch { return false; }
        }

        public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        // ------------------------------------------------------------- закладка у края

        public static Color TabColor { get; private set; }

        /// <summary>Тёмные ли окна программы (по теме Windows или по своему цвету окна).</summary>
        public static bool UiDark { get; private set; }

        /// <summary>Задан ли свой цвет окна (тогда им красится и заголовок окон).</summary>
        public static bool CustomWindow { get; private set; }

        public static Color WindowColor { get; private set; }

        private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));

        private static void ApplyTab(ResourceDictionary r)
        {
            var cfg = App.Cfg;
            TabColor = cfg != null && TryParse(cfg.TabColor, out var c) ? c : Accent;
            Set(r, "TabColor", TabColor);
            Set(r, "TabFg", Luma(TabColor) > 0.6 ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White);

            // Акцент в окнах (кнопка «Сохранить», переключатели, выделение) — тот же цвет, что у закладки.
            Set(r, "Accent", TabColor);
            Set(r, "AccentFg", Luma(TabColor) > 0.6 ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White);
            Set(r, "AccentSoft", Color.FromArgb(0x33, TabColor.R, TabColor.G, TabColor.B));
        }

        // ------------------------------------------------------------- окно со списком папок
        // У меню свои ключи (M.*): его цвет может отличаться от темы Windows,
        // например светлое меню при тёмной системе — тогда и текст в нём тёмный.

        /// <summary>Тёмное ли сейчас меню (для текста и системного акрила).</summary>
        public static bool MenuDark { get; private set; }

        private static void ApplyMenu(ResourceDictionary r)
        {
            var cfg = App.Cfg;
            Color baseC = CustomWindow
                ? WindowColor
                : (IsDark ? Color.FromRgb(0x17, 0x17, 0x19) : Color.FromRgb(0xE6, 0xE6, 0xE8));
            MenuDark = UiDark;

            // С акрилом фон слегка просвечивает, без него почти непрозрачный.
            byte alpha = HasAcrylic ? (byte)0xDE : (byte)0xF7;
            Set(r, "MenuBg", Color.FromArgb(alpha, baseC.R, baseC.G, baseC.B));

            bool d = MenuDark;
            Set(r, "M.Fg", d ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x1A, 0x1A, 0x1A));
            Set(r, "M.SubFg", d ? Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x99, 0x00, 0x00, 0x00));
            Set(r, "M.Border", d ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x26, 0x00, 0x00, 0x00));
            Set(r, "M.Separator", d ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0x00, 0x00, 0x00));

            // Строки-«ячейки»: утопленная плитка (темнее фона) со светлой кромкой снизу, без обводки.
            SetGradient(r, "M.RowBg",
                d ? Color.FromArgb(0x5C, 0x00, 0x00, 0x00) : Color.FromArgb(0x1F, 0x00, 0x00, 0x00),
                d ? Color.FromArgb(0x38, 0x00, 0x00, 0x00) : Color.FromArgb(0x0D, 0x00, 0x00, 0x00));
            SetGradient(r, "M.RowHover",
                d ? Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF),
                d ? Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
            Set(r, "M.RowPressed", d ? Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            Set(r, "M.RowEdge", d ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
            Set(r, "M.RowShadow", d ? Color.FromArgb(0x66, 0x00, 0x00, 0x00) : Color.FromArgb(0x1A, 0x00, 0x00, 0x00));
            Set(r, "M.Hover", d ? Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0x00, 0x00, 0x00));
            Set(r, "M.Pressed", d ? Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0A, 0x00, 0x00, 0x00));
            Set(r, "M.ActionBg", d ? Color.FromArgb(0x4D, 0x00, 0x00, 0x00) : Color.FromArgb(0x14, 0x00, 0x00, 0x00));
            Set(r, "M.ActionHover", d ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x24, 0x00, 0x00, 0x00));
            // Цветная полоска слева у каждой строки (как подписи строк в Re:Factory UI) — в цвет закладки.
            Set(r, "M.Stripe", Color.FromArgb(0x99, TabColor.R, TabColor.G, TabColor.B));
            Set(r, "M.StripeHover", TabColor);
        }

        private static void SetGradient(ResourceDictionary r, string key, Color top, Color bottom)
        {
            var b = new LinearGradientBrush(top, bottom, 90);
            b.Freeze();
            r[key] = b;
        }

        private static void Set(ResourceDictionary r, string key, Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            r[key] = b;
        }

        private static double Luma(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

        private static bool ReadDark()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (k?.GetValue("AppsUseLightTheme") is int v) return v == 0;
            }
            catch { }
            return false;
        }

        private static Color ReadAccent()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (k?.GetValue("AccentColor") is int v)
                {
                    uint abgr = unchecked((uint)v);
                    return Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
                }
            }
            catch { }
            return Color.FromRgb(0x00, 0x78, 0xD4);
        }
    }
}
