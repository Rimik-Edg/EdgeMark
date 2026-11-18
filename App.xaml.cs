using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace EdgeMark
{
    public partial class App : Application
    {
        public static Config Cfg { get; private set; }

        private static Mutex _mutex;
        private TabWindow _tab;
        private MenuWindow _menu;
        private SettingsWindow _settings;
        private TrayIcon _tray;

        public static new App Current => (App)Application.Current;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, "EdgeMark_SingleInstance_7F3A2C", out bool first);
            if (!first)
            {
                MessageBox.Show(Loc.T("AlreadyRunning"), Loc.T("AppName"));
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (s, ex) =>
            {
                Log(ex.Exception);
                ex.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => Log(ex.ExceptionObject as Exception);

            base.OnStartup(e);

            Loc.Apply(Loc.SystemLang); // чтобы сообщения при чтении настроек уже были на нужном языке
            Cfg = Config.Load();
            Loc.Apply(Loc.Resolve(Cfg.Language));
            Autostart.MigrateOldName();
            Theme.Init();

            _tab = new TabWindow();
            _tab.Show();

            _tray = new TrayIcon();
            Theme.Changed += () => IconCache.Clear();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            base.OnExit(e);
        }

        // ------------------------------------------------------------------ окна закладок и заметок
        // Открыть можно от язычка, из трея, горячей клавишей или в оверлее.
        // Одновременно открыто только одно из двух окон.

        public bool MenuOpen => _menu != null;

        private NotesWindow _notes;
        private NoteEditWindow _noteEdit;

        public bool NotesOpen => _notes != null;

        // Когда окно закрылось (для кликов по значку в трее: клик сначала закрывает окно «как клик мимо»,
        // и тот же клик не должен открыть его снова).
        private DateTime _menuClosedAt, _notesClosedAt;

        public void ToggleMenu(Anchor at = Anchor.Tab)
        {
            if (_menu != null) { _menu.CloseMenu(); return; }
            OpenMenu(at, fromDrag: false);
        }

        public void OpenMenu(Anchor at, bool fromDrag)
        {
            if (_menu != null) return;
            // Сначала показываем новое окно (оно забирает фокус), и только потом закрываем заметки.
            // Если закрыть первым старое, Windows отдаст фокус другой программе и новое окно не откроется.
            var old = _notes;
            ShowMenuNow(at, fromDrag);
            old?.CloseNotes();
        }

        private void ShowMenuNow(Anchor at, bool fromDrag)
        {
            _lastMenuAnchor = at;
            _menu = new MenuWindow(Cfg, fromDrag);
            if (at == Anchor.Center && _overlay != null) _menu.Owner = _overlay;
            _menu.Closed += (s, e) =>
            {
                if (ReferenceEquals(_menu, s)) _menu = null;
                _menuClosedAt = DateTime.Now;
                _tab?.MenuClosed();
                CloseOverlayIfIdle();
            };
            _menu.ShowAt(_tab, at);
            _tab?.UpdateHitZones();
        }

        public void CloseMenu() => _menu?.CloseMenu();

        public void ToggleNotes(Anchor at = Anchor.Tab)
        {
            if (_notes != null) _notes.CloseNotes();
            else OpenNotes(at);
        }

        /// <param name="fromDrag">Открыто, пока пользователь тащит файл: не закрывать, пока кнопка мыши зажата.</param>
        public void OpenNotes(Anchor at = Anchor.Tab, bool fromDrag = false)
        {
            if (_notes != null) return;
            var old = _menu; // см. OpenMenu: сначала новое окно, потом закрываем старое
            ShowNotesNow(at, fromDrag);
            old?.CloseMenu();
        }

        private void ShowNotesNow(Anchor at, bool fromDrag)
        {
            _lastNotesAnchor = at;
            _notes = new NotesWindow(fromDrag);
            if (at == Anchor.Center && _overlay != null) _notes.Owner = _overlay;
            _notes.Closed += (s, e) =>
            {
                if (ReferenceEquals(_notes, s)) _notes = null;
                _notesClosedAt = DateTime.Now;
                _tab?.NotesClosed();
                CloseOverlayIfIdle();
            };
            _notes.ShowAt(_tab, at);
            _tab?.UpdateHitZones();
        }

        public void CloseNotes() => _notes?.CloseNotes();

        // ------------------------------------------------------------------ трей

        public void TrayMenuClicked()
        {
            if (_menu != null) { CloseMenu(); return; }
            if ((DateTime.Now - _menuClosedAt).TotalMilliseconds < 400) return; // этот же клик только что закрыл окно
            OpenMenu(Anchor.Tray, fromDrag: false);
        }

        public void TrayNotesClicked()
        {
            if (_notes != null) { CloseNotes(); return; }
            if ((DateTime.Now - _notesClosedAt).TotalMilliseconds < 400) return;
            OpenNotes(Anchor.Tray);
        }

        // ------------------------------------------------------------------ горячие клавиши и оверлей

        private OverlayWindow _overlay;

        public bool OverlayOpen => _overlay != null;

        /// <summary>Нажата горячая клавиша закладок (notes = false) или заметок (notes = true).</summary>
        public void HotkeyPressed(bool notes)
        {
            bool shownNow = notes ? _notes != null : _menu != null;

            if (!Cfg.HotkeyOverlay)
            {
                if (notes) ToggleNotes(Anchor.Tab); else ToggleMenu(Anchor.Tab);
                return;
            }

            if (_overlay != null)
            {
                // Повторное нажатие той же клавиши — закрыть; другой — переключиться, не убирая затемнения.
                if (shownNow) { CloseMenu(); CloseNotes(); return; }
                if (notes) OpenNotes(Anchor.Center); else OpenMenu(Anchor.Center, fromDrag: false);
                return;
            }

            CloseMenu();
            CloseNotes();
            _overlay = new OverlayWindow();
            _overlay.Closed += (s, e) => _overlay = null;
            _overlay.Show();
            _tab?.HideForOverlay();

            // Открываем окно после того, как старые окна (если были) закрылись.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_overlay == null) return;
                if (notes) OpenNotes(Anchor.Center); else OpenMenu(Anchor.Center, fromDrag: false);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>Убрать затемнение, когда в нём не осталось открытых окон (с задержкой: возможно, открывается соседнее).</summary>
        private void CloseOverlayIfIdle()
        {
            if (_overlay == null) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_overlay != null && _menu == null && _notes == null) _overlay.FadeOutAndClose();
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        /// <summary>Клик по затемнению или Esc — закрыть всё.</summary>
        public void CloseOverlay()
        {
            CloseMenu();
            CloseNotes();
            _overlay?.FadeOutAndClose();
        }

        /// <summary>Открыть заметку в редакторе (null — новая).</summary>
        public void EditNote(string path)
        {
            CloseNotes();
            if (_noteEdit != null)
            {
                // Та же заметка уже открыта — просто показать окно.
                if (path != null && string.Equals(_noteEdit.NotePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    _noteEdit.Activate();
                    return;
                }
                _noteEdit.Close();
                if (_noteEdit != null) return; // пользователь отменил закрытие несохранённой заметки
            }
            var w = new NoteEditWindow(path);
            w.Closed += (s, e) => { if (ReferenceEquals(_noteEdit, s)) _noteEdit = null; };
            _noteEdit = w;
            w.Show();
            w.Activate();
        }

        /// <summary>Заметку сохранили или удалили — обновить окно заметок, если оно открыто.</summary>
        public void NotesChanged()
        {
            _notes?.Reload();
        }

        /// <summary>Скопировать .txt файлы в папку заметок (перетащили на язычок или в окно заметок).</summary>
        /// <param name="groupId">В какую группу заметок положить (null — без группы).</param>
        /// <returns>Сколько заметок добавлено.</returns>
        public int ImportNotes(System.Collections.Generic.IEnumerable<string> files, string groupId = null)
        {
            int n = 0;
            var errors = new System.Text.StringBuilder();
            foreach (var f in files)
            {
                if (!Notes.IsNoteFile(f)) continue;
                try
                {
                    var created = Notes.Import(f);
                    if (groupId != null) Cfg.NoteGroupOf[Path.GetFileName(created)] = groupId;
                    n++;
                }
                catch (Exception ex) { App.Log(ex); errors.AppendLine(Path.GetFileName(f) + ": " + ex.Message); }
            }
            if (n > 0) { Cfg.Save(); NotesChanged(); }
            if (errors.Length > 0)
                MessageBox.Show(Loc.T("MoveFailed") + "\n\n" + errors, Loc.T("AppName"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return n;
        }

        public TabWindow Tab => _tab;

        // ------------------------------------------------------------------ действия

        public void OpenEntry(FolderEntry entry)
        {
            CloseMenu();
            var path = entry.ExpandedPath;
            if (string.IsNullOrEmpty(path)) return;

            if (!entry.Exists)
            {
                MessageBox.Show(Loc.T("NotFound") + ":\n" + path, Loc.T("AppName"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (entry.IsShell || Directory.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
                else
                {
                    // Файл, ярлык или программа — как двойной клик в проводнике.
                    // Программы запускаем из их папки: многие (особенно игры) ищут свои файлы рядом с собой.
                    var psi = new ProcessStartInfo(path) { UseShellExecute = true };
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) psi.WorkingDirectory = dir;
                    Process.Start(psi);
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // Пользователь нажал «Нет» в запросе прав администратора — это не ошибка.
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.T("OpenFailed") + "\n" + path + "\n\n" + ex.Message, Loc.T("AppName"));
            }
        }

        public void AddFolderInteractive()
        {
            CloseMenu();
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("AddFolder").TrimEnd('…'), Multiselect = true };
            if (dlg.ShowDialog() == true) AddPaths(dlg.FolderNames);
        }

        public void AddFileInteractive()
        {
            CloseMenu();
            var dlg = NewFileDialog(Loc.T("AddFile").TrimEnd('…'), multiselect: true);
            if (dlg.ShowDialog() == true) AddPaths(dlg.FileNames);
        }

        /// <summary>Диалог выбора файлов для закладок. Ярлыки (.lnk) остаются ярлыками, а не подменяются целью.</summary>
        public static Microsoft.Win32.OpenFileDialog NewFileDialog(string title, bool multiselect) => new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Multiselect = multiselect,
            DereferenceLinks = false,
            Filter = Loc.T("AllFiles") + "|*.*",
        };

        /// <summary>Добавить в закладку папки и/или файлы (программы, ярлыки, документы).</summary>
        /// <param name="groupId">В какую группу положить (null — без группы).</param>
        /// <returns>Добавленные закладки (уже бывшие в списке пропускаются).</returns>
        public System.Collections.Generic.List<FolderEntry> AddPaths(System.Collections.Generic.IEnumerable<string> paths, string groupId = null)
        {
            var added = new System.Collections.Generic.List<FolderEntry>();
            foreach (var p in paths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (!Directory.Exists(p) && !File.Exists(p)) continue;
                var stored = Shorten(p);
                if (Cfg.Folders.Any(f => string.Equals(f.ExpandedPath.TrimEnd('\\'), p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                    continue;
                // Название не задаём: в списке показывается настоящее имя (его можно сменить в «Изменить»).
                var entry = new FolderEntry { Name = "", Path = stored, Group = groupId };
                Cfg.Folders.Add(entry);
                added.Add(entry);
            }
            if (added.Count > 0)
            {
                Cfg.Save();
                _settings?.ReloadFromConfig();
            }
            return added;
        }

        /// <summary>C:\Users\Имя\AppData\Roaming\X → %APPDATA%\X, чтобы конфиг был переносимым.</summary>
        public static string Shorten(string path)
        {
            var map = new[]
            {
                ("%LOCALAPPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
                ("%APPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
                ("%USERPROFILE%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            };
            foreach (var (token, real) in map)
            {
                if (string.IsNullOrEmpty(real)) continue;
                if (path.Equals(real, StringComparison.OrdinalIgnoreCase)) return token;
                if (path.StartsWith(real + "\\", StringComparison.OrdinalIgnoreCase)) return token + path.Substring(real.Length);
            }
            return path;
        }

        // ------------------------------------------------------------------ правка одной папки

        private EditWindow _edit;

        public void EditEntry(FolderEntry entry)
        {
            CloseMenu();
            _edit?.Close();
            _edit = new EditWindow(entry);
            _edit.Closed += (s, e) => { if (ReferenceEquals(s, _edit)) _edit = null; };
            _edit.Show();
            _edit.Activate();
        }

        private int IndexOfEntry(FolderEntry entry)
        {
            int i = Cfg.Folders.IndexOf(entry);
            if (i >= 0) return i;
            // Список могли пересохранить в настройках — ищем ту же папку по пути.
            return Cfg.Folders.FindIndex(f => string.Equals(f.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
        }

        public void ReplaceEntry(FolderEntry original, FolderEntry updated)
        {
            int i = IndexOfEntry(original);
            if (i >= 0) Cfg.Folders[i] = updated;
            else Cfg.Folders.Add(updated);
            ApplyConfig();
            _settings?.ReloadAll();
        }

        public void RemoveEntry(FolderEntry entry)
        {
            int i = IndexOfEntry(entry);
            if (i < 0) return;
            Cfg.Folders.RemoveAt(i);
            ApplyConfig();
            _settings?.ReloadAll();
        }

        /// <summary>Новый порядок папок после перетаскивания в меню.</summary>
        /// <summary>Новый порядок закладок и их группы (после перетаскивания в меню).</summary>
        public void SetFolders(System.Collections.Generic.List<FolderEntry> order)
        {
            Cfg.Folders = order;
            Cfg.Save();
            _settings?.ReloadAll();
        }

        // ------------------------------------------------------------------ группы закладок

        private GroupEditWindow _groupEdit;
        private Anchor _lastMenuAnchor = Anchor.Tab, _lastNotesAnchor = Anchor.Tab;

        /// <summary>Открыть окно группы. notes = true — группа заметок, иначе закладок.</summary>
        public void EditGroup(GroupDef g, bool notes)
        {
            CloseMenu();
            CloseNotes();
            _groupEdit?.Close();
            _groupEdit = new GroupEditWindow(g, notes);
            _groupEdit.Closed += (s, e) => { if (ReferenceEquals(s, _groupEdit)) _groupEdit = null; };
            _groupEdit.Show();
            _groupEdit.Activate();
        }

        /// <summary>Группу сохранили или удалили — показать список снова, там же, где он был.</summary>
        public void GroupChanged(bool notes)
        {
            Cfg.Save();
            _settings?.ReloadAll();
            var at = notes ? _lastNotesAnchor : _lastMenuAnchor;
            if (at == Anchor.Center && Cfg.HotkeyOverlay) { HotkeyPressed(notes); return; }
            if (at == Anchor.Center) at = Anchor.Tab;
            if (notes) OpenNotes(at); else OpenMenu(at, fromDrag: false);
        }

        public void DeleteGroup(GroupDef g, bool notes)
        {
            if (notes)
            {
                foreach (var k in Cfg.NoteGroupOf.Where(kv => kv.Value == g.Id).Select(kv => kv.Key).ToList())
                    Cfg.NoteGroupOf.Remove(k);
                Cfg.NoteGroups.Remove(g);
            }
            else
            {
                foreach (var f in Cfg.Folders)
                    if (f.Group == g.Id) f.Group = null;
                Cfg.Groups.Remove(g);
            }
            GroupChanged(notes);
        }

        /// <summary>Перечитать список в окне настроек (если открыто).</summary>
        public void SettingsReload() => _settings?.ReloadAll();

        /// <summary>Заметку переименовали (сменилось имя файла) — сохранить её место и группу.</summary>
        public void NoteRenamed(string oldPath, string newPath)
        {
            if (oldPath == null || newPath == null) return;
            var o = Path.GetFileName(oldPath);
            var n = Path.GetFileName(newPath);
            if (string.Equals(o, n, StringComparison.OrdinalIgnoreCase)) return;
            int i = Cfg.NoteOrder.FindIndex(x => string.Equals(x, o, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) Cfg.NoteOrder[i] = n;
            if (Cfg.NoteGroupOf.TryGetValue(o, out var gid)) { Cfg.NoteGroupOf.Remove(o); Cfg.NoteGroupOf[n] = gid; }
            Cfg.Save();
        }

        public void OpenSettings()
        {
            CloseMenu();
            if (_settings != null)
            {
                _settings.Activate();
                return;
            }
            _settings = new SettingsWindow();
            _settings.Closed += (s, e) => _settings = null;
            _settings.Show();
            _settings.Activate();
        }

        /// <summary>Сохранить и применить настройки. Возвращает горячие клавиши, которые не удалось назначить.</summary>
        public System.Collections.Generic.List<string> ApplyConfig()
        {
            Cfg.Save();
            Theme.Reapply();
            IconCache.Clear();
            _tab?.ApplyScale(); // заодно прижмёт язычки к краю (Place)
            _tab?.ApplyIdleOpacity();

            return Hotkeys.Apply();
        }

        public void OpenConfigFile()
        {
            Cfg.Save();
            try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Config.FilePath + "\"") { UseShellExecute = true }); }
            catch { }
        }

        public void ExitApp()
        {
            CloseMenu();
            _tray?.Dispose();
            _tray = null;
            Shutdown();
        }

        public static void Log(Exception ex)
        {
            if (ex == null) return;
            try
            {
                Directory.CreateDirectory(Config.Dir);
                File.AppendAllText(Path.Combine(Config.Dir, "error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }
    }
}
