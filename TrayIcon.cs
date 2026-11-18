using System;
using System.Windows;
using WF = System.Windows.Forms;

namespace EdgeMark
{
    /// <summary>
    /// Два значка в трее: «закладки» и «заметки». Левый клик открывает соответствующее окно,
    /// правый — общее меню (настройки, автозапуск, выход).
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly WF.NotifyIcon _bookmarks;
        private readonly WF.NotifyIcon _notes;
        private readonly WF.ToolStripMenuItem _autostart;

        private static System.Drawing.Icon LoadIcon(string name)
        {
            try
            {
                var s = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/" + name)).Stream;
                return new System.Drawing.Icon(s, WF.SystemInformation.SmallIconSize);
            }
            catch
            {
                return System.Drawing.SystemIcons.Application;
            }
        }

        public TrayIcon()
        {
            var menu = new WF.ContextMenuStrip();
            var bookmarks = new WF.ToolStripMenuItem("", null, (s, e) => App.Current.ToggleMenu(Anchor.Tray));
            var notes = new WF.ToolStripMenuItem("", null, (s, e) => App.Current.ToggleNotes(Anchor.Tray));
            var settings = new WF.ToolStripMenuItem("", null, (s, e) => App.Current.OpenSettings());
            var config = new WF.ToolStripMenuItem("", null, (s, e) => App.Current.OpenConfigFile());
            _autostart = new WF.ToolStripMenuItem("") { CheckOnClick = true };
            _autostart.Click += (s, e) => Autostart.IsEnabled = _autostart.Checked;
            var exit = new WF.ToolStripMenuItem("", null, (s, e) => App.Current.ExitApp());

            menu.Items.AddRange(new WF.ToolStripItem[]
            {
                bookmarks, notes, settings, config, new WF.ToolStripSeparator(),
                _autostart, new WF.ToolStripSeparator(), exit,
            });
            menu.Opening += (s, e) => _autostart.Checked = Autostart.IsEnabled;

            _bookmarks = new WF.NotifyIcon { Icon = LoadIcon("app.ico"), ContextMenuStrip = menu, Visible = true };
            _notes = new WF.NotifyIcon { Icon = LoadIcon("notes.ico"), ContextMenuStrip = menu, Visible = true };

            _bookmarks.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) App.Current.TrayMenuClicked(); };
            _notes.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) App.Current.TrayNotesClicked(); };

            // Надписи — на текущем языке; при смене языка обновляются.
            void Texts()
            {
                bookmarks.Text = Loc.T("TrayBookmarks");
                notes.Text = Loc.T("TrayNotes");
                settings.Text = Loc.T("TraySettings");
                config.Text = Loc.T("OpenConfig");
                _autostart.Text = Loc.T("Autostart");
                exit.Text = Loc.T("Exit");
                _bookmarks.Text = Loc.T("AppName") + " — " + Loc.T("Bookmarks");
                _notes.Text = Loc.T("AppName") + " — " + Loc.T("Notes");
            }
            Texts();
            Loc.Changed += Texts;
        }

        public void Dispose()
        {
            _bookmarks.Visible = false;
            _notes.Visible = false;
            _bookmarks.Dispose();
            _notes.Dispose();
        }
    }
}
