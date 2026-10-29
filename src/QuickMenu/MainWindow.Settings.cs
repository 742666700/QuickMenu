using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using QuickMenu.Models;
using QuickMenu.Services;

namespace QuickMenu;

/// <summary>
/// The settings page: every control applies and saves at once (no save button), a section list on the left jumps
/// within the page, the greeting clock is configurable, and a one-time welcome greets a new install.
/// </summary>
public partial class MainWindow
{
    /// <summary>The project's home page and the developer's address, shown under 关于.</summary>
    internal const string ProjectUrl = "https://github.com/742666700/QuickMenu";
    internal const string ContactEmail = "742666700@163.com";
    internal string CurrentHotkey => FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);
    internal static string AppVersion => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "";
    private static readonly IReadOnlyList<TimeZoneInfo> TimeZones = TimeZoneInfo.GetSystemTimeZones();
    private static readonly Dictionary<string, TimeZoneInfo?> ZoneCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly DependencyProperty SettingsScrollOffsetProperty = DependencyProperty.Register(
        nameof(SettingsScrollOffset), typeof(double), typeof(MainWindow),
        new PropertyMetadata(0.0, (d, e) => ((MainWindow)d).SettingsPanel.ScrollToVerticalOffset((double)e.NewValue)));
    private double SettingsScrollOffset { get => (double)GetValue(SettingsScrollOffsetProperty); set => SetValue(SettingsScrollOffsetProperty, value); }
    private bool _welcome;
    private int _settingsScrollGeneration;

    private void InitializeSettingsPage()
    {
        SecondClockPicker.Items.Add(Loc.T("Character.None"));
        foreach (var zone in TimeZones) SecondClockPicker.Items.Add(zone.DisplayName);
        VersionText.Text = Loc.F("Settings.Version", AppVersion);
        ProjectLinkText.Text = ProjectUrl;
        ProjectLink.Tag = ProjectUrl;
        ContactLinkText.Text = ContactEmail;
        ContactLink.Tag = "mailto:" + ContactEmail;
        SettingsPanel.PreviewMouseWheel += (_, _) => CancelSettingsScroll();
        SettingsPanel.PreviewMouseDown += (_, _) => CancelSettingsScroll();
        SettingsPanel.PreviewKeyDown += (_, _) => CancelSettingsScroll();
        SettingsPanel.PreviewTouchDown += (_, _) => CancelSettingsScroll();
        SettingsPanel.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => CancelSettingsScroll()), true);
        SettingsPanel.IsVisibleChanged += (_, _) => { if (!SettingsPanel.IsVisible) CancelSettingsScroll(); };
        ThemePicker.SelectionChanged += (_, _) => { FollowTheme(); CommitSettings(); };
        CharacterPicker.SelectionChanged += (_, _) => CommitSettings();
        CharacterMotionCheck.Click += (_, _) => CommitSettings();
        FollowThemeCheck.Click += (_, _) => { FollowTheme(); CommitSettings(); };
        WorldClockCheck.Click += (_, _) => CommitSettings();
        StartupCheck.Click += (_, _) => CommitSettings();
        GamepadCheck.Click += (_, _) => CommitSettings();
        SearchFocusCheck.Click += (_, _) => CommitSettings();
        StayOpenCheck.Click += (_, _) => CommitSettings();
        LanguagePicker.SelectionChanged += (_, _) => CommitSettings();
        Loc.Changed += OnLanguageChanged;
        ClockLabelBox.TextChanged += (_, _) => CommitSettings();
        SecondClockLabelBox.TextChanged += (_, _) => CommitSettings();
        SecondClockPicker.SelectionChanged += (_, _) => CommitSettings();
    }

    private void LoadSettingsPage()
    {
        _draftModifiers = _settings.HotkeyModifiers; _draftKey = _settings.HotkeyKey;
        HotkeyBox.Text = FormatHotkey(_draftModifiers, _draftKey);
        _loadingSettings = true;
        LanguagePicker.SelectedIndex = _settings.Language switch { Loc.Chinese => 1, Loc.English => 2, _ => 0 };
        ThemePicker.SelectedIndex = Math.Max(0, Array.IndexOf(ThemeChoices, _settings.Theme));
        CharacterPicker.SelectedIndex = PickerIndex(Characters.Find(_settings.Character));
        FollowThemeCheck.IsChecked = _settings.CharacterFollowsTheme;
        CharacterMotionCheck.IsChecked = _settings.CharacterMotion;
        WorldClockCheck.IsChecked = _settings.ShowWorldClock;
        ClockLabelBox.Text = _settings.ClockLabel ?? "";
        var zone = ZoneOf(_settings.SecondClockZone);
        SecondClockPicker.SelectedIndex = zone is null ? 0 : 1 + TimeZones.ToList().FindIndex(item => item.Id == zone.Id);
        SecondClockLabelBox.Text = _settings.SecondClockLabel ?? "";
        StartupCheck.IsChecked = _settings.StartWithWindows;
        GamepadCheck.IsChecked = _settings.Gamepad;
        SearchFocusCheck.IsChecked = _settings.FocusSearchOnOpen;
        StayOpenCheck.IsChecked = _settings.StayOpenOnClickOutside;
        _loadingSettings = false;
        UpdateClockOptions();
        RenderGameLibrary();
        RenderCategorySettings();
        CancelSettingsScroll();
        SettingsPanel.ScrollToTop();
        UpdateSectionButtons();
    }

    /// <summary>Saves what the page shows. A hotkey another program holds is refused and the old one kept.</summary>
    private void CommitSettings()
    {
        if (_loadingSettings || _session.Page != MenuPage.Settings) return;
        var next = _settings.Clone();
        next.HotkeyModifiers = _draftModifiers; next.HotkeyKey = _draftKey;
        next.Theme = ThemeChoices[Math.Max(0, ThemePicker.SelectedIndex)];
        next.Character = CharacterPicker.SelectedIndex > 0 ? Characters.All[CharacterPicker.SelectedIndex - 1].Id : "";
        next.CharacterMotion = CharacterMotionCheck.IsChecked == true;
        next.CharacterFollowsTheme = FollowThemeCheck.IsChecked == true;
        next.ShowWorldClock = WorldClockCheck.IsChecked == true;
        next.ClockLabel = ClockLabelBox.Text.Trim();
        next.SecondClockZone = SecondClockPicker.SelectedIndex > 0 ? TimeZones[SecondClockPicker.SelectedIndex - 1].Id : null;
        next.SecondClockLabel = SecondClockLabelBox.Text.Trim();
        next.StartWithWindows = StartupCheck.IsChecked == true;
        next.Gamepad = GamepadCheck.IsChecked == true;
        next.FocusSearchOnOpen = SearchFocusCheck.IsChecked == true;
        next.StayOpenOnClickOutside = StayOpenCheck.IsChecked == true;
        next.Language = LanguagePicker.SelectedIndex switch { 1 => Loc.Chinese, 2 => Loc.English, _ => "" };
        var relanguage = Loc.Resolve(next.Language) != Loc.Current;
        UpdateClockOptions(next);
        if (_isQa) { _settings = next; _companion?.SetSpontaneous(next.CharacterMotion); if (CurrentPose?.MotionOnly == true && !next.CharacterMotion) ApplyCompanion(); if (relanguage) Loc.Use(next.Language); return; }
        var hotkeyChanged = next.HotkeyModifiers != _settings.HotkeyModifiers || next.HotkeyKey != _settings.HotkeyKey;
        if (hotkeyChanged)
        {
            var hotkey = _hotkeys?.TryRegister(next.HotkeyModifiers, next.HotkeyKey) ?? new OperationResult(false, Loc.T("Status.HotkeyNotReady"));
            if (!hotkey.Success)
            {
                _draftModifiers = _settings.HotkeyModifiers; _draftKey = _settings.HotkeyKey;
                HotkeyBox.Text = FormatHotkey(_draftModifiers, _draftKey);
                SetStatus(hotkey.Message); return;
            }
        }
        var saved = _settingsService.Save(next);
        if (!saved.Success) { if (hotkeyChanged) _hotkeys?.TryRegister(_settings.HotkeyModifiers, _settings.HotkeyKey); SetStatus(saved.Message); return; }
        var restyle = next.Theme != _settings.Theme || next.Character != _settings.Character;
        _settings = next; _hotkeyError = "";
        _companion?.SetSpontaneous(next.CharacterMotion);
        if (CurrentPose?.MotionOnly == true && !next.CharacterMotion) ApplyCompanion();
        if (restyle) ApplyTheme(next.Theme);
        if (_gamepad != null) _gamepad.Enabled = next.Gamepad;
        if (relanguage) Loc.Use(next.Language);
        SetStatus(Loc.T("Status.SettingsSaved"));
    }

    /// <summary>
    /// Text set from code follows a language change here; text from XAML follows by itself (DynamicResource).
    /// </summary>
    private void OnLanguageChanged()
    {
        if (_disposed) return;
        _loadingSettings = true;
        var character = CharacterPicker.SelectedIndex;
        CharacterPicker.Items.Clear();
        CharacterPicker.Items.Add(Loc.T("Character.None"));
        foreach (var each in Characters.All) CharacterPicker.Items.Add(each.Name);
        CharacterPicker.SelectedIndex = character;
        var zone = SecondClockPicker.SelectedIndex;
        SecondClockPicker.Items[0] = Loc.T("Settings.NoSecondClock");
        SecondClockPicker.SelectedIndex = zone;
        _loadingSettings = false;
        VersionText.Text = Loc.F("Settings.Version", AppVersion);
        RenderCategoryTabs();
        UpdateSortLabel();
        _desktopView.Refresh();
        _appsView.Refresh();
        // Re-enter the page so its titles, placeholders, counts and the settings lists are written again.
        var status = StatusText.Text;
        SetPage(_session.Page);
        if (_session.Page == MenuPage.Settings) ScrollSettingsTo("SectionGeneral");
        SetStatus(_session.Page == MenuPage.Settings ? Loc.T("Status.SettingsSaved") : status);
    }

    private void UpdateClockOptions(AppSettings? settings = null)
    {
        settings ??= _settings;
        ClockOptions.IsEnabled = WorldClockCheck.IsChecked == true;
        SecondClockLabelBox.IsEnabled = SecondClockPicker.SelectedIndex > 0;
        ClockPreview.Text = Loc.F("Settings.ClockPreview", Greeting(settings, previewTime: true));
    }

    // ---- Greeting ------------------------------------------------------------------------------------------------

    // "早上好，现在是 08:42", optionally naming the local clock and adding a second time zone:
    // "早上好，现在是美国时间 08:42，北京时间 23:42".
    private string Greeting()
    {
        if (!_isQa) return Greeting(_settings, previewTime: false);
        var preview = _settings.Clone();
        preview.ShowWorldClock = _previewWorldClock;
        return Greeting(preview, previewTime: true);
    }

    private static string Greeting(AppSettings settings, bool previewTime)
    {
        var local = previewTime ? new DateTime(2026, 10, 16, 15, 0, 0) : DateTime.Now;
        var salute = local.Hour switch { < 5 => Loc.T("Greeting.Late"), < 11 => Loc.T("Greeting.Morning"), < 13 => Loc.T("Greeting.Noon"), < 18 => Loc.T("Greeting.Afternoon"), _ => Loc.T("Greeting.Evening") };
        if (!settings.ShowWorldClock) return salute;
        var label = string.IsNullOrWhiteSpace(settings.ClockLabel) ? (Loc.IsChinese ? " " : "") : settings.ClockLabel.Trim() + " ";
        var text = Loc.F("Greeting.Time", salute, label, local);
        if (ZoneOf(settings.SecondClockZone) is { } zone)
        {
            var other = previewTime ? TimeZoneInfo.ConvertTime(new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)), zone).DateTime : TimeZoneInfo.ConvertTime(DateTimeOffset.Now, zone).DateTime;
            text += Loc.F("Greeting.Second", string.IsNullOrWhiteSpace(settings.SecondClockLabel) ? zone.StandardName : settings.SecondClockLabel.Trim(), other);
        }
        return text;
    }

    private static TimeZoneInfo? ZoneOf(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (ZoneCache)
        {
            if (ZoneCache.TryGetValue(id, out var cached)) return cached;
            TimeZoneInfo? zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            { zone = id == "China Standard Time" ? TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.FromHours(8), "北京时间", "北京时间") : null; }
            return ZoneCache[id] = zone;
        }
    }

    // ---- Section list ----------------------------------------------------------------------------------------------

    private void SettingsSection_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string name) ScrollSettingsTo(name);
    }

    internal void ScrollSettingsTo(string name)
    {
        if (FindName(name) is not FrameworkElement section) return;
        SettingsPanel.UpdateLayout();
        var target = Math.Clamp(section.TranslatePoint(new Point(), SettingsContent).Y - 2, 0, SettingsPanel.ScrollableHeight);
        CancelSettingsScroll();
        if (!AnimationsEnabled || !IsVisible)
        {
            SettingsPanel.ScrollToVerticalOffset(target);
            SettingsPanel.UpdateLayout();
            UpdateSectionButtons();
            return;
        }
        var from = SettingsPanel.VerticalOffset;
        if (Math.Abs(target - from) < 1) { UpdateSectionButtons(); return; }
        var glide = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(MotionTimings.SettingsScroll))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var generation = _settingsScrollGeneration;
        glide.Completed += (_, _) =>
        {
            if (generation != _settingsScrollGeneration) return;
            SettingsScrollOffset = target;
            BeginAnimation(SettingsScrollOffsetProperty, null);
            SettingsPanel.ScrollToVerticalOffset(target);
            UpdateSectionButtons();
        };
        BeginAnimation(SettingsScrollOffsetProperty, glide);
    }

    private void CancelSettingsScroll()
    {
        // Removing a clock exposes its base value; keep the current offset so taking over never jumps to zero.
        _settingsScrollGeneration++;
        SettingsScrollOffset = SettingsPanel.VerticalOffset;
        BeginAnimation(SettingsScrollOffsetProperty, null);
    }

    private void SettingsPanel_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateSectionButtons();

    // The section whose heading has scrolled to (or past) the top is current; at the very end, the last one is.
    private void UpdateSectionButtons()
    {
        if (SettingsSections is null || SettingsContent is null) return;
        var buttons = SettingsSections.Children.OfType<Button>().ToArray();
        var current = buttons[0];
        foreach (var button in buttons)
            if (FindName((string)button.Tag) is FrameworkElement section && section.IsVisible && section.TranslatePoint(new Point(), SettingsContent).Y <= SettingsPanel.VerticalOffset + 40) current = button;
        if (SettingsPanel.ScrollableHeight > 0 && SettingsPanel.VerticalOffset >= SettingsPanel.ScrollableHeight - 1) current = buttons[^1];
        foreach (var button in buttons)
        {
            button.SetValue(System.Windows.Controls.Primitives.Selector.IsSelectedProperty, button == current);
            if (button == current) button.SetResourceReference(BackgroundProperty, "SelectedBrush"); else button.Background = System.Windows.Media.Brushes.Transparent;
            button.SetResourceReference(ForegroundProperty, button == current ? "TextBrush" : "MutedBrush");
        }
    }

    // ---- About ---------------------------------------------------------------------------------------------------

    /// <summary>Opens the project page or a new mail to the developer (the address is in the link's Tag).</summary>
    private void WebLink_Click(object sender, RoutedEventArgs e)
    {
        if (_isQa || sender is not FrameworkContentElement { Tag: string target }) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose(); HideMenu(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { SetStatus(Loc.T("Settings.WebFailed") + ex.Message); }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_isQa) { SetStatus(Loc.T("Status.PreviewNoExplorer")); return; }
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickMenu");
        try { Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose(); HideMenu(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException) { SetStatus(Loc.T("Settings.DataFailed") + ex.Message); }
    }

    // ---- Welcome (first run) ---------------------------------------------------------------------------------------

    internal void ShowWelcome()
    {
        _welcome = true; _pendingApp = null; _pendingProcesses = null;
        DialogTitle.Text = Loc.T("Welcome.Title");
        DialogDescription.Text = Loc.F("Welcome.Hotkey", FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey));
        DialogDetail.Text = WelcomeDetail();
        WindowPicker.Visibility = Visibility.Collapsed;
        DialogCancel.Content = Loc.T("Welcome.GotIt");
        DialogConfirm.Content = Loc.T("Welcome.OpenSettings");
        DialogConfirm.SetResourceReference(BackgroundProperty, "ActionBrush");
        ShowOverlay();
    }

    private string WelcomeDetail() => _desktopLoading && _desktop.Count == 0 ? Loc.T("Welcome.Loading")
        : Loc.F("Welcome.Found", _desktop.Count, _desktop.Count(item => item.Category == DesktopCategory.Games));

    // Closing the welcome in any way counts as seen.
    private void FinishWelcome()
    {
        if (!_welcome) return;
        _welcome = false;
        if (_isQa) return;
        var next = _settings.Clone();
        next.WelcomeShown = true;
        if (_settingsService.Save(next).Success) _settings = next;
    }
}
