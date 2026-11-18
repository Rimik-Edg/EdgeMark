using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EdgeMark
{
    public class MenuItemVm : INotifyPropertyChanged, IRowVm
    {
        public FolderEntry Entry { get; set; }
        public string Name { get; set; }
        public string Tip { get; set; }
        public ImageSource Icon { get; set; }
        public bool Exists { get; set; }
        public GroupDef Group { get; set; }

        object IRowVm.Key => Entry;

        /// <summary>Цвет полоски слева: у закладок группы — цвет группы, иначе цвет язычков.</summary>
        public Brush StripeBrush { get; set; }

        /// <summary>Закладки внутри группы чуть сдвинуты вправо.</summary>
        public Thickness RowMargin { get; set; } = new Thickness(0, 2, 0, 2);

        private string _number;
        /// <summary>Номер справа (только для ориентира — клавишами 1–9 закладки не открываются).</summary>
        public string Number
        {
            get => _number;
            set { _number = value; Raise(nameof(Number)); }
        }

        public void SetNumber(int n) => Number = n > 0 ? n.ToString() : "";

        /// <summary>Подложка развёрнутой группы (у строк вне групп — нет).</summary>
        public Brush FrameBg { get; private set; }
        public CornerRadius FrameCorner { get; private set; }
        public Thickness FramePad { get; private set; }

        public void SetFrame(Brush bg, CornerRadius corner, Thickness pad)
        {
            FrameBg = bg;
            FrameCorner = corner;
            FramePad = pad;
        }


        private bool _dragging;
        /// <summary>Строку сейчас перетаскивают — она полупрозрачная.</summary>
        public bool IsDragging
        {
            get => _dragging;
            set { _dragging = value; Raise(nameof(IsDragging)); Raise(nameof(Opacity)); }
        }

        public double Opacity => (Exists ? 1.0 : 0.45) * (IsDragging ? 0.7 : 1.0);

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public partial class MenuWindow : Window
    {
        private readonly Config _cfg;
        private readonly GroupedList _list;
        private readonly DispatcherTimer _outside;
        private IntPtr _hwnd;
        private bool _closing;

        /// <summary>Окно открыли, пока пользователь тащит файл — не закрываться, пока кнопка мыши зажата.</summary>
        private bool _dragGrace;

        public MenuWindow(Config cfg, bool fromDrag = false)
        {
            InitializeComponent();
            PopupKit.Setup(this, Card, h => _hwnd = h);
            _cfg = cfg;
            _dragGrace = fromDrag;

            _list = new GroupedList(FolderItems, Scroll, new GroupedListSource
            {
                Entries = () => _cfg.Folders.Cast<object>().ToList(),
                Groups = () => _cfg.Groups,
                GetGroupId = e => ((FolderEntry)e).Group,
                SetGroupId = (e, id) => ((FolderEntry)e).Group = id,
                MakeRow = (e, g) => MakeItem((FolderEntry)e, g),
                SaveOrder = order => App.Current.SetFolders(order.Cast<FolderEntry>().ToList()),
                Save = () => { _cfg.Save(); App.Current.SettingsReload(); },
                EditGroup = g => App.Current.EditGroup(g, notes: false),
            });
            _list.Rebuilt += () => Empty.Visibility = _list.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _list.Rebuild();
            Scroll.MaxHeight = Math.Max(200, SystemParameters.WorkArea.Height * 0.7);

            DragEnter += Files_DragOver;
            DragOver += Files_DragOver;
            DragLeave += (s, e) => ClearDropHint();
            Drop += Files_Drop;

            // Потеря фокуса из-за клика по язычку — не повод закрываться: язычок сам переключит окна.
            Deactivated += (s, e) =>
            {
                if (!_list.IsDragging && !_dragGrace && !PopupKit.JustShown(this) && !PopupKit.CursorOverTab()) CloseMenu();
            };
            Closed += (s, e) => _list.Stop();
            PreviewKeyDown += (s, e) =>
            {
                if (Keyboard.FocusedElement is TextBox) return; // вводят название группы
                if (e.Key == Key.Escape) { CloseMenu(); e.Handled = true; }
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
                if (PopupKit.ClickedOutside(_hwnd)) CloseMenu();
            };
        }

        private static Brush ResBrush(string key) => (Brush)Application.Current.Resources[key];

        private MenuItemVm MakeItem(FolderEntry f, GroupDef g)
        {
            bool ok = f.Exists;
            return new MenuItemVm
            {
                Entry = f,
                Group = g,
                Name = f.DisplayName,
                Tip = ok ? null : Loc.T("NotFound") + ": " + f.ExpandedPath,
                Icon = IconCache.Get(f),
                Exists = ok,
                StripeBrush = Theme.TryParse(g?.ItemColor, out var c) ? new SolidColorBrush(c) : ResBrush("TabColor"),
                RowMargin = new Thickness(0, 2, 0, 2), // отступ строк группы даёт подложка группы
            };
        }

        public void ShowAt(TabWindow tab, Anchor at)
        {
            PopupKit.Place(this, Card, tab, at, tab.FoldersTop);
            PopupKit.ShowAnimated(this, _hwnd, Body, Slide);
            if (at == Anchor.Center) PopupKit.CursorToCenter(this);
            _outside.Start();
        }

        public void CloseMenu()
        {
            if (_closing) return;
            _closing = true;
            _outside.Stop();
            Close();
        }

        // ------------------------------------------------------------- файлы и папки, брошенные в окно

        private static string[] DroppedPaths(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return Array.Empty<string>();
            var items = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
            return Array.FindAll(items, p => System.IO.Directory.Exists(p) || System.IO.File.Exists(p));
        }

        private void ClearDropHint()
        {
            DropOverlay.Visibility = Visibility.Collapsed;
            foreach (var g in _list.Rows.OfType<GroupRowVm>()) g.IsDropTarget = false;
        }

        private void Files_DragOver(object sender, DragEventArgs e)
        {
            var paths = DroppedPaths(e);
            e.Handled = true;
            if (paths.Length == 0) { e.Effects = DragDropEffects.None; ClearDropHint(); return; }

            e.Effects = DragDropEffects.Link;
            var target = _list.HeaderAt(e.GetPosition(FolderItems));
            foreach (var g in _list.Rows.OfType<GroupRowVm>()) g.IsDropTarget = g == target;
            DropText.Text = target == null ? Loc.T("MenuDrop") : Loc.T("MenuDrop") + " → " + target.Name;
            DropOverlay.Visibility = Visibility.Visible;
        }

        private void Files_Drop(object sender, DragEventArgs e)
        {
            var target = _list.HeaderAt(e.GetPosition(FolderItems));
            ClearDropHint();
            _dragGrace = false;
            e.Handled = true;

            var added = App.Current.AddPaths(DroppedPaths(e), target?.Group.Id);
            if (added.Count > 0)
            {
                if (target != null && target.Group.Collapsed) { target.Group.Collapsed = false; _cfg.Save(); }
                _list.Rebuild();
            }
        }

        // ------------------------------------------------------------- кнопки

        private void NewGroup_Click(object sender, RoutedEventArgs e) => _list.NewGroup();

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true; // иначе клик «всплывёт» до строки и откроет папку
            if (((Button)sender).Tag is MenuItemVm vm) App.Current.EditEntry(vm.Entry);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var btn = (Button)sender;
            if (btn.Tag is MenuItemVm vm) PopupKit.CopyWithFeedback(btn, () => Clipboard.SetText(vm.Entry.ExpandedPath));
        }

        private void Item_Click(object sender, RoutedEventArgs e)
        {
            if (_list.ShouldIgnoreClick) return;
            if (((Button)sender).Tag is MenuItemVm vm) App.Current.OpenEntry(vm.Entry);
        }

        private void Add_Click(object sender, RoutedEventArgs e) => App.Current.AddFolderInteractive();

        private void AddFile_Click(object sender, RoutedEventArgs e) => App.Current.AddFileInteractive();

        private void Settings_Click(object sender, RoutedEventArgs e) => App.Current.OpenSettings();
    }
}
