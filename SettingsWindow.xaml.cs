using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace EdgeMark
{
    public class EditEntry : INotifyPropertyChanged
    {
        private string _name = "";
        private string _path = "";

        public string Name
        {
            get => _name;
            set { _name = value ?? ""; Raise(); Raise(nameof(DisplayName)); }
        }

        public string Path
        {
            get => _path;
            set
            {
                _path = value ?? "";
                Raise();
                Raise(nameof(DisplayName));
                Raise(nameof(Icon));
                Raise(nameof(IconOpacity));
                Raise(nameof(StatusText));
                Raise(nameof(StatusBrush));
            }
        }

        /// <summary>Свой значок (задаётся в окне «Изменить»), здесь просто сохраняется как есть.</summary>
        public string IconSpec { get; set; }

        /// <summary>Группа (задаётся перетаскиванием в меню), здесь сохраняется как есть.</summary>
        public string GroupId { get; set; }

        public FolderEntry ToEntry() => new FolderEntry { Name = Name.Trim(), Path = Path.Trim(), Icon = IconSpec, Group = GroupId };

        public string DisplayName => ToEntry().DisplayName;

        public ImageSource Icon => IconCache.Get(ToEntry());

        public double IconOpacity => ToEntry().Exists ? 1.0 : 0.45;

        public string StatusText
        {
            get
            {
                var e = ToEntry();
                if (string.IsNullOrWhiteSpace(e.Path)) return Loc.T("EnterPath");
                if (e.IsShell) return Loc.T("ShellPath");
                return (e.Exists ? Loc.T("Found") : Loc.T("NotFound")) + ": " + e.ExpandedPath;
            }
        }

        public Brush StatusBrush
        {
            get
            {
                var e = ToEntry();
                if (string.IsNullOrWhiteSpace(e.Path) || e.IsShell)
                    return (Brush)Application.Current.Resources["SubFg"];
                return (Brush)Application.Current.Resources[e.Exists ? "Success" : "Danger"];
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise([CallerMemberName] string p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public partial class SettingsWindow : Window
    {
        private readonly ObservableCollection<EditEntry> _items = new ObservableCollection<EditEntry>();

        public SettingsWindow()
        {
            InitializeComponent();

            foreach (var f in App.Cfg.Folders)
                _items.Add(new EditEntry { Name = f.Name, Path = f.Path, IconSpec = f.Icon, GroupId = f.Group });
            Folders.ItemsSource = _items;

            EdgeLeft.IsChecked = App.Cfg.Edge == Edge.Left;
            EdgeRight.IsChecked = App.Cfg.Edge == Edge.Right;
            AutostartBox.IsChecked = Autostart.IsEnabled;
            HideFsBox.IsChecked = App.Cfg.HideOverFullscreen;

            InitAppearance();
            InitLanguage();
            HotkeyMenuBox.Hotkey = App.Cfg.HotkeyMenu;
            HotkeyNotesBox.Hotkey = App.Cfg.HotkeyNotes;
            HotkeyOverlayBox.IsChecked = App.Cfg.HotkeyOverlay;
            foreach (var a in App.Cfg.HideInApps) _hideApps.Add(a);
            HideApps.ItemsSource = _hideApps;

            SourceInitialized += (s, e) => Backdrop.StyleWindow(this);
            Theme.Changed += OnThemeChanged;
            Closed += (s, e) => Theme.Changed -= OnThemeChanged;

            if (_items.Count > 0) Folders.SelectedIndex = 0;
            UpdateButtons();
        }

        // ------------------------------------------------------------- язык (сразу, «Отмена» возвращает)

        private string _origLang;
        private bool _langReady;

        private void InitLanguage()
        {
            _origLang = Loc.Lang;
            LangRu.IsChecked = Loc.Lang == "ru";
            LangEn.IsChecked = Loc.Lang == "en";
            _langReady = true;
            Closed += (s, e) =>
            {
                if (!_saved && Loc.Lang != _origLang) Loc.Apply(_origLang);
            };
        }

        private void Lang_Checked(object sender, RoutedEventArgs e)
        {
            if (!_langReady) return;
            var lang = LangEn.IsChecked == true ? "en" : "ru";
            if (lang != Loc.Lang) Loc.Apply(lang);
            Folders.Items.Refresh(); // подписи «Найдено / Не найдено» и т.п.
        }

        // ------------------------------------------------------------- оформление (с живым предпросмотром)

        private string _origTab, _origMenu;
        private double _origOpacity, _origScale;

        private void Scale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ScaleLabel == null) return; // ещё строится окно
            ScaleLabel.Text = (int)Math.Round(ScaleSlider.Value) + "%";
            if (!_appearanceReady) return;
            App.Cfg.UiScale = ScaleSlider.Value / 100.0;
            App.Current.Tab?.ApplyScale(); // язычки меняются сразу; окна закладок и заметок — при следующем открытии
        }

        private void ScaleReset_Click(object sender, RoutedEventArgs e) => ScaleSlider.Value = 100;

        private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (OpacityLabel == null) return; // ещё строится окно
            OpacityLabel.Text = (int)Math.Round(OpacitySlider.Value) + "%";
            if (!_appearanceReady) return;
            App.Cfg.TabOpacity = OpacitySlider.Value / 100.0;
            App.Current.Tab?.ApplyIdleOpacity();
        }
        private bool _saved, _appearanceReady;

        private void InitAppearance()
        {
            _origTab = App.Cfg.TabColor;
            _origMenu = App.Cfg.MenuColor;
            _origOpacity = App.Cfg.TabOpacity;
            OpacitySlider.Value = Math.Round(Math.Clamp(_origOpacity, 0.15, 1.0) * 100);
            OpacityLabel.Text = (int)OpacitySlider.Value + "%";
            _origScale = App.Cfg.UiScale;
            ScaleSlider.Value = Math.Round(Math.Clamp(_origScale, 0.5, 2.0) * 100);
            ScaleLabel.Text = (int)ScaleSlider.Value + "%";

            TabPicker.Color = Theme.TryParse(_origTab, out var t) ? t : Theme.Accent;
            MenuPicker.Color = Theme.TryParse(_origMenu, out var m) ? m : Color.FromRgb(0x17, 0x17, 0x1A);
            TabCustom.IsChecked = _origTab != null;
            TabSystem.IsChecked = _origTab == null;
            MenuCustom.IsChecked = _origMenu != null;
            MenuSystem.IsChecked = _origMenu == null;

            TabPicker.ColorChanged += c => { TabCustom.IsChecked = true; Preview(); };
            MenuPicker.ColorChanged += c => { MenuCustom.IsChecked = true; Preview(); };

            // Не сохранили — вернуть цвета как были.
            Closed += (s, e) =>
            {
                if (_saved) return;
                App.Cfg.TabColor = _origTab;
                App.Cfg.MenuColor = _origMenu;
                App.Cfg.TabOpacity = _origOpacity;
                if (App.Cfg.UiScale != _origScale) { App.Cfg.UiScale = _origScale; App.Current.Tab?.ApplyScale(); }
                App.Current.Tab?.ApplyIdleOpacity();
                Theme.Reapply();
            };

            _appearanceReady = true;
            UpdateAppearanceUi();
        }

        private void Appearance_Changed(object sender, RoutedEventArgs e)
        {
            if (!_appearanceReady) return;
            UpdateAppearanceUi();
            Preview();
        }

        private void UpdateAppearanceUi()
        {
            TabPicker.Opacity = TabCustom.IsChecked == true ? 1 : 0.45;
            MenuPicker.Opacity = MenuCustom.IsChecked == true ? 1 : 0.45;
        }

        /// <summary>Сразу показывает выбранные цвета на закладке и в меню; окончательно — по «Сохранить».</summary>
        private void Preview()
        {
            if (!_appearanceReady) return;
            UpdateAppearanceUi();
            App.Cfg.TabColor = TabCustom.IsChecked == true ? TabPicker.Hex : null;
            App.Cfg.MenuColor = MenuCustom.IsChecked == true ? MenuPicker.Hex : null;
            Theme.Reapply();
        }

        // ------------------------------------------------------------- программы, в которых язычки прячутся

        private readonly ObservableCollection<string> _hideApps = new ObservableCollection<string>();

        public class OpenApp
        {
            public string Exe { get; set; }
            public string Title { get; set; }
        }

        private void AddHideApp(string exe)
        {
            if (string.IsNullOrWhiteSpace(exe)) return;
            exe = exe.Trim();
            if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
            if (_hideApps.Any(a => string.Equals(a, exe, StringComparison.OrdinalIgnoreCase))) return;
            _hideApps.Add(exe);
        }

        private void RemoveApp_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).Tag is string exe) _hideApps.Remove(exe);
        }

        private void AddExe_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.T("HideInApps"),
                Filter = Loc.T("Programs") + " (*.exe)|*.exe",
                Multiselect = true,
            };
            if (dlg.ShowDialog(this) == true)
                foreach (var f in dlg.FileNames) AddHideApp(Path.GetFileName(f));
        }

        /// <summary>Открытые сейчас программы с окнами: проще выбрать игру отсюда, чем искать её .exe.</summary>
        private void AddFromOpen_Click(object sender, RoutedEventArgs e)
        {
            var own = System.Diagnostics.Process.GetCurrentProcess().Id;
            var list = new System.Collections.Generic.List<OpenApp>();
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (p.Id == own || p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                    var exe = p.ProcessName + ".exe";
                    if (exe.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) || !seen.Add(exe)) continue;
                    list.Add(new OpenApp { Exe = exe, Title = p.MainWindowTitle });
                }
                catch { }
                finally { p.Dispose(); }
            }
            list.Sort((a, b) => string.Compare(a.Exe, b.Exe, StringComparison.OrdinalIgnoreCase));
            if (list.Count == 0) list.Add(new OpenApp { Exe = Loc.T("NoOpenApps"), Title = "" });
            OpenAppsList.ItemsSource = list;
            OpenAppsPop.IsOpen = true;
        }

        private void PickOpenApp_Click(object sender, RoutedEventArgs e)
        {
            OpenAppsPop.IsOpen = false;
            if (((FrameworkElement)sender).Tag is string exe && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                AddHideApp(exe);
        }

        private void OnThemeChanged()
        {
            Backdrop.StyleWindow(this);
            // Перерисовать иконки/цвета статуса.
            var sel = Folders.SelectedIndex;
            Folders.Items.Refresh();
            Folders.SelectedIndex = sel;
        }

        /// <summary>Вызывается, если папки добавили перетаскиванием на закладку, пока открыты настройки.</summary>
        public void ReloadFromConfig()
        {
            foreach (var f in App.Cfg.Folders)
            {
                bool present = _items.Any(i => string.Equals(FolderEntry.Expand(i.Path), f.ExpandedPath, StringComparison.OrdinalIgnoreCase));
                if (!present) _items.Add(new EditEntry { Name = f.Name, Path = f.Path, IconSpec = f.Icon, GroupId = f.Group });
            }
        }

        /// <summary>Перечитать весь список из конфига (после правки папки в окне «Изменить»).</summary>
        public void ReloadAll()
        {
            int sel = Folders.SelectedIndex;
            _items.Clear();
            foreach (var f in App.Cfg.Folders)
                _items.Add(new EditEntry { Name = f.Name, Path = f.Path, IconSpec = f.Icon, GroupId = f.Group });
            if (_items.Count > 0) Folders.SelectedIndex = Math.Clamp(sel, 0, _items.Count - 1);
            UpdateButtons();
        }

        private EditEntry Selected => Folders.SelectedItem as EditEntry;

        private void UpdateButtons()
        {
            int i = Folders.SelectedIndex;
            Editor.IsEnabled = i >= 0;
            Editor.DataContext = Selected;
            RemoveBtn.IsEnabled = i >= 0;
            UpBtn.IsEnabled = i > 0;
            DownBtn.IsEnabled = i >= 0 && i < _items.Count - 1;
        }

        private void Folders_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateButtons();

        // ------------------------------------------------------------- кнопки списка

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("AddFolder").TrimEnd('…'), Multiselect = true };
            if (dlg.ShowDialog(this) == true)
                foreach (var p in dlg.FolderNames) AddPath(p);
        }

        private void AddFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = App.NewFileDialog(Loc.T("AddFile").TrimEnd('…'), multiselect: true);
            if (dlg.ShowDialog(this) == true)
                foreach (var p in dlg.FileNames) AddPath(p);
        }

        private void AddPath(string p)
        {
            var stored = App.Shorten(p);
            if (_items.Any(i => string.Equals(FolderEntry.Expand(i.Path).TrimEnd('\\'), p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                return;
            var item = new EditEntry { Name = "", Path = stored }; // название пустое — показывается настоящее имя
            _items.Add(item);
            Folders.SelectedItem = item;
            Folders.ScrollIntoView(item);
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            int i = Folders.SelectedIndex;
            if (i < 0) return;
            _items.RemoveAt(i);
            if (_items.Count > 0) Folders.SelectedIndex = Math.Min(i, _items.Count - 1);
            UpdateButtons();
        }

        private void Up_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
        private void Down_Click(object sender, RoutedEventArgs e) => MoveSelected(+1);

        private void MoveSelected(int delta)
        {
            int i = Folders.SelectedIndex, j = i + delta;
            if (i < 0 || j < 0 || j >= _items.Count) return;
            _items.Move(i, j);
            Folders.SelectedIndex = j;
            UpdateButtons();
        }

        private void Folders_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
            e.Handled = true;
        }

        private void Folders_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
                foreach (var p in paths.Where(x => Directory.Exists(x) || File.Exists(x))) AddPath(p);
        }

        // ------------------------------------------------------------- редактор

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var sel = Selected;
            if (sel == null) return;
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("PickFolder") };
            var cur = FolderEntry.Expand(sel.Path);
            if (Directory.Exists(cur)) dlg.InitialDirectory = cur;
            if (dlg.ShowDialog(this) == true) sel.Path = App.Shorten(dlg.FolderName);
        }

        private void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var sel = Selected;
            if (sel == null) return;
            var dlg = App.NewFileDialog(Loc.T("PickFile"), multiselect: false);
            var cur = FolderEntry.Expand(sel.Path);
            var dir = File.Exists(cur) ? Path.GetDirectoryName(cur) : cur;
            if (Directory.Exists(dir)) dlg.InitialDirectory = dir;
            if (dlg.ShowDialog(this) == true) sel.Path = App.Shorten(dlg.FileName);
        }

        private void Test_Click(object sender, RoutedEventArgs e)
        {
            var sel = Selected;
            if (sel != null) App.Current.OpenEntry(sel.ToEntry());
        }

        // ------------------------------------------------------------- низ

        private void Config_Click(object sender, RoutedEventArgs e) => App.Current.OpenConfigFile();

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var cfg = App.Cfg;
            cfg.Folders = _items.Select(i => i.ToEntry()).Where(f => !string.IsNullOrWhiteSpace(f.Path)).ToList();
            cfg.Edge = EdgeLeft.IsChecked == true ? Edge.Left : Edge.Right;
            cfg.HideOverFullscreen = HideFsBox.IsChecked == true;
            Autostart.IsEnabled = AutostartBox.IsChecked == true;
            cfg.TabColor = TabCustom.IsChecked == true ? TabPicker.Hex : null;
            cfg.MenuColor = MenuCustom.IsChecked == true ? MenuPicker.Hex : null;
            cfg.TabOpacity = OpacitySlider.Value / 100.0;
            cfg.UiScale = ScaleSlider.Value / 100.0;
            cfg.Language = LangEn.IsChecked == true ? "en" : "ru";
            cfg.HotkeyMenu = HotkeyMenuBox.Hotkey;
            cfg.HotkeyNotes = HotkeyNotesBox.Hotkey;
            cfg.HotkeyOverlay = HotkeyOverlayBox.IsChecked == true;
            cfg.HideInApps = _hideApps.ToList();
            _saved = true;
            var failed = App.Current.ApplyConfig();
            if (failed.Count > 0)
                MessageBox.Show(this, Loc.T("HotkeyBusy") + "\n\n" + string.Join("\n", failed), Loc.T("AppName"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
        }
    }
}
