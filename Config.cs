using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace EdgeMark
{
    public class FolderEntry
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";

        /// <summary>
        /// Свой значок: null — системный; "glyph:E8B7:#FFB900" (или :accent / :fg) — значок из набора;
        /// "file:путь" — картинка .png/.jpg/.ico или значок .exe/.dll.
        /// </summary>
        public string Icon { get; set; }

        /// <summary>Id группы (GroupDef.Id), null — без группы.</summary>
        public string Group { get; set; }

        /// <summary>Подпись в закладке: своё название или настоящее имя папки / файла.</summary>
        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Name)) return Loc.Name(Name.Trim());
                if (IsShell) return Path.Trim();
                var p = ExpandedPath.TrimEnd('\\', '/');
                var n = System.IO.Path.GetFileName(p);
                // У ярлыков расширение не показываем — как на рабочем столе.
                var ext = System.IO.Path.GetExtension(n).ToLowerInvariant();
                if ((ext == ".lnk" || ext == ".url") && File.Exists(p)) n = System.IO.Path.GetFileNameWithoutExtension(n);
                return string.IsNullOrEmpty(n) ? (string.IsNullOrEmpty(p) ? Loc.T("Untitled") : p) : n;
            }
        }

        public FolderEntry Clone() => new FolderEntry { Name = Name, Path = Path, Icon = Icon, Group = Group };

        [JsonIgnore]
        public string ExpandedPath => Expand(Path);

        public static string Expand(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "";
            p = p.Trim().Trim('"');
            if (p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || p.StartsWith("::")) return p;
            p = Environment.ExpandEnvironmentVariables(p);
            if (p.StartsWith("~"))
                p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                           p.Substring(1).TrimStart('\\', '/'));
            return p;
        }

        [JsonIgnore]
        public bool IsShell => Path != null &&
            (Path.Trim().StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || Path.Trim().StartsWith("::"));

        [JsonIgnore]
        public bool Exists => IsShell || Directory.Exists(ExpandedPath) || File.Exists(ExpandedPath);
    }

    public enum Edge { Left, Right }

    /// <summary>Группа закладок: сворачивается, у неё своё название и цвета.</summary>
    public class GroupDef
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name { get; set; } = "";
        /// <summary>Цвет названия группы (#RRGGBB), null — обычный.</summary>
        public string Color { get; set; }
        /// <summary>Цвет полоски слева у закладок группы, null — цвет язычков.</summary>
        public string ItemColor { get; set; }
        public bool Collapsed { get; set; }

        /// <summary>
        /// Где группа стоит в списке: сколько элементов вне групп идёт перед ней
        /// (0 — в самом верху; большое число — в самом низу).
        /// </summary>
        public int Position { get; set; } = 1_000_000;
    }


    public class Config
    {
        public Edge Edge { get; set; } = Edge.Right;

        /// <summary>Положение закладки вдоль края: 0 — верх, 1 — низ.</summary>
        public double Position { get; set; } = 0.25;

        public bool HideOverFullscreen { get; set; } = true;

        /// <summary>Цвет закладки у края: null — системный (акцентный цвет Windows), иначе #RRGGBB.</summary>
        public string TabColor { get; set; }

        /// <summary>Непрозрачность язычков, пока на них не навели мышь: 0.15–1.</summary>
        public double TabOpacity { get; set; } = 0.6;

        /// <summary>Масштаб язычков и окон закладок/заметок: 0.5–2 (1 = 100%).</summary>
        public double UiScale { get; set; } = 1.0;

        [JsonIgnore]
        public double Scale => Math.Clamp(UiScale <= 0 ? 1.0 : UiScale, 0.5, 2.0);

        /// <summary>Цвет окна со списком папок: null — по теме Windows, иначе #RRGGBB.</summary>
        public string MenuColor { get; set; }

        /// <summary>Папка заметок: null — «Документы\EdgeMark\Заметки» (по-английски «Documents\EdgeMark\Notes»).</summary>
        public string NotesFolder { get; set; }

        /// <summary>Горячая клавиша окна закладок ("" — нет).</summary>
        public string HotkeyMenu { get; set; } = "Ctrl+Alt+OemTilde";

        /// <summary>Горячая клавиша окна заметок ("" — нет).</summary>
        public string HotkeyNotes { get; set; } = "Ctrl+OemTilde";

        /// <summary>
        /// Программы (имя .exe, например "FSD-Win64-Shipping.exe"), пока активна любая из которых
        /// язычки у края скрыты и не реагируют на мышь.
        /// </summary>
        public List<string> HideInApps { get; set; } = new List<string>();

        /// <summary>По горячей клавише открывать поверх всего с затемнением (как оверлей Steam), а не у язычка.</summary>
        public bool HotkeyOverlay { get; set; } = true;

        /// <summary>Группы закладок (в порядке показа). Закладка попадает в группу через FolderEntry.Group.</summary>
        public List<GroupDef> Groups { get; set; } = new List<GroupDef>();

        /// <summary>Группы заметок (как у закладок).</summary>
        public List<GroupDef> NoteGroups { get; set; } = new List<GroupDef>();

        /// <summary>В какой группе заметка: имя файла → Id группы.</summary>
        public Dictionary<string, string> NoteGroupOf { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Порядок заметок (имена файлов). Новые заметки, которых тут нет, показываются сверху.</summary>
        public List<string> NoteOrder { get; set; } = new List<string>();

        /// <summary>Версия раскладки настроек — для однократных обновлений старых config.json.</summary>
        public int LayoutVersion { get; set; }

        /// <summary>Язык интерфейса: "ru", "en" или null — как в системе.</summary>
        public string Language { get; set; }

        public List<FolderEntry> Folders { get; set; } = new List<FolderEntry>();

        // ----------------------------------------------------------------

        public static string Dir =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EdgeMark");

        /// <summary>Где жили настройки, пока программа называлась «Закладка» / Zakladka.</summary>
        private static string OldDir =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zakladka");

        /// <summary>Первый запуск под новым именем: забираем настройки из старой папки, чтобы ничего не пропало.</summary>
        private static void MigrateOldDir()
        {
            try
            {
                if (File.Exists(FilePath) || !Directory.Exists(OldDir)) return;
                Directory.CreateDirectory(Dir);
                foreach (var f in Directory.GetFiles(OldDir))
                    File.Copy(f, System.IO.Path.Combine(Dir, System.IO.Path.GetFileName(f)), false);
            }
            catch { /* не получилось — начнём с настроек по умолчанию */ }
        }

        public static string FilePath => System.IO.Path.Combine(Dir, "config.json");

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            Converters = { new JsonStringEnumConverter() },
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static Config Load()
        {
            MigrateOldDir();
            try
            {
                if (File.Exists(FilePath))
                {
                    var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath), Json);
                    if (cfg != null)
                    {
                        cfg.Folders ??= new List<FolderEntry>();
                        cfg.Groups ??= new List<GroupDef>();
                        cfg.HotkeyMenu ??= "";
                        cfg.HotkeyNotes ??= "";
                        cfg.HideInApps ??= new List<string>();
                        cfg.NoteGroups ??= new List<GroupDef>();
                        cfg.NoteOrder ??= new List<string>();
                        cfg.NoteGroupOf = cfg.NoteGroupOf == null
                            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, string>(cfg.NoteGroupOf, StringComparer.OrdinalIgnoreCase);
                        if (cfg.LayoutVersion < 2) { AddQuickAccess(cfg, migrate: true); cfg.LayoutVersion = 2; cfg.Save(); }
                        // Прежние клавиши по умолчанию (Ctrl+Alt+B / N) меняем на новые, если их не трогали.
                        if (cfg.HotkeyMenu == "Ctrl+Alt+B" && cfg.HotkeyNotes == "Ctrl+Alt+N")
                        {
                            cfg.HotkeyMenu = "Ctrl+Alt+OemTilde";
                            cfg.HotkeyNotes = "Ctrl+OemTilde";
                            cfg.Save();
                        }
                        cfg.Position = Math.Clamp(cfg.Position, 0, 1);
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                // Битый конфиг не удаляем, а откладываем в сторону, чтобы пользователь не потерял список.
                try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
                System.Windows.MessageBox.Show(
                    Loc.T("ConfigBroken") + "\n\n" + ex.Message,
                    Loc.T("AppName"));
            }

            var def = CreateDefault();
            def.Save();
            return def;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
                File.Move(tmp, FilePath, true);
            }
            catch { /* не критично */ }
        }

        public static Config CreateDefault()
        {
            var c = new Config { LayoutVersion = 2 };
            c.Folders.Add(new FolderEntry { Name = "AppData\\Local", Path = "%LOCALAPPDATA%" });
            c.Folders.Add(new FolderEntry { Name = Loc.T("DefDocuments"), Path = "%USERPROFILE%\\Documents" });
            c.Folders.Add(new FolderEntry { Name = Loc.T("DefStartup"), Path = "shell:startup" });
            AddQuickAccess(c, migrate: false);
            return c;
        }

        /// <summary>Раздел проводника «Этот компьютер» (диски).</summary>
        public const string ThisPcPath = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";

        /// <summary>
        /// Группа «Быстрый доступ» в самом верху: AppData, Загрузки, Рабочий стол, Скриншоты, Этот компьютер.
        /// При обновлении старых настроек такие закладки, если они уже были, переезжают в группу (без дублей).
        /// </summary>
        private static void AddQuickAccess(Config c, bool migrate)
        {
            var g = new GroupDef { Name = Loc.T("QuickAccess"), Position = 0 };
            var wanted = new (string Name, string Path)[]
            {
                ("AppData", "%APPDATA%"),
                (Loc.T("DefDownloads"), "%USERPROFILE%\\Downloads"),
                (Loc.T("DefDesktop"), "%USERPROFILE%\\Desktop"),
                (Loc.T("DefScreenshots"), "%USERPROFILE%\\Pictures\\Screenshots"),
                (Loc.T("DefThisPC"), ThisPcPath),
            };
            var members = new List<FolderEntry>();
            foreach (var (name, path) in wanted)
            {
                var exp = FolderEntry.Expand(path).TrimEnd('\\');
                var existing = migrate
                    ? c.Folders.FirstOrDefault(f => f.Group == null &&
                        string.Equals(f.ExpandedPath.TrimEnd('\\'), exp, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (existing != null)
                {
                    c.Folders.Remove(existing);
                    existing.Group = g.Id;
                    members.Add(existing);
                }
                else members.Add(new FolderEntry { Name = name, Path = path, Group = g.Id });
            }
            c.Folders.InsertRange(0, members);
            c.Groups.Insert(0, g);
        }
    }
}
