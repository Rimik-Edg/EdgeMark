using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EdgeMark
{
    /// <summary>Значки папок: системные (как в проводнике), из набора или из файла. С кэшем.</summary>
    public static class IconCache
    {
        private static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static void Clear() => Cache.Clear();

        public static ImageSource Get(FolderEntry e)
        {
            if (!string.IsNullOrWhiteSpace(e.Icon))
            {
                var key = "spec|" + e.Icon;
                if (Cache.TryGetValue(key, out var custom) && custom != null) return custom;
                custom = LoadSpec(e.Icon);
                if (custom != null)
                {
                    Cache[key] = custom;
                    return custom;
                }
                // Не получилось (например, файл удалён) — показываем системный.
            }

            // «Этот компьютер» и другие разделы проводника вида ::{CLSID} — у них есть свой системный значок.
            if (e.Path != null && e.Path.Trim().StartsWith("::"))
            {
                var key = "clsid|" + e.Path.Trim();
                if (!Cache.TryGetValue(key, out var pc)) { pc = LoadShellItem(e.Path.Trim()); Cache[key] = pc; }
                if (pc != null) return pc;
            }

            string path = e.IsShell ? "" : e.ExpandedPath;
            bool real = !string.IsNullOrEmpty(path) && (Directory.Exists(path) || File.Exists(path));
            string k = real ? path : "<generic>";

            if (Cache.TryGetValue(k, out var img)) return img;
            img = LoadShell(real ? path : "folder", !real);
            Cache[k] = img;
            return img;
        }

        // ------------------------------------------------------------- свои значки

        private static ImageSource LoadSpec(string spec)
        {
            try
            {
                if (spec.StartsWith("glyph:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = spec.Split(':');
                    if (parts.Length < 2) return null;
                    char ch = (char)Convert.ToInt32(parts[1], 16);
                    var color = parts.Length > 2 ? string.Join(":", parts, 2, parts.Length - 2) : "accent";
                    return Glyph(ch.ToString(), new SolidColorBrush(ResolveColor(color)));
                }

                if (spec.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    var p = FolderEntry.Expand(spec.Substring(5));
                    if (!File.Exists(p)) return null;
                    var ext = Path.GetExtension(p).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif" || ext == ".ico")
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                        bi.UriSource = new Uri(p);
                        bi.DecodePixelWidth = 64;
                        bi.EndInit();
                        bi.Freeze();
                        return bi;
                    }
                    return LoadShell(p, false); // .exe, .dll, .lnk и т.п. — их собственный значок
                }
            }
            catch (Exception ex)
            {
                App.Log(ex);
            }
            return null;
        }

        /// <summary>Цвет по ключу: accent — акцент Windows, fg — цвет текста темы, иначе #RRGGBB.</summary>
        public static Color ResolveColor(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Equals("accent", StringComparison.OrdinalIgnoreCase)) return Theme.Accent;
            if (key.Equals("fg", StringComparison.OrdinalIgnoreCase))
                return Theme.UiDark ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x1A, 0x1A, 0x1A);
            try { return (Color)ColorConverter.ConvertFromString(key); }
            catch { return Theme.Accent; }
        }

        /// <summary>Значок из шрифта Segoe Fluent Icons / MDL2 как картинка (квадратная, с полями).</summary>
        public static ImageSource Glyph(string text, Brush brush)
        {
            var family = Application.Current.TryFindResource("IconFont") as FontFamily ?? new FontFamily("Segoe MDL2 Assets");
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 32, brush, 1.0);
            var geo = ft.BuildGeometry(new Point(0, 0));
            var b = geo.Bounds;
            if (b.IsEmpty || b.Width <= 0 || b.Height <= 0) return null;

            double size = Math.Max(b.Width, b.Height) * 1.22; // поля, чтобы по размеру совпадало с системными значками
            var box = new RectangleGeometry(new Rect(b.X + b.Width / 2 - size / 2, b.Y + b.Height / 2 - size / 2, size, size));

            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, box));
            g.Children.Add(new GeometryDrawing(brush, null, geo));
            var img = new DrawingImage(g);
            img.Freeze();
            return img;
        }

        // ------------------------------------------------------------- системные значки

        /// <summary>Значок раздела проводника по его имени вида ::{CLSID} («Этот компьютер» и т.п.).</summary>
        private static ImageSource LoadShellItem(string parsingName)
        {
            IntPtr pidl = IntPtr.Zero;
            var info = new Native.SHFILEINFO();
            try
            {
                if (Native.SHParseDisplayName(parsingName, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
                    return null;
                Native.SHGetFileInfoPidl(pidl, 0, ref info, (uint)Marshal.SizeOf(info),
                    Native.SHGFI_PIDL | Native.SHGFI_ICON | Native.SHGFI_LARGEICON);
                if (info.hIcon == IntPtr.Zero) return null;
                var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch { return null; }
            finally
            {
                if (info.hIcon != IntPtr.Zero) Native.DestroyIcon(info.hIcon);
                if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
            }
        }

        private static ImageSource LoadShell(string path, bool generic)
        {
            var info = new Native.SHFILEINFO();
            uint flags = Native.SHGFI_ICON | Native.SHGFI_LARGEICON;
            uint attrs = 0;
            if (generic)
            {
                flags |= Native.SHGFI_USEFILEATTRIBUTES;
                attrs = Native.FILE_ATTRIBUTE_DIRECTORY;
            }

            try
            {
                Native.SHGetFileInfo(path, attrs, ref info, (uint)Marshal.SizeOf(info), flags);
                if (info.hIcon == IntPtr.Zero) return null;
                var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (info.hIcon != IntPtr.Zero) Native.DestroyIcon(info.hIcon);
            }
        }
    }
}
