# QuickMenu user guide

[中文](使用说明.md)

QuickMenu is a 64-bit Windows 11 desktop tool: press a hotkey and a menu opens in the middle of the screen to open desktop items and games and switch between running apps. It runs with normal user rights.

> **QuickMenu is open source and always free.** If you paid for it, you were scammed: ask the seller for a refund right away. Any sale of it has nothing to do with the developer, who takes no responsibility for it.

## Getting started

1. Run the installer `QuickMenu-Setup-<version>-x64.exe` and open QuickMenu from the Start menu; it checks for the .NET 10 Desktop Runtime and installs it when missing. Or unzip a portable package and run `QuickMenu.exe`. The first run shows a short welcome.
   - The full zip (`-x64.zip`) carries its own .NET runtime; unzip and run.
   - The small zip (`-x64-small.zip`) needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64, or `winget install Microsoft.DotNet.DesktopRuntime.10`). Without it, starting the app shows a .NET message with a download button; run it again once the runtime is installed. Both zips have the same features.
2. The default hotkey is **Alt + Space**: press it to open the menu, and again (or Esc) to hide it.
3. When hidden it waits in the notification area at the right of the taskbar; its tray menu opens the menu, opens settings or exits.
4. The menu opens in the middle of the display the mouse is on; drag the empty top area to move it.

## Desktop page

- Shows the shortcuts, files, folders and system icons on your desktop, plus the games in the game library. Click or press Enter to open; just type to search.
- Tabs along the top: **All, Games, AI tools, More⌄, Docs+**, and any of your own categories shown as tabs. Hovering Docs+ shows Documents, Spreadsheets and Presentations.
- **Pin**: right-click › Pin to the top puts an item in the pinned row.
- **Sort**: the menu at the right offers most used, modified, type or custom; press and hold an item to drag it, which switches to the custom order.
- **Right-click menu**: pin, category, mark or unmark as an AI tool, hide from the game library (library games), open file location, end process (while the program runs).
- Put AI tool shortcuts in a desktop folder named "AI Tools" (or "AI工具") and the menu lists them directly.

## Running page

Shows apps with windows (minimised ones included) and the tray apps that can be read. Several windows of one process are grouped, and you choose one when switching. Ending a process always asks first, ends only that process and never elevates.

## Keyboard

| Key | Does |
| --- | --- |
| Hotkey (Alt + Space by default) | Open / hide the menu |
| Tab | Switch between Desktop and Running |
| Arrow keys | Move the selection |
| Enter | Open the item or switch to the window |
| Esc | Close a dialog, or hide the menu |
| F5 | Scan the desktop and games again |
| Shift + F10 / Menu key | Right-click menu of the selected item |

## Controller (Windows handhelds, Xbox controllers)

| Button | Does |
| --- | --- |
| Menu + View together | Open / hide the menu, any time |
| D-pad / left stick | Move the selection, repeating while held; also works in right-click menus, drop-downs, dialogs and settings |
| A | Open the selected item, or confirm |
| B | Right-click menu of the selected item; closes a menu, drop-down or dialog that is open; leaves settings |
| LB / RB | Switch between Desktop and Running |
| LT / RT | Step through the tabs along the top of Desktop (All, Games, AI tools…); in settings, jump to the previous / next part |
| Y | Open / leave settings |

XInput controllers are supported (Xbox controllers and most Windows handhelds). Turn it off in Settings › General › Controller.

## Settings

The gear at the top right opens settings; the list on the left jumps to each part. Every change applies and saves at once.

- **General**: the free-software notice, language (same as Windows / 简体中文 / English), the hotkey (must include Ctrl or Alt; if another program holds it, the old one is kept), the time in the greeting (name the local time and add a second time zone), whether clicking outside hides the menu (it does by default; turned off, the menu steps behind the window you clicked and the hotkey or tray icon brings it back with your search and page kept), start when you sign in, controller.
- **Theme**: OLED Day, OLED Night, Cream Daisy, Pearl, Deep Sea, Starry Violet or same as Windows, and a character (goes with any theme; optionally switches with the theme). **Character idle actions** is on by default. Turning it off stops spontaneous actions, dozing and theme reactions; hover and click responses remain. Turning Windows animations off stops all character and theme motion.
- **Games**:
  - Reads games installed through Steam (every library), Epic, GOG, Ubisoft Connect and the Xbox app / Microsoft Store, plus EA, Battle.net, HoYoverse, WeGame and others whose game is registered with Windows; each store can be turned off.
  - Add game folder: scans for games made with Unity, Unreal, Godot, GameMaker, RPG Maker, Ren'Py, KiriKiri and more.
  - Add game: pick a game's exe or shortcut.
  - Hidden games are restored here.
- **Categories**:
  - The built-in rules can be edited or restored: Games (which folders shortcuts point into), AI tools (words in the name), Documents / Spreadsheets / Presentations / Media (extensions). The Games and AI tools tabs can be hidden.
  - New category: words in the name, extensions, text in the path, any one of which is enough; show it as a tab, move it up or down, delete it.
  - Order: set by hand › your own categories › automatic.
- **About**: version, project page, contact email, license, open the data folder.

## Data and uninstalling

Everything is kept in `%LOCALAPPDATA%\QuickMenu`:

| File | Holds |
| --- | --- |
| `settings.json` | Hotkey, theme, language and other settings |
| `desktop-preferences.json` | Pins, order, categories set by hand, launch counts |
| `game-library.json` | Stores read, game folders, games added and hidden |
| `categories.json` | Your categories and changed built-in rules |
| `Logs\app.log` | Error log |

To uninstall: turn off start-on-sign-in, exit from the tray, delete the program folder, and delete the folder above to remove settings too. Start-on-sign-in only uses a value named `QuickMenu` in your user's Run key.

The program isn't code-signed, so Windows may warn about an unknown publisher on first run.

## Characters

Each character has her own pace: rice-bowl DeepSeek reacts a little late, eats and dozes; Claude reads and nods decisively; ChatGPT responds gently; Gemini twitches her ears and swishes her tail. Rice-bowl DeepSeek is more likely to eat at meal times and appear asleep during lunchtime or late at night, using your computer's local time. Front-facing Claude keeps a focused gaze while the search box contains text.

After two minutes without keyboard, mouse or controller input, character clips, loops and theme motion stop. Applicable poses keep blinking, while sleeping DeepSeek stays asleep. Input resumes motion; hiding the menu stops all motion immediately. The character window never takes keyboard focus from the menu.

The characters and their themes are fan art, not affiliated with Anthropic, OpenAI, DeepSeek or Google; their names and trademarks belong to their owners.

## Contact

Project page: https://github.com/742666700/QuickMenu (please open an issue for bugs and ideas)

Email: 742666700@163.com
