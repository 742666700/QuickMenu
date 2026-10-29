using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuickMenu.Models;
using QuickMenu.Services;
using ThemeMode = QuickMenu.Models.ThemeMode;

namespace QuickMenu;

internal static partial class Diagnostics
{
    internal static async Task ObserveLifecycleAsync(MainWindow window, Func<bool> trayVisible, string[] args)
    {
        var output = GetOutput(args);
        Directory.CreateDirectory(output);
        try
        {
            var initialCount = window.InvocationCount;
            var initiallyHidden = !window.IsVisible;
            var registered = window.HotkeyRegistered;
            await File.WriteAllTextAsync(Path.Combine(output, "lifecycle-ready.json"), JsonSerializer.Serialize(new { ProcessId = Environment.ProcessId, InitiallyHidden = initiallyHidden, Registered = registered }));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (window.InvocationCount <= initialCount && DateTime.UtcNow < deadline) await Task.Delay(20);
            var activated = window.InvocationCount > initialCount;
            await File.WriteAllTextAsync(Path.Combine(output, "lifecycle-report.json"), JsonSerializer.Serialize(new { InitiallyHidden = initiallyHidden, GlobalHotkeyRegistered = registered, TrayVisible = trayVisible(), SecondInstanceActivated = activated, InvocationCount = window.InvocationCount, ProcessId = Environment.ProcessId }, new JsonSerializerOptions { WriteIndented = true }));
            System.Windows.Application.Current.Shutdown(activated ? 0 : 1);
        }
        catch (Exception ex) { App.Log(ex); await File.WriteAllTextAsync(Path.Combine(output, "failure.txt"), ex.ToString()); System.Windows.Application.Current.Shutdown(1); }
    }

    public static async Task RunAsync(string[] args)
    {
        var output = GetOutput(args);
        Directory.CreateDirectory(output);
        // The self-check reads Chinese text; its English pass switches over and back.
        Loc.Use(Loc.Chinese);
        try
        {
            if (args.Contains("--companion-only")) await InspectCompanions(output);
            else if (args.Contains("--measure-performance")) await InspectPerformance(output,
                args.FirstOrDefault(arg => arg.StartsWith("--performance-case=", StringComparison.Ordinal))?.Split('=', 2)[1]);
            else if (args.Contains("--diagnostics")) await InspectSystem(output);
            else await InspectInterface(output, args.Contains("--hold-preview"), args.Contains("--preview-light"));
            System.Windows.Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            await File.WriteAllTextAsync(Path.Combine(output, "failure.txt"), ex.ToString());
            System.Windows.Application.Current.Shutdown(1);
        }
    }

    private static string GetOutput(string[] args)
    {
        var index = Array.IndexOf(args, "--output");
        return Path.GetFullPath(index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(AppContext.BaseDirectory, "qa"));
    }

    private static async Task InspectPerformance(string output, string? only = null)
    {
        var rows = new List<object>();
        var window = new MainWindow(isQa: true);
        window.Left = 100; window.Top = 100;
        window.Show();
        using var process = Process.GetCurrentProcess();
        async Task<double> Sample()
        {
            process.Refresh(); var cpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
            await Task.Delay(TimeSpan.FromSeconds(10));
            process.Refresh(); return (process.TotalProcessorTime - cpu).TotalSeconds / watch.Elapsed.TotalSeconds * 100;
        }
        var cases = new[] { (ThemeMode.Oled, "claude"), (ThemeMode.Cream, "claude"), (ThemeMode.OledDay, "claude"), (ThemeMode.ChatGpt, "chatgpt"), (ThemeMode.DeepSeek, "deepseek"), (ThemeMode.DeepSeek, "deepseek-rice"), (ThemeMode.Gemini, "gemini") }
            .Where(pair => only is null || $"{pair.Item1}/{pair.Item2}" == only).ToArray();
        if (cases.Length == 0) throw new ArgumentException("Unknown performance case.", nameof(only));
        foreach (var (theme, id) in cases)
        {
            window.ApplyTheme(theme); window.PreviewCompanion(id);
            window.InvokeMenu(); window.PreviewHide(); window.InvokeMenu(); window.PreviewPoseIndex(0);
            await window.Companion!.MotionReady;
            window.PreviewPerformanceScene();
            await Task.Delay(TimeSpan.FromSeconds(35));
            window.Companion.PreviewIdleClip();
            window.Companion.PreviewSampleActions();
            var actionsBefore = window.Companion.IdlePlays;
            var alignedBefore = window.Companion.AlignedIdlePlays;
            var scene = new { Host = process.ProcessName, WindowDpi = VisualTreeHelper.GetDpi(window).DpiScaleX,
                CompanionDpi = VisualTreeHelper.GetDpi(window.Companion).DpiScaleX, WindowWidth = window.ActualWidth, WindowHeight = window.ActualHeight,
                CompanionWidth = window.Companion.ActualWidth, CompanionHeight = window.Companion.ActualHeight, WindowVisible = window.IsVisible,
                CompanionVisible = window.Companion.IsVisible, WindowActive = window.IsActive };
            var shown = await Sample();
            var actions = window.Companion.IdlePlays - actionsBefore;
            var aligned = window.Companion.AlignedIdlePlays - alignedBefore;
            await File.WriteAllTextAsync(Path.Combine(output, $"trace-{theme}-{id}.json"), JsonSerializer.Serialize(window.Companion.SampleTrace, new JsonSerializerOptions { WriteIndented = true }));
            if (actions < 1) throw new InvalidOperationException("Performance sampling missed its idle clip.");
            window.PreviewHide(); await Task.Delay(1500); var hidden = await Sample();
            rows.Add(new { Theme = theme.ToString(), Character = id, ShownSingleCorePercent = Math.Round(shown, 3), HiddenSingleCorePercent = Math.Round(hidden, 3), SampleIdleClips = actions, SampleCoalescedClips = aligned,
                Scene = scene });
            await File.WriteAllTextAsync(Path.Combine(output, "performance-report.json"), JsonSerializer.Serialize(new { Samples = rows, WarmupSeconds = 35, SampleSeconds = 10, RenderTier = RenderCapability.Tier >> 16, Notes = "QA-only windows, front pose, fixed scene phase, second invocation, CPU time divided by wall time (one core = 100%). Each shown sample includes an authored idle clip; only poses without authored idle content use a base/blink/vector fixture. QA intervals are 5-8 seconds, aligned within that range to existing sparkle bursts. User settings are never written." }, new JsonSerializerOptions { WriteIndented = true }));
            window.Show();
        }
        window.DisposeServices(); window.Close();
    }

    private static async Task InspectSystem(string output)
    {
        var stopwatch = Stopwatch.StartNew();
        var desktopService = new DesktopService();
        var desktop = await desktopService.GetItemsAsync();
        var windows = await new WindowService().GetAppsAsync(HostNative.GetForegroundWindow());
        var tray = await new TrayService().GetItemsAsync();
        var report = new
        {
            DesktopCount = desktop.Count,
            DesktopWithIconCount = desktop.Count(d => d.Icon != null),
            GameCount = desktop.Count(d => d.Category == DesktopCategory.Games),
            GameSources = desktopService.Games.Counts,
            Desktop = desktop.Select(d => new { d.Name, d.ParsingName, d.Category, d.IsAiTool, d.GameSource, d.Location }),
            WindowProcessCount = windows.Count,
            ForegroundProcessCount = windows.Count(w => w.IsForeground),
            Windows = windows.Select(w => new { w.Name, ProcessId = w.Identity?.ProcessId, Count = w.Windows.Count }),
            TrayCount = tray.Count,
            UnverifiedTrayCount = tray.Count(t => t.Identity == null),
            Tray = tray.Select(t => new { t.Name, t.CanTerminate }),
            DurationMs = stopwatch.Elapsed.TotalMilliseconds,
            SystemThemeDark = ThemeService.IsDark(ThemeMode.System)
        };
        await File.WriteAllTextAsync(Path.Combine(output, "system-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task InspectInterface(string output, bool holdPreview = false, bool previewLight = false)
    {
        var icon = new IconService();
        var desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var samples = new[] { ("此电脑", "shell:MyComputerFolder"), ("回收站", "shell:RecycleBinFolder"), ("浏览器", Path.Combine(system, "shell32.dll")), ("微信", Path.Combine(system, "notepad.exe")), ("代码编辑器", Path.Combine(system, "cmd.exe")), ("记事本", Path.Combine(system, "notepad.exe")), ("工作资料", desktopFolder), ("项目文件", desktopFolder), ("截图", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)), ("文档", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)), ("音乐", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)), ("下载", desktopFolder), ("会议", Path.Combine(system, "shell32.dll")), ("表格", Path.Combine(system, "shell32.dll")), ("演示文稿", Path.Combine(system, "shell32.dll")), ("设计资料", desktopFolder), ("终端", Path.Combine(system, "cmd.exe")), ("计算器", Path.Combine(system, "calc.exe")) };
        var items = samples.Select((s, i) => new DesktopItem($"qa:{i}", s.Item1, icon.GetForParsingName(s.Item2), s.Item2, i switch { 8 => DesktopCategory.Media, 9 => DesktopCategory.Documents, 13 => DesktopCategory.Spreadsheets, 14 => DesktopCategory.Presentations, 2 or 3 or 4 or 5 or 12 or 16 or 17 => DesktopCategory.Programs, _ => DesktopCategory.Other }, IsAiTool: i == 4)).ToArray();
        var sampleApp = new RunningApp("qa:wechat", "微信", items[3].Icon, new ProcessIdentity(4820, DateTime.UtcNow), new[] { new WindowEntry(1, "微信") }, true);
        var sampleApps = new[]
        {
            sampleApp,
            new RunningApp("qa:browser", "浏览器", items[2].Icon, new ProcessIdentity(4818, DateTime.UtcNow), new[] { new WindowEntry(2, "文档"), new WindowEntry(3, "搜索") }, false),
            new RunningApp("qa:notes", "记事本", items[5].Icon, new ProcessIdentity(4821, DateTime.UtcNow), new[] { new WindowEntry(4, "笔记.txt") }, false),
            new RunningApp("qa:cloud", "云盘", items[7].Icon, null, Array.Empty<WindowEntry>(), false, true),
            new RunningApp("qa:tray", "托盘项目", null, null, Array.Empty<WindowEntry>(), false, true)
        };
        var checks = new Dictionary<string, bool>();
        object? oledBlack = null;
        var window = new MainWindow(isQa: true);
        window.SetPreviewData(items, sampleApps);
        window.Show();
        await YieldRender();
        window.ShowDesktopItemMenu(items[5], window.DesktopList);
        var itemMenu = window.DesktopMenu!;
        ((System.Windows.Controls.MenuItem)itemMenu.Items[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        itemMenu.IsOpen = false;
        checks["context pin moves item first and displays badge"] = window.DesktopList.Items[0] is DesktopItem { Id: "qa:5", IsPinned: true };
        checks["pin appears in tool shelf"] = window.PinnedItems.Items.Cast<DesktopItem>().Single().Id == "qa:5";
        window.PreviewSearch("不存在");
        checks["tool shelf follows search"] = window.PinnedItems.Items.Count == 0;
        window.PreviewSearch("");
        Capture(window, Path.Combine(output, "desktop-pinned.png"), 1);
        var pinLeft = window.PinnedMark.TranslatePoint(new Point(), window).X;
        var sparkLeft = window.GreetingSpark.TranslatePoint(new Point(), window).X;
        var placeholderLeft = window.SearchPlaceholder.TranslatePoint(new Point(), window.SearchBox).X;
        checks["search caret starts clear of the placeholder"] = placeholderLeft - window.SearchBox.Padding.Left >= 5;
        checks["pinned row uses the pin mark on the title column"] = window.PinnedMark.Text == "\uE718" && Math.Abs(pinLeft - sparkLeft) <= 1.5;
        window.ShowDesktopItemMenu(items[5], window.DesktopList);
        itemMenu = window.DesktopMenu!;
        var categoryMenu = (System.Windows.Controls.MenuItem)itemMenu.Items[1];
        ((System.Windows.Controls.MenuItem)categoryMenu.Items[5]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        itemMenu.IsOpen = false;
        window.SelectCategory(DesktopCategory.Media);
        checks["manual category joins filtered page"] = window.DesktopList.Items.Count == 2;
        window.ShowDesktopItemMenu(items[5], window.DesktopList);
        itemMenu = window.DesktopMenu!;
        categoryMenu = (System.Windows.Controls.MenuItem)itemMenu.Items[1];
        ((System.Windows.Controls.MenuItem)categoryMenu.Items[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        itemMenu.IsOpen = false;
        checks["automatic category restores filter"] = window.DesktopList.Items.Count == 1;
        window.SelectCategory(DesktopCategory.All);
        window.ShowDesktopItemMenu(items[5], window.DesktopList);
        categoryMenu = (System.Windows.Controls.MenuItem)window.DesktopMenu!.Items[1];
        categoryMenu.Items.Cast<System.Windows.Controls.MenuItem>().Single(entry => Equals(entry.Header, "游戏")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        window.DesktopMenu.IsOpen = false;
        checks["games gather in their own group"] = window.DesktopList.Items.Groups?.Cast<System.Windows.Data.CollectionViewGroup>().Any(group => Equals(group.Name, "游戏") && group.ItemCount == 1) == true;
        window.SelectCategory(DesktopCategory.Games);
        checks["manual game category filters desktop"] = window.DesktopList.Items.Count == 1 && ((DesktopItem)window.DesktopList.Items[0]).Id == "qa:5";
        Capture(window, Path.Combine(output, "category-games.png"), 1);
        window.ShowDesktopItemMenu(items[5], window.DesktopList);
        categoryMenu = (System.Windows.Controls.MenuItem)window.DesktopMenu!.Items[1];
        ((System.Windows.Controls.MenuItem)categoryMenu.Items[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        window.DesktopMenu.IsOpen = false;
        checks["games filter empties after restoring automatic category"] = window.DesktopList.Items.Count == 0;
        window.ShowDesktopItemMenu(items[5], window.DesktopList);
        var locate = window.DesktopMenu!.Items.OfType<System.Windows.Controls.MenuItem>().FirstOrDefault(entry => Equals(entry.Header, "打开文件所在位置"));
        checks["item menu offers file location"] = locate is { IsCheckable: false };
        window.DesktopMenu.IsOpen = false;
        window.ShowDesktopItemMenu(items[2], window.DesktopList);
        var end = window.DesktopMenu!.Items.OfType<System.Windows.Controls.MenuItem>().LastOrDefault();
        checks["end process is greyed out when the item is not running"] = end is { Header: "结束进程", IsEnabled: false };
        end?.ApplyTemplate();
        checks["disabled menu entries are visibly dimmed"] = end?.Template.FindName("MenuChrome", end) is FrameworkElement { Opacity: < 0.5 };
        window.DesktopMenu.IsOpen = false;
        window.SelectCategory(DesktopCategory.All);
        window.ShowDesktopItemMenu(items[5], window.DesktopList);
        itemMenu = window.DesktopMenu!;
        ((System.Windows.Controls.MenuItem)itemMenu.Items[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        itemMenu.IsOpen = false;
        window.SortButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        itemMenu = window.DesktopMenu!;
        ((System.Windows.Controls.MenuItem)itemMenu.Items[1]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        itemMenu.IsOpen = false;
        checks["sort button selects modified time"] = window.SortButton.Content.ToString() == "修改时间⌄";
        window.SortButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        itemMenu = window.DesktopMenu!;
        ((System.Windows.Controls.MenuItem)itemMenu.Items[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        itemMenu.IsOpen = false;
        window.ApplyTheme(ThemeMode.Oled);
        checks["OLED uses opaque black surfaces"] = window.IsDarkAppearance && new[] { "SurfaceBrush", "SidebarBrush", "InputBrush", "PopupBrush", "CardBrush", "IconPlateBrush" }.All(key => ((SolidColorBrush)System.Windows.Application.Current.Resources[key]).Color == Colors.Black);
        Capture(window, Path.Combine(output, "desktop-oled.png"), 1);
        // the stars, three glints, two shooting stars
        checks["OLED night scene is the night sky"] = window.AmbientLayer.Children.Count == 7 && window.Ambient.Mode == ThemeMode.Oled;
        var sceneBlack = BlackShare(window);
        window.AmbientLayer.Visibility = Visibility.Hidden;
        var contentBlack = BlackShare(window);
        window.AmbientLayer.Visibility = Visibility.Visible;
        oledBlack = new { WithScene = Math.Round(sceneBlack, 3), ContentOnly = Math.Round(contentBlack, 3) };
        checks["OLED night scene lights at most 12% more pixels than the content alone"] = contentBlack - sceneBlack <= 0.12;
        window.PreviewCompanion("claude");
        var companion = window.Companion!;
        checks["Claude娘 stands across the menu's right edge"] = window.CompanionShown && window.ActualWidth <= 821
            && companion.Footprint.Width > 200 && companion.Footprint.Height > window.ActualHeight && !companion.ShowActivated && !companion.ShowInTaskbar;
        checks["her avatar takes the greeting spark's place"] = window.GreetingAvatar.IsVisible && !window.GreetingSpark.IsVisible;
        // Every pose in turn, each in a different theme: the pose changes on every invocation, never repeating.
        var poses = new List<CharacterPose>();
        foreach (var (theme, name) in new[] { (ThemeMode.Oled, "oled"), (ThemeMode.OledDay, "oledday"), (ThemeMode.Cream, "cream"), (ThemeMode.Oled, "oled-again") })
        {
            window.ApplyTheme(theme);
            poses.Add(window.CurrentPose!);
            await companion.MotionReady;
            checks[$"pose shown matches the pose picked ({name})"] = companion.Pose == window.CurrentPose!.Body && companion.CanBlink == (window.CurrentPose.HasBlink);
            CaptureWithCompanion(window, companion, Path.Combine(output, $"desktop-{name}-companion.png"));
            window.ChangePose();
        }
        checks["each invocation shows a different pose than the last"] = poses.Zip(poses.Skip(1)).All(pair => pair.First != pair.Second);
        checks["every Claude娘 pose is used and the front one can blink"] = poses.Distinct().Count() == Characters.All[0].Poses.Count && Characters.All[0].Poses.Count >= 2 && Characters.All[0].Poses.Any(pose => pose.HasBlink);
        window.PreviewHide();
        checks["Claude娘 leaves with the menu"] = !window.CompanionShown && !companion.IsAnimating;
        window.Show();
        window.PreviewCompanion(null);
        checks["choosing no character puts her and her avatar away"] = !window.CompanionShown && !window.GreetingAvatar.IsVisible && window.GreetingSpark.IsVisible;
        window.ApplyTheme(ThemeMode.Oled);
        window.Ambient.Start();
        checks["scene runs while shown"] = window.Ambient.IsRunning;
        window.PreviewHide();
        checks["scene pauses when the menu hides"] = !window.Ambient.IsRunning;
        window.Show();
        window.ApplyTheme(ThemeMode.Cream);
        window.UpdateLayout();
        // two glows, ten daisies
        checks["cream daisy theme has falling daisies and no character of its own"] = window.AmbientLayer.Children.Count == 13 && !window.CompanionShown && window.ActualWidth <= 821;
        Capture(window, Path.Combine(output, "desktop-cream.png"), 1);
        window.ApplyTheme(ThemeMode.System);
        checks["following the system shows OLED day or night"] = window.Ambient.Mode == (ThemeService.IsDark(ThemeMode.System) ? ThemeMode.Oled : ThemeMode.OledDay);
        await InspectCharacters(window, checks, output);
        window.ApplyTheme(ThemeMode.Oled);
        window.PreviewPage(MenuPage.Settings);
        checks["settings offer seven themes and a separate character choice"] = window.ThemePicker.Items.Count == MainWindow.ThemeChoices.Length && MainWindow.ThemeChoices.Length == 7
            && window.CharacterPicker.Items.Count == Characters.All.Count + 1;
        checks["empty pinned row is hidden"] = !window.PinnedShelf.IsVisible;
        checks["page titles use the embedded serif"] = new Typeface(window.DesktopNav.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal).TryGetGlyphTypeface(out var heading) && heading.FamilyNames.Values.Contains("QuickMenu Serif SC") && heading.CharacterToGlyphMap.ContainsKey('桌');
        Capture(window, Path.Combine(output, "settings-oled.png"), 1);
        // The game library section lists every store with a switch, and the folders and hidden games with a button each.
        checks["settings list every game store, the library folders and hidden games"] = window.GameSourcesPanel.Children.Count == GameLibraryService.Sources.Count
            && window.GameLibraryRows.Children.Count == 2;
        window.ScrollSettingsTo("SectionGames");
        Capture(window, Path.Combine(output, "settings-games.png"), 1);
        window.SettingsPanel.ScrollToTop();

        // A custom category with a rule gets its own tab and column; a built-in tab can be hidden; settings list the
        // six built-in rules and the custom category, and the editor opens over the page.
        window.PreviewPage(MenuPage.Desktop);
        window.PreviewCategories(new CategorySettings { ShowAiTab = false, Custom = [new CustomCategory { Name = "沟通", Keywords = ["微信", "会议"] }] });
        var customTab = window.CategoryBar.Children.OfType<System.Windows.Controls.Button>().FirstOrDefault(button => (button.Tag as string)?.StartsWith("custom:", StringComparison.Ordinal) == true);
        customTab?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var inCustom = window.DesktopList.Items.Cast<DesktopItem>().Select(item => item.Name).ToArray();
        checks["custom category gets a tab and collects the items its rule matches"] = customTab is not null && inCustom.Length == 2 && inCustom.All(name => name is "微信" or "会议")
            && window.AiCategory.Visibility == Visibility.Collapsed;
        Capture(window, Path.Combine(output, "desktop-custom-category.png"), 1);
        window.SelectCategory(DesktopCategory.All);
        checks["custom category gets its own column"] = window.DesktopList.Items.Groups?.OfType<System.Windows.Data.CollectionViewGroup>().Any(group => (string)group.Name == "沟通" && group.ItemCount == 2) == true;
        window.PreviewPage(MenuPage.Settings);
        checks["settings list the built-in rules and custom categories"] = window.CategoryRows.Children.Count == 7;
        window.ScrollSettingsTo("SectionCategories");
        checks["settings section list jumps to a section"] = window.SettingsPanel.VerticalOffset > 0;
        Capture(window, Path.Combine(output, "settings-categories.png"), 1);
        window.PreviewCategoryEditor(true);
        checks["category editor opens over the page"] = window.HasOverlay && window.CategoryEditor.Visibility == Visibility.Visible && window.DialogBorder.Visibility == Visibility.Collapsed;
        Capture(window, Path.Combine(output, "category-editor.png"), 1);
        window.PreviewCategoryEditor(false);
        window.SettingsPanel.ScrollToTop();
        window.PreviewCategories(new CategorySettings());

        // The greeting clock: no label and no second zone shows just the local time; the preview follows the fields.
        window.ScrollSettingsTo("SectionGeneral");
        window.ClockLabelBox.Text = ""; window.SecondClockPicker.SelectedIndex = 0;
        checks["greeting clock can show just the local time"] = window.ClockPreview.Text == "预览：下午好，现在是 15:00";
        window.ClockLabelBox.Text = "美国时间";
        window.SecondClockPicker.SelectedIndex = 1 + TimeZoneInfo.GetSystemTimeZones().ToList().FindIndex(zone => zone.Id == "China Standard Time");
        window.SecondClockLabelBox.Text = "北京时间";
        checks["greeting clock can name the local clock and add a time zone"] = window.ClockPreview.Text.StartsWith("预览：下午好，现在是美国时间 15:00，北京时间 ", StringComparison.Ordinal);
        Capture(window, Path.Combine(output, "settings-general.png"), 1);
        window.ScrollSettingsTo("SectionGeneral");
        checks["general settings open with the free-software notice"] = window.FreeNotice.TranslatePoint(new Point(), window.SettingsContent).Y < window.LanguageLabel.TranslatePoint(new Point(), window.SettingsContent).Y && window.FreeNotice.ActualHeight > 0;
        window.ScrollSettingsTo("SectionAbout");
        checks["about shows the version from the build"] = window.VersionText.Text == "快捷菜单 " + MainWindow.AppVersion && MainWindow.AppVersion.Length > 0;
        checks["about links the project page and the developer's email"] = window.ProjectLinkText.Text == MainWindow.ProjectUrl && (string)window.ProjectLink.Tag == MainWindow.ProjectUrl && (string)window.ContactLink.Tag == "mailto:" + MainWindow.ContactEmail && window.ContactRow.ActualHeight > 0;
        Capture(window, Path.Combine(output, "settings-about.png"), 1);
        window.PreviewPage(MenuPage.Desktop);
        window.ShowWelcome();
        checks["first-run welcome names the hotkey and offers settings"] = window.HasOverlay && window.DialogDescription.Text.Contains(window.CurrentHotkey) && new AppSettings().HotkeyModifiers == 1 && new AppSettings().HotkeyKey == 32 && (string)window.DialogConfirm.Content == "打开设置";
        Capture(window, Path.Combine(output, "welcome.png"), 1);
        window.PreviewCategoryEditor(false);
        checks["closing the welcome restores the dialog's buttons"] = !window.HasOverlay && (string)window.DialogCancel.Content == "取消";
        window.PreviewPage(MenuPage.RunningApps);
        Capture(window, Path.Combine(output, "apps-oled.png"), 1);
        window.PreviewTermination(sampleApp);
        Capture(window, Path.Combine(output, "terminate-oled.png"), 1);
        RaiseKey(window, Key.Escape);
        window.PreviewPage(MenuPage.Desktop);
        checks["every theme paints an opaque surface"] = MainWindow.ThemeChoices.All(theme => { window.ApplyTheme(theme); return ((SolidColorBrush)System.Windows.Application.Current.Resources["SurfaceBrush"]).Color.A == 255; });
        window.ApplyTheme(ThemeMode.OledDay);
        Capture(window, Path.Combine(output, "desktop-oledday.png"), 1);
        window.ApplyTheme(ThemeMode.Cream);
        var placeholderStart = window.SearchPlaceholder.TranslatePoint(new Point(0, 0), window).X;
        window.PreviewSearch("测"); window.SearchBox.Focus(); window.SearchBox.CaretIndex = 0; window.UpdateLayout();
        var caretStart = window.SearchBox.TranslatePoint(new Point(window.SearchBox.GetRectFromCharacterIndex(0).X, 0), window).X;
        checks["search caret aligns with placeholder start"] = Math.Abs(caretStart - placeholderStart) <= 3;
        window.PreviewSearch("");
        // Layout rounding at fractional DPI can add a fraction of one logical pixel.
        checks["compact search geometry"] = window.SearchBox.ActualHeight <= 35 && window.SearchBox.ActualWidth <= 191;
        window.SelectCategory(DesktopCategory.Office);
        checks["office groups three file categories"] = window.DesktopList.Items.Count == 3;
        window.OfficeCategory.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        checks["office hover opens secondary categories"] = window.OfficePopup.IsOpen && window.OfficeSubcategories.Children.Count == 3;
        ((System.Windows.Controls.Button)window.OfficeSubcategories.Children[1]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        checks["secondary category click filters and closes popup"] = window.DesktopList.Items.Count == 1 && !window.OfficePopup.IsOpen;
        window.PreviewPage(MenuPage.RunningApps);
        checks["page switch closes secondary categories"] = !window.OfficePopup.IsOpen;
        window.PreviewPage(MenuPage.Desktop);
        window.SelectCategory(DesktopCategory.AiTools);
        checks["AI filter selects desktop launchers"] = window.DesktopList.Items.Count == 1;
        Capture(window, Path.Combine(output, "category-ai-night.png"), 1);
        window.SelectCategory(DesktopCategory.Documents);
        checks["documents category filters desktop"] = window.DesktopList.Items.Count == 1 && ((DesktopItem)window.DesktopList.Items[0]).Category == DesktopCategory.Documents;
        window.PreviewPage(MenuPage.RunningApps); window.PreviewPage(MenuPage.Desktop);
        checks["category survives page switching"] = window.DesktopList.Items.Count == 1;
        window.PreviewSearch("不存在"); checks["category combines with search"] = window.DesktopList.Items.Count == 0;
        window.PreviewSearch(""); window.SelectCategory(DesktopCategory.Media);
        Capture(window, Path.Combine(output, "category-media-night.png"), 1);
        window.InvokeMenu();
        checks["reinvoke resets category"] = window.DesktopList.Items.Count == 18;
        window.UpdateLayout();
        var firstRow = (System.Windows.Controls.ListBoxItem)window.DesktopList.ItemContainerGenerator.ContainerFromIndex(0);
        checks["column heading remains visible after invocation"] = firstRow.TranslatePoint(new Point(), window.DesktopList).Y >= 30;
        var programRows = window.DesktopList.Items.Cast<DesktopItem>().Where(item => !DesktopCategories.Matches(DesktopCategory.Office, item.Category) && item.Category != DesktopCategory.Media)
            .Select(item => (System.Windows.Controls.ListBoxItem)window.DesktopList.ItemContainerGenerator.ContainerFromItem(item)).Select(box => box.TranslatePoint(new Point(), window.DesktopList)).ToArray();
        var programColumns = programRows.Select(point => Math.Round(point.X)).Distinct().Count();
        checks["long group splits into columns of at most seven rows"] = programColumns >= 2 && programRows.GroupBy(point => Math.Round(point.X)).All(column => column.Count() <= 7);
        var officeColumns = window.DesktopList.Items.Cast<DesktopItem>().Where(item => DesktopCategories.Matches(DesktopCategory.Office, item.Category))
            .Select(item => Math.Round(((System.Windows.Controls.ListBoxItem)window.DesktopList.ItemContainerGenerator.ContainerFromItem(item)).TranslatePoint(new Point(), window.DesktopList).X)).Distinct().Count();
        checks["group of four or fewer stays one column"] = officeColumns == 1;
        firstRow.Focus();
        RaiseKey(window, Key.Right);
        var sideRow = (System.Windows.Controls.ListBoxItem)window.DesktopList.ItemContainerGenerator.ContainerFromItem(window.DesktopList.SelectedItem);
        checks["right arrow moves to the next column on the same row"] = sideRow != firstRow && Math.Abs(sideRow.TranslatePoint(new Point(), window.DesktopList).Y - firstRow.TranslatePoint(new Point(), window.DesktopList).Y) < 1;
        for (var step = 0; step < programColumns - 1; step++) RaiseKey(window, Key.Right);
        checks["right arrow reaches the document column"] = window.DesktopList.SelectedItem is DesktopItem adjacent && DesktopCategories.Matches(DesktopCategory.Office, adjacent.Category);
        for (var step = 0; step < programColumns; step++) RaiseKey(window, Key.Left);
        checks["left arrow returns to program column"] = window.DesktopList.SelectedIndex == 0;
        window.PreviewSearch("资料");
        checks["search filters desktop"] = window.DesktopList.Items.Count == 2;
        RaiseKey(window, Key.Tab);
        checks["Tab changes page"] = window.CurrentPage == MenuPage.RunningApps;
        window.PreviewSearch("记事本");
        checks["search filters running apps"] = window.AppsList.Items.Count == 1;
        RaiseKey(window, Key.Tab);
        checks["Tab restores page search"] = window.CurrentPage == MenuPage.Desktop && window.CurrentSearch == "资料";
        window.InvokeMenu();
        checks["reinvoke resets page and search"] = window.CurrentPage == MenuPage.Desktop && window.CurrentSearch == "";
        window.PreviewPage(MenuPage.RunningApps);
        window.ApplyTheme(ThemeMode.Cream);
        Capture(window, Path.Combine(output, "apps-cream.png"), 1);
        window.ApplyTheme(ThemeMode.Oled);
        Capture(window, Path.Combine(output, "apps-night.png"), 1);
        window.PreviewTermination(sampleApp);
        checks["confirmation defaults to cancel"] = window.HasOverlay && window.DialogCancel.IsKeyboardFocused;
        Capture(window, Path.Combine(output, "terminate-night.png"), 1);
        RaiseKey(window, Key.Escape);
        checks["Escape cancels without hiding"] = !window.HasOverlay && window.IsVisible;
        checks["unknown tray cannot terminate"] = sampleApps.Where(a => a.IsTray).All(a => !a.CanTerminate);
        var moreApps = new[] { ("终端", 16, "命令提示符"), ("计算器", 17, "计算器"), ("会议", 12, "周会"), ("设计资料", 15, "资源管理器") }
            .Select((s, i) => new RunningApp($"qa:extra{i}", s.Item1, items[s.Item2].Icon, new ProcessIdentity(4830 + i, DateTime.UtcNow), new[] { new WindowEntry(10 + i, s.Item3) }, false)).ToArray();
        window.SetPreviewData(Array.Empty<DesktopItem>(), moreApps);
        window.ApplyTheme(ThemeMode.Cream); window.UpdateLayout();
        var appColumns = window.AppsList.Items.Cast<RunningApp>().Where(app => !app.IsTray)
            .Select(app => Math.Round(((System.Windows.Controls.ListBoxItem)window.AppsList.ItemContainerGenerator.ContainerFromItem(app)).TranslatePoint(new Point(), window.AppsList).X)).Distinct().Count();
        checks["running apps split into two columns after four"] = appColumns == 2;
        Capture(window, Path.Combine(output, "apps-columns-cream.png"), 1);
        window.ApplyTheme(ThemeMode.Oled);
        foreach (var scale in new[] { 1.0, 1.5, 2.0 })
        {
            window.PreviewPage(MenuPage.Desktop);
            Capture(window, Path.Combine(output, $"desktop-night-{(int)(scale * 100)}.png"), scale);
        }
        window.PreviewPage(MenuPage.Settings);
        Capture(window, Path.Combine(output, "settings-night.png"), 1);
        window.Width = 520; window.Height = 380;
        window.PreviewPage(MenuPage.Desktop);
        Capture(window, Path.Combine(output, "desktop-small.png"), 1);
        checks["small window has scrollable items"] = window.DesktopList.Items.Count == 18 && window.ActualWidth <= 520;
        window.DesktopList.SelectedIndex = 0;
        RaiseKey(window, Key.Down);
        checks["small column arrows follow the next visual row"] = window.DesktopList.SelectedIndex == 1;
        var liveWindow = new MainWindow();
        liveWindow.InvokeMenu();
        liveWindow.InvokeMenu();
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while ((liveWindow.DesktopCount == 0 || liveWindow.RunningCount == 0) && DateTime.UtcNow < deadline) await Task.Delay(30);
        checks["rapid reinvocation eventually loads desktop"] = liveWindow.DesktopCount > 0;
        checks["native running apps reach UI"] = liveWindow.RunningCount > 0;
        await YieldRender();
        var nativeTimings = new List<double>();
        for (var i = 0; i < 8; i++)
        {
            liveWindow.Hide();
            var stopwatch = Stopwatch.StartNew();
            liveWindow.InvokeMenu();
            await YieldRender();
            nativeTimings.Add(stopwatch.Elapsed.TotalMilliseconds);
        }
        liveWindow.DisposeServices(); liveWindow.Close();
        window.Width = 820; window.Height = 600;
        // A populated shelf for visual comparison; all mutations stay in the QA-only store.
        foreach (var index in new[] { 2, 3, 4, 16 })
        {
            window.ShowDesktopItemMenu(items[index], window.DesktopList);
            var pinMenu = window.DesktopMenu!;
            ((System.Windows.Controls.MenuItem)pinMenu.Items[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            pinMenu.IsOpen = false;
        }
        window.InvokeMenu(); window.ApplyTheme(ThemeMode.Cream);
        await YieldRender();
        Capture(window, Path.Combine(output, "tool-shelf-cream.png"), 1);
        checks["pinned row is one compact line"] = window.PinnedShelf.IsVisible && window.PinnedShelf.ActualHeight <= 44 && window.PinnedItems.Items.Count == 4;
        // Inside each pinned pill, border-to-icon on the left equals label-to-border on the right.
        var pillGaps = Enumerable.Range(0, window.PinnedItems.Items.Count).Select(i =>
        {
            var pill = (System.Windows.Controls.Button)((System.Windows.Controls.ContentPresenter)window.PinnedItems.ItemContainerGenerator.ContainerFromIndex(i)).ContentTemplate.FindName("PinnedPill", (FrameworkElement)window.PinnedItems.ItemContainerGenerator.ContainerFromIndex(i));
            var row = (System.Windows.Controls.StackPanel)pill.Content;
            var icon = (FrameworkElement)row.Children[0];
            var label = (System.Windows.Controls.TextBlock)row.Children[1];
            var left = icon.TranslatePoint(new Point(), pill).X;
            var right = pill.ActualWidth - label.TranslatePoint(new Point(label.ActualWidth, 0), pill).X;
            return (left, right);
        }).ToArray();
        checks["pinned pills have equal space left and right"] = pillGaps.All(g => Math.Abs(g.left - g.right) <= 1);
        window.ApplyTheme(ThemeMode.Oled);
        Capture(window, Path.Combine(output, "tool-shelf-night.png"), 1);
        window.ApplyTheme(ThemeMode.OledDay);
        checks["OLED day uses an opaque gradient backdrop"] = !window.IsDarkAppearance && System.Windows.Application.Current.Resources["BackdropBrush"] is DrawingBrush;
        Capture(window, Path.Combine(output, "tool-shelf-oled-day.png"), 1);
        window.PreviewPage(MenuPage.RunningApps);
        Capture(window, Path.Combine(output, "apps-oled-day.png"), 1);
        window.PreviewPage(MenuPage.Settings);
        Capture(window, Path.Combine(output, "settings-oled-day.png"), 1);
        window.PreviewPage(MenuPage.RunningApps);
        window.PreviewTermination(sampleApp);
        Capture(window, Path.Combine(output, "terminate-oled-day.png"), 1);
        RaiseKey(window, Key.Escape);
        window.PreviewPage(MenuPage.Desktop);
        window.ApplyTheme(ThemeMode.Oled);
        Capture(window, Path.Combine(output, "tool-shelf-oled-night.png"), 1);

        // Controller: the D-pad moves the selection, B opens the item's right-click menu and the D-pad moves through it,
        // B closes it again, Y opens settings where the D-pad lands on the first control, and B goes back.
        window.PreviewPage(MenuPage.Desktop);
        window.DesktopList.SelectedIndex = 0;
        window.OnGamepad(GamepadButton.Down);
        var padMoved = window.DesktopList.SelectedIndex > 0;
        window.OnGamepad(GamepadButton.B);
        await YieldRender();
        var padMenu = window.DesktopMenu?.IsOpen == true;
        window.OnGamepad(GamepadButton.Down);
        await YieldRender();
        var padHighlight = window.DesktopMenu?.Items.OfType<System.Windows.Controls.MenuItem>().Count(item => item.IsHighlighted) == 1;
        window.OnGamepad(GamepadButton.B);
        await YieldRender();
        var padClosed = window.DesktopMenu?.IsOpen != true;
        window.OnGamepad(GamepadButton.Y);
        var padSettings = window.CurrentPage == MenuPage.Settings;
        window.OnGamepad(GamepadButton.Down);
        var padFocus = window.LanguagePicker.IsKeyboardFocusWithin;
        window.OnGamepad(GamepadButton.B);
        checks["controller moves, opens and closes the item menu, and reaches settings"] = padMoved && padMenu && padHighlight && padClosed && padSettings && padFocus && window.CurrentPage == MenuPage.Desktop;
        File.WriteAllText(Path.Combine(output, "gamepad.txt"), $"{padMoved} {padMenu} {padHighlight} {padClosed} {padSettings} {padFocus} {window.CurrentPage}");
        // LT / RT step through the category tabs and wrap at the ends; in settings they jump between sections.
        bool Tab(System.Windows.Controls.Button tab) => (bool)tab.GetValue(System.Windows.Controls.Primitives.Selector.IsSelectedProperty);
        window.SelectCategory(DesktopCategory.All);
        window.OnGamepad(GamepadButton.RightTrigger);
        var padNext = Tab(window.GamesCategory);
        window.OnGamepad(GamepadButton.LeftTrigger);
        var padBack = window.CategoryBar.Children.OfType<System.Windows.Controls.Button>().First(tab => (string?)tab.Tag == "All") is { } all && Tab(all);
        window.OnGamepad(GamepadButton.LeftTrigger);
        var padWrap = Tab(window.OfficeCategory);
        window.SelectCategory(DesktopCategory.All);
        window.OnGamepad(GamepadButton.Y);
        window.ScrollSettingsTo("SectionGeneral");
        window.OnGamepad(GamepadButton.RightTrigger);
        var padSection = window.SettingsPanel.VerticalOffset > 0;
        window.OnGamepad(GamepadButton.B);
        checks["controller triggers step through category tabs and settings sections"] = padNext && padBack && padWrap && padSection;

        // English: both tables have the same keys, and the interface's own text has no Chinese left. Item names, the
        // greeting's clock names and Windows' own time zone names are data, not interface text, so they are skipped.
        checks["Chinese and English tables have the same keys"] = Loc.Keys(Loc.Chinese).OrderBy(key => key).SequenceEqual(Loc.Keys(Loc.English).OrderBy(key => key));
        Loc.Use(Loc.English);
        var data = new HashSet<string> { "LanguageLabel", "DesktopList", "AppsList", "PinnedItems", "GreetingText", "ClockPreview", "ClockLabelBox", "SecondClockLabelBox", "SecondClockPicker", "LanguagePicker" };
        var leftovers = new List<string>();
        void Collect(string page)
        {
            window.UpdateLayout();
            leftovers.AddRange(VisibleText(window, data).Where(text => text.Any(c => c is >= '一' and <= '鿿')).Select(text => page + ": " + text));
        }
        window.PreviewPage(MenuPage.Desktop); Collect("desktop");
        Capture(window, Path.Combine(output, "desktop-english.png"), 1);
        window.PreviewPage(MenuPage.RunningApps); Collect("apps");
        window.PreviewPage(MenuPage.Settings); Collect("settings");
        Capture(window, Path.Combine(output, "settings-english.png"), 1);
        window.ScrollSettingsTo("SectionCategories");
        Capture(window, Path.Combine(output, "settings-categories-english.png"), 1);
        window.PreviewCategoryEditor(true); Collect("category editor");
        window.PreviewCategoryEditor(false);
        window.PreviewPage(MenuPage.Desktop);
        window.ShowWelcome(); Collect("welcome");
        Capture(window, Path.Combine(output, "welcome-english.png"), 1);
        window.PreviewCategoryEditor(false);
        await File.WriteAllLinesAsync(Path.Combine(output, "english-leftovers.txt"), leftovers);
        checks["English interface shows no Chinese interface text"] = leftovers.Count == 0;
        Loc.Use(Loc.Chinese);
        window.PreviewPage(MenuPage.Desktop);
        checks["switching back to Chinese restores the interface"] = window.SearchPlaceholder.Text == "搜索桌面" && (string)window.DesktopNav.Content == "桌面";
        window.ApplyTheme(ThemeMode.Oled);
        await InspectMotion(window, checks, output, items);
        await InspectCompanionEngine(window, checks, output);
        var timings = new List<double>();
        for (var i = 0; i < 8; i++)
        {
            window.Hide();
            var stopwatch = Stopwatch.StartNew();
            window.InvokeMenu();
            await YieldRender();
            timings.Add(stopwatch.Elapsed.TotalMilliseconds);
        }
        await File.WriteAllTextAsync(Path.Combine(output, "ui-report.json"), JsonSerializer.Serialize(new { Checks = checks, SearchGeometry = new { PlaceholderStart = placeholderStart, CaretStart = caretStart }, IconPixelWidths = items.Select(item => (item.Icon as BitmapSource)?.PixelWidth).Distinct(), WarmFrameMilliseconds = timings, NativeWarmFrameMilliseconds = nativeTimings, AllChecksPassed = checks.Values.All(c => c), OledBlackShare = oledBlack, Notes = "Images at 150/200 percent are WPF rasterizations; physical monitor DPI transitions require manual multi-display testing. Screenshots use sample data; native timings use cached real desktop items. No external applications were opened or terminated." }, new JsonSerializerOptions { WriteIndented = true }));
        if (holdPreview)
        {
            window.Title = "快捷菜单 · 验证预览";
            window.ShowInTaskbar = true;
            // Changing taskbar visibility may recreate the native window; reapply its material.
            window.ApplyTheme(previewLight ? ThemeMode.OledDay : window.IsDarkAppearance ? ThemeMode.Oled : ThemeMode.OledDay);
            window.Width = 820; window.Height = 600;
            window.Left = 150; window.Top = 150;
            window.Show(); window.Activate();
            await Task.Delay(TimeSpan.FromSeconds(180));
            await File.WriteAllTextAsync(Path.Combine(output, "drag-position.json"), JsonSerializer.Serialize(new { window.Left, window.Top }));
        }
        window.DisposeServices(); window.Close();
        if (checks.Values.Any(c => !c)) throw new InvalidOperationException("界面检查存在失败，见 ui-report.json。");
    }

    private static async Task InspectCharacters(MainWindow window, Dictionary<string, bool> checks, string output)
    {
        var sheet = new List<List<string>>();
        foreach (var character in Characters.All)
        {
            var folder = character.Folder;
            checks[$"{character.Id}: two poses, blink and avatar"] = character.Poses.Count >= 2 && character.Poses.Any(pose => pose.HasBlink) && character.Avatar is not null;
            foreach (var pose in character.Poses)
            {
                var body = ReadArt(folder, pose.File);
                var pixels = new byte[body.PixelWidth * body.PixelHeight * 4];
                new FormatConvertedBitmap(body, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, body.PixelWidth * 4, 0);
                var corners = new[] { 3, body.PixelWidth * 4 - 1, (body.PixelHeight - 1) * body.PixelWidth * 4 + 3, pixels.Length - 1 };
                var ratio = body.PixelWidth / (double)body.PixelHeight;
                // Standing figures are portrait; a sitting, animated pose may be nearly square.
                checks[$"{character.Id}/{pose.Id}: 1200px, transparent corners and portrait ratio"] = body.PixelHeight == 1200 && ratio >= 0.35 && ratio <= (pose.HasFrames || pose.MotionOnly ? 1.0 : 0.8) && corners.All(i => pixels[i] == 0);
                var prepared = await pose.Prepare(CancellationToken.None);
                if (prepared.Frames is { Count: > 0 } frames)
                    checks[$"{character.Id}/{pose.File}: animation frames share one canvas"] = frames.Count >= 2 && frames.All(frame => frame.Picture.PixelWidth == pose.Body.PixelWidth && frame.Picture.PixelHeight == pose.Body.PixelHeight && frame.Hold > TimeSpan.Zero);
            }
            var front = ReadArt(folder, "front.png"); var blink = ReadArt(folder, "front.blink-1.png");
            checks[$"{character.Id}: blink dimensions match"] = front.PixelWidth == blink.PixelWidth && front.PixelHeight == blink.PixelHeight;
            window.PreviewCompanion(character.Id);
            var companion = window.Companion!;
            var seen = new HashSet<CharacterPose>();
            CharacterPose? previous = null;
            var distinct = true;
            var groupsAlternate = true;
            var grouped = character.Poses.Select(pose => pose.Group).Distinct().Count() > 1;
            // Coverage scales with the pose count; the two-pose figures deterministically alternate.
            for (var i = 0; i < character.Poses.Count * 12; i++)
            {
                var pose = window.CurrentPose!;
                if (pose == previous) distinct = false;
                if (grouped && previous is not null && previous.Group == pose.Group) groupsAlternate = false;
                previous = pose; seen.Add(pose); window.ChangePose();
            }
            checks[$"{character.Id}: random poses cover every picture without adjacent repeats"] = distinct && seen.Count == character.Poses.Count;
            if (grouped) checks[$"{character.Id}: pose groups take turns"] = groupsAlternate;
            var paired = character.Theme;
            var shots = new List<string>();
            foreach (var theme in MainWindow.ThemeChoices)
            {
                window.ApplyTheme(theme); window.UpdateLayout();
                checks[$"{character.Id}/{theme}: appears on right edge without focus"] = window.CompanionShown && !companion.ShowActivated && !companion.ShowInTaskbar && !companion.Focusable
                    && Math.Abs(companion.Left - (window.Left + window.ActualWidth - CompanionWindow.Overlap)) <= 2
                    && Math.Abs(companion.Footprint.Height - (650 * character.HeightScale * window.CurrentPose!.Scale + 24 - CompanionWindow.FeetDrop)) <= 1;
                checks[$"{character.Id}/{theme}: avatar and pose are present"] = window.GreetingAvatar.IsVisible && companion.Pose == window.CurrentPose!.Body;
                var captured = new HashSet<CharacterPose>();
                for (var i = 0; i < character.Poses.Count * 12 && captured.Count < character.Poses.Count; i++)
                {
                    var pose = window.CurrentPose!;
                    if (captured.Add(pose))
                    {
                        var path = Path.Combine(output, $"desktop-{theme.ToString().ToLowerInvariant()}-{character.Id}-{pose.Id}.png");
                        CaptureWithCompanion(window, companion, path);
                        if (theme == paired) shots.Add(path);
                    }
                    window.ChangePose();
                }
            }
            // Each row keeps the poses in their defined order regardless of the random starting pose.
            sheet.Add(shots.OrderBy(path => character.Poses.ToList().FindIndex(pose => path.EndsWith($"-{pose.Id}.png"))).ToList());
            window.PreviewHide();
            checks[$"{character.Id}: hidden companion stops motion"] = !window.CompanionShown && !companion.IsAnimating;
            window.Show();
        }
        CaptureSheet(sheet, Path.Combine(output, "characters-sheet.png"));

        // The rice-bowl DeepSeek娘's eating pose plays its pictures in turn and starts over from the first.
        window.ApplyTheme(ThemeMode.DeepSeek);
        window.PreviewCompanion("deepseek-rice");
        window.PreviewPoseIndex(Characters.Find("deepseek-rice")!.Poses.ToList().FindIndex(p => p.HasFrames));
        var eating = window.CurrentPose!;
        var eater = window.Companion!;
        eater.StartMotion();
        await eater.MotionReady;
        await Task.Delay(eater.PreparedMotion.Frames[0].Hold + TimeSpan.FromMilliseconds(400));
        var advanced = eater.IsPlayingFrames && eater.Pose != eating.Body;
        eater.StopMotion();
        checks["deepseek-rice: eating pose plays its frames and rewinds"] = advanced && eater.Pose == eating.Body && !eater.IsPlayingFrames;
        CaptureWithCompanion(window, eater, Path.Combine(output, "desktop-deepseek-rice-eating.png"));

        // Settings: choosing a character's theme brings her along, unless one of hers is already chosen; by hand
        // she can still be changed, and with the switch off the theme leaves her alone.
        window.PreviewCompanion(null);
        window.PreviewPage(MenuPage.Settings);
        int Index(string id) => 1 + Characters.All.ToList().FindIndex(character => character.Id == id);
        void Pick(ThemeMode theme) => window.ThemePicker.SelectedIndex = Array.IndexOf(MainWindow.ThemeChoices, theme);
        window.FollowThemeCheck.IsChecked = true;
        Pick(ThemeMode.Gemini);
        var follows = window.CharacterPicker.SelectedIndex == Index("gemini");
        window.CharacterPicker.SelectedIndex = Index("deepseek-rice");
        Pick(ThemeMode.DeepSeek);
        var keepsOwn = window.CharacterPicker.SelectedIndex == Index("deepseek-rice");
        Pick(ThemeMode.Oled);
        var keepsOnPlainTheme = window.CharacterPicker.SelectedIndex == Index("deepseek-rice");
        Pick(ThemeMode.ChatGpt);
        follows &= window.CharacterPicker.SelectedIndex == Index("chatgpt");
        window.FollowThemeCheck.IsChecked = false;
        Pick(ThemeMode.Cream);
        var switchOff = window.CharacterPicker.SelectedIndex == Index("chatgpt");
        checks["character follows the theme, keeps a matching or hand-picked choice, and stops when switched off"] = follows && keepsOwn && keepsOnPlainTheme && switchOff;
        window.PreviewPage(MenuPage.Desktop);
        window.PreviewWorldClock(false);
        var plain = window.GreetingText.Text;
        window.PreviewWorldClock(true);
        checks["greeting can leave out the world clock"] = !plain.Contains("北京") && !plain.Contains('，') && window.GreetingText.Text.Contains("北京时间");

        // The content keeps one inset on all four sides: the greeting from the top, the page from the bottom and the
        // tab rule from the left and right are all within a few pixels of each other.
        window.UpdateLayout();
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(window.ContentRoot).TransformBounds(new Rect(element.RenderSize));
        var rule = Bounds(window.CategoryToolbar);
        var insets = new[] { Bounds(window.GreetingText).Top, window.ContentRoot.ActualHeight - Bounds(window.PageHost).Bottom, rule.Left, window.ContentRoot.ActualWidth - rule.Right };
        File.WriteAllText(Path.Combine(output, "insets.txt"), string.Join(" ", insets.Select(inset => inset.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))));
        checks["content keeps an even inset on all four sides"] = insets.Max() - insets.Min() <= 4;

        // A backdrop drawing larger than its base wash stretches the wash short of the window edge.
        checks["every backdrop drawing exactly covers the window"] = MainWindow.ThemeChoices.All(theme =>
        {
            window.ApplyTheme(theme);
            if (Application.Current.Resources["BackdropBrush"] is not DrawingBrush { Drawing: DrawingGroup drawing }) return true;
            var wash = drawing.Children[0].Bounds;
            return Math.Abs(drawing.Bounds.Width - wash.Width) < 0.01 && Math.Abs(drawing.Bounds.Height - wash.Height) < 0.01 && Math.Abs(drawing.Bounds.X - wash.X) < 0.01;
        });
        window.PreviewCompanion("nobody");
        checks["unknown character is safely hidden"] = !window.CompanionShown && !window.GreetingAvatar.IsVisible;
        window.PreviewCompanion(null);
        foreach (var (theme, elements, dark) in new[] { (ThemeMode.ChatGpt, 5, false), (ThemeMode.DeepSeek, 6, false), (ThemeMode.Gemini, 9, true) })
        {
            window.ApplyTheme(theme);
            checks[$"{theme}: appearance and scene elements"] = window.IsDarkAppearance == dark && window.AmbientLayer.Children.Count == elements;
            checks[$"{theme}: opaque surface and frozen backdrop"] = ((SolidColorBrush)Application.Current.Resources["SurfaceBrush"]).Color.A == 255 && Application.Current.Resources["BackdropBrush"] is DrawingBrush { IsFrozen: true };
            checks[$"{theme}: text contrast at least 4.5"] = new[] { "SurfaceBrush", "SidebarBrush", "InputBrush", "PopupBrush", "CardBrush", "SelectedBrush" }
                .All(key => new[] { "TextBrush", "MutedBrush" }.All(text => Contrast(((SolidColorBrush)Application.Current.Resources[text]).Color, ((SolidColorBrush)Application.Current.Resources[key]).Color) >= 4.5));
            checks[$"{theme}: button label contrast at least 4.5"] = ((LinearGradientBrush)Application.Current.Resources["ActionBrush"]).GradientStops
                .All(stop => Contrast(((SolidColorBrush)Application.Current.Resources["OnActionBrush"]).Color, stop.Color) >= 4.5);
            window.Ambient.Start(); window.PreviewHide();
            checks[$"{theme}: hiding removes animation clocks"] = !window.Ambient.IsRunning;
            window.Show();
            window.PreviewPage(MenuPage.Settings);
            window.ThemePicker.SelectedIndex = Array.IndexOf(MainWindow.ThemeChoices, theme);
            window.CharacterPicker.SelectedIndex = 0;
            Capture(window, Path.Combine(output, $"settings-{theme.ToString().ToLowerInvariant()}.png"), 1);
            window.PreviewPage(MenuPage.RunningApps); Capture(window, Path.Combine(output, $"apps-{theme.ToString().ToLowerInvariant()}.png"), 1);
            window.PreviewPage(MenuPage.Desktop);
        }
        // Each theme's signature moment, held at its liveliest for a look. DeepSeek's whale rests under the water between spouts.
        foreach (var (theme, seconds) in new[] { (ThemeMode.Oled, 3.0), (ThemeMode.OledDay, 2.25), (ThemeMode.Cream, 3.4), (ThemeMode.ChatGpt, 3.3), (ThemeMode.DeepSeek, 1.85), (ThemeMode.Gemini, 3.9) })
        {
            window.ApplyTheme(theme);
            var resting = theme != ThemeMode.DeepSeek || window.Ambient.WhaleRise is { Y: >= 40 };
            var shown = window.Ambient.PreviewMoment(TimeSpan.FromSeconds(seconds));
            var surfaced = theme != ThemeMode.DeepSeek || window.Ambient.WhaleRise is { Y: > -3 and < 1 };
            Capture(window, Path.Combine(output, $"moment-{theme.ToString().ToLowerInvariant()}.png"), 1);
            window.Ambient.Stop();
            checks[$"{theme}: signature moment plays and leaves nothing behind"] = resting && shown && surfaced && (theme != ThemeMode.DeepSeek || window.Ambient.WhaleRise is { Y: >= 40 });
        }
        await YieldRender();
    }

    private static BitmapSource ReadArt(string folder, string file) => CharacterArt.Load(folder, file);


    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Linear(byte v) { var s = v / 255.0; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }
        var x = L(a); var y = L(b); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static void CaptureSheet(List<List<string>> rows, string path)
    {
        const int cellWidth = 600, cellHeight = 370;
        var columns = Math.Max(1, rows.Max(row => row.Count));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x4A, 0x52, 0x60)), null, new Rect(0, 0, cellWidth * columns, cellHeight * rows.Count));
            for (var row = 0; row < rows.Count; row++)
                for (var col = 0; col < rows[row].Count; col++)
                {
                    var image = new BitmapImage(new Uri(rows[row][col]));
                    var scale = Math.Min((cellWidth - 16) / image.Width, (cellHeight - 16) / image.Height);
                    context.DrawImage(image, new Rect(col * cellWidth + 8, row * cellHeight + 8, image.Width * scale, image.Height * scale));
                }
        }
        var bitmap = new RenderTargetBitmap(cellWidth * columns, cellHeight * rows.Count, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }

    private static void RaiseKey(MainWindow window, Key key)
    {
        var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        window.RaiseEvent(input);
    }

    private static async Task YieldRender()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var completion = new TaskCompletionSource();
        EventHandler? handler = null;
        handler = (_, _) => { CompositionTarget.Rendering -= handler; completion.TrySetResult(); };
        CompositionTarget.Rendering += handler;
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Share of rendered pixels that are exactly #000000.</summary>
    private static double BlackShare(Window window, int width = 0)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var columns = width > 0 ? Math.Min(width, bitmap.PixelWidth) : bitmap.PixelWidth;
        var pixels = new byte[columns * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(new Int32Rect(0, 0, columns, bitmap.PixelHeight), pixels, columns * 4, 0);
        var black = 0;
        for (var i = 0; i < pixels.Length; i += 4) if (pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0) black++;
        return black / (double)(pixels.Length / 4);
    }

    /// <summary>The menu and Claude娘's window as they sit on screen, over a neutral wallpaper grey.</summary>
    private static void CaptureWithCompanion(Window menu, CompanionWindow companion, string path)
    {
        menu.UpdateLayout(); companion.UpdateLayout();
        static RenderTargetBitmap Render(Window window)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            return bitmap;
        }
        var menuImage = Render(menu);
        var herImage = Render(companion);
        var herTop = menu.ActualHeight + CompanionWindow.FeetDrop - companion.ActualHeight;
        var lift = Math.Max(0, -herTop);
        var size = new Size(menu.ActualWidth - CompanionWindow.Overlap + companion.ActualWidth, Math.Max(menu.ActualHeight, menu.ActualHeight + CompanionWindow.FeetDrop) + lift);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x4A, 0x52, 0x60)), null, new Rect(size));
            context.DrawImage(menuImage, new Rect(0, lift, menu.ActualWidth, menu.ActualHeight));
            context.DrawImage(herImage, new Rect(menu.ActualWidth - CompanionWindow.Overlap, herTop + lift, companion.ActualWidth, companion.ActualHeight));
        }
        var result = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(result));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    // Text of every visible TextBlock under root, skipping the named data-showing elements and what is inside them.
    private static IEnumerable<string> VisibleText(DependencyObject root, HashSet<string> skip)
    {
        if (root is FrameworkElement { Name: var name } && skip.Contains(name)) yield break;
        if (root is UIElement { IsVisible: false }) yield break;
        if (root is System.Windows.Controls.TextBlock { Text: { Length: > 0 } text }) yield return text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in VisibleText(VisualTreeHelper.GetChild(root, i), skip)) yield return child;
    }

    private static void Capture(Window window, string path, double scale)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * scale), (int)Math.Ceiling(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
