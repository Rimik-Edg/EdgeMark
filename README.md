# EdgeMark

**EdgeMark** is a small Windows utility that lives at the edge of your screen.
Two slim tabs sit on top of all windows: one opens your **bookmarks** (folders, programs, shortcuts, any files),
the other opens your **notes** (plain `.txt` files). Everything is one click away, without digging through Explorer.

English | [Русский](README.ru.md)

> **Screenshots:** put images into a `docs/` folder and reference them here, e.g. `![Bookmarks](docs/bookmarks.png)`.

## Features

**Bookmarks**
- Pin folders, programs (`.exe`), shortcuts (`.lnk`), documents — any file or folder. They open exactly like a double-click in Explorer.
- Add by drag & drop: hover a file over the tab, the window opens, drop it where you want (even straight into a group).
- **Groups**: create, rename, recolor (title color and stripe color), collapse, delete. Groups can be dragged anywhere in the list, and bookmarks can be dragged in and out of groups.
- A ready-made **Quick access** group: AppData, Downloads, Desktop, Screenshots, This PC.
- Custom display names and icons (system icon, built-in icon set with colors, or any image / `.exe` / `.ico`).
- Copy a bookmark's full path with one click.

**Notes**
- Notes are ordinary `.txt` files in `Documents\EdgeMark\Notes` (or a folder of your choice).
- Expand a note to read, scroll, select and copy its text right in the list.
- Copy the note's text or the file itself (pastes as a `.txt` into Explorer, messengers, etc.).
- Built-in editor (`Ctrl+S` to save). Drop a `.txt` onto the notes tab to add a copy of it.
- Groups work the same way as for bookmarks.

**Ways to open**
- Edge tabs (click). Tabs can be dragged along the screen edge, left or right side.
- Two tray icons: bookmarks and notes.
- Global hotkeys, configurable (defaults: `` Ctrl+Alt+` `` for bookmarks, `` Ctrl+` `` for notes; `` ` `` is the key left of 1).
- Optional **overlay mode**: the hotkey dims all screens (like the Steam overlay) and shows the window in the center with the cursor already on it. `Esc` or a click on the dimmed area closes it.

**Stays out of your way**
- Tabs hide automatically over full-screen games and videos (including borderless windowed mode).
- A list of programs in which the tabs are hidden and ignore the mouse (pick from open windows or choose an `.exe`).
- Tabs reappear instantly when you switch to another window, press `Win` or open the taskbar.

**Looks**
- Windows 11 style: acrylic background, rounded corners, light and dark theme following the system.
- Custom colors for the tabs and for the windows, tab opacity, UI scale from 50% to 200%.
- English and Russian interface (follows the system language by default, switchable in settings).

## Download

Grab `EdgeMark.exe` from the [Releases](../../releases) page. It is a single self-contained file:
no installer and no .NET installation needed. Requires **Windows 10 or 11, 64-bit**.

On the first run Windows SmartScreen may show "Windows protected your PC", because the program is not code-signed.
Click **More info → Run anyway**. Some antiviruses may be suspicious for the same reason.

## Usage

| Action | How |
|---|---|
| Open bookmarks / notes | Click the edge tab, the tray icon, or press the hotkey |
| Add a bookmark | Drag a file/folder onto the bookmarks tab, or use the buttons in the window header |
| New group | Thin button left of the "Bookmarks" / "Notes" title |
| Collapse / expand a group | Click its header |
| Reorder | Press and hold a row or a group header, then drag |
| Edit a bookmark / group | ✎ button on hover |
| Settings | Right-click an edge tab, or right-click a tray icon → Settings |
| Close a window | `Esc` or click outside |

In Windows 11 new tray icons are hidden under the `^` arrow at first — drag them onto the taskbar to keep them visible.

## Where data is stored

| What | Where |
|---|---|
| Settings, bookmarks, groups | `%APPDATA%\EdgeMark\config.json` |
| Error log | `%APPDATA%\EdgeMark\error.log` |
| Notes | `Documents\EdgeMark\Notes` (`Documents\EdgeMark\Заметки` for the Russian interface), configurable via `"NotesFolder"` in `config.json` |

Nothing is sent anywhere — the program works fully offline.

## Building from source

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (x64).
2. Run `build.bat` — it produces a self-contained single-file `EdgeMark.exe` next to it.

Or manually:

```bat
dotnet publish EdgeMark.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist
```

Tech: C# / WPF (.NET 8), no third-party packages. The Windows 11 look is done with DWM APIs directly.

## Project structure

| File | What it does |
|---|---|
| `App.xaml(.cs)` | Startup, shared styles, opening/closing windows, overlay, tray and hotkey actions |
| `TabWindow` | The edge tabs: placement, drag along the edge, drag & drop, hiding in games |
| `MenuWindow`, `NotesWindow` | Bookmarks and notes windows |
| `GroupedList.cs` | The list with groups shared by both windows: ordering, numbering, drag & drop |
| `SettingsWindow`, `EditWindow`, `GroupEditWindow`, `NoteEditWindow` | Settings and editors |
| `Config.cs`, `Notes.cs` | Data model and storage |
| `Theme.cs`, `Backdrop.cs`, `PopupKit.cs` | Colors, acrylic, popup positioning |
| `Hotkeys.cs`, `HotkeyBox.cs`, `TrayIcon.cs`, `OverlayWindow.cs` | Global hotkeys, tray icons, overlay |
| `Loc.cs` | English / Russian strings |
| `Native.cs` | Windows API declarations |

## License

[MIT](LICENSE)
