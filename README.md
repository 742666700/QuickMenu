# 快捷菜单 QuickMenu

[English](README.en.md)

按一个快捷键，在屏幕中央呼出一个菜单：桌面上的程序、文件、游戏和正在运行的应用都在里面，点一下或回车就能打开、切换。支持 Windows 掌机和手柄。

> **快捷菜单完全开源、永久免费。** 如果你是花钱买到的，说明被骗了，请立即找卖家退款。任何售卖行为都与开发者无关，开发者不承担由此产生的任何责任。

![快捷菜单（奶油雏菊主题与 Claude娘）](docs/images/menu-claude.jpg)

## 能做什么

- **桌面一键直达**：列出桌面上的快捷方式、文件和文件夹，自动分成程序、游戏、文档、视频图片、AI 工具等；可以搜索、置顶、拖动排序。
- **游戏库**：自动读取 Steam、Epic、GOG、Ubisoft Connect、Xbox / 微软商店，以及 EA、战网、米哈游、WeGame 等登记的游戏；也可以添加自己的游戏文件夹（识别 Unity、虚幻、Godot、RPG Maker、Ren'Py 等引擎）或单个游戏。
- **自定义分类**：按名称关键词、扩展名、路径建自己的分类，内置规则也都能改；右键可以手动归类。
- **运行应用**：切换到正在运行的窗口和托盘程序，确认后可以结束进程。
- **手柄操作**：菜单键 + 视图键呼出，十字键 / 左摇杆移动，A 确认，B 打开右键菜单，LB / RB 切换页面，LT / RT 切换分类标签。
- **主题**：OLED 白天 / 黑夜、奶油雏菊、珍珠白、深海蓝白、星夜紫，带轻量动效；可选一位 AI娘站在菜单旁边。
- **中文 / English** 界面，随系统或手动切换。

| | |
| --- | --- |
| ![OLED 黑夜主题](docs/images/menu-oled.png) | ![深海蓝白主题与 DeepSeek娘](docs/images/menu-deepseek.jpg) |
| ![设置 › 游戏库](docs/images/settings-games.png) | ![English interface](docs/images/menu-english.png) |

## 下载与使用

1. 在 [Releases](../../releases) 下载 `QuickMenu-Portable-<版本>-x64.zip`，解压到任意文件夹。
2. 运行 `QuickMenu.exe`。默认快捷键是 **Ctrl + Alt + 空格**，可以在设置里改。
3. 收起菜单后，程序在任务栏右下角的托盘里待命；设置里可以开机自动启动。

需要 64 位 Windows 11（Windows 10 应该也能运行，但没有完整测试过）。便携版自带运行时，不需要另外安装 .NET。详细说明见 [使用说明](docs/使用说明.md)。

设置、游戏库和分类保存在 `%LOCALAPPDATA%\QuickMenu`；删除这个文件夹即可恢复默认。

## 从源码构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download) 和 Windows。

```powershell
./scripts/build.ps1                          # 构建并运行全部测试
./scripts/build.ps1 -SkipInteractiveTests    # 跳过需要桌面会话的测试（CI 用）
./scripts/build.ps1 -Package                 # 另外生成便携版 artifacts/QuickMenu-Portable-<版本>-x64.zip
dotnet run --project src/QuickMenu           # 直接运行
```

自检：`QuickMenu.exe --qa --output <文件夹>` 用示例数据检查界面并保存截图，不会启动或结束任何程序，也不写入设置。

## 项目结构

- `src/QuickMenu`：WPF 程序。`Services/` 是桌面、游戏库、窗口、托盘、进程、手柄等服务，`Strings/` 是中英文界面文字，`Assets/Characters/` 是 AI娘立绘。
- `tests/QuickMenu.Tests`：单元与集成测试；`tests/QuickMenu.Fixture` 是集成测试用的受控窗口。
- `scripts`：构建、打包、图标和字体子集脚本。

欢迎提交 Issue 和 Pull Request。新增界面文字时，请同时加到 `Strings/zh-CN.json` 和 `Strings/en.json`（自检会检查两边的键是否一致）。

## 许可与声明

代码以 [MIT 许可](LICENSE) 开源。标题字体是 Noto Serif SC 的子集，采用 [SIL Open Font License](src/QuickMenu/Assets/Fonts/OFL.txt)。

AI娘形象和对应主题是粉丝创作，与 Anthropic、OpenAI、DeepSeek、Google 无关；Claude、ChatGPT、DeepSeek、Gemini 等名称和商标归各自所有者。如果权利人有异议，请提交 Issue，我们会及时移除相关内容。

## 联系

问题和建议请提交 [Issue](../../issues)，也可以发邮件到 742666700@163.com。
