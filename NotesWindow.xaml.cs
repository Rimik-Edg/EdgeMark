using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace EdgeMark
{
    public class NoteVm : INotifyPropertyChanged, IRowVm
    {
        object IRowVm.Key => Path;
        public GroupDef Group { get; set; }
        public void SetNumber(int n) { } // у заметок номеров нет — справа дата

        /// <summary>Подложка развёрнутой группы (у строк вне групп — нет).</summary>
        public System.Windows.Media.Brush FrameBg { get; private set; }
        public CornerRadius FrameCorner { get; private set; }
        public Thickness FramePad { get; private set; }

        public void SetFrame(System.Windows.Media.Brush bg, CornerRadius corner, Thickness pad)
        {
            FrameBg = bg;
            FrameCorner = corner;
            FramePad = pad;
        }


        /// <summary>Цвет полоски слева: у заметок группы — цвет группы, иначе цвет язычков.</summary>
        public System.Windows.Media.Brush StripeBrush { get; set; }

        /// <summary>Заметки внутри группы чуть сдвинуты вправо.</summary>
        public Thickness RowMargin { get; set; } = new Thickness(0, 2, 0, 2);

        private bool _dragging;
        public bool IsDragging
        {
            get => _dragging;
            set { _dragging = value; Raise(nameof(IsDragging)); Raise(nameof(Opacity)); }
        }

        public double Opacity => IsDragging ? 0.7 : 1.0;

        /// <summary>Какие заметки раскрыты — помним, пока программа запущена.</summary>
        public static readonly HashSet<string> Expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public string Path { get; set; }
        public string Title { get; set; }
        public string Date { get; set; }

        private string _text;
        /// <summary>Весь текст заметки (читается при первом раскрытии).</summary>
        public string FullText
        {
            get
            {
                if (_text == null)
                {
                    var t = Notes.Read(Path);
                    if (string.IsNullOrWhiteSpace(t)) t = Loc.T("Empty");
                    else if (t.Length > 60000) t = t.Substring(0, 60000) + "\n…";
                    _text = t.TrimEnd();
                }
                return _text;
            }
        }

        public bool IsExpanded
        {
            get => Expanded.Contains(Path);
            set
            {
                if (value) Expanded.Add(Path); else Expanded.Remove(Path);
                Raise(nameof(IsExpanded));
                Raise(nameof(Chevron));
                Raise(nameof(PreviewVisibility));
                Raise(nameof(FullText));
            }
        }

        public string Chevron => IsExpanded ? "" : ""; // вверх / вниз
        public Visibility PreviewVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public partial class NotesWindow : Window
    {
        private readonly GroupedList _list;
        private readonly DispatcherTimer _outside;
        private IntPtr _hwnd;
        private bool _closing;

        /// <summary>
        /// Окно открыли, пока пользователь тащит файл: кнопка мыши всё время зажата,
        /// поэтому не закрываемся «по клику мимо», пока её не отпустят.
        /// </summary>
        private bool _dragGrace;

        public NotesWindow(bool fromDrag = false)
        {
            InitializeComponent();
            PopupKit.Setup(this, Card, h => _hwnd = h);
            _dragGrace = fromDrag;

            DragEnter += Notes_DragOver;
            DragOver += Notes_DragOver;
            DragLeave += (s, e) => DropOverlay.Visibility = Visibility.Collapsed;
            Drop += Notes_Drop;

            var cfg = App.Cfg;
            _list = new GroupedList(NoteItems, Scroll, new GroupedListSource
            {
                Entries = () => OrderedNotes().Cast<object>().ToList(),
                Groups = () => cfg.NoteGroups,
                GetGroupId = e => cfg.NoteGroupOf.TryGetValue(System.IO.Path.GetFileName((string)e), out var id) ? id : null,
                SetGroupId = (e, id) =>
                {
                    var name = System.IO.Path.GetFileName((string)e);
                    if (id == null) cfg.NoteGroupOf.Remove(name); else cfg.NoteGroupOf[name] = id;
                },
                MakeRow = (e, g) => MakeNote((string)e, g),
                SaveOrder = order =>
                {
                    cfg.NoteOrder = order.Select(o => System.IO.Path.GetFileName((string)o)).ToList();
                    cfg.Save();
                },
                Save = () => cfg.Save(),
                EditGroup = g => App.Current.EditGroup(g, notes: true),
            });
            _list.Rebuilt += () => Empty.Visibility = _list.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Reload();
            Scroll.MaxHeight = Math.Max(200, SystemParameters.WorkArea.Height * 0.7);

            // Потеря фокуса из-за клика по язычку — не повод закрываться: язычок сам переключит окна.
            Deactivated += (s, e) =>
            {
                if (!_dragGrace && !_list.IsDragging && !PopupKit.JustShown(this) && !PopupKit.CursorOverTab()) CloseNotes();
            };
            Closed += (s, e) => _list.Stop();
            PreviewKeyDown += (s, e) =>
            {
                if (Keyboard.FocusedElement is TextBox tb && !tb.IsReadOnly) return; // вводят название группы
                if (e.Key == Key.Escape) { CloseNotes(); e.Handled = true; }
                else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { App.Current.EditNote(null); e.Handled = true; }
            };

            _outside = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _outside.Tick += (s, e) =>
            {
                if (_closing || _list.IsDragging) return;
                if (_dragGrace)
                {
                    bool down = Native.IsKeyDown(Native.VK_LBUTTON) || Native.IsKeyDown(Native.VK_RBUTTON);
                    if (!down) _dragGrace = false; // файл бросили (сюда или куда-то ещё) — дальше как обычно
                    return;
                }
                if (PopupKit.ClickedOutside(_hwnd)) CloseNotes();
            };
        }

        public void Reload() => _list.Rebuild();

        /// <summary>
        /// Заметки в сохранённом порядке. Новые (которых ещё нет в порядке) — сверху, свежие первыми.
        /// Удалённые файлы просто пропадают из списка.
        /// </summary>
        private static List<string> OrderedNotes()
        {
            var files = Notes.List(); // уже от свежих к старым
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files) byName[System.IO.Path.GetFileName(f)] = f;

            var known = new HashSet<string>(App.Cfg.NoteOrder, StringComparer.OrdinalIgnoreCase);
            var result = files.Where(f => !known.Contains(System.IO.Path.GetFileName(f))).ToList();
            foreach (var name in App.Cfg.NoteOrder)
                if (byName.TryGetValue(name, out var full) && !result.Contains(full)) result.Add(full);
            return result;
        }

        private NoteVm MakeNote(string p, GroupDef g)
        {
            DateTime t;
            try { t = File.GetLastWriteTime(p); } catch { t = DateTime.Now; }
            return new NoteVm
            {
                Path = p,
                Group = g,
                Title = Notes.Title(p),
                Date = t.Date == DateTime.Today ? t.ToString("HH:mm") : t.ToString("dd.MM"),
                StripeBrush = Theme.TryParse(g?.ItemColor, out var c)
                    ? new System.Windows.Media.SolidColorBrush(c)
                    : (System.Windows.Media.Brush)Application.Current.Resources["TabColor"],
                RowMargin = new Thickness(0, 2, 0, 2), // отступ строк группы даёт подложка группы
            };
        }

        private void NewGroup_Click(object sender, RoutedEventArgs e) => _list.NewGroup();

        public void ShowAt(TabWindow tab, Anchor at)
        {
            PopupKit.Place(this, Card, tab, at, tab.NotesTop);
            PopupKit.ShowAnimated(this, _hwnd, Body, Slide);
            if (at == Anchor.Center) PopupKit.CursorToCenter(this);
            _outside.Start();
        }

        public void CloseNotes()
        {
            if (_closing) return;
            _closing = true;
            _outside.Stop();
            Close();
        }

        // ------------------------------------------------------------- перетаскивание .txt в окно

        private static string[] DroppedNotes(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return Array.Empty<string>();
            var items = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
            return Array.FindAll(items, Notes.IsNoteFile);
        }

        private void Notes_DragOver(object sender, DragEventArgs e)
        {
            bool ok = DroppedNotes(e).Length > 0;
            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            DropOverlay.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            if (ok) Empty.Visibility = Visibility.Collapsed;
            var target = ok ? _list.HeaderAt(e.GetPosition(NoteItems)) : null;
            foreach (var g in _list.Rows.OfType<GroupRowVm>()) g.IsDropTarget = g == target;
        }

        private void Notes_Drop(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
            _dragGrace = false;
            var target = _list.HeaderAt(e.GetPosition(NoteItems));
            foreach (var g in _list.Rows.OfType<GroupRowVm>()) g.IsDropTarget = false;
            if (target != null && target.Group.Collapsed) { target.Group.Collapsed = false; App.Cfg.Save(); }
            App.Current.ImportNotes(DroppedNotes(e), target?.Group.Id); // сам обновит список
            e.Handled = true;
        }

        // ------------------------------------------------------------- кнопки

        private void Row_Click(object sender, RoutedEventArgs e)
        {
            if (_list.ShouldIgnoreClick) return;
            if (((Button)sender).Tag is NoteVm vm) App.Current.EditNote(vm.Path);
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true; // не открывать редактор
            if (((Button)sender).Tag is NoteVm vm) vm.IsExpanded = !vm.IsExpanded;
        }

        private void CopyText_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var btn = (Button)sender;
            if (btn.Tag is not NoteVm vm) return;
            var text = Notes.Read(vm.Path);
            PopupKit.CopyWithFeedback(btn, () => Clipboard.SetText(string.IsNullOrEmpty(text) ? " " : text));
        }

        private void CopyFile_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var btn = (Button)sender;
            if (btn.Tag is not NoteVm vm) return;
            // Файл в буфере — вставляется в проводник, мессенджеры и т.п. как обычный .txt
            PopupKit.CopyWithFeedback(btn, () => Clipboard.SetFileDropList(new StringCollection { vm.Path }));
        }

        private void New_Click(object sender, RoutedEventArgs e) => App.Current.EditNote(null);

        private void Folder_Click(object sender, RoutedEventArgs e)
        {
            CloseNotes();
            try
            {
                var dir = Notes.EnsureFolder();
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { App.Log(ex); }
        }
    }
}
