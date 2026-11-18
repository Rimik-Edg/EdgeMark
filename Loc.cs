using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;

namespace EdgeMark
{
    /// <summary>
    /// Язык интерфейса (русский / английский).
    /// В разметке надписи берутся как {DynamicResource L.Ключ} — поэтому язык меняется сразу, без перезапуска.
    /// В коде — Loc.T("Ключ").
    /// </summary>
    public static class Loc
    {
        public static string Lang { get; private set; } = SystemLang;

        public static event Action Changed;

        /// <summary>Язык системы: русский, если Windows на русском, иначе английский.</summary>
        public static string SystemLang =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";

        /// <summary>Язык из настроек ("ru" / "en"), а если не задан — системный.</summary>
        public static string Resolve(string configured) =>
            configured == "ru" || configured == "en" ? configured : SystemLang;

        public static void Apply(string lang)
        {
            Lang = lang == "en" ? "en" : "ru";
            var r = Application.Current?.Resources;
            if (r != null)
                foreach (var kv in S)
                    r["L." + kv.Key] = Lang == "en" ? kv.Value.En : kv.Value.Ru;
            Changed?.Invoke();
        }

        public static string T(string key) =>
            S.TryGetValue(key, out var v) ? (Lang == "en" ? v.En : v.Ru) : key;

        /// <summary>
        /// Стандартные названия, которые программа сама кладёт в настройки («Быстрый доступ», «Загрузки»…),
        /// показываются на текущем языке. Свои названия пользователя не трогаются.
        /// </summary>
        public static string Name(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return stored;
            foreach (var key in DefaultNameKeys)
                if (S.TryGetValue(key, out var v) &&
                    (string.Equals(stored, v.Ru, StringComparison.Ordinal) || string.Equals(stored, v.En, StringComparison.Ordinal)))
                    return T(key);
            return stored;
        }

        private static readonly string[] DefaultNameKeys =
        {
            "QuickAccess", "NewGroup", "DefDownloads", "DefDocuments", "DefDesktop", "DefStartup", "DefScreenshots", "DefThisPC",
        };

        /// <summary>Выбор из пары строк по текущему языку (для списков вроде названий цветов).</summary>
        public static string Pick(string ru, string en) => Lang == "en" ? en : ru;

        private static readonly Dictionary<string, (string Ru, string En)> S = new Dictionary<string, (string, string)>
        {
            // общее
            ["AppName"] = ("EdgeMark", "EdgeMark"),
            ["Cancel"] = ("Отмена", "Cancel"),
            ["Save"] = ("Сохранить", "Save"),
            ["Delete"] = ("Удалить", "Delete"),
            ["Settings"] = ("Настройки", "Settings"),
            ["Path"] = ("Путь", "Path"),
            ["Name"] = ("Название", "Name"),
            ["Untitled"] = ("(без названия)", "(untitled)"),
            ["Empty"] = ("(пусто)", "(empty)"),
            ["AllFiles"] = ("Все файлы", "All files"),

            // меню закладок
            ["MenuTitle"] = ("EdgeMark — закладки", "EdgeMark — bookmarks"),
            ["Bookmarks"] = ("Закладки", "Bookmarks"),
            ["AddFolder"] = ("Добавить папку…", "Add folder…"),
            ["AddFile"] = ("Добавить файл или программу…", "Add file or program…"),
            ["CopyPath"] = ("Копировать путь", "Copy path"),
            ["Edit"] = ("Изменить", "Edit"),
            ["MenuEmpty"] = ("Список пуст. Перетащите папку или файл на язычок или нажмите «+».",
                             "The list is empty. Drag a folder or file onto the tab, or press \"+\"."),
            ["NotFound"] = ("Не найдено", "Not found"),

            // окно «Изменить»
            ["EditTitle"] = ("Изменить закладку", "Edit bookmark"),
            ["DisplayName"] = ("Название в списке", "Display name"),
            ["DisplayNameHint"] = ("Меняется только подпись в списке, сам файл или папка не переименовываются. Оставьте пустым, чтобы показывать настоящее имя.",
                                   "Only the label in the list changes; the file or folder itself is not renamed. Leave empty to show the real name."),
            ["PickFolder"] = ("Выбрать папку", "Choose folder"),
            ["PickFile"] = ("Выбрать файл", "Choose file"),
            ["Icon"] = ("Значок", "Icon"),
            ["IconSystem"] = ("Системный", "System"),
            ["IconSet"] = ("Из набора", "From set"),
            ["IconFile"] = ("Из файла", "From file"),
            ["PickIconFile"] = ("Выбрать файл…", "Choose file…"),
            ["PickIconDialog"] = ("Выберите картинку или программу", "Choose an image or program"),
            ["ImagesAndIcons"] = ("Картинки и значки", "Images and icons"),
            ["NoFileChosen"] = ("файл не выбран", "no file chosen"),
            ["RemoveFromList"] = ("Убрать из списка", "Remove from list"),
            ["ConfirmRemove"] = ("Точно убрать?", "Remove?"),
            ["ConfirmRemoveTip"] = ("Нажмите ещё раз — пункт пропадёт из списка (на диске он останется)",
                                    "Click again — the item is removed from the list (it stays on disk)"),
            ["EnterPath"] = ("Укажите путь.", "Enter a path."),
            ["ShellPath"] = ("Системный путь, откроется через проводник.", "Shell path, opens via Explorer."),
            ["Found"] = ("✓ Найдено", "✓ Found"),

            // заметки
            ["NotesTitle"] = ("EdgeMark — заметки", "EdgeMark — notes"),
            ["Notes"] = ("Заметки", "Notes"),
            ["NewNote"] = ("Новая заметка", "New note"),
            ["OpenNotesFolder"] = ("Открыть папку с заметками", "Open notes folder"),
            ["ToggleText"] = ("Показать / скрыть текст", "Show / hide text"),
            ["CopyText"] = ("Копировать текст", "Copy text"),
            ["CopyFile"] = ("Копировать файл (вставится как .txt)", "Copy file (pastes as .txt)"),
            ["NotesEmpty"] = ("Заметок пока нет. Нажмите «+» или перетащите сюда .txt файл.",
                              "No notes yet. Press \"+\" or drop a .txt file here."),
            ["NotesDrop"] = ("Отпустите, чтобы добавить копию в заметки", "Release to add a copy to notes"),
            ["Note"] = ("Заметка", "Note"),
            ["NoteTitlePlaceholder"] = ("Название заметки", "Note title"),
            ["Unsaved"] = ("● не сохранено", "● unsaved"),
            ["SaveFailed"] = ("Не удалось сохранить заметку:", "Could not save the note:"),
            ["SaveChanges"] = ("Сохранить изменения в заметке?", "Save changes to the note?"),
            ["ConfirmDelete"] = ("Точно удалить?", "Delete?"),
            ["ConfirmDeleteTip"] = ("Нажмите ещё раз — файл заметки будет удалён", "Click again — the note file will be deleted"),
            ["MoveFailed"] = ("Не удалось скопировать в заметки:", "Could not copy into notes:"),

            // настройки
            ["SettingsTitle"] = ("EdgeMark — настройки", "EdgeMark — settings"),
            ["SettingsSub"] = ("Перетащите папки и файлы из проводника в список или на язычок у края экрана.",
                               "Drag folders and files from Explorer into the list or onto the tab at the screen edge."),
            ["AddFolderShort"] = ("Папку", "Folder"),
            ["AddFileShort"] = ("Файл", "File"),
            ["Remove"] = ("Удалить", "Remove"),
            ["MoveUp"] = ("Выше", "Move up"),
            ["MoveDown"] = ("Ниже", "Move down"),
            ["TestOpen"] = ("Открыть для проверки", "Open to test"),
            ["PathHint"] = ("Можно писать переменные %APPDATA%, %LOCALAPPDATA%, %USERPROFILE%, %TEMP%, а также системные пути вида shell:startup или shell:downloads.",
                            "You can use variables such as %APPDATA%, %LOCALAPPDATA%, %USERPROFILE%, %TEMP%, and shell paths like shell:startup or shell:downloads."),
            ["ScreenEdge"] = ("Край экрана", "Screen edge"),
            ["EdgeLeft"] = ("Слева", "Left"),
            ["EdgeRight"] = ("Справа", "Right"),
            ["Autostart"] = ("Запускать вместе с Windows", "Start with Windows"),
            ["HideFullscreen"] = ("Прятать язычки над играми и видео на весь экран", "Hide over full-screen games and videos"),
            ["TabColor"] = ("Цвет язычков у края", "Tab color"),
            ["WindowColor"] = ("Цвет окон", "Window color"),
            ["ColorSystem"] = ("Системный", "System"),
            ["ColorCustom"] = ("Свой", "Custom"),
            ["TabOpacity"] = ("Прозрачность язычков", "Tab opacity"),
            ["TabOpacityHint"] = ("видно, пока мышь не наведена", "when not hovered"),
            ["Language"] = ("Язык", "Language"),
            ["UiScale"] = ("Масштаб", "Scale"),
            ["UiScaleHint"] = ("язычки у края и окна закладок и заметок", "edge tabs and the bookmarks / notes windows"),
            ["OpenConfig"] = ("Открыть config.json", "Open config.json"),

            // горячие клавиши
            ["Hotkeys"] = ("Горячие клавиши", "Hotkeys"),
            ["HotkeyTip"] = ("Щёлкните и нажмите сочетание клавиш. Backspace — убрать.", "Click and press a key combination. Backspace clears it."),
            ["HotkeyPress"] = ("нажмите клавиши…", "press keys…"),
            ["HotkeyNone"] = ("нет", "none"),
            ["HotkeyOverlay"] = ("Открывать поверх всего с затемнением", "Open on top of everything, dim the screen"),
            ["HotkeyBusy"] = ("Не удалось назначить — сочетание уже занято другой программой:", "Could not assign — the combination is already used by another program:"),

            // программы, в которых язычки прячутся
            ["HideInApps"] = ("Прятать язычки в программах", "Hide tabs in these programs"),
            ["HideInAppsHint"] = ("Пока одна из этих программ на переднем плане, язычки у края исчезают и не реагируют на мышь — чтобы случайно не нажать, например, в игре. Переключились на другое окно, нажали Win или открыли панель задач — язычки снова на месте. Горячие клавиши и значки в трее работают всегда.",
                                  "While one of these programs is in the foreground, the edge tabs disappear and ignore the mouse — so you can't click them by accident, e.g. in a game. Switch to another window, press Win or open the taskbar and the tabs are back. Hotkeys and tray icons always work."),
            ["AddFromOpen"] = ("Из открытых", "From open windows"),
            ["AddExe"] = ("Выбрать .exe…", "Choose .exe…"),
            ["NoOpenApps"] = ("Нет подходящих открытых окон", "No suitable open windows"),
            ["RemoveApp"] = ("Убрать из списка", "Remove from list"),
            ["Programs"] = ("Программы", "Programs"),

            // группы
            ["NewGroup"] = ("Новая группа", "New group"),
            ["GroupTitle"] = ("Группа закладок", "Bookmark group"),
            ["GroupName"] = ("Название группы", "Group name"),
            ["GroupTitleColor"] = ("Цвет названия", "Title color"),
            ["GroupItemColor"] = ("Цвет полоски у закладок", "Bookmark stripe color"),
            ["EditGroup"] = ("Изменить группу", "Edit group"),
            ["DeleteGroup"] = ("Удалить группу", "Delete group"),
            ["ConfirmDeleteGroup"] = ("Точно удалить группу?", "Delete the group?"),
            ["ConfirmDeleteGroupTip"] = ("Закладки из группы останутся в списке, просто без группы", "Bookmarks from the group stay in the list, just without a group"),
            ["MenuDrop"] = ("Отпустите, чтобы добавить в закладки", "Release to add to bookmarks"),
            ["GroupHint"] = ("Перетащите закладки в группу. Клик по названию сворачивает её.", "Drag bookmarks into the group. Click its name to collapse it."),

            // трей и сообщения
            ["TrayBookmarks"] = ("Открыть закладки", "Open bookmarks"),
            ["TrayNotes"] = ("Открыть заметки", "Open notes"),
            ["TraySettings"] = ("Настройки…", "Settings…"),
            ["Exit"] = ("Выход", "Exit"),
            ["AlreadyRunning"] = ("EdgeMark уже запущен — ищите его у края экрана или в трее.",
                                  "EdgeMark is already running — look for it at the screen edge or in the tray."),
            ["OpenFailed"] = ("Не удалось открыть:", "Could not open:"),
            ["ConfigBroken"] = ("Не удалось прочитать config.json, создан список по умолчанию.\nСтарый файл сохранён как config.json.broken.",
                                "Could not read config.json; a default list was created.\nThe old file was saved as config.json.broken."),

            // папки по умолчанию
            ["DefDownloads"] = ("Загрузки", "Downloads"),
            ["DefDocuments"] = ("Документы", "Documents"),
            ["DefDesktop"] = ("Рабочий стол", "Desktop"),
            ["DefStartup"] = ("Автозагрузка", "Startup"),
            ["DefScreenshots"] = ("Скриншоты", "Screenshots"),
            ["DefThisPC"] = ("Этот компьютер", "This PC"),
            ["QuickAccess"] = ("Быстрый доступ", "Quick access"),
        };
    }
}
