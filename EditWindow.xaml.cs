using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EdgeMark
{
    /// <summary>Окно «Изменить папку»: подпись в закладке, путь, значок, удаление из закладки.</summary>
    public partial class EditWindow : Window
    {
        // Значки из шрифта Segoe Fluent Icons / Segoe MDL2 Assets.
        private static readonly (string Code, string Ru, string En)[] GlyphSet =
        {
            ("E8B7", "Папка", "Folder"), ("E838", "Открытая папка", "Open folder"), ("E80F", "Дом", "Home"), ("E734", "Звезда", "Star"),
            ("E896", "Загрузки", "Downloads"), ("E8A5", "Документ", "Document"), ("E722", "Фото", "Photo"), ("E8D6", "Музыка", "Music"),
            ("E714", "Видео", "Video"), ("E7FC", "Игры", "Games"), ("E943", "Код", "Code"), ("E756", "Консоль", "Console"),
            ("E713", "Настройки", "Settings"), ("E7B8", "Пакет", "Package"), ("E753", "Облако", "Cloud"), ("E909", "Интернет", "Web"),
            ("E72E", "Замок", "Lock"), ("E7C1", "Флажок", "Flag"), ("E718", "Закреплено", "Pin"), ("EA86", "Пазл", "Puzzle"),
        };

        private static readonly (string Key, string Ru, string En)[] ColorSet =
        {
            ("accent", "Цвет Windows", "Windows color"), ("fg", "Как текст", "Text color"),
            ("#FFB900", "Жёлтый", "Yellow"), ("#F7630C", "Оранжевый", "Orange"),
            ("#E81123", "Красный", "Red"), ("#E3008C", "Розовый", "Pink"), ("#8764B8", "Фиолетовый", "Purple"),
            ("#0078D4", "Синий", "Blue"), ("#00B7C3", "Бирюзовый", "Teal"), ("#10893E", "Зелёный", "Green"),
            ("#7A7574", "Серый", "Gray"),
        };

        private readonly FolderEntry _original;
        private string _glyph = "E8B7";
        private string _color = "#FFB900";
        private string _file;
        private bool _ready;
        private bool _removeArmed;

        public EditWindow(FolderEntry entry)
        {
            InitializeComponent();
            _original = entry;

            NameBox.Text = Loc.Name(entry.Name ?? "");
            PathBox.Text = entry.Path ?? "";
            ParseIcon(entry.Icon);

            BuildGlyphs();
            BuildColors();

            if (_file != null) IconFile.IsChecked = true;
            else if (!string.IsNullOrWhiteSpace(entry.Icon)) IconGlyph.IsChecked = true;
            else IconSystem.IsChecked = true;

            SourceInitialized += (s, e) => Backdrop.StyleWindow(this);
            Theme.Changed += OnThemeChanged;
            Loc.Changed += OnThemeChanged;
            Closed += (s, e) => { Theme.Changed -= OnThemeChanged; Loc.Changed -= OnThemeChanged; };
            Loaded += (s, e) => { NameBox.Focus(); NameBox.SelectAll(); };

            _ready = true;
            Refresh();
        }

        private void OnThemeChanged()
        {
            Backdrop.StyleWindow(this);
            BuildGlyphs();
            BuildColors();
            Refresh();
        }

        // ------------------------------------------------------------- значок

        private void ParseIcon(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return;
            if (spec.StartsWith("glyph:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = spec.Split(':');
                if (parts.Length > 1) _glyph = parts[1].ToUpperInvariant();
                if (parts.Length > 2) _color = string.Join(":", parts, 2, parts.Length - 2);
            }
            else if (spec.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                _file = spec.Substring(5);
            }
        }

        private string CurrentSpec()
        {
            if (IconGlyph.IsChecked == true) return "glyph:" + _glyph + ":" + _color;
            if (IconFile.IsChecked == true && !string.IsNullOrWhiteSpace(_file)) return "file:" + _file;
            return null;
        }

        private void BuildGlyphs()
        {
            Glyphs.Children.Clear();
            foreach (var (code, ru, en) in GlyphSet)
            {
                var rb = new RadioButton
                {
                    Style = (Style)FindResource("Chip"),
                    GroupName = "Glyph",
                    Tag = code,
                    ToolTip = Loc.Pick(ru, en),
                    IsChecked = string.Equals(code, _glyph, StringComparison.OrdinalIgnoreCase),
                    Content = new TextBlock
                    {
                        Text = ((char)Convert.ToInt32(code, 16)).ToString(),
                        FontFamily = (FontFamily)FindResource("IconFont"),
                        FontSize = 17,
                    },
                };
                rb.Checked += (s, e) => { _glyph = (string)((RadioButton)s).Tag; Refresh(); };
                Glyphs.Children.Add(rb);
            }
        }

        private void BuildColors()
        {
            ColorsPanel.Children.Clear();
            foreach (var (key, ru, en) in ColorSet)
            {
                var rb = new RadioButton
                {
                    Style = (Style)FindResource("ColorChip"),
                    GroupName = "Color",
                    Tag = key,
                    ToolTip = Loc.Pick(ru, en),
                    Background = new SolidColorBrush(IconCache.ResolveColor(key)),
                    IsChecked = string.Equals(key, _color, StringComparison.OrdinalIgnoreCase),
                };
                rb.Checked += (s, e) => { _color = (string)((RadioButton)s).Tag; Refresh(); };
                ColorsPanel.Children.Add(rb);
            }
        }

        private void IconKind_Checked(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            if (IconFile.IsChecked == true && string.IsNullOrWhiteSpace(_file)) PickFile();
            Refresh();
        }

        private void PickFile_Click(object sender, RoutedEventArgs e) => PickFile();

        private void PickFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.T("PickIconDialog"),
                Filter = Loc.T("ImagesAndIcons") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.exe;*.dll;*.lnk|" + Loc.T("AllFiles") + "|*.*",
            };
            if (dlg.ShowDialog(this) == true)
            {
                _file = App.Shorten(dlg.FileName);
                IconCache.Clear();
            }
            Refresh();
        }

        // ------------------------------------------------------------- путь

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("PickFolder") };
            var cur = FolderEntry.Expand(PathBox.Text);
            if (Directory.Exists(cur)) dlg.InitialDirectory = cur;
            if (dlg.ShowDialog(this) == true) PathBox.Text = App.Shorten(dlg.FolderName);
        }

        private void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = App.NewFileDialog(Loc.T("PickFile"), multiselect: false);
            var cur = FolderEntry.Expand(PathBox.Text);
            var dir = File.Exists(cur) ? Path.GetDirectoryName(cur) : cur;
            if (Directory.Exists(dir)) dlg.InitialDirectory = dir;
            if (dlg.ShowDialog(this) == true) PathBox.Text = App.Shorten(dlg.FileName);
        }

        private void Any_Changed(object sender, TextChangedEventArgs e)
        {
            if (_ready) Refresh();
        }

        // ------------------------------------------------------------- предпросмотр

        private FolderEntry Current() => new FolderEntry
        {
            Name = NameBox.Text.Trim(),
            Path = PathBox.Text.Trim(),
            Icon = CurrentSpec(),
            Group = _original.Group, // группа меняется перетаскиванием в меню, здесь её сохраняем как есть
        };

        private void Refresh()
        {
            if (!_ready) return;
            var cur = Current();

            GlyphPanel.Visibility = IconGlyph.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            FilePanel.Visibility = IconFile.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            FileLabel.Text = string.IsNullOrWhiteSpace(_file) ? Loc.T("NoFileChosen") : Path.GetFileName(FolderEntry.Expand(_file));

            // Значки в наборе красим выбранным цветом — сразу видно результат.
            var brush = new SolidColorBrush(IconCache.ResolveColor(_color));
            foreach (var child in Glyphs.Children)
                if (child is RadioButton rb && rb.Content is TextBlock tb) tb.Foreground = brush;

            PreviewIcon.Source = IconCache.Get(cur);
            PreviewName.Text = cur.DisplayName;
            PreviewPath.Text = cur.IsShell ? cur.Path : cur.ExpandedPath;

            var realName = new FolderEntry { Path = cur.Path }.DisplayName;
            NamePlaceholder.Text = realName;
            NamePlaceholder.Visibility = string.IsNullOrEmpty(NameBox.Text) ? Visibility.Visible : Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(cur.Path))
            {
                PathStatus.Text = Loc.T("EnterPath");
                PathStatus.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
            }
            else if (cur.IsShell)
            {
                PathStatus.Text = Loc.T("ShellPath");
                PathStatus.SetResourceReference(TextBlock.ForegroundProperty, "SubFg");
            }
            else if (cur.Exists)
            {
                PathStatus.Text = Loc.T("Found");
                PathStatus.SetResourceReference(TextBlock.ForegroundProperty, "Success");
            }
            else
            {
                PathStatus.Text = Loc.T("NotFound");
                PathStatus.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
            }
        }

        // ------------------------------------------------------------- кнопки

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (!_removeArmed)
            {
                // Защита от случайного клика: убираем только со второго нажатия.
                _removeArmed = true;
                RemoveText.Text = Loc.T("ConfirmRemove");
                RemoveBtn.ToolTip = Loc.T("ConfirmRemoveTip");
                return;
            }
            App.Current.RemoveEntry(_original);
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var cur = Current();
            if (string.IsNullOrWhiteSpace(cur.Path))
            {
                PathBox.Focus();
                return;
            }
            App.Current.ReplaceEntry(_original, cur);
            Close();
        }
    }
}
