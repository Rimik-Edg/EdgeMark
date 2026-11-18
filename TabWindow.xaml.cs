using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace EdgeMark
{
    public partial class TabWindow : Window
    {
        private const double IdleWidth = 7;
        private const double HoverWidth = 28;
        private const int DragThresholdPx = 5;
        private const int NotesHoverDelayMs = 280;

        private IntPtr _hwnd;
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _notesHover;
        private readonly DispatcherTimer _menuHover;

        // Держим ссылку на делегат, иначе сборщик мусора его удалит, а Windows продолжит его вызывать.
        private readonly Native.WinEventProc _fgHook;
        private readonly IntPtr _fgHookHandle;

        private bool _expanded, _notesExpanded;
        private bool _pressed;
        private bool _dragging;
        private FrameworkElement _pressedHit;
        private Native.POINT _pressCursor;
        private int _pressTop;

        public IntPtr Handle => _hwnd;

        public TabWindow()
        {
            InitializeComponent();

            SourceInitialized += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                // Не попадать в Alt+Tab и никогда не забирать фокус у активного окна.
                Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
                Place();
                Hotkeys.Init(_hwnd); // окно язычков живёт всё время работы программы — ему и приходят горячие клавиши
            };
            Loaded += (s, e) => { ApplyScale(); ApplyIdleOpacity(); };

            // Оба язычка открываются только кликом. Наведение их лишь выдвигает.
            Hit.MouseEnter += (s, e) => SetExpanded(true);
            Hit.MouseLeave += (s, e) => { if (!_pressed && !App.Current.MenuOpen) SetExpanded(false); };
            Hit.MouseRightButtonUp += (s, e) => { e.Handled = true; App.Current.OpenSettings(); };

            NoteHit.MouseEnter += (s, e) => SetNotesExpanded(true);
            NoteHit.MouseLeave += (s, e) => { if (!_pressed && !App.Current.NotesOpen) SetNotesExpanded(false); };
            NoteHit.MouseRightButtonUp += (s, e) => { e.Handled = true; App.Current.OpenSettings(); };

            // Тащат .txt файл и держат его над язычком заметок — через мгновение открываются заметки,
            // туда файл можно бросить.
            _notesHover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(NotesHoverDelayMs) };
            _notesHover.Tick += (s, e) =>
            {
                _notesHover.Stop();
                if (!App.Current.NotesOpen) App.Current.OpenNotes(Anchor.Tab, fromDrag: true);
            };

            // То же для закладок: держат файл/папку/ярлык над язычком закладок — открывается окно закладок,
            // туда (и прямо в нужную группу) его можно бросить.
            _menuHover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(NotesHoverDelayMs) };
            _menuHover.Tick += (s, e) =>
            {
                _menuHover.Stop();
                if (!App.Current.MenuOpen) App.Current.OpenMenu(Anchor.Tab, fromDrag: true);
            };

            // Оба язычка можно тащить вдоль края — двигаются вместе.
            foreach (var hit in new FrameworkElement[] { Hit, NoteHit })
            {
                hit.MouseLeftButtonDown += OnDown;
                hit.MouseMove += OnMove;
                hit.MouseLeftButtonUp += OnUp;
                hit.LostMouseCapture += OnLostCapture;
            }

            DragEnter += OnDragOver;
            DragOver += OnDragOver;
            DragLeave += (s, e) =>
            {
                _notesHover.Stop();
                _menuHover.Stop();
                if (!App.Current.MenuOpen) SetExpanded(false);
                if (!App.Current.NotesOpen) SetNotesExpanded(false);
            };
            Drop += OnDrop;

            SystemEvents.DisplaySettingsChanged += (s, e) => Dispatcher.BeginInvoke(new Action(Place));
            SystemParameters.StaticPropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SystemParameters.WorkArea))
                    Dispatcher.BeginInvoke(new Action(Place));
            };

            // Смена активного окна — сразу проверить, прятать ли язычки (без задержки таймера).
            _fgHook = (hook, ev, hwnd, idObj, idChild, thread, time) => CheckFullscreen();
            _fgHookHandle = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _fgHook, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
            Closed += (s, e) => { if (_fgHookHandle != IntPtr.Zero) Native.UnhookWinEvent(_fgHookHandle); };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _timer.Tick += (s, e) => { CheckFullscreen(); KeepOnTop(); };
            _timer.Start();
        }

        // ------------------------------------------------------------- расположение

        /// <summary>Ставит закладку к краю основного монитора (в физических пикселях, без «щелей» при масштабе 125%/150%).</summary>
        public void Place()
        {
            if (_hwnd == IntPtr.Zero) return;

            bool right = App.Cfg.Edge == Edge.Right;
            var align = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            Hit.HorizontalAlignment = align;
            NoteHit.HorizontalAlignment = align;
            PillBox.HorizontalAlignment = align;
            NotePillBox.HorizontalAlignment = align;
            Pill.CornerRadius = right ? new CornerRadius(8, 0, 0, 8) : new CornerRadius(0, 8, 8, 0);
            NotePill.CornerRadius = right ? new CornerRadius(6, 0, 0, 6) : new CornerRadius(0, 6, 6, 0);

            var wa = Native.PrimaryWorkArea();
            Native.GetWindowRect(_hwnd, out var r);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;

            int x = right ? wa.Right - w : wa.Left;
            int y = wa.Top + (int)Math.Round(App.Cfg.Position * Math.Max(0, (wa.Bottom - wa.Top) - h));
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        private void MoveToY(int y)
        {
            var wa = Native.PrimaryWorkArea();
            Native.GetWindowRect(_hwnd, out var r);
            int h = r.Bottom - r.Top;
            y = Math.Clamp(y, wa.Top, Math.Max(wa.Top, wa.Bottom - h));
            Native.SetWindowPos(_hwnd, IntPtr.Zero, r.Left, y, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);

            int range = (wa.Bottom - wa.Top) - h;
            App.Cfg.Position = range > 0 ? (double)(y - wa.Top) / range : 0;
        }

        /// <summary>Экранный прямоугольник закладки (физические пиксели).</summary>
        public Native.RECT ScreenRect
        {
            get { Native.GetWindowRect(_hwnd, out var r); return r; }
        }

        private int TopOf(FrameworkElement el)
        {
            try { return (int)el.PointToScreen(new Point(0, 0)).Y; }
            catch { return ScreenRect.Top; }
        }

        /// <summary>Верх язычка папок на экране (физ. пиксели) — к нему прикладывается меню.</summary>
        public int FoldersTop => TopOf(PillBox);

        /// <summary>Верх язычка заметок на экране (физ. пиксели).</summary>
        public int NotesTop => TopOf(NotePillBox);

        // ------------------------------------------------------------- мышь

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            _pressed = true;
            _dragging = false;
            _pressedHit = (FrameworkElement)sender;
            _notesHover.Stop();
            Native.GetCursorPos(out _pressCursor);
            _pressTop = ScreenRect.Top;
            _pressedHit.CaptureMouse();
            e.Handled = true;
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!_pressed) return;
            Native.GetCursorPos(out var cur);
            int dy = cur.Y - _pressCursor.Y;
            if (!_dragging && Math.Abs(dy) > DragThresholdPx)
            {
                _dragging = true;
                App.Current.CloseMenu();
                App.Current.CloseNotes();
            }
            if (_dragging) MoveToY(_pressTop + dy);
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!_pressed) return;
            _pressed = false; // до ReleaseMouseCapture: тогда OnLostCapture не сработает второй раз
            var hit = _pressedHit;
            hit?.ReleaseMouseCapture();
            e.Handled = true;
            FinishPress(hit);
        }

        /// <summary>
        /// Windows может отобрать мышь посреди клика (например, когда закрывается соседнее окно).
        /// Тогда отпускание кнопки до язычка не дойдёт — выполняем клик сразу, чтобы он не терялся.
        /// </summary>
        private void OnLostCapture(object sender, MouseEventArgs e)
        {
            if (!_pressed || sender != _pressedHit) return;
            _pressed = false;
            var hit = _pressedHit;
            Dispatcher.BeginInvoke(new Action(() => FinishPress(hit)), DispatcherPriority.Input);
        }

        private void FinishPress(FrameworkElement hit)
        {
            if (_dragging)
            {
                _dragging = false;
                App.Cfg.Save();
            }
            else if (hit == NoteHit)
            {
                App.Current.ToggleNotes();
            }
            else
            {
                App.Current.ToggleMenu();
            }

            if (!Hit.IsMouseOver && !App.Current.MenuOpen) SetExpanded(false);
            if (!NoteHit.IsMouseOver && !App.Current.NotesOpen) SetNotesExpanded(false);
        }

        public void MenuClosed()
        {
            if (!Hit.IsMouseOver && !_pressed) SetExpanded(false);
            UpdateHitZones();
        }

        public void NotesClosed()
        {
            if (!NoteHit.IsMouseOver && !_pressed) SetNotesExpanded(false);
            UpdateHitZones();
        }

        /// <summary>
        /// Пока хоть один язычок выдвинут или его окно открыто, области наведения обоих язычков широкие:
        /// можно просто провести мышью выше/ниже и попасть на соседний.
        /// </summary>
        public void UpdateHitZones()
        {
            bool wide = _expanded || _notesExpanded || App.Current.MenuOpen || App.Current.NotesOpen;
            double w = wide ? HoverWidth : IdleWidth;
            Hit.Width = w;
            NoteHit.Width = w;
        }

        // ------------------------------------------------------------- перетаскивание папок на закладку

        private static string[] DroppedPaths(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return Array.Empty<string>();
            var items = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
            return items.Where(p => Directory.Exists(p) || File.Exists(p)).ToArray();
        }

        /// <summary>Курсор над язычком заметок (или над его областью)?</summary>
        private bool OverNotes(DragEventArgs e)
        {
            var p = e.GetPosition(NoteHit);
            return p.Y >= 0 && p.Y <= NoteHit.ActualHeight;
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            var paths = DroppedPaths(e);
            e.Handled = true;

            if (OverNotes(e))
            {
                // Над заметками: принимаем только .txt — их перенесём в папку заметок.
                bool txt = paths.Any(Notes.IsNoteFile);
                e.Effects = txt ? DragDropEffects.Copy : DragDropEffects.None;
                SetNotesExpanded(true);
                if (!App.Current.MenuOpen) SetExpanded(false);
                _menuHover.Stop();
                if (txt && !App.Current.NotesOpen && !_notesHover.IsEnabled) _notesHover.Start();
            }
            else
            {
                // Над закладками: любые папки и файлы — добавятся в список.
                _notesHover.Stop();
                e.Effects = paths.Length > 0 ? DragDropEffects.Link : DragDropEffects.None;
                SetExpanded(true);
                if (paths.Length > 0 && !App.Current.MenuOpen && !_menuHover.IsEnabled) _menuHover.Start();
                if (!App.Current.NotesOpen) SetNotesExpanded(false);
            }
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            _notesHover.Stop();
            _menuHover.Stop();
            var paths = DroppedPaths(e);
            if (OverNotes(e))
            {
                if (App.Current.ImportNotes(paths) > 0) Flash(NotePill);
            }
            else
            {
                if (App.Current.AddPaths(paths).Count > 0) Flash(Pill);
            }
            if (!App.Current.MenuOpen) SetExpanded(false);
            if (!App.Current.NotesOpen) SetNotesExpanded(false);
        }

        // ------------------------------------------------------------- анимации

        private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        public void SetExpanded(bool on)
        {
            if (_expanded == on) return;
            _expanded = on;
            Animate(PillBox, Pill, Glyph, on);
            UpdateHitZones();
        }

        public void SetNotesExpanded(bool on)
        {
            if (_notesExpanded == on) return;
            _notesExpanded = on;
            Animate(NotePillBox, NotePill, NoteGlyph, on);
            UpdateHitZones();
        }

        private static double IdleOpacity => Math.Clamp(App.Cfg?.TabOpacity ?? 0.6, 0.15, 1.0);

        private const double BaseWidth = 34, BaseHeight = 94;

        /// <summary>Масштаб из настроек: всё содержимое окна язычков увеличивается целиком, окно — вместе с ним.</summary>
        public void ApplyScale()
        {
            double s = App.Cfg.Scale;
            Root.LayoutTransform = Math.Abs(s - 1) < 0.001 ? null : new ScaleTransform(s, s);
            Width = BaseWidth * s;
            Height = BaseHeight * s;
            UpdateLayout();
            Place(); // по новому размеру заново прижать к краю
        }

        /// <summary>Применить прозрачность из настроек к невыдвинутым язычкам.</summary>
        public void ApplyIdleOpacity()
        {
            if (!_expanded) Pill.BeginAnimation(OpacityProperty, new DoubleAnimation(IdleOpacity, TimeSpan.FromMilliseconds(120)));
            if (!_notesExpanded) NotePill.BeginAnimation(OpacityProperty, new DoubleAnimation(IdleOpacity, TimeSpan.FromMilliseconds(120)));
        }

        private static void Animate(FrameworkElement box, UIElement pill, UIElement glyph, bool on)
        {
            var d = TimeSpan.FromMilliseconds(on ? 140 : 220);
            box.BeginAnimation(WidthProperty, new DoubleAnimation(on ? HoverWidth : IdleWidth, d) { EasingFunction = Ease });
            pill.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1.0 : IdleOpacity, d));
            glyph.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1.0 : 0.0, d) { BeginTime = on ? TimeSpan.FromMilliseconds(60) : TimeSpan.Zero });
        }

        private void Flash(UIElement pill)
        {
            var a = new DoubleAnimation(1.0, 0.25, TimeSpan.FromMilliseconds(130))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(2),
            };
            a.Completed += (s, e) => pill.BeginAnimation(OpacityProperty,
                new DoubleAnimation(IdleOpacity, TimeSpan.FromMilliseconds(150)));
            pill.BeginAnimation(OpacityProperty, a);
        }

        // ------------------------------------------------------------- поверх всех / полноэкранные приложения

        private void KeepOnTop()
        {
            if (_hwnd == IntPtr.Zero || Root.Visibility != Visibility.Visible) return;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        private void CheckFullscreen()
        {
            if (_hwnd == IntPtr.Zero) return;
            // Пока показан оверлей, язычки не нужны (и не должны вылезать поверх затемнения).
            bool popupOpen = App.Current.MenuOpen || App.Current.NotesOpen;
            bool hide = App.Current.OverlayOpen ||
                        (!popupOpen && IsForegroundExcluded()) ||
                        (App.Cfg.HideOverFullscreen && !popupOpen && IsForegroundFullscreen());
            var v = hide ? Visibility.Hidden : Visibility.Visible;
            if (Root.Visibility != v) Root.Visibility = v;
        }

        /// <summary>Оверлей открылся — спрятать язычки сразу, не дожидаясь таймера.</summary>
        public void HideForOverlay() => CheckFullscreen();

        /// <summary>Активна программа из списка «прятать язычки в программах»?</summary>
        private bool IsForegroundExcluded()
        {
            var list = App.Cfg.HideInApps;
            if (list == null || list.Count == 0) return false;
            var fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == _hwnd) return false;
            if (IsShellWindow(fg)) return false; // нажали Win, открыли трей и т.п. — язычки видны
            var exe = Native.ProcessExeOf(fg);
            if (exe == null) return false;
            foreach (var name in list)
                if (string.Equals(name, exe, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private bool IsForegroundFullscreen()
        {
            var fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == _hwnd) return false;

            // Рабочий стол, панель задач, «Пуск», поиск, центр уведомлений — язычки должны быть видны,
            // даже если под ними открыта игра.
            if (IsShellWindow(fg)) return false;
            if (Native.IsOwnWindow(fg)) return false; // свои окна (оверлей, настройки) — не игра

            // Игра в настоящем (эксклюзивном) полноэкранном режиме — Windows сообщает об этом сама.
            if (Native.SHQueryUserNotificationState(out int state) == 0 && state == Native.QUNS_RUNNING_D3D_FULL_SCREEN)
                return true;

            // Окно на переднем плане закрывает весь монитор вместе с панелью задач
            // и при этом это не обычное «развёрнутое» окно — игра или видео в режиме без рамки.
            if (Native.IsZoomed(fg)) return false;
            if (!Native.GetWindowRect(fg, out var r)) return false;
            var mon = Native.MonitorRect(fg);
            var mine = Native.MonitorRect(_hwnd);
            bool sameMonitor = mon.Left == mine.Left && mon.Top == mine.Top;

            return sameMonitor &&
                   r.Left <= mon.Left && r.Top <= mon.Top &&
                   r.Right >= mon.Right && r.Bottom >= mon.Bottom;
        }

        private static readonly string[] ShellClasses =
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
            "Windows.UI.Core.CoreWindow",          // «Пуск», поиск, центр уведомлений (Windows 10/11)
            "XamlExplorerHostIslandWindow",        // всплывающие окна панели задач Windows 11
            "TopLevelWindowForOverflowXamlIsland", // окно скрытых значков трея (^)
            "NotifyIconOverflowWindow",            // то же в Windows 10
            "MultitaskingViewFrame", "TaskSwitcherWnd", "ForegroundStaging", // Alt+Tab / Win+Tab
        };

        private static bool IsShellWindow(IntPtr hwnd)
        {
            var cls = Native.ClassOf(hwnd);
            foreach (var c in ShellClasses)
                if (cls == c) return true;
            var exe = Native.ProcessExeOf(hwnd);
            return exe != null &&
                   (exe.Equals("StartMenuExperienceHost.exe", StringComparison.OrdinalIgnoreCase) ||
                    exe.Equals("SearchHost.exe", StringComparison.OrdinalIgnoreCase) ||
                    exe.Equals("SearchApp.exe", StringComparison.OrdinalIgnoreCase) ||
                    exe.Equals("ShellExperienceHost.exe", StringComparison.OrdinalIgnoreCase));
        }
    }
}
