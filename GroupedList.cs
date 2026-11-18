using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EdgeMark
{
    /// <summary>Строка-элемент списка (закладка или заметка).</summary>
    public interface IRowVm
    {
        /// <summary>Что за строкой стоит: FolderEntry у закладок, путь к файлу у заметок.</summary>
        object Key { get; }
        /// <summary>Группа, в которой строка сейчас (null — без группы).</summary>
        GroupDef Group { get; set; }
        bool IsDragging { get; set; }
        /// <summary>Номер строки (0 — без номера).</summary>
        void SetNumber(int n);
        /// <summary>Подложка развёрнутой группы под строкой (null — строка вне группы).</summary>
        void SetFrame(Brush bg, CornerRadius corner, Thickness pad);
    }

    /// <summary>Строка-заголовок группы.</summary>
    public class GroupRowVm : INotifyPropertyChanged
    {
        public GroupDef Group { get; set; }
        public string Name => string.IsNullOrWhiteSpace(Group.Name) ? Loc.T("NewGroup") : Loc.Name(Group.Name);
        public Brush TitleBrush { get; set; }
        public Brush BandBrush { get; set; }
        public string Count { get; set; }

        /// <summary>Развёрнутая группа с закладками: заголовок — «шапка» блока, скруглена только сверху.</summary>
        public CornerRadius HeaderCorner { get; set; } = new CornerRadius(5);
        public Thickness HeaderMargin { get; set; } = new Thickness(0, 8, 0, 2);
        public string Chevron => Group.Collapsed ? "" : ""; // вправо (свёрнута) / вниз

        private bool _editing;
        public bool IsEditing
        {
            get => _editing;
            set { _editing = value; Raise(nameof(IsEditing)); }
        }

        public string EditName { get; set; }

        private bool _dropTarget;
        /// <summary>Сюда сейчас бросят файлы — линия группы подсвечивается.</summary>
        public bool IsDropTarget
        {
            get => _dropTarget;
            set { if (_dropTarget != value) { _dropTarget = value; Raise(nameof(IsDropTarget)); } }
        }

        private bool _isDragging;
        public bool IsDragging
        {
            get => _isDragging;
            set { _isDragging = value; Raise(nameof(IsDragging)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    /// <summary>Откуда список берёт данные и куда сохраняет (у закладок и заметок — своё).</summary>
    public class GroupedListSource
    {
        /// <summary>Все элементы в сохранённом порядке.</summary>
        public Func<List<object>> Entries;
        public Func<List<GroupDef>> Groups;
        public Func<object, string> GetGroupId;
        public Action<object, string> SetGroupId;
        /// <summary>Создать строку для элемента (группа может быть null).</summary>
        public Func<object, GroupDef, IRowVm> MakeRow;
        /// <summary>Сохранить новый порядок элементов (группы уже переставлены в Groups()).</summary>
        public Action<List<object>> SaveOrder;
        /// <summary>Сохранить настройки (свернули группу, переименовали…).</summary>
        public Action Save;
        /// <summary>Нажали ✎ у группы.</summary>
        public Action<GroupDef> EditGroup;
    }

    /// <summary>
    /// Список с группами для окон закладок и заметок:
    /// группы стоят где угодно среди элементов, сворачиваются, перетаскиваются вместе с содержимым;
    /// элементы перетаскиваются между группами. Нумерация: элементы вне групп считаются подряд,
    /// пропуская группы; внутри каждой группы — своя нумерация с 1.
    /// </summary>
    public class GroupedList
    {
        private const double DragThreshold = 6;

        private readonly ItemsControl _list;
        private readonly ScrollViewer _scroll;
        private readonly GroupedListSource _src;

        public ObservableCollection<object> Rows { get; } = new ObservableCollection<object>();

        /// <summary>Список перестроен (например, чтобы показать/спрятать надпись «пусто»).</summary>
        public event Action Rebuilt;

        // перетаскивание
        private IRowVm _pressItem;
        private GroupRowVm _pressHeader;
        private Point _pressPos;
        private bool _dragging, _suppressClick;
        private GroupDef _dragGroup;
        private bool _dragGroupWasCollapsed;
        private DispatcherTimer _holdTimer, _dragPoll;

        public bool IsDragging => _dragging;

        /// <summary>Клик по строке сразу после перетаскивания не должен ничего открывать.</summary>
        public bool ShouldIgnoreClick => _dragging || _suppressClick;

        public GroupedList(ItemsControl list, ScrollViewer scroll, GroupedListSource src)
        {
            _list = list;
            _scroll = scroll;
            _src = src;
            _list.ItemsSource = Rows;

            _list.PreviewMouseLeftButtonDown += Down;
            _list.PreviewMouseMove += Move;
            _list.PreviewMouseLeftButtonUp += Up;

            // Кнопка ✎ на заголовке группы и поле её названия — события ловим здесь, общими для обоих окон.
            _list.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnButtonClick));
            _list.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKeyDown), true);
            _list.AddHandler(UIElement.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnLostFocus), true);
        }

        public void Stop()
        {
            _dragPoll?.Stop();
            _holdTimer?.Stop();
            Mouse.OverrideCursor = null;
        }

        // ------------------------------------------------------------- сборка строк

        private static Brush ResBrush(string key) => (Brush)Application.Current.Resources[key];

        private static Color GroupColor(GroupDef g) => Theme.TryParse(g.Color, out var gc) ? gc : Theme.TabColor;

        /// <summary>Подложка развёрнутой группы — тот же цвет, что у её заголовка, только бледнее.</summary>
        private static byte FrameAlpha => Theme.MenuDark ? (byte)0x18 : (byte)0x14;

        /// <summary>
        /// Фон заголовка группы: её цвет слева, к правому краю бледнеет.
        /// У развёрнутой группы бледнеет ровно до цвета подложки — заголовок и закладки выглядят одним блоком.
        /// </summary>
        private static Brush BandFor(GroupDef g, bool expanded)
        {
            Color c = GroupColor(g);
            byte a = Theme.MenuDark ? (byte)0x33 : (byte)0x2A;
            byte end = expanded ? FrameAlpha : (byte)(a / 3);
            var b = new LinearGradientBrush(Color.FromArgb(a, c.R, c.G, c.B), Color.FromArgb(end, c.R, c.G, c.B), 0);
            b.Freeze();
            return b;
        }

        private static Brush FrameFor(GroupDef g)
        {
            Color c = GroupColor(g);
            var b = new SolidColorBrush(Color.FromArgb(FrameAlpha, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        private GroupRowVm MakeHeader(GroupDef g, int count, bool open) => new GroupRowVm
        {
            Group = g,
            IsDragging = g == _dragGroup,
            BandBrush = BandFor(g, open),
            HeaderCorner = open ? new CornerRadius(6, 6, 0, 0) : new CornerRadius(5),
            HeaderMargin = open ? new Thickness(0, 8, 0, 0) : new Thickness(0, 8, 0, 2),
            TitleBrush = Theme.TryParse(g.Color, out var c) ? new SolidColorBrush(c) : ResBrush("M.SubFg"),
            Count = count.ToString(),
            EditName = Loc.Name(g.Name),
        };

        /// <summary>Собрать строки заново: группы встают среди элементов вне групп по своей позиции.</summary>
        public void Rebuild()
        {
            Rows.Clear();
            var entries = _src.Entries();
            var groups = _src.Groups();
            var ids = groups.Select(g => g.Id).ToHashSet();

            var loose = entries.Where(e => { var id = _src.GetGroupId(e); return id == null || !ids.Contains(id); }).ToList();
            var ordered = groups.Select((g, i) => (g, i)).OrderBy(x => x.g.Position).ThenBy(x => x.i).Select(x => x.g).ToList();

            void EmitGroup(GroupDef g)
            {
                var members = entries.Where(e => _src.GetGroupId(e) == g.Id).ToList();
                bool open = !g.Collapsed && members.Count > 0;
                Rows.Add(MakeHeader(g, members.Count, open));
                if (!open) return;

                // Закладки группы лежат на общей подложке; последняя закрывает блок скруглением снизу.
                var frame = FrameFor(g);
                for (int k = 0; k < members.Count; k++)
                {
                    bool first = k == 0, last = k == members.Count - 1;
                    var row = _src.MakeRow(members[k], g);
                    row.SetFrame(frame,
                        last ? new CornerRadius(0, 0, 6, 6) : new CornerRadius(0),
                        new Thickness(6, first ? 3 : 0, 6, last ? 5 : 0));
                    Rows.Add(row);
                }
            }

            int gi = 0;
            for (int i = 0; i < loose.Count; i++)
            {
                while (gi < ordered.Count && ordered[gi].Position <= i) EmitGroup(ordered[gi++]);
                Rows.Add(_src.MakeRow(loose[i], null));
            }
            while (gi < ordered.Count) EmitGroup(ordered[gi++]);

            Renumber();
            Rebuilt?.Invoke();
        }

        /// <summary>Группа, в которую попадёт строка, если её поставить на место index (смотрим на строку выше).</summary>
        private GroupDef GroupAbove(int index)
        {
            for (int i = index - 1; i >= 0; i--)
            {
                if (Rows[i] is GroupRowVm h) return h.Group;
                if (Rows[i] is IRowVm r && r != _pressItem) return r.Group;
            }
            return null;
        }

        private GroupDef EffectiveGroup(IRowVm r) =>
            _dragging && r == _pressItem ? GroupAbove(Rows.IndexOf(r)) : r.Group;

        /// <summary>Номера: вне групп — подряд, группы пропускаются; в каждой группе — с 1.</summary>
        private void Renumber()
        {
            int loose = 0;
            var perGroup = new Dictionary<GroupDef, int>();
            foreach (var row in Rows)
            {
                if (row is not IRowVm r) continue;
                var g = EffectiveGroup(r);
                if (g == null) r.SetNumber(++loose);
                else
                {
                    perGroup.TryGetValue(g, out int n);
                    perGroup[g] = ++n;
                    r.SetNumber(n);
                }
            }
        }

        /// <summary>
        /// Сохранить то, что видно: порядок элементов, их группы и места групп среди элементов.
        /// Скрытое содержимое свёрнутых групп остаётся при своей группе.
        /// </summary>
        private void Persist()
        {
            var entries = _src.Entries();
            var groups = _src.Groups();
            var visible = new HashSet<object>(Rows.OfType<IRowVm>().Select(r => r.Key));
            var order = new List<object>();
            var groupOrder = new List<GroupDef>();
            int looseSeen = 0;

            for (int i = 0; i < Rows.Count; i++)
            {
                if (Rows[i] is GroupRowVm h)
                {
                    h.Group.Position = looseSeen;
                    groupOrder.Add(h.Group);
                    // Содержимое свёрнутой группы не видно — ставим его сюда же, к заголовку.
                    foreach (var e in entries)
                        if (!visible.Contains(e) && _src.GetGroupId(e) == h.Group.Id) order.Add(e);
                }
                else if (Rows[i] is IRowVm r)
                {
                    var g = EffectiveGroup(r);
                    r.Group = g;
                    _src.SetGroupId(r.Key, g?.Id);
                    order.Add(r.Key);
                    if (g == null) looseSeen++;
                }
            }

            foreach (var g in groups) if (!groupOrder.Contains(g)) groupOrder.Add(g);
            groups.Clear();
            groups.AddRange(groupOrder);
            foreach (var e in entries) if (!order.Contains(e)) order.Add(e); // ничего не теряем
            _src.SaveOrder(order);
        }

        // ------------------------------------------------------------- группы

        public GroupDef NewGroup()
        {
            var groups = _src.Groups();
            int looseCount = _src.Entries().Count(e => _src.GetGroupId(e) == null);
            var g = new GroupDef { Name = Loc.T("NewGroup"), Position = looseCount }; // в конец списка
            groups.Add(g);
            _src.Save();
            Rebuild();

            // Сразу даём ввести название прямо в списке.
            var row = Rows.OfType<GroupRowVm>().FirstOrDefault(r => r.Group == g);
            if (row != null)
            {
                row.IsEditing = true;
                _list.UpdateLayout();
                _scroll?.ScrollToEnd();
                _list.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var box = FindNameBox(row);
                    if (box != null) { box.Focus(); box.SelectAll(); }
                }), DispatcherPriority.Input);
            }
            return g;
        }

        private TextBox FindNameBox(GroupRowVm row)
        {
            if (_list.ItemContainerGenerator.ContainerFromItem(row) is not DependencyObject c) return null;
            return FindChild<TextBox>(c);
        }

        private static T FindChild<T>(DependencyObject d) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var ch = VisualTreeHelper.GetChild(d, i);
                if (ch is T t) return t;
                var deeper = FindChild<T>(ch);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private void CommitRename(GroupRowVm row)
        {
            if (!row.IsEditing) return;
            row.IsEditing = false;
            var name = (row.EditName ?? "").Trim();
            if (name.Length > 0) row.Group.Name = name;
            _src.Save();
            Rebuild();
        }

        private void OnButtonClick(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is Button b && b.Tag is GroupRowVm row)
            {
                e.Handled = true;
                _src.EditGroup?.Invoke(row.Group);
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is not TextBox tb || tb.Tag is not GroupRowVm row) return;
            if (e.Key == Key.Enter) { CommitRename(row); e.Handled = true; }
            else if (e.Key == Key.Escape) { row.IsEditing = false; e.Handled = true; }
        }

        private void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (e.OriginalSource is TextBox tb && tb.Tag is GroupRowVm row && row.IsEditing) CommitRename(row);
        }

        /// <summary>В какую группу попадёт то, что бросят в эту точку списка (null — без группы).</summary>
        public GroupRowVm HeaderAt(Point posInList)
        {
            int idx = IndexAt(posInList.Y);
            if (idx < 0 || idx >= Rows.Count) return null;
            GroupDef g = Rows[idx] is GroupRowVm h ? h.Group : (Rows[idx] as IRowVm)?.Group;
            return g == null ? null : Rows.OfType<GroupRowVm>().FirstOrDefault(r => r.Group == g);
        }

        // ------------------------------------------------------------- мышь

        private static T FindUp<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && d is not T)
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }

        private void Down(object sender, MouseButtonEventArgs e)
        {
            _pressItem = null;
            _pressHeader = null;
            var src = e.OriginalSource as DependencyObject;
            if (FindUp<TextBoxBase>(src) != null) return; // поле названия, текст заметки — не перетаскивание

            var btn = FindUp<Button>(src);
            if (btn != null && btn.Style == (Style)Application.Current.Resources["FolderRow"] && btn.DataContext is IRowVm r)
                _pressItem = r;
            else if (btn == null && FindUp<FrameworkElement>(src)?.DataContext is GroupRowVm h && !h.IsEditing)
                _pressHeader = h;
            else
                return; // маленькие кнопки в строке («копировать», «изменить», стрелка) — не перетаскивание

            _pressPos = e.GetPosition(_list);
            _holdTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
            _holdTimer.Tick -= HoldTick;
            _holdTimer.Tick += HoldTick;
            _holdTimer.Stop();
            _holdTimer.Start();
        }

        private void HoldTick(object sender, EventArgs e)
        {
            _holdTimer.Stop();
            if ((_pressItem != null || _pressHeader != null) && Native.IsKeyDown(Native.VK_LBUTTON)) BeginDrag();
        }

        private void Move(object sender, MouseEventArgs e)
        {
            if ((_pressItem == null && _pressHeader == null) || _dragging) return;
            if (e.LeftButton != MouseButtonState.Pressed) { CancelPress(); return; }
            if (Math.Abs(e.GetPosition(_list).Y - _pressPos.Y) >= DragThreshold) BeginDrag();
        }

        private void Up(object sender, MouseButtonEventArgs e)
        {
            if (_dragging) { e.Handled = true; EndDrag(); return; }

            // Обычный клик по заголовку группы — свернуть / развернуть.
            if (_pressHeader != null && !_suppressClick)
            {
                var h = _pressHeader;
                CancelPress();
                h.Group.Collapsed = !h.Group.Collapsed;
                _src.Save();
                Rebuild();
                e.Handled = true;
                return;
            }
            CancelPress();
        }

        private void CancelPress()
        {
            _holdTimer?.Stop();
            _pressItem = null;
            _pressHeader = null;
        }

        private void BeginDrag()
        {
            if (_dragging) return;
            _holdTimer?.Stop();
            _dragging = true;
            _suppressClick = true;

            if (_pressHeader != null)
            {
                // Группу таскаем свёрнутой — так её проще поставить между другими строками.
                _dragGroup = _pressHeader.Group;
                _dragGroupWasCollapsed = _dragGroup.Collapsed;
                _dragGroup.Collapsed = true;
                Rebuild();
                _pressHeader = Rows.OfType<GroupRowVm>().First(r => r.Group == _dragGroup);
            }
            else _pressItem.IsDragging = true;

            // Забираем мышь у строки — отпускание не засчитается как клик.
            _list.CaptureMouse();
            Mouse.OverrideCursor = Cursors.SizeNS;

            _dragPoll ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _dragPoll.Tick -= PollTick;
            _dragPoll.Tick += PollTick;
            _dragPoll.Start();
        }

        private void PollTick(object sender, EventArgs e)
        {
            if (!_dragging) { EndDrag(); return; }
            if (!Native.IsKeyDown(Native.VK_LBUTTON)) { EndDrag(); return; }

            Native.GetCursorPos(out var p);
            Point pos;
            try { pos = _list.PointFromScreen(new Point(p.X, p.Y)); }
            catch { return; }

            object moving = (object)_pressHeader ?? _pressItem;
            int target = IndexAt(pos.Y);
            int current = Rows.IndexOf(moving);
            if (target >= 0 && current >= 0 && target != current)
            {
                Rows.Move(current, target);
                Renumber();
            }
        }

        private int IndexAt(double y)
        {
            if (y < 0) return 0;
            for (int i = 0; i < Rows.Count; i++)
            {
                if (_list.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
                double top = c.TranslatePoint(new Point(0, 0), _list).Y;
                if (y < top + c.ActualHeight) return i;
            }
            return Rows.Count - 1;
        }

        private void EndDrag()
        {
            _holdTimer?.Stop();
            _dragPoll?.Stop();
            if (!_dragging) { CancelPress(); return; }

            Persist(); // пока _dragging = true: перетащенная строка получает группу по строке над ней
            _dragging = false;
            if (_list.IsMouseCaptured) _list.ReleaseMouseCapture();
            Mouse.OverrideCursor = null;

            if (_dragGroup != null)
            {
                _dragGroup.Collapsed = _dragGroupWasCollapsed;
                _dragGroup = null;
                _src.Save();
            }
            if (_pressItem != null) _pressItem.IsDragging = false;
            CancelPress();
            Rebuild();

            // Клик, который мог прийти сразу после отпускания, игнорируем.
            _list.Dispatcher.BeginInvoke(new Action(() => _suppressClick = false), DispatcherPriority.Input);
        }
    }
}
