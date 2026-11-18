using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EdgeMark
{
    /// <summary>Редактор заметки: название = имя .txt файла, текст сохраняется в файл по «Сохранить» (или Ctrl+S).</summary>
    public partial class NoteEditWindow : Window
    {
        private string _path;          // null — новая заметка, ещё не сохранена
        private string _savedTitle = "";
        private string _savedText = "";
        private bool _ready, _deleteArmed, _closingConfirmed;

        public string NotePath => _path;

        public NoteEditWindow(string path)
        {
            InitializeComponent();
            _path = path;

            if (path != null)
            {
                _savedTitle = Notes.Title(path);
                _savedText = Notes.Read(path);
            }
            TitleBox.Text = _savedTitle;
            BodyBox.Text = _savedText;
            UpdateTitle();
            DeleteBtn.Visibility = path == null ? Visibility.Collapsed : Visibility.Visible;

            SourceInitialized += (s, e) => Backdrop.StyleWindow(this);
            Theme.Changed += OnThemeChanged;
            Loc.Changed += OnLangChanged;
            Closed += (s, e) => { Theme.Changed -= OnThemeChanged; Loc.Changed -= OnLangChanged; };
            Closing += OnClosing;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; Save(close: false); }
                else if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            };

            Loaded += (s, e) =>
            {
                if (path == null) TitleBox.Focus();
                else { BodyBox.Focus(); BodyBox.CaretIndex = BodyBox.Text.Length; }
            };

            _ready = true;
            UpdateUi();
        }

        private void OnThemeChanged() => Backdrop.StyleWindow(this);

        private void UpdateTitle() =>
            Title = _path == null ? Loc.T("NewNote") : Loc.T("Note") + " — " + _savedTitle;

        private void OnLangChanged()
        {
            UpdateTitle();
            UpdateUi();
        }

        private bool Dirty => TitleBox.Text.Trim() != _savedTitle || BodyBox.Text != _savedText;

        private void Any_Changed(object sender, TextChangedEventArgs e)
        {
            if (_ready) UpdateUi();
        }

        private void UpdateUi()
        {
            TitlePlaceholder.Visibility = string.IsNullOrEmpty(TitleBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            var where = _path != null ? _path : Path.Combine(Notes.Folder, "…");
            InfoText.Text = (Dirty ? Loc.T("Unsaved") + "   " : "") + where;
            InfoText.ToolTip = where;
        }

        // ------------------------------------------------------------- сохранение

        private bool Save(bool close)
        {
            try
            {
                var oldPath = _path;
                _path = Notes.Save(_path, TitleBox.Text, BodyBox.Text);
                App.Current.NoteRenamed(oldPath, _path);
                _savedTitle = Notes.Title(_path);
                _savedText = BodyBox.Text;

                // Если имя пришлось поправить (запрещённые символы, совпадение) — показываем итоговое.
                _ready = false;
                TitleBox.Text = _savedTitle;
                _ready = true;

                UpdateTitle();
                DeleteBtn.Visibility = Visibility.Visible;
                UpdateUi();
                App.Current.NotesChanged();

                if (close) { _closingConfirmed = true; Close(); }
                return true;
            }
            catch (Exception ex)
            {
                App.Log(ex);
                MessageBox.Show(this, Loc.T("SaveFailed") + "\n" + ex.Message, Loc.T("AppName"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e) => Save(close: true);

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_closingConfirmed || !Dirty) return;
            var r = MessageBox.Show(this, Loc.T("SaveChanges"), Loc.T("AppName"),
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (r == MessageBoxResult.Yes && !Save(close: false)) e.Cancel = true;
        }

        // ------------------------------------------------------------- удаление

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_path == null) return;
            if (!_deleteArmed)
            {
                // Защита от случайного клика: удаляем только со второго нажатия.
                _deleteArmed = true;
                DeleteText.Text = Loc.T("ConfirmDelete");
                DeleteBtn.ToolTip = Loc.T("ConfirmDeleteTip");
                return;
            }
            Notes.Delete(_path);
            App.Current.NotesChanged();
            _closingConfirmed = true;
            Close();
        }
    }
}
