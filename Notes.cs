using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace EdgeMark
{
    /// <summary>Заметки — обычные .txt файлы в одной папке (по умолчанию «Документы\EdgeMark\Заметки» / «Notes»).</summary>
    public static class Notes
    {
        public static string Folder
        {
            get
            {
                var custom = App.Cfg?.NotesFolder;
                if (!string.IsNullOrWhiteSpace(custom)) return FolderEntry.Expand(custom);

                // Папку выбираем один раз (по языку) и запоминаем в настройках,
                // чтобы при смене языка заметки никуда не «пропадали».
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var ru = Path.Combine(docs, "EdgeMark", "Заметки");
                var en = Path.Combine(docs, "EdgeMark", "Notes");
                // Папки времён старого названия: если заметки уже там — продолжаем пользоваться ими.
                var oldRu = Path.Combine(docs, "Закладка", "Заметки");
                var oldEn = Path.Combine(docs, "Zakladka", "Notes");
                var chosen = new[] { ru, en, oldRu, oldEn }.FirstOrDefault(Directory.Exists)
                             ?? (Loc.Lang == "en" ? en : ru);
                if (App.Cfg != null)
                {
                    App.Cfg.NotesFolder = App.Shorten(chosen);
                    App.Cfg.Save();
                }
                return chosen;
            }
        }

        public static string EnsureFolder()
        {
            var f = Folder;
            Directory.CreateDirectory(f);
            return f;
        }

        /// <summary>Все заметки, свежие сверху.</summary>
        public static List<string> List()
        {
            try
            {
                var dir = EnsureFolder();
                return new DirectoryInfo(dir).GetFiles("*.txt")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Select(f => f.FullName)
                    .ToList();
            }
            catch (Exception ex)
            {
                App.Log(ex);
                return new List<string>();
            }
        }

        public static string Title(string path) => Path.GetFileNameWithoutExtension(path);

        public static string Read(string path)
        {
            try { return File.ReadAllText(path); } // кодировку (UTF-8/UTF-16 с BOM) .NET определяет сам
            catch (Exception ex) { App.Log(ex); return ""; }
        }

        /// <summary>Начало текста для раскрытой строки: несколько строк, не больше limit символов.</summary>
        public static string Preview(string text, int maxLines = 8, int limit = 420)
        {
            if (string.IsNullOrEmpty(text)) return Loc.T("Empty");
            var t = text.Replace("\r\n", "\n").TrimEnd();
            var lines = t.Split('\n');
            bool cut = false;
            if (lines.Length > maxLines) { t = string.Join("\n", lines.Take(maxLines)); cut = true; }
            if (t.Length > limit) { t = t.Substring(0, limit); cut = true; }
            return cut ? t.TrimEnd() + " …" : t;
        }

        /// <summary>
        /// Сохраняет заметку. Если название поменялось — файл переименовывается.
        /// Возвращает путь к сохранённому файлу.
        /// </summary>
        public static string Save(string oldPath, string title, string text)
        {
            var dir = EnsureFolder();
            var name = SafeName(title);
            if (string.IsNullOrEmpty(name)) name = Loc.T("Note") + " " + DateTime.Now.ToString("dd.MM.yyyy HH-mm");

            var target = Path.Combine(dir, name + ".txt");
            bool samePlace = oldPath != null && string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
            if (!samePlace && File.Exists(target)) target = Unique(dir, name);

            // Сначала пишем во временный файл: если что-то пойдёт не так, старая заметка не пострадает.
            var tmp = target + ".tmp";
            File.WriteAllText(tmp, text ?? "", new UTF8Encoding(false));
            File.Move(tmp, target, true);

            if (oldPath != null && !samePlace && File.Exists(oldPath))
            {
                try { File.Delete(oldPath); } catch (Exception ex) { App.Log(ex); }
            }
            return target;
        }

        /// <summary>Подходит ли файл, чтобы стать заметкой (перетаскивание в заметки).</summary>
        public static bool IsNoteFile(string path) =>
            !string.IsNullOrEmpty(path) && File.Exists(path) &&
            string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Копирует .txt в папку заметок (исходный файл остаётся на месте).
        /// Если файл с таким именем уже есть — добавляется (2), (3)…
        /// </summary>
        public static string Import(string src)
        {
            var dir = EnsureFolder();
            var srcDir = Path.GetDirectoryName(Path.GetFullPath(src))?.TrimEnd('\\') ?? "";
            if (string.Equals(srcDir, Path.GetFullPath(dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return src; // уже лежит в заметках

            var name = Path.GetFileNameWithoutExtension(src);
            var target = Path.Combine(dir, name + ".txt");
            if (File.Exists(target)) target = Unique(dir, name);
            File.Copy(src, target);
            // Чтобы новая заметка оказалась вверху списка (он отсортирован по дате изменения).
            try { File.SetLastWriteTime(target, DateTime.Now); } catch { }
            return target;
        }

        public static void Delete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { App.Log(ex); }
        }

        private static string Unique(string dir, string name)
        {
            for (int i = 2; ; i++)
            {
                var p = Path.Combine(dir, $"{name} ({i}).txt");
                if (!File.Exists(p)) return p;
            }
        }

        private static string SafeName(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "";
            var bad = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var ch in title.Trim())
                sb.Append(Array.IndexOf(bad, ch) >= 0 ? '_' : ch);
            var s = sb.ToString().Trim().TrimEnd('.');
            return s.Length > 120 ? s.Substring(0, 120) : s;
        }
    }
}
