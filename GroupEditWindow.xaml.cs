using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EdgeMark
{
    /// <summary>Окно группы закладок: название, цвет названия, цвет полоски у закладок, удаление группы.</summary>
    public partial class GroupEditWindow : Window
    {
        private readonly GroupDef _group;
        private readonly bool _notes;
        private bool _ready, _deleteArmed, _done;

        public GroupEditWindow(GroupDef group, bool notes)
        {
            InitializeComponent();
            _group = group;
            _notes = notes;

            NameBox.Text = Loc.Name(group.Name ?? "");
            TitlePicker.Color = Theme.TryParse(group.Color, out var tc) ? tc : Color.FromRgb(0xFF, 0xB9, 0x00);
            ItemPicker.Color = Theme.TryParse(group.ItemColor, out var ic) ? ic : Theme.TabColor;
            TitleCustom.IsChecked = group.Color != null;
            TitleSystem.IsChecked = group.Color == null;
            ItemCustom.IsChecked = group.ItemColor != null;
            ItemSystem.IsChecked = group.ItemColor == null;

            // Тронули палитру — значит, нужен свой цвет.
            TitlePicker.ColorChanged += c => { TitleCustom.IsChecked = true; Refresh(); };
            ItemPicker.ColorChanged += c => { ItemCustom.IsChecked = true; Refresh(); };

            SourceInitialized += (s, e) => Backdrop.StyleWindow(this);
            Theme.Changed += OnThemeChanged;
            Loc.Changed += Refresh;
            Closed += (s, e) => { Theme.Changed -= OnThemeChanged; Loc.Changed -= Refresh; };
            Loaded += (s, e) => { NameBox.Focus(); NameBox.SelectAll(); };

            // Закрыли крестиком / Esc — всё равно вернуть список закладок на место.
            Closed += (s, e) => { if (!_done) App.Current.GroupChanged(_notes); };

            _ready = true;
            Refresh();
        }

        private void OnThemeChanged()
        {
            Backdrop.StyleWindow(this);
            Refresh();
        }

        private void Any_Changed(object sender, TextChangedEventArgs e) => Refresh();
        private void Any_Checked(object sender, RoutedEventArgs e) => Refresh();

        private void Refresh()
        {
            if (!_ready) return;
            var title = TitleCustom.IsChecked == true
                ? new SolidColorBrush(TitlePicker.Color)
                : (Brush)Application.Current.Resources["M.SubFg"];
            var stripe = ItemCustom.IsChecked == true
                ? new SolidColorBrush(ItemPicker.Color)
                : (Brush)Application.Current.Resources["TabColor"];

            PreviewName.Text = string.IsNullOrWhiteSpace(NameBox.Text) ? Loc.T("NewGroup") : NameBox.Text.Trim();
            PreviewName.Foreground = title;
            PreviewChevron.Foreground = title;
            PreviewLine.Background = title;
            PreviewStripe.Background = stripe;
            TitlePicker.Opacity = TitleCustom.IsChecked == true ? 1 : 0.45;
            ItemPicker.Opacity = ItemCustom.IsChecked == true ? 1 : 0.45;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text.Trim();
            _group.Name = name.Length > 0 ? name : Loc.T("NewGroup");
            _group.Color = TitleCustom.IsChecked == true ? TitlePicker.Hex : null;
            _group.ItemColor = ItemCustom.IsChecked == true ? ItemPicker.Hex : null;
            _done = true;
            Close();
            App.Current.GroupChanged(_notes);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!_deleteArmed)
            {
                // Защита от случайного клика: удаляем только со второго нажатия.
                _deleteArmed = true;
                DeleteText.Text = Loc.T("ConfirmDeleteGroup");
                DeleteBtn.ToolTip = Loc.T("ConfirmDeleteGroupTip");
                return;
            }
            _done = true;
            Close();
            App.Current.DeleteGroup(_group, _notes);
        }
    }
}
