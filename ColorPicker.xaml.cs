using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EdgeMark
{
    /// <summary>Кнопка с образцом цвета; по клику — палитра (квадрат насыщенность/яркость, оттенок, готовые цвета, HEX).</summary>
    public partial class ColorPicker : UserControl
    {
        private static readonly string[] PresetColors =
        {
            // яркие
            "#FFB900", "#F7630C", "#E81123", "#E3008C", "#8764B8", "#0078D4", "#00B7C3", "#10893E",
            // тёмные — для фона окна
            "#17171A", "#1F2630", "#26202E", "#1C2A22", "#2E2419", "#2B2B2B", "#4A4A4A", "#EDEDED",
        };

        private double _h, _s = 1, _v = 1; // оттенок 0..360, насыщенность и яркость 0..1
        private bool _dragSv, _dragHue, _updatingHex;

        /// <summary>Вызывается при каждом изменении цвета (в том числе во время перетаскивания).</summary>
        public event Action<Color> ColorChanged;

        public ColorPicker()
        {
            InitializeComponent();
            foreach (var hex in PresetColors)
            {
                Theme.TryParse(hex, out var c);
                var rb = new RadioButton
                {
                    Style = (Style)FindResource("ColorChip"),
                    GroupName = "Preset_" + GetHashCode(),
                    Background = new SolidColorBrush(c),
                    ToolTip = hex,
                    Tag = c,
                };
                rb.Click += (s, e) => { SetColor((Color)((RadioButton)s).Tag); rb.IsChecked = false; };
                Presets.Children.Add(rb);
            }
            UpdateVisuals();
        }

        public Color Color
        {
            get => HsvToRgb(_h, _s, _v);
            set => SetColor(value, raise: false);
        }

        public string Hex => Theme.ToHex(Color);

        private void SetColor(Color c, bool raise = true)
        {
            RgbToHsv(c, out var h, out _s, out _v);
            if (_s > 0.0001 && _v > 0.0001) _h = h; // у серых оттенок не определён — оставляем прежний
            UpdateVisuals();
            if (raise) ColorChanged?.Invoke(Color);
        }

        // ------------------------------------------------------------- отрисовка

        private void UpdateVisuals()
        {
            var c = Color;
            var brush = new SolidColorBrush(c);
            SwatchFill.Background = brush;
            Preview.Background = brush;
            HexLabel.Text = Theme.ToHex(c);
            SvHue.Background = new SolidColorBrush(HsvToRgb(_h, 1, 1));

            if (!_updatingHex && !HexBox.IsKeyboardFocused) HexBox.Text = Theme.ToHex(c);

            double w = SvArea.ActualWidth, h = SvArea.ActualHeight;
            if (w > 0 && h > 0)
            {
                Canvas.SetLeft(SvThumb, _s * w - SvThumb.Width / 2);
                Canvas.SetTop(SvThumb, (1 - _v) * h - SvThumb.Height / 2);
            }
            double hw = HueArea.ActualWidth;
            if (hw > 0) Canvas.SetLeft(HueThumb, _h / 360.0 * hw - HueThumb.Width / 2);
        }

        private void Swatch_Click(object sender, RoutedEventArgs e) => Pop.IsOpen = !Pop.IsOpen;

        private void Pop_Opened(object sender, EventArgs e)
        {
            // Размеры палитры известны только после открытия.
            Dispatcher.BeginInvoke(new Action(UpdateVisuals), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ------------------------------------------------------------- мышь

        private void Sv_Down(object sender, MouseButtonEventArgs e) { _dragSv = true; SvArea.CaptureMouse(); SvFrom(e); }
        private void Sv_Move(object sender, MouseEventArgs e) { if (_dragSv) SvFrom(e); }
        private void Sv_Up(object sender, MouseButtonEventArgs e) { _dragSv = false; SvArea.ReleaseMouseCapture(); }

        private void SvFrom(MouseEventArgs e)
        {
            var p = e.GetPosition(SvArea);
            _s = Math.Clamp(p.X / Math.Max(1, SvArea.ActualWidth), 0, 1);
            _v = Math.Clamp(1 - p.Y / Math.Max(1, SvArea.ActualHeight), 0, 1);
            UpdateVisuals();
            ColorChanged?.Invoke(Color);
        }

        private void Hue_Down(object sender, MouseButtonEventArgs e) { _dragHue = true; HueArea.CaptureMouse(); HueFrom(e); }
        private void Hue_Move(object sender, MouseEventArgs e) { if (_dragHue) HueFrom(e); }
        private void Hue_Up(object sender, MouseButtonEventArgs e) { _dragHue = false; HueArea.ReleaseMouseCapture(); }

        private void HueFrom(MouseEventArgs e)
        {
            var p = e.GetPosition(HueArea);
            _h = Math.Clamp(p.X / Math.Max(1, HueArea.ActualWidth), 0, 1) * 360.0;
            if (_h >= 360) _h = 359.999;
            UpdateVisuals();
            ColorChanged?.Invoke(Color);
        }

        // ------------------------------------------------------------- HEX

        private void HexBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { ApplyHex(); e.Handled = true; }
        }

        private void HexBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => ApplyHex();

        private void ApplyHex()
        {
            var t = HexBox.Text.Trim();
            if (!t.StartsWith("#")) t = "#" + t;
            if (t.Length == 7 && Theme.TryParse(t, out var c))
            {
                _updatingHex = true;
                SetColor(c);
                _updatingHex = false;
            }
            HexBox.Text = Hex;
        }

        // ------------------------------------------------------------- HSV

        private static Color HsvToRgb(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        private static void RgbToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            h = 0;
            if (d > 0)
            {
                if (max == r) h = 60 * (((g - b) / d) % 6);
                else if (max == g) h = 60 * ((b - r) / d + 2);
                else h = 60 * ((r - g) / d + 4);
            }
            if (h < 0) h += 360;
            s = max <= 0 ? 0 : d / max;
            v = max;
        }
    }
}
