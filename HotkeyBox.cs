using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EdgeMark
{
    /// <summary>
    /// Поле для ввода сочетания клавиш: щёлкните и нажмите нужные клавиши.
    /// Backspace / Delete — убрать сочетание. Пока поле в фокусе, глобальные клавиши программы отключены,
    /// чтобы их можно было ввести заново.
    /// </summary>
    public class HotkeyBox : TextBox
    {
        private string _hotkey = "";

        public event Action Changed;

        public HotkeyBox()
        {
            IsReadOnly = true;
            IsReadOnlyCaretVisible = false;
            Cursor = Cursors.Hand;
            MinWidth = 150;
            ToolTip = Loc.T("HotkeyTip");
            GotKeyboardFocus += (s, e) => { Hotkeys.Suspend(); UpdateText(listening: true); };
            LostKeyboardFocus += (s, e) => { Hotkeys.Apply(); UpdateText(listening: false); };
            Loc.Changed += () => { ToolTip = Loc.T("HotkeyTip"); UpdateText(IsKeyboardFocused); };
            UpdateText(false);
        }

        public string Hotkey
        {
            get => _hotkey;
            set { _hotkey = value ?? ""; UpdateText(IsKeyboardFocused); }
        }

        private void UpdateText(bool listening)
        {
            if (!string.IsNullOrEmpty(_hotkey)) Text = Hotkeys.Display(_hotkey);
            else Text = listening ? Loc.T("HotkeyPress") : Loc.T("HotkeyNone");
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Только модификатор — ждём основную клавишу.
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
                Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return;

            var mods = Keyboard.Modifiers;
            if (mods == ModifierKeys.None)
            {
                if (key is Key.Back or Key.Delete) { Hotkey = ""; Changed?.Invoke(); return; }
                if (key == Key.Escape || key == Key.Tab) { MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); return; }
                // Без Ctrl/Alt/Shift/Win разрешаем только F1–F24: иначе клавиша перестанет печататься везде.
                if (key < Key.F1 || key > Key.F24) return;
            }

            Hotkey = Hotkeys.Format(mods, key);
            Changed?.Invoke();
        }
    }
}
