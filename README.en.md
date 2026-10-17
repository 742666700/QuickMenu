# QuickMenu

[中文](README.md)

Press one hotkey and a menu opens in the middle of the screen with everything on your desktop, your games and your running apps: click or press Enter to open or switch. Works with Windows handhelds and gamepads.

> **QuickMenu is open source and always free.** If you paid for it, you were scammed: ask the seller for a refund right away. Any sale of it has nothing to do with the developer, who takes no responsibility for it.

![QuickMenu with the Cream Daisy theme and Claude-chan](docs/images/menu-claude.jpg)

## What it does

- **Your desktop, one key away**: shortcuts, files and folders from the desktop, sorted into programs, games, documents, media and AI tools; search, pin and drag to reorder.
- **Game library**: games installed through Steam, Epic, GOG, Ubisoft Connect and the Xbox app / Microsoft Store, plus EA, Battle.net, HoYoverse, WeGame and others registered with Windows; add your own game folders (Unity, Unreal, Godot, RPG Maker, Ren'Py and more are recognised) or single games.
- **Your own categories**: by words in the name, file extension or path; the built-in rules can be edited too, and right-click sets a category by hand.
- **Running apps**: switch to open windows and tray apps, and end a process after confirming.
- **Controller**: Menu + View opens the menu, D-pad / left stick move, A confirms, B opens the right-click menu, LB / RB switch pages, LT / RT switch tabs.
- **Themes**: OLED Day / Night, Cream Daisy, Pearl, Deep Sea and Starry Violet with light motion, and an optional character standing beside the menu.
- **English / 中文** interface, following Windows or chosen in settings.

| | |
| --- | --- |
| ![OLED Night](docs/images/menu-oled.png) | ![Deep Sea with DeepSeek-chan](docs/images/menu-deepseek.jpg) |
| ![Settings › Games](docs/images/settings-games.png) | ![English interface](docs/images/menu-english.png) |

## Download and use

1. Download `QuickMenu-Portable-<version>-x64.zip` from [Releases](../../releases) and unzip it anywhere.
2. Run `QuickMenu.exe`. The default hotkey is **Ctrl + Alt + Space**; change it in settings.
3. When hidden it waits in the notification area; settings can start it when you sign in.

Needs 64-bit Windows 11 (Windows 10 should work but is not fully tested). The portable build carries its own runtime, so .NET doesn't need to be installed. See the [user guide](docs/user-guide.md).

Settings, the game library and categories live in `%LOCALAPPDATA%\QuickMenu`; delete that folder to start over.

## Build from source

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```powershell
./scripts/build.ps1                          # build and run every test
./scripts/build.ps1 -SkipInteractiveTests    # skip tests that need a desktop session (as CI does)
./scripts/build.ps1 -Package                 # also make artifacts/QuickMenu-Portable-<version>-x64.zip
dotnet run --project src/QuickMenu           # run it
```

Self-check: `QuickMenu.exe --qa --output <folder>` exercises the interface with sample data and saves screenshots; it never starts or ends programs and never writes settings.

## Layout

- `src/QuickMenu`: the WPF app. `Services/` holds the desktop, game library, window, tray, process and controller services; `Strings/` the Chinese and English text; `Assets/Characters/` the character art.
- `tests/QuickMenu.Tests`: unit and integration tests; `tests/QuickMenu.Fixture` is a controlled window used by the integration tests.
- `scripts`: build, packaging, icon and font-subset scripts.

Issues and pull requests are welcome. New interface text goes into both `Strings/zh-CN.json` and `Strings/en.json` (the self-check compares their keys).

## License and notice

The code is released under the [MIT license](LICENSE). The heading font is a subset of Noto Serif SC under the [SIL Open Font License](src/QuickMenu/Assets/Fonts/OFL.txt).

The characters and their themes are fan art and are not affiliated with Anthropic, OpenAI, DeepSeek or Google; Claude, ChatGPT, DeepSeek, Gemini and other names and trademarks belong to their owners. If you hold rights and object, please open an issue and the material will be removed promptly.

## Contact

Please open an [issue](../../issues) for bugs and ideas, or email 742666700@163.com.
