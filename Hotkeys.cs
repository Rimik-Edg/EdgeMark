using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;

namespace EdgeMark
{
    /// <summary>
    /// Глобальные горячие клавиши (работают, в какой бы программе вы ни были).
    /// Хранятся строкой вида "Ctrl+Alt+B"; пустая строка — клавиша не назначена.
    /// </summary>
    public static class Hotkeys
    {
        private const int IdMenu = 0xB001, IdNotes = 0xB002;
        private static IntPtr _hwnd;
        private static bool _suspended;

        public static void Init(IntPtr hwnd)
        {
            _hwnd = hwnd;
            HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
            Apply();
        }

        /// <summary>
        /// Зарегистрировать клавиши из настроек. Возвращает сочетания, которые не удалось назначить
        /// (обычно потому, что их уже заняла другая программа).
        /// </summary>
        public static List<string> Apply()
        {
            var failed = new List<string>();
            if (_hwnd == IntPtr.Zero) return failed;
            Native.UnregisterHotKey(_hwnd, IdMenu);
            Native.UnregisterHotKey(_hwnd, IdNotes);
            _suspended = false;
            Register(IdMenu, App.Cfg.HotkeyMenu, failed);
            Register(IdNotes, App.Cfg.HotkeyNotes, failed);
            return failed;
        }

        /// <summary>Временно снять клавиши — пока пользователь вводит новое сочетание в настройках.</summary>
        public static void Suspend()
        {
            if (_hwnd == IntPtr.Zero || _suspended) return;
            Native.UnregisterHotKey(_hwnd, IdMenu);
            Native.UnregisterHotKey(_hwnd, IdNotes);
            _suspended = true;
        }

        private static void Register(int id, string text, List<string> failed)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!TryParse(text, out var mods, out var key)) { failed.Add(text); return; }
            uint m = Native.MOD_NOREPEAT;
            if (mods.HasFlag(ModifierKeys.Control)) m |= Native.MOD_CONTROL;
            if (mods.HasFlag(ModifierKeys.Alt)) m |= Native.MOD_ALT;
            if (mods.HasFlag(ModifierKeys.Shift)) m |= Native.MOD_SHIFT;
            if (mods.HasFlag(ModifierKeys.Windows)) m |= Native.MOD_WIN;
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            if (!Native.RegisterHotKey(_hwnd, id, m, vk)) failed.Add(Display(text));
        }

        private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == IdMenu || id == IdNotes)
                {
                    handled = true;
                    App.Current.HotkeyPressed(notes: id == IdNotes);
                }
            }
            return IntPtr.Zero;
        }

        // ------------------------------------------------------------- строка <-> клавиши

        private static readonly ModifierKeys[] Order = { ModifierKeys.Control, ModifierKeys.Alt, ModifierKeys.Shift, ModifierKeys.Windows };

        private static string ModName(ModifierKeys m) => m switch
        {
            ModifierKeys.Control => "Ctrl",
            ModifierKeys.Alt => "Alt",
            ModifierKeys.Shift => "Shift",
            _ => "Win",
        };

        /// <summary>Сочетание для хранения в настройках, например "Ctrl+Alt+B".</summary>
        public static string Format(ModifierKeys mods, Key key)
        {
            var parts = new List<string>();
            foreach (var m in Order) if (mods.HasFlag(m)) parts.Add(ModName(m));
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        public static bool TryParse(string text, out ModifierKeys mods, out Key key)
        {
            mods = ModifierKeys.None;
            key = Key.None;
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var raw in text.Split('+'))
            {
                var p = raw.Trim();
                switch (p.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= ModifierKeys.Control; break;
                    case "alt": mods |= ModifierKeys.Alt; break;
                    case "shift": mods |= ModifierKeys.Shift; break;
                    case "win": case "windows": mods |= ModifierKeys.Windows; break;
                    default:
                        if (!Enum.TryParse(p, true, out key)) return false;
                        break;
                }
            }
            return key != Key.None;
        }

        /// <summary>Как показывать пользователю: "Ctrl+Alt+B", цифры без «D», «Пробел» и т.п.</summary>
        public static string Display(string text)
        {
            if (!TryParse(text, out var mods, out var key)) return text ?? "";
            var parts = new List<string>();
            foreach (var m in Order) if (mods.HasFlag(m)) parts.Add(ModName(m));
            parts.Add(KeyName(key));
            return string.Join(" + ", parts);
        }

        private static string KeyName(Key k)
        {
            if (k >= Key.D0 && k <= Key.D9) return ((int)(k - Key.D0)).ToString();
            if (k >= Key.NumPad0 && k <= Key.NumPad9) return "Num " + (int)(k - Key.NumPad0);
            return k switch
            {
                Key.Space => Loc.Pick("Пробел", "Space"),
                Key.OemTilde => Loc.Pick("` (Ё)", "`"),
                Key.OemMinus => "-",
                Key.OemPlus => "=",
                Key.OemComma => ",",
                Key.OemPeriod => ".",
                Key.OemQuestion => "/",
                Key.OemSemicolon => ";",
                Key.OemQuotes => "'",
                Key.OemOpenBrackets => "[",
                Key.OemCloseBrackets => "]",
                Key.OemPipe => "\\",
                _ => k.ToString(),
            };
        }
    }
}
