using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using QuickMenu.Models;
using QuickMenu.Services;
using ThemeMode = QuickMenu.Models.ThemeMode;

namespace QuickMenu;

public partial class MainWindow : Window
{
    private readonly MenuSession _session = new();
    private readonly DesktopService _desktopService = new();
    private readonly WindowService _windowService = new();
    private readonly TrayService _trayService = new();
    private readonly ProcessService _processService = new();
    private readonly SettingsService _settingsService;
    private readonly DesktopPreferencesService _desktopPreferences;
    private ContextMenu? _desktopMenu;
    private readonly DesktopGroup _desktopGroups;
    private readonly AmbientScene _ambient;
    private ThemeMode _theme;
    private IReadOnlyList<ProcessIdentity>? _pendingProcesses;
    private readonly ObservableCollection<DesktopItem> _desktop = new();
    private readonly ObservableCollection<RunningApp> _apps = new();
    private readonly ICollectionView _desktopView;
    private readonly ICollectionView _appsView;
    private readonly DispatcherTimer _refreshTimer;
    private AppSettings _settings;
    private HotkeyService? _hotkeys;
    private CancellationTokenSource? _invocation;
    private nint _previousForeground;
    private bool _desktopLoading, _appsLoading, _changingSearch, _updatingItems, _nativeInitialized, _disposed;
    private uint _draftModifiers, _draftKey;
    private RunningApp? _pendingApp;
    private bool _windowSelection;
    private bool _isQa;
    private bool _desktopRefreshRequested;
    private bool _appsRefreshRequested;
    private string _desktopFilter = "", _appsFilter = "";
    private string _hotkeyError = "";
    internal bool HotkeyRegistered => _hotkeyError.Length == 0;
    internal bool IsDarkAppearance { get; private set; }
    internal event Action<bool>? AppearanceChanged;
    internal MenuPage CurrentPage => _session.Page;
    internal string CurrentSearch => SearchBox.Text;
    internal int DesktopCount => _desktop.Count;
    internal int RunningCount => _apps.Count;
    internal int InvocationCount { get; private set; }
    internal bool HasOverlay => Overlay.Visibility == Visibility.Visible;
    internal ContextMenu? DesktopMenu => _desktopMenu;

    public MainWindow(bool isQa = false)
    {
        _isQa = isQa;
        _settingsService = new SettingsService(manageStartup: !isQa);
        _settings = _settingsService.Load();
        _desktopPreferences = new DesktopPreferencesService(ephemeral: isQa);
        InitializeComponent();
        _ambient = new AmbientScene(AmbientLayer, GreetingSpark);
        // Every menu window, self-check ones included, gets the input-method guard as soon as it has a handle.
        SourceInitialized += (_, _) => HostNative.GuardImeRequests(this);
        CharacterPicker.Items.Add(Loc.T("Character.None"));
        foreach (var character in Characters.All) CharacterPicker.Items.Add(character.Name);
        InitializeSettingsPage();
        // The self-check shows the clock as 1.x did, whatever this PC's settings say.
        if (isQa) { _settings.ClockLabel = "美国时间"; _settings.SecondClockZone = "China Standard Time"; _settings.SecondClockLabel = "北京时间"; }
        // She stands outside the menu's window, so she follows it when the header is dragged.
        LocationChanged += (_, _) => { if (CompanionShown) _companion!.PlaceBeside(this); };
        // Swiping past the end of a list would otherwise drag the whole window along with it.
        ManipulationBoundaryFeedback += (_, e) => e.Handled = true;
        _session.Begin();
        _desktopView = CollectionViewSource.GetDefaultView(_desktop);
        _desktopView.Filter = item => { var desktopItem = (DesktopItem)item; return InDesktopFilter(desktopItem) && Matches(desktopItem.Name, _session.Searches.GetValueOrDefault(MenuPage.Desktop, "")); };
        ((ListCollectionView)_desktopView).CustomSort = _desktopPreferences;
        _desktopGroups = new DesktopGroup(GroupOf);
        _desktopView.GroupDescriptions.Add(_desktopGroups);
        InitializeCategories();
        _holdTimer.Tick += (_, _) => BeginReorder();
        UpdateSortLabel();
        _appsView = CollectionViewSource.GetDefaultView(_apps);
        _appsView.Filter = item => { var app = (RunningApp)item; var text = _session.Searches.GetValueOrDefault(MenuPage.RunningApps, ""); return Matches(app.Name, text) || app.Windows.Any(w => Matches(w.Title, text)); };
        _appsView.GroupDescriptions.Add(new AppGroup());
        DesktopList.ItemsSource = _desktopView;
        AppsList.ItemsSource = _appsView;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += async (_, _) => { if (IsVisible && _session.Page != MenuPage.Settings) GreetingText.Text = Greeting(); if (IsVisible && _session.Page == MenuPage.RunningApps && !HasOverlay) await RefreshAppsAsync(); };
        if (!isQa) SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        ApplyTheme(_settings.Theme);
        SetPage(MenuPage.Desktop);
        SizeChanged += (_, e) => { UpdateColumns(); if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 40 || Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 40) RebuildAmbient(); };
        Loaded += (_, _) => UpdateColumns();
    }

    private static bool Matches(string source, string query) => string.IsNullOrWhiteSpace(query) || CultureInfo.CurrentCulture.CompareInfo.IndexOf(source, query.Trim(), CompareOptions.IgnoreCase) >= 0;

    public void InitializeNative()
    {
        if (_nativeInitialized || _isQa) return;
        _nativeInitialized = true;
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        _hotkeys = new HotkeyService(hwnd);
        _hotkeys.Pressed += (_, _) => ToggleMenu();
        StartGamepad();
        var result = _hotkeys.TryRegister(_settings.HotkeyModifiers, _settings.HotkeyKey);
        _hotkeyError = result.Success ? "" : result.Message;
        ApplyTheme(_settings.Theme);
    }

    public void InvokeMenu()
    {
        _opening = true;
        try { OpenMenu(); }
        finally { _opening = false; }
    }

    private void OpenMenu()
    {
        InvocationCount++;
        _previousForeground = HostNative.GetForegroundWindow();
        if (_previousForeground == new WindowInteropHelper(this).Handle) _previousForeground = 0;
        _invocation?.Cancel();
        _invocation?.Dispose();
        _invocation = new CancellationTokenSource();
        var resetCategory = _session.DesktopCategory != DesktopCategory.All || _session.CustomCategory != null;
        _session.Begin();
        if (resetCategory) _desktopView.Refresh();
        CloseOverlay();
        SetPage(MenuPage.Desktop);
        if (!_isQa && !_settings.WelcomeShown) ShowWelcome();
        ChangePose();
        // Position before showing, so the very first appearance is not drawn at the default spot and then moved.
        if (!_isQa) PlaceWindow();
        ShiftPixels();
        Show();
        PlayEntrance();
        if (AnimationsEnabled) _ambient.Start();
        if (_gamepad != null) _gamepad.Navigating = true;
        ShowCompanion();
        Activate();
        if (!_isQa) HostNative.EnsureForeground(this);
        FocusStart();
        // The window appears under a pointer that has not moved, so WPF does not yet know what is beneath it and
        // ignores the wheel until the next move or click. Re-read the pointer once the new layout is in place.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Mouse.Synchronize));
        _refreshTimer.Start();
        _ = WarmDesktopAsync();
        _ = RefreshAppsAsync();
        if (_hotkeyError.Length > 0) { OpenSettings(); SetStatus(_hotkeyError); }
    }

    // Only a menu that is in front counts as open; one left visible behind other windows is brought back instead of hidden.
    private void ToggleMenu() { if (HasOverlay) return; if (IsVisible && IsActive) HideMenu(); else InvokeMenu(); }

    public void OpenSettings()
    {
        if (!IsVisible) InvokeMenu();
        CloseOverlay();
        SetPage(MenuPage.Settings);
        HotkeyBox.Focus();
    }

    public async Task WarmDesktopAsync()
    {
        if (_isQa || _disposed) return;
        if (_desktopLoading) { _desktopRefreshRequested = true; return; }
        _desktopRefreshRequested = false;
        _desktopLoading = true;
        var token = _invocation?.Token ?? CancellationToken.None;
        try
        {
            var rawItems = await _desktopService.GetItemsAsync(token);
            var items = rawItems.Select(item => item with { IsPinned = _desktopPreferences.Get(item.Id).Pinned, DisplayCategory = _desktopPreferences.Category(item) }).ToArray();
            token.ThrowIfCancellationRequested();
            if (_disposed) return;
            if (!items.SequenceEqual(_desktop))
            {
                _updatingItems = true;
                _desktop.Clear(); foreach (var item in items) _desktop.Add(item);
                _updatingItems = false;
            }
            RestoreSelection();
            UpdateEmptyState();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.Log(ex); SetStatus(Loc.T("Status.DesktopFailed")); }
        finally
        {
            _updatingItems = false; _desktopLoading = false;
            if (!_disposed) UpdateEmptyState();
            if (_welcome) DialogDetail.Text = WelcomeDetail();
            if (_desktopRefreshRequested && !_disposed && IsVisible && _invocation is { IsCancellationRequested: false }) _ = WarmDesktopAsync();
        }
    }

    // The game library is cached between invocations; a manual refresh rescans it along with the desktop.
    private async void RefreshDesktop_Click(object sender, RoutedEventArgs e)
    {
        if (HasOverlay || _isQa || _disposed || _desktopLoading) return;
        _desktopService.RefreshGames();
        SetStatus(Loc.T("Status.Scanning"));
        await WarmDesktopAsync();
        SetStatus(Loc.T("Status.ScanDone"));
    }

    private async Task RefreshAppsAsync()
    {
        if (_isQa || _disposed) return;
        if (_appsLoading) { _appsRefreshRequested = true; return; }
        _appsRefreshRequested = false;
        _appsLoading = true;
        var token = _invocation?.Token ?? CancellationToken.None;
        try
        {
            var windowsTask = _windowService.GetAppsAsync(_previousForeground, token);
            var trayTask = _trayService.GetItemsAsync(token);
            var windows = await windowsTask;
            var tray = await trayTask;
            token.ThrowIfCancellationRequested();
            if (_disposed) return;
            var selectedId = (AppsList.SelectedItem as RunningApp)?.Id;
            var allApps = windows.Concat(tray).ToArray();
            var unchanged = allApps.Length == _apps.Count && allApps.Zip(_apps).All(pair => pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name && pair.First.IsForeground == pair.Second.IsForeground && pair.First.IsTray == pair.Second.IsTray && pair.First.Windows.SequenceEqual(pair.Second.Windows));
            if (!unchanged)
            {
                _updatingItems = true;
                _apps.Clear(); foreach (var item in allApps) _apps.Add(item);
                _updatingItems = false;
            }
            var existing = _appsView.Cast<RunningApp>().FirstOrDefault(a => a.Id == selectedId);
            if (existing is not null) AppsList.SelectedItem = existing;
            else RestoreSelection();
            UpdateEmptyState();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.Log(ex); if (_session.Page == MenuPage.RunningApps) SetStatus(Loc.T("Status.AppsPartial")); }
        finally
        {
            _updatingItems = false; _appsLoading = false;
            if (_appsRefreshRequested && !_disposed && IsVisible && _invocation is { IsCancellationRequested: false }) _ = RefreshAppsAsync();
        }
    }

    private void SetPage(MenuPage page)
    {
        if (Overlay is null) return;
        OfficePopup.IsOpen = false;
        _session.Navigate(page);
        DesktopList.Visibility = page == MenuPage.Desktop ? Visibility.Visible : Visibility.Collapsed;
        AppsList.Visibility = page == MenuPage.RunningApps ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = page == MenuPage.Settings ? Visibility.Visible : Visibility.Collapsed;
        SearchHost.Visibility = page == MenuPage.Settings ? Visibility.Collapsed : Visibility.Visible;
        CategoryToolbar.Visibility = page == MenuPage.Desktop ? Visibility.Visible : Visibility.Collapsed;
        UpdatePinnedShelf();
        UpdateCategoryButtons();
        PageTitle.Text = page switch { MenuPage.Desktop => Loc.T("Nav.Desktop"), MenuPage.RunningApps => Loc.T("Nav.Apps"), _ => Loc.T("Tray.Settings") };
        _changingSearch = true;
        SearchBox.Text = _session.Searches.GetValueOrDefault(page, "");
        SearchBox.CaretIndex = 0;
        _changingSearch = false;
        SearchPlaceholder.Text = page == MenuPage.Desktop ? Loc.T("Search.Desktop") : Loc.T("Search.Apps");
        System.Windows.Automation.AutomationProperties.SetName(SearchBox, SearchPlaceholder.Text);
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var nav in new[] { DesktopNav, AppsNav, SettingsNav }) { nav.SetValue(Selector.IsSelectedProperty, false); nav.Background = System.Windows.Media.Brushes.Transparent; nav.SetResourceReference(Control.ForegroundProperty, "MutedBrush"); }
        var selectedNav = page switch { MenuPage.Desktop => DesktopNav, MenuPage.RunningApps => AppsNav, _ => SettingsNav };
        selectedNav.SetValue(Selector.IsSelectedProperty, true);
        selectedNav.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        if (page == MenuPage.Settings) { SettingsNav.SetResourceReference(Control.BackgroundProperty, "SelectedBrush"); SettingsNav.SetResourceReference(Control.ForegroundProperty, "AccentBrush"); }
        GreetingText.Text = page == MenuPage.Settings ? Loc.T("Tray.Settings") : Greeting();
        if (page == MenuPage.Settings) LoadSettingsPage();
        SetStatus(page == MenuPage.RunningApps ? Loc.T("Status.TrayIncomplete") : "");
        var desktopQuery = _session.Searches.GetValueOrDefault(MenuPage.Desktop, "");
        var appsQuery = _session.Searches.GetValueOrDefault(MenuPage.RunningApps, "");
        if (_desktopFilter != desktopQuery) { _desktopFilter = desktopQuery; _desktopView?.Refresh(); }
        if (_appsFilter != appsQuery) { _appsFilter = appsQuery; _appsView?.Refresh(); }
        RestoreSelection();
        UpdateEmptyState();
        if (page == MenuPage.RunningApps) _ = RefreshAppsAsync();
    }

    private void RestoreSelection()
    {
        var page = _session.Page;
        if (page == MenuPage.Settings) return;
        var list = page == MenuPage.Desktop ? DesktopList : AppsList;
        var desired = _session.Selections.GetValueOrDefault(page, 0);
        list.SelectedIndex = list.Items.Count == 0 ? -1 : Math.Clamp(desired, 0, list.Items.Count - 1);
        if (list.SelectedItem is not null) list.ScrollIntoView(list.SelectedItem);
        if (page == MenuPage.Desktop && list.SelectedIndex == 0)
        {
            list.UpdateLayout();
            FindVisual<ScrollViewer>(list)?.ScrollToTop();
        }
    }

    private void UpdateEmptyState()
    {
        if (DesktopList is null) return;
        var pins = _desktopView.Cast<DesktopItem>().Where(item => item.IsPinned).ToArray();
        Array.Sort(pins, (a, b) => _reorder is { Active: true } drag ? drag.Compare(a, b) : _desktopPreferences.Compare(a, b));
        PinnedItems.ItemsSource = pins;
        UpdatePinnedShelf();
        var count = _session.Page == MenuPage.Desktop ? DesktopList.Items.Count : AppsList.Items.Count;
        HeaderDetail.Text = _session.Page switch
        {
            MenuPage.Desktop => Loc.F("Header.DesktopCount", count),
            MenuPage.RunningApps => Loc.F("Header.AppsCount", count),
            _ => Loc.T("Header.Settings")
        };
        EmptyState.Visibility = _session.Page != MenuPage.Settings && count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _session.Page == MenuPage.Desktop && _desktopLoading ? Loc.T("Empty.Loading") : SearchBox.Text.Length > 0 ? Loc.T("Empty.NoMatch") : _session.Page == MenuPage.Desktop ? Loc.T("Empty.Desktop") : Loc.T("Empty.Apps");
    }

    private void UpdateColumns()
    {
        HeaderBar.ColumnDefinitions[1].Width = new GridLength(ActualWidth < 650 ? 140 : 180);
        HeaderDetail.Visibility = ActualWidth < 650 ? Visibility.Collapsed : Visibility.Visible;
        GreetingRow.Visibility = ActualWidth < 600 ? Visibility.Collapsed : Visibility.Visible;
        UpdatePinnedShelf();
    }

    // The pinned row only appears on the desktop page, when something is pinned and the window is tall enough to spare it.
    private void UpdatePinnedShelf()
    {
        var height = ActualHeight > 0 ? ActualHeight : Height;
        PinnedShelf.Visibility = _session.Page == MenuPage.Desktop && PinnedItems.Items.Count > 0 && height >= 420 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static T? FindVisual<T>(DependencyObject element) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is T found) return found;
            if (FindVisual<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_changingSearch || _desktopView is null) return;
        _session.Searches[_session.Page] = SearchBox.Text;
        _session.Selections[_session.Page] = 0;
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_session.Page == MenuPage.Desktop) { _desktopFilter = SearchBox.Text; _desktopView.Refresh(); }
        else { _appsFilter = SearchBox.Text; _appsView.Refresh(); }
        RestoreSelection(); UpdateEmptyState();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_reorder is { Active: true } && e.Key == Key.Escape) { CancelReorder(); e.Handled = true; return; }
        if (!HasOverlay && _session.Page == MenuPage.Desktop && e.Key == Key.F5) { RefreshDesktop_Click(sender, e); e.Handled = true; return; }
        if (!HasOverlay && _session.Page == MenuPage.Desktop && (e.Key == Key.Apps || e.Key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) && DesktopList.SelectedItem is DesktopItem contextItem)
        { ShowDesktopItemMenu(contextItem, (FrameworkElement?)DesktopList.ItemContainerGenerator.ContainerFromItem(contextItem) ?? DesktopList); e.Handled = true; return; }
        if (e.Key == Key.Escape)
        {
            if (_desktopMenu?.IsOpen == true) { _desktopMenu.IsOpen = false; e.Handled = true; return; }
            if (HasOverlay) CloseOverlay(); else HideMenu();
            e.Handled = true; return;
        }
        if (_desktopMenu?.IsOpen == true || HasOverlay || _session.Page == MenuPage.Settings || e.Key == Key.ImeProcessed || Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        if (e.Key == Key.Tab)
        {
            _session.Cycle(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            SetPage(_session.Page); FocusStart(); e.Handled = true; return;
        }
        if (e.Key == Key.Enter) { ActivateSelected(); e.Handled = true; return; }
        var desktop = _session.Page == MenuPage.Desktop;
        var delta = e.Key switch { Key.Left => -1, Key.Right => 1, Key.Up => -1, Key.Down => 1, _ => 0 };
        if (delta != 0)
        {
            if (SearchBox.IsKeyboardFocusWithin && e.Key is Key.Left or Key.Right) return;
            var list = desktop ? DesktopList : AppsList;
            if (desktop) MoveVisualSelection(DesktopList, e.Key);
            // Running apps keep wrap-around up/down; left/right jump between columns when the list is split.
            else if (e.Key is Key.Up or Key.Down || !MoveVisualSelection(AppsList, e.Key)) list.SelectedIndex = MenuSession.MoveSelection(list.SelectedIndex, list.Items.Count, delta);
            if (list.SelectedItem is not null) { list.ScrollIntoView(list.SelectedItem); (list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as ListBoxItem)?.Focus(); }
            e.Handled = true;
        }
        else if (e.Key is >= Key.A and <= Key.Z || e.Key is >= Key.D0 and <= Key.D9 || e.Key == Key.Space)
        {
            if (!SearchBox.IsKeyboardFocusWithin) { SearchBox.Focus(); SearchBox.CaretIndex = SearchBox.Text.Length; }
        }
    }

    /// <summary>
    /// Where the keyboard goes when the menu opens or changes page: the search box, or with that turned off in
    /// settings the selected item, so a touch handheld does not pop up its on-screen keyboard. Typing still lands
    /// in the search box either way (see the key handlers above).
    /// </summary>
    private void FocusStart()
    {
        if (_settings.FocusSearchOnOpen || _session.Page == MenuPage.Settings) { SearchBox.Focus(); return; }
        var list = _session.Page == MenuPage.RunningApps ? AppsList : DesktopList;
        if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
        list.Focus();
        // Item containers exist only after layout; move onto the selected one then, unless the user went elsewhere.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (list.IsKeyboardFocusWithin && list.SelectedItem is { } item && list.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem row) row.Focus();
        });
    }

    private void ActivateSelected()
    {
        if (_session.Page == MenuPage.Desktop && DesktopList.SelectedItem is DesktopItem item)
        {
            if (_isQa) { SetStatus(Loc.T("Status.PreviewNoLaunch")); return; }
            var result = _desktopService.Open(item);
            if (result.Success) { var saved = _desktopPreferences.RecordLaunch(item.Id); if (!saved.Success) App.Log(new IOException(saved.Message)); _desktopView.Refresh(); HideMenu(); }
            else SetStatus(result.Message);
        }
        else if (_session.Page == MenuPage.RunningApps && AppsList.SelectedItem is RunningApp app) ActivateApp(app);
    }

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_desktopMenu?.IsOpen == true || HasOverlay || _session.Page == MenuPage.Settings || SearchBox.IsKeyboardFocusWithin || string.IsNullOrEmpty(e.Text)) return;
        var position = SearchBox.Text.Length;
        SearchBox.Focus();
        SearchBox.Text = SearchBox.Text.Insert(position, e.Text);
        SearchBox.CaretIndex = position + e.Text.Length;
        e.Handled = true;
    }

    private void ActivateApp(RunningApp app)
    {
        if (_isQa) return;
        if (app.Windows.Count > 1)
        {
            _pendingApp = app; _windowSelection = true;
            DialogTitle.Text = Loc.T("Dialog.PickWindow");
            DialogDescription.Text = app.Name;
            DialogDetail.Text = Loc.F("Dialog.WindowCount", app.Windows.Count);
            WindowPicker.Visibility = Visibility.Visible; WindowPicker.ItemsSource = app.Windows; WindowPicker.SelectedIndex = 0;
            DialogConfirm.Content = Loc.T("Dialog.SwitchWindow"); DialogConfirm.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
            ShowOverlay(); return;
        }
        var result = app.Windows.Count == 1 ? _windowService.Activate(app.Windows[0]) : _trayService.TryActivate(app);
        if (result.Success) HideMenu(); else SetStatus(result.Message);
    }

    private void Terminate_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is not RunningApp app || !app.CanTerminate) return;
        RequestTermination(app);
    }

    internal void HandleTermination(RunningApp app) { if (app.CanTerminate) RequestTermination(app); }

    private void RequestTermination(RunningApp app)
    {
        _pendingApp = app; _windowSelection = false;
        DialogTitle.Text = Loc.F("Dialog.EndAppTitle", app.Name);
        DialogDescription.Text = Loc.T("Dialog.UnsavedLost");
        DialogDetail.Text = Loc.F("Dialog.ProcessDetail", app.Name, app.Identity?.ProcessId) + (app.Windows.Count > 1 ? Loc.F("Dialog.WindowsClose", app.Windows.Count) : "");
        WindowPicker.Visibility = Visibility.Collapsed;
        DialogConfirm.Content = Loc.T("Template.EndProcess"); DialogConfirm.SetResourceReference(Control.BackgroundProperty, "DangerBrush");
        ShowOverlay();
    }

    private void ShowOverlay(Border? panel = null)
    {
        panel ??= DialogBorder;
        DialogBorder.Visibility = panel == DialogBorder ? Visibility.Visible : Visibility.Collapsed;
        CategoryEditor.Visibility = panel == CategoryEditor ? Visibility.Visible : Visibility.Collapsed;
        Overlay.Visibility = Visibility.Visible;
        DesktopNav.IsEnabled = AppsNav.IsEnabled = SettingsNav.IsEnabled = SearchHost.IsEnabled = DesktopList.IsEnabled = AppsList.IsEnabled = SettingsPanel.IsEnabled = false;
        PlayDialogEntrance(panel);
        if (panel == DialogBorder) DialogCancel.Focus();
    }

    // Motion is skipped in preview captures and when Windows animations are turned off.
    private bool AnimationsEnabled => !_isQa && SystemParameters.ClientAreaAnimation && RenderCapability.Tier > 0;

    private void PlayEntrance()
    {
        var shift = (TranslateTransform)ContentRoot.RenderTransform;
        if (!AnimationsEnabled) { ContentRoot.BeginAnimation(OpacityProperty, null); shift.BeginAnimation(TranslateTransform.YProperty, null); return; }
        var duration = TimeSpan.FromMilliseconds(120);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ContentRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, duration) { EasingFunction = ease });
    }

    private void PlayDialogEntrance(Border panel)
    {
        var scale = (ScaleTransform)panel.RenderTransform;
        if (!AnimationsEnabled) { Overlay.BeginAnimation(OpacityProperty, null); scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); return; }
        var duration = TimeSpan.FromMilliseconds(140);
        var grow = new DoubleAnimation(0.97, 1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    private bool _previewWorldClock = true;
    internal void PreviewWorldClock(bool shown) { _previewWorldClock = shown; GreetingText.Text = Greeting(); }

    private void CloseOverlay()
    {
        if (Overlay is null) return;
        Overlay.Visibility = Visibility.Collapsed;
        DesktopNav.IsEnabled = AppsNav.IsEnabled = SettingsNav.IsEnabled = SearchHost.IsEnabled = DesktopList.IsEnabled = AppsList.IsEnabled = SettingsPanel.IsEnabled = true;
        _pendingApp = null; _pendingProcesses = null; _editingBuiltIn = null; _editingCategory = null;
        FinishWelcome();
        DialogCancel.Content = Loc.T("Dialog.Cancel");
        FocusStart();
    }

    private async void DialogConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_welcome) { CloseOverlay(); SetPage(MenuPage.Settings); return; }
        if (_pendingProcesses is { } processes)
        {
            if (_isQa) { CloseOverlay(); return; }
            DialogConfirm.IsEnabled = false;
            var results = await Task.Run(() => processes.Select(_processService.Terminate).ToArray());
            DialogConfirm.IsEnabled = true;
            CloseOverlay();
            var failed = results.FirstOrDefault(result => !result.Success);
            SetStatus(failed is null ? (results.Length == 1 ? results[0].Message : Loc.F("Dialog.EndedMany", results.Length)) : failed.Message);
            return;
        }
        var app = _pendingApp;
        if (app is null) return;
        if (_windowSelection && WindowPicker.SelectedItem is WindowEntry window)
        {
            var result = _isQa ? new OperationResult(false, Loc.T("Status.Preview")) : _windowService.Activate(window);
            CloseOverlay(); if (result.Success) HideMenu(); else SetStatus(result.Message); return;
        }
        if (app.Identity is null || _isQa) { CloseOverlay(); return; }
        DialogConfirm.IsEnabled = false;
        var outcome = await Task.Run(() => _processService.Terminate(app.Identity));
        DialogConfirm.IsEnabled = true;
        CloseOverlay(); SetStatus(outcome.Message);
        await RefreshAppsAsync();
        SetStatus(outcome.Message);
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Tab or Key.Escape) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) { e.Handled = true; return; }
        var modifiers = Keyboard.Modifiers;
        if (!(modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Alt))) { SetStatus(Loc.T("Status.HotkeyNeedsModifier")); e.Handled = true; return; }
        _draftModifiers = (uint)modifiers;
        _draftKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        HotkeyBox.Text = FormatHotkey(_draftModifiers, _draftKey);
        e.Handled = true;
        CommitSettings();
    }

    private bool _loadingSettings;
    private static int PickerIndex(Character? character) => character is null ? 0 : 1 + Characters.All.ToList().IndexOf(character);

    /// <summary>
    /// With "follow the theme" on, picking a theme made from a character's colours picks her too, unless the chosen
    /// character already belongs to that theme (either DeepSeek娘 suits the deep-sea theme). The character can still
    /// be changed by hand afterwards; nothing is saved until "保存设置".
    /// </summary>
    private void FollowTheme()
    {
        if (_loadingSettings || FollowThemeCheck.IsChecked != true || ThemePicker.SelectedIndex < 0) return;
        var theme = ThemeChoices[ThemePicker.SelectedIndex];
        var chosen = CharacterPicker.SelectedIndex > 0 ? Characters.All[CharacterPicker.SelectedIndex - 1] : null;
        if (Characters.ForTheme(theme) is { } partner && chosen?.Theme != theme) CharacterPicker.SelectedIndex = PickerIndex(partner);
    }

    // ---- Game library (settings) ----------------------------------------------------------------------------------
    // Unlike the rest of the page these changes apply at once: they are lists edited item by item, and the menu
    // rescans so the counts beside each store stay true.

    private GameLibraryService GameLibrary => _desktopService.Games;
    private GameLibrarySettings? _previewGameLibrary;

    private GameLibrarySettings LoadGameLibrary() => _isQa ? _previewGameLibrary ??= new GameLibrarySettings { Folders = [@"D:\Games"], Hidden = ["steam://rungameid/0"] } : GameLibrary.Load();

    private void RenderGameLibrary()
    {
        var library = LoadGameLibrary();
        var counts = GameLibrary.Counts;
        GameSourcesPanel.Children.Clear();
        foreach (var (id, name) in GameLibraryService.Sources)
        {
            var count = id == "Folders" ? counts.GetValueOrDefault("Folders") + counts.GetValueOrDefault("Added") : counts.GetValueOrDefault(id);
            var toggle = new CheckBox { Content = library.IsEnabled(id) && count > 0 ? $"{name} · {count}" : name, IsChecked = library.IsEnabled(id), Margin = new Thickness(0, 0, 12, 10), Tag = id };
            System.Windows.Automation.AutomationProperties.SetName(toggle, Loc.F("Games.ReadSource", name));
            toggle.Click += (_, _) => UpdateGameLibrary(settings => settings.Sources[id] = toggle.IsChecked == true);
            GameSourcesPanel.Children.Add(toggle);
        }
        GameLibraryRows.Children.Clear();
        foreach (var folder in library.Folders) GameLibraryRows.Children.Add(GameLibraryRow(Loc.T("Games.Folder"), folder, Loc.T("Games.Remove"), () => UpdateGameLibrary(settings => settings.Folders.RemoveAll(path => string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)))));
        foreach (var game in library.Added) GameLibraryRows.Children.Add(GameLibraryRow(Loc.T("Games.Game"), game, Loc.T("Games.Remove"), () => UpdateGameLibrary(settings => settings.Added.RemoveAll(path => string.Equals(path, game, StringComparison.OrdinalIgnoreCase)))));
        foreach (var hidden in library.Hidden) GameLibraryRows.Children.Add(GameLibraryRow(Loc.T("Games.Hidden"), GameLibrary.NameOf(hidden) ?? hidden, Loc.T("Games.Restore"), () => UpdateGameLibrary(settings => settings.Hidden.RemoveAll(path => string.Equals(path, hidden, StringComparison.OrdinalIgnoreCase)))));
        GameLibraryRows.Visibility = GameLibraryRows.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var total = counts.Values.Sum() - library.Hidden.Count;
        GameLibrarySummary.Text = counts.Count == 0 ? Loc.T("Games.NotScanned") : Loc.F("Games.Found", Math.Max(0, total));
    }

    private FrameworkElement GameLibraryRow(string kind, string path, string action, Action click)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock { Text = kind, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var text = new TextBlock { Text = path, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = path };
        Grid.SetColumn(text, 1);
        var button = new Button { Content = action, Padding = new Thickness(12, 4, 12, 4), FontSize = 12, Margin = new Thickness(10, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(button, $"{action} {path}");
        button.Click += (_, _) => click();
        Grid.SetColumn(button, 2);
        row.Children.Add(label); row.Children.Add(text); row.Children.Add(button);
        return row;
    }

    private void UpdateGameLibrary(Action<GameLibrarySettings> change, string? done = null)
    {
        var library = LoadGameLibrary();
        change(library);
        if (!_isQa)
        {
            var saved = GameLibrary.Save(library);
            if (!saved.Success) { SetStatus(saved.Message); RenderGameLibrary(); return; }
        }
        RenderGameLibrary();
        _ = RescanGamesAsync(done);
    }

    private async Task RescanGamesAsync(string? done = null)
    {
        if (_isQa || _disposed) return;
        _desktopService.RefreshGames();
        SetStatus(Loc.T("Games.Scanning"));
        RescanGamesButton.IsEnabled = false;
        await WarmDesktopAsync();
        RescanGamesButton.IsEnabled = true;
        if (_session.Page == MenuPage.Settings) RenderGameLibrary();
        SetStatus(done ?? Loc.T("Games.Updated"));
    }

    private void RescanGames_Click(object sender, RoutedEventArgs e) => _ = RescanGamesAsync();

    private void AddGameFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Games.PickFolder") };
        if (dialog.ShowDialog(this) != true) return;
        var folder = dialog.FolderName;
        UpdateGameLibrary(settings => { if (!settings.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) settings.Folders.Add(folder); });
    }

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Loc.T("Games.PickGame"), Filter = Loc.T("Games.PickFilter"), DereferenceLinks = false };
        if (dialog.ShowDialog(this) != true) return;
        var game = dialog.FileName;
        UpdateGameLibrary(settings =>
        {
            settings.Hidden.RemoveAll(path => string.Equals(path, game, StringComparison.OrdinalIgnoreCase));
            if (!settings.Added.Contains(game, StringComparer.OrdinalIgnoreCase)) settings.Added.Add(game);
        });
    }

    private void HideGame(DesktopItem item)
    {
        if (_isQa) { SetStatus(Loc.T("Status.PreviewNoLibrary")); return; }
        UpdateGameLibrary(settings =>
        {
            // A game added by hand is simply taken out again; one found by a store or folder scan is remembered as hidden.
            if (settings.Added.RemoveAll(path => string.Equals(path, item.ParsingName, StringComparison.OrdinalIgnoreCase)) == 0 && !settings.Hidden.Contains(item.ParsingName, StringComparer.OrdinalIgnoreCase))
                settings.Hidden.Add(item.ParsingName);
        }, Loc.F("Games.HiddenStatus", item.Name));
    }

    internal static string FormatHotkey(uint modifiers, uint key)
    {
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add(Loc.T("Key.Ctrl"));
        if ((modifiers & 1) != 0) parts.Add(Loc.T("Key.Alt"));
        if ((modifiers & 4) != 0) parts.Add(Loc.T("Key.Shift"));
        if ((modifiers & 8) != 0) parts.Add(Loc.T("Key.Win"));
        var k = KeyInterop.KeyFromVirtualKey((int)key);
        parts.Add(k == Key.Space ? Loc.T("Key.Space") : k.ToString());
        return string.Join(" + ", parts);
    }

    /// <summary>The themes offered in settings, in the picker's order.</summary>
    internal static readonly ThemeMode[] ThemeChoices = { QuickMenu.Models.ThemeMode.System, QuickMenu.Models.ThemeMode.OledDay, QuickMenu.Models.ThemeMode.Oled, QuickMenu.Models.ThemeMode.Cream,
        QuickMenu.Models.ThemeMode.ChatGpt, QuickMenu.Models.ThemeMode.DeepSeek, QuickMenu.Models.ThemeMode.Gemini };

    /// <summary>Applies a theme; following the system shows OLED day or OLED night to match Windows.</summary>
    public void ApplyTheme(ThemeMode mode)
    {
        mode = ThemeService.Resolve(mode);
        var dark = ThemeService.IsDark(mode);
        IsDarkAppearance = dark;
        HostNative.ApplyFrame(this, dark);
        var colors = ThemePalette.Colors(mode);
        for (var i = 0; i < ThemePalette.Keys.Length; i++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i])); brush.Freeze();
            System.Windows.Application.Current.Resources[ThemePalette.Keys[i]] = brush;
        }
        var resources = System.Windows.Application.Current.Resources;
        resources["BackdropBrush"] = Backdrops.Window(mode) ?? resources["SurfaceBrush"];
        if (Backdrops.Action(mode) is { } action) resources["ActionBrush"] = action;
        _theme = mode;
        ApplyCompanion();
        RebuildAmbient();
        AppearanceChanged?.Invoke(dark);
    }

    private void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_settings.Theme == QuickMenu.Models.ThemeMode.System && !_disposed) Dispatcher.BeginInvoke(() => { if (!_disposed) ApplyTheme(QuickMenu.Models.ThemeMode.System); });
    }

    private void HideMenu() { if (_gamepad != null) _gamepad.Navigating = false; _ambient.Stop(); HideCompanion(); CancelReorder(); if (_desktopMenu != null) _desktopMenu.IsOpen = false; OfficePopup.IsOpen = false; CloseOverlay(); _refreshTimer.Stop(); _invocation?.Cancel(); Hide(); }
    private void SetStatus(string message) { if (StatusText is not null) { StatusText.Text = message; StatusText.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible; } }
    // Opening on a screen with another scale moves her window across it too, and WPF's DPI handling for that
    // move briefly takes activation from the menu. That is not the user clicking away, so it must not hide the menu;
    // InvokeMenu activates the menu again once she is in place.
    private bool _opening;
    private void Window_Deactivated(object sender, EventArgs e) { if (!_disposed && !_opening && !_isQa && !HasOverlay && _desktopMenu?.IsOpen != true && _session.Page != MenuPage.Settings) HideMenu(); }
    private void Window_Closing(object? sender, CancelEventArgs e) { if (!_disposed) { e.Cancel = true; HideMenu(); } }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (e.LeftButton != MouseButtonState.Pressed || HasOverlay || FindAncestor<Button>(source) is not null || FindAncestor<TextBox>(source) is not null) return;
        e.Handled = true;
        DragMove();
    }
    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (HasOverlay || (sender as FrameworkElement)?.Tag is not string tag) return;
        if (tag.StartsWith("custom:", StringComparison.Ordinal)) SelectCustomCategory(tag["custom:".Length..]);
        else if (Enum.TryParse<DesktopCategory>(tag, out var category)) SelectCategory(category);
        OfficePopup.IsOpen = false;
    }
    private int _officeHoverGeneration;
    private void Office_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _officeHoverGeneration++;
        if (_session.Page == MenuPage.Desktop && !HasOverlay) OfficePopup.IsOpen = true;
    }
    private async void Office_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var generation = ++_officeHoverGeneration;
        await Task.Delay(180);
        if (generation == _officeHoverGeneration && !OfficeCategory.IsMouseOver && !OfficeSubcategories.IsMouseOver) OfficePopup.IsOpen = false;
    }
    internal void SelectCategory(DesktopCategory category)
    {
        _session.DesktopCategory = category;
        _session.CustomCategory = null;
        _session.Selections[MenuPage.Desktop] = 0;
        _desktopView.Refresh(); UpdateCategoryButtons(); RestoreSelection(); UpdateEmptyState();
    }
    private void UpdateCategoryButtons()
    {
        CategoryMenuButton.Content = CategoryMenuLabel();
        var tag = SelectedCategoryTag;
        foreach (Button button in CategoryBar.Children.OfType<Button>().Concat(OfficeSubcategories.Children.OfType<Button>()))
        {
            var selected = button.Tag?.ToString() == tag || (button == OfficeCategory && _session.CustomCategory is null && _session.DesktopCategory is DesktopCategory.Documents or DesktopCategory.Spreadsheets or DesktopCategory.Presentations);
            button.SetValue(Selector.IsSelectedProperty, selected);
            button.SetResourceReference(Control.BackgroundProperty, selected ? "SelectedBrush" : "ChipBrush");
            button.SetResourceReference(Control.BorderBrushProperty, selected ? "AccentBrush" : "BorderBrush");
            button.SetResourceReference(Control.ForegroundProperty, selected ? "TextBrush" : "MutedBrush");
        }
    }
    private void DesktopNav_Click(object sender, RoutedEventArgs e) { SetPage(MenuPage.Desktop); FocusStart(); }
    private void UpdateSortLabel() => SortButton.Content = (_desktopPreferences.Sort switch { DesktopSort.Modified => Loc.T("Sort.Modified"), DesktopSort.Type => Loc.T("Sort.Type"), DesktopSort.Custom => Loc.T("Sort.Custom"), _ => Loc.T("Sort.Frequent") }) + "⌄";

    private ContextMenu NewDesktopMenu(FrameworkElement target)
    {
        if (_desktopMenu != null) _desktopMenu.IsOpen = false;
        OfficePopup.IsOpen = false;
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        menu.SetResourceReference(Control.BackgroundProperty, "PopupBrush");
        menu.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        menu.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        menu.Closed += (_, _) => { if (!_isQa && IsVisible && !IsActive && !HasOverlay && _session.Page != MenuPage.Settings) HideMenu(); };
        _desktopMenu = menu;
        return menu;
    }

    private static MenuItem MenuAction(string text, Action action, bool selected = false)
    {
        var entry = new MenuItem { Header = text, IsCheckable = true, IsChecked = selected };
        entry.Click += (_, _) => action();
        return entry;
    }

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (HasOverlay) return;
        var menu = NewDesktopMenu(SortButton);
        foreach (var (label, sort) in new[] { (Loc.T("SortMenu.Frequent"), DesktopSort.Frequent), (Loc.T("SortMenu.Modified"), DesktopSort.Modified), (Loc.T("SortMenu.Type"), DesktopSort.Type), (Loc.T("SortMenu.Custom"), DesktopSort.Custom) })
            menu.Items.Add(MenuAction(label, () => ApplyDesktopPreference(_desktopPreferences.SetSort(sort)), _desktopPreferences.Sort == sort));
        menu.IsOpen = true;
    }

    private void DesktopList_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (HasOverlay || FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not { DataContext: DesktopItem item } tile) return;
        DesktopList.SelectedItem = item;
        ShowDesktopItemMenu(item, tile);
        e.Handled = true;
    }

    internal void ShowDesktopItemMenu(DesktopItem item, FrameworkElement target)
    {
        var menu = NewDesktopMenu(target);
        var preference = _desktopPreferences.Get(item.Id);
        menu.Items.Add(MenuAction(preference.Pinned ? Loc.T("Item.Unpin") : Loc.T("Item.Pin"), () => ApplyDesktopPreference(_desktopPreferences.SetPinned(item.Id, !preference.Pinned)), preference.Pinned));
        var categories = new MenuItem { Header = Loc.T("Item.MarkCategory") };
        foreach (var (label, category) in new (string, DesktopCategory?)[] { (Loc.T("Item.Auto"), null), (Loc.T("Item.OfficeAll"), DesktopCategory.Office), (Loc.T("Office.Documents"), DesktopCategory.Documents), (Loc.T("Office.Spreadsheets"), DesktopCategory.Spreadsheets), (Loc.T("Office.Presentations"), DesktopCategory.Presentations), (Loc.T("Item.Media"), DesktopCategory.Media), (Loc.T("Item.Programs"), DesktopCategory.Programs), (Loc.T("Item.Games"), DesktopCategory.Games), (Loc.T("Item.Other"), DesktopCategory.Other) })
            categories.Items.Add(MenuAction(label, () => ApplyDesktopPreference(_desktopPreferences.SetCategory(item.Id, category)), preference.Category == category && (category != null || preference.Custom is null)));
        menu.Items.Add(categories);
        AddItemCategoryActions(menu, categories, item);
        if (item.GameSource != null)
        {
            var hide = new MenuItem { Header = Loc.T("Item.HideGame") };
            hide.Click += (_, _) => HideGame(item);
            menu.Items.Add(hide);
        }
        menu.Items.Add(new Separator());
        // Shortcuts are followed to their target, so a game or program opens in its install folder.
        var locate = new MenuItem { Header = Loc.T("Item.OpenLocation") };
        locate.Click += (_, _) =>
        {
            if (_isQa) { SetStatus(Loc.T("Status.PreviewNoExplorer")); return; }
            var result = FileLocationService.Open(item.Location ?? item.ParsingName);
            if (result.Success) HideMenu(); else SetStatus(result.Message);
        };
        menu.Items.Add(locate);
        // Enabled only while the program behind this item is running; matched by its full exe path.
        var running = RunningProcesses(item);
        var end = new MenuItem { Header = running.Count > 1 ? Loc.F("Item.EndProcessCount", running.Count) : Loc.T("Item.EndProcess"), IsEnabled = running.Count > 0, ToolTip = running.Count > 0 ? Loc.T("Item.EndProcessTip") : Loc.T("Item.NotRunning") };
        ToolTipService.SetShowOnDisabled(end, true);
        end.Click += (_, _) => RequestDesktopTermination(item, running);
        menu.Items.Add(end);
        menu.IsOpen = true;
    }

    private void ApplyDesktopPreference(OperationResult result)
    {
        if (!result.Success) { SetStatus(result.Message); return; }
        var id = (DesktopList.SelectedItem as DesktopItem)?.Id;
        _updatingItems = true;
        for (var index = 0; index < _desktop.Count; index++)
        {
            var item = _desktop[index]; var pinned = _desktopPreferences.Get(item.Id).Pinned;
            if (item.IsPinned != pinned || item.DisplayCategory != _desktopPreferences.Category(item)) _desktop[index] = item with { IsPinned = pinned, DisplayCategory = _desktopPreferences.Category(item) };
        }
        _desktopView.Refresh();
        DesktopList.SelectedItem = _desktopView.Cast<DesktopItem>().FirstOrDefault(item => item.Id == id);
        _updatingItems = false;
        if (DesktopList.SelectedIndex < 0 && DesktopList.Items.Count > 0) DesktopList.SelectedIndex = 0;
        _session.Selections[MenuPage.Desktop] = DesktopList.SelectedIndex;
        UpdateSortLabel(); UpdateEmptyState(); SetStatus("");
    }
    private void AppsNav_Click(object sender, RoutedEventArgs e) { SetPage(MenuPage.RunningApps); FocusStart(); }
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => OpenSettings();
    private void Hide_Click(object sender, RoutedEventArgs e) => HideMenu();
    private void DialogCancel_Click(object sender, RoutedEventArgs e) => CloseOverlay();
    private void DesktopList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_updatingItems && _session.Page == MenuPage.Desktop) _session.Selections[MenuPage.Desktop] = DesktopList.SelectedIndex; }
    private void AppsList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_updatingItems && _session.Page == MenuPage.RunningApps) _session.Selections[MenuPage.RunningApps] = AppsList.SelectedIndex; }
    private void DesktopList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { if (FinishReorder() || _suppressClick) { _suppressClick = false; e.Handled = true; return; } if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { DataContext: DesktopItem item }) { DesktopList.SelectedItem = item; ActivateSelected(); e.Handled = true; } }
    private void AppsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is null && FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not null) ActivateSelected(); }
    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject { while (element is not null) { if (element is T found) return found; element = element is Visual || element is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element); } return null; }

    public void DisposeServices()
    {
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= SystemThemeChanged;
        Loc.Changed -= OnLanguageChanged;
        _gamepad?.Dispose(); _gamepad = null;
        _refreshTimer.Stop(); _invocation?.Cancel(); _invocation?.Dispose(); _invocation = null; _hotkeys?.Dispose();
    }

    internal void SetPreviewData(IEnumerable<DesktopItem> desktop, IEnumerable<RunningApp> apps)
    {
        _updatingItems = true;
        foreach (var item in desktop) _desktop.Add(item);
        foreach (var app in apps) _apps.Add(app);
        _updatingItems = false; _desktopView.Refresh(); _appsView.Refresh(); RestoreSelection(); UpdateEmptyState();
    }
    internal void PreviewPage(MenuPage page) => SetPage(page);

    // Where the page runs on past the bottom, its last visible row fades out instead of being cut off flat at the
    // edge, so the bottom inset reads as open space like the other three sides.
    private const double PageFade = 18;
    private ScrollViewer? _pageViewer;

    private void PageHost_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer { IsVisible: true } viewer || viewer.ViewportHeight <= 0) return;
        _pageViewer = viewer;
        UpdatePageFade();
    }

    private void PageHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePageFade();

    private void UpdatePageFade()
    {
        var more = _pageViewer is { IsVisible: true } viewer && viewer.VerticalOffset + viewer.ViewportHeight < viewer.ExtentHeight - 1;
        var height = PageHost.ActualHeight;
        if (!more || height <= PageFade) { PageHost.OpacityMask = null; return; }
        if (PageHost.OpacityMask is LinearGradientBrush { EndPoint.Y: var end } && Math.Abs(end - height) < 0.5) return;
        var fade = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, height - PageFade), EndPoint = new Point(0, height) };
        fade.GradientStops.Add(new GradientStop(Colors.Black, 0));
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        fade.Freeze();
        PageHost.OpacityMask = fade;
    }
    internal void PreviewSearch(string text) => SearchBox.Text = text;
    internal void PreviewTermination(RunningApp app) => RequestTermination(app);
    internal void PreviewCancel() => CloseOverlay();
    internal void PreviewHide() => HideMenu();

    private void Pinned_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressClick) { _suppressClick = false; return; }
        if (HasOverlay || (sender as FrameworkElement)?.Tag is not DesktopItem item) return;
        DesktopList.SelectedItem = item; ActivateSelected();
    }

    private void Pinned_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (HasOverlay || sender is not FrameworkElement { Tag: DesktopItem item } target) return;
        DesktopList.SelectedItem = item; ShowDesktopItemMenu(item, target); e.Handled = true;
    }

    private void CategoryMenu_Click(object sender, RoutedEventArgs e)
    {
        if (HasOverlay) return;
        var menu = NewDesktopMenu(CategoryMenuButton);
        var office = new MenuItem { Header = Loc.T("Tab.Office") };
        foreach (var (name, category) in new[] { (Loc.T("CategoryMenu.OfficeAll"), DesktopCategory.Office), (Loc.T("Office.Documents"), DesktopCategory.Documents), (Loc.T("Office.Spreadsheets"), DesktopCategory.Spreadsheets), (Loc.T("Office.Presentations"), DesktopCategory.Presentations) })
            office.Items.Add(MenuAction(name, () => SelectCategory(category), _session.DesktopCategory == category));
        menu.Items.Add(office);
        foreach (var (name, category) in new[] { (Loc.T("Item.Media"), DesktopCategory.Media), (Loc.T("Item.Programs"), DesktopCategory.Programs), (Loc.T("Item.Other"), DesktopCategory.Other) })
            menu.Items.Add(MenuAction(name, () => SelectCategory(category), _session.CustomCategory is null && _session.DesktopCategory == category));
        AddCategoryMenuExtras(menu);
        menu.IsOpen = true;
    }

    // ---- Theme motion and Claude娘 ----

    internal AmbientScene Ambient => _ambient;
    internal CompanionWindow? Companion => _companion;
    internal bool CompanionShown => _companion is { IsVisible: true };
    private CompanionWindow? _companion;
    private bool _companionFits;

    private void RebuildAmbient()
    {
        if (_ambient is null) return;
        var running = _ambient.IsRunning;
        _ambient.Build(_theme, new Size(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height), GreetingSpark.Visibility == Visibility.Visible);
        if (running || (IsVisible && AnimationsEnabled)) _ambient.Start();
    }

    // OLED panels age where the same pixels stay lit. Each invocation moves the whole content by one pixel
    // around a 3×3 grid; the shift is too small to notice but spreads the wear.
    private static int _pixelShift;
    private void ShiftPixels()
    {
        if (!ThemePalette.IsOled(_theme) || _isQa) { ContentRoot.Margin = default; return; }
        var step = _pixelShift++ % 9;
        int dx = step % 3 - 1, dy = step / 3 - 1;
        ContentRoot.Margin = new Thickness(dx, dy, -dx, -dy);
    }

    private const double BaseWidth = 820;
    // A little taller than the 600-DIP menu, so her head rises past its top edge.
    private const double FigureHeight = 650;

    /// <summary>The character chosen in settings, if her pictures are there. Self-check captures keep the menu alone unless a check asks for her.</summary>
    private Character? CurrentCharacter => Characters.Find(_isQa ? _previewCompanion : _settings.Character) is { Poses.Count: > 0 } character ? character : null;
    private bool CompanionWanted => CurrentCharacter is not null;
    private string? _previewCompanion;
    internal void PreviewCompanion(string? id) { _previewCompanion = id; ApplyCompanion(); ShowCompanion(); }
    // Explicit opt-in for CPU measurements; normal QA screenshots keep animation stopped.
    internal void PreviewMotion() { _ambient.Start(); if (CompanionShown) _companion!.StartMotion(); }
    private Character? _posed;
    private int _pose = -1;
    private readonly Random _poseDice = new();
    internal CharacterPose? CurrentPose => _posed is { } character && _pose >= 0 ? character.Poses[_pose] : null;

    /// <summary>
    /// Another pose for each invocation. A character with several pose groups goes through the groups in turn (the
    /// rice-bowl DeepSeek娘 stands, then sits and eats); within a group the pose is random and never the one shown
    /// last time.
    /// </summary>
    internal void ChangePose()
    {
        if (CurrentCharacter is not { } character) return;
        var poses = character.Poses;
        if (_posed != character || _pose < 0) _pose = _poseDice.Next(poses.Count);
        else
        {
            var groups = poses.Select(pose => pose.Group).Distinct().Order().ToArray();
            var group = groups.Length == 1 ? groups[0] : groups[(Array.IndexOf(groups, poses[_pose].Group) + 1) % groups.Length];
            var choices = Enumerable.Range(0, poses.Count).Where(i => poses[i].Group == group && i != _pose).ToArray();
            if (choices.Length > 0) _pose = choices[_poseDice.Next(choices.Length)];
        }
        _posed = character;
        ApplyCompanion();
    }

    /// <summary>Puts the character's avatar by the greeting and dresses her for the theme, or puts her away when none is chosen.</summary>
    private void ApplyCompanion()
    {
        var character = CurrentCharacter;
        var avatar = character?.Avatar;
        GreetingAvatar.Source = avatar;
        GreetingAvatar.Visibility = avatar is null ? Visibility.Collapsed : Visibility.Visible;
        GreetingSpark.Visibility = avatar is null ? Visibility.Visible : Visibility.Hidden;
        if (character is null) { HideCompanion(); return; }
        if (_posed != character || _pose < 0) { _posed = character; _pose = _poseDice.Next(character.Poses.Count); }
        _companion ??= new CompanionWindow();
        var previousFootprint = _companion.Footprint;
        var night = _theme is QuickMenu.Models.ThemeMode.Oled or QuickMenu.Models.ThemeMode.Gemini;
        var glint = (Color)ColorConverter.ConvertFromString(_theme switch
        {
            QuickMenu.Models.ThemeMode.Oled => "#FFE6D6", QuickMenu.Models.ThemeMode.ChatGpt => "#9C92D0",
            QuickMenu.Models.ThemeMode.DeepSeek => "#4D6BFE", QuickMenu.Models.ThemeMode.Gemini => "#C9B8FF", _ => "#E87A3C"
        });
        var pose = character.Poses[_pose];
        _companion.Present(character.Name, pose, FigureHeight * character.HeightScale * pose.Scale, glint, night ? (byte)0x70 : (byte)0x38, 2, character.Glint);
        // Saving a wider character while the menu is open must recheck the available screen space too.
        if (IsVisible && !_isQa && previousFootprint != _companion.Footprint) PlaceWindow();
        if (IsVisible) ShowCompanion();
    }

    private void ShowCompanion()
    {
        if (_companion is null || !CompanionWanted || !(_companionFits || _isQa)) { HideCompanion(); return; }
        // A window can only be owned by one that has been shown; owned, she stays above the menu.
        _companion.Owner ??= this;
        _companion.PlaceBeside(this);
        var appearing = !_companion.IsVisible;
        _companion.Show();
        if (!AnimationsEnabled) return;
        if (appearing) _companion.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        _companion.StartMotion();
    }

    private void HideCompanion()
    {
        if (_companion is null) return;
        _companion.StopMotion();
        _companion.Hide();
    }

    // She stands to the menu's right. The menu stays centred when that side of the screen has room for her; on a
    // small screen the menu moves left for her and she may be drawn smaller. Only when even that fails she stays away.
    private void PlaceWindow()
    {
        var size = HostNative.CenterOnPointerMonitor(this, BaseWidth, CompanionWanted && _companion is not null ? _companion.Footprint : default);
        _companionFits = size > 0;
        if (_companionFits) _companion!.Scale = size;
    }

    // Long-press reordering: hold an item for a moment, then drag it. The list rearranges live while dragging;
    // releasing saves the arrangement as the custom sort, Esc or losing the mouse puts everything back.
    private sealed class Reorder(ItemsControl host, DesktopItem item, Point start)
    {
        public ItemsControl Host { get; } = host;
        public DesktopItem Item { get; } = item;
        public Point Start { get; } = start;
        public bool Active { get; set; }
        public List<string> Order { get; set; } = new();
        public List<string> Original { get; set; } = new();
        public System.Collections.IComparer? Saved { get; set; }
        public int Compare(DesktopItem a, DesktopItem b)
        {
            var result = b.IsPinned.CompareTo(a.IsPinned);
            return result != 0 ? result : Order.IndexOf(a.Id).CompareTo(Order.IndexOf(b.Id));
        }
    }
    private sealed class ReorderComparer(Reorder drag) : System.Collections.IComparer
    {
        public int Compare(object? x, object? y) => x is DesktopItem a && y is DesktopItem b ? drag.Compare(a, b) : 0;
    }
    private Reorder? _reorder;
    private bool _suppressClick;
    private readonly DispatcherTimer _holdTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private static DesktopItem? ReorderItem(DependencyObject? source) => FindAncestor<ListBoxItem>(source)?.DataContext as DesktopItem ?? FindAncestor<Button>(source)?.Tag as DesktopItem;

    private void Reorder_MouseDown(object sender, MouseButtonEventArgs e)
    {
        CancelReorder();
        _suppressClick = false;
        if (HasOverlay || sender is not ItemsControl host || ReorderItem(e.OriginalSource as DependencyObject) is not { } item) return;
        _reorder = new Reorder(host, item, e.GetPosition(this));
        _holdTimer.Start();
    }

    private void BeginReorder()
    {
        _holdTimer.Stop();
        if (_reorder is not { Active: false } drag || Mouse.LeftButton != MouseButtonState.Pressed || HasOverlay) { _reorder = null; return; }
        var current = _desktop.ToList();
        current.Sort((a, b) => _desktopPreferences.Compare(a, b));
        drag.Order = current.Select(item => item.Id).ToList();
        drag.Original = drag.Order.ToList();
        drag.Saved = ((ListCollectionView)_desktopView).CustomSort;
        // Taking capture makes the pressed button underneath lose it; that must not cancel the drag being started.
        if (!drag.Host.CaptureMouse()) { _reorder = null; return; }
        drag.Active = true;
        Mouse.OverrideCursor = System.Windows.Input.Cursors.SizeAll;
        ShowReorder(drag);
        SetStatus(Loc.T("Status.ReorderHint"));
    }

    private void Reorder_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_reorder is not { } drag) return;
        if (!drag.Active)
        {
            // Moving before the hold completes is neither a drag nor a click, so releasing it opens nothing.
            var moved = e.GetPosition(this) - drag.Start;
            if (Math.Abs(moved.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(moved.Y) > SystemParameters.MinimumVerticalDragDistance) { _holdTimer.Stop(); _reorder = null; _suppressClick = true; }
            return;
        }
        e.Handled = true;
        var point = e.GetPosition(drag.Host);
        AutoScroll(drag.Host, point);
        if (ReorderItem(drag.Host.InputHitTest(point) as DependencyObject) is not { } target || target.Id == drag.Item.Id) return;
        // Items stay in their own column, and pinned items stay ahead of the rest.
        if (target.IsPinned != drag.Item.IsPinned) return;
        if (drag.Host == DesktopList && !Equals(GroupName(target), GroupName(drag.Item))) return;
        var from = drag.Order.IndexOf(drag.Item.Id);
        var to = drag.Order.IndexOf(target.Id);
        if (from < 0 || to < 0) return;
        drag.Order.RemoveAt(from);
        drag.Order.Insert(to, drag.Item.Id);
        ShowReorder(drag);
    }

    private object GroupName(DesktopItem item) => _desktopGroups.GroupNameFromItem(item, 0, CultureInfo.CurrentCulture);

    private void AutoScroll(ItemsControl host, Point point)
    {
        if (host == DesktopList && FindVisual<ScrollViewer>(DesktopList) is { } list)
        {
            if (point.Y < 36) list.LineUp(); else if (point.Y > host.ActualHeight - 36) list.LineDown();
        }
        else if (host == PinnedItems && FindAncestor<ScrollViewer>(VisualTreeHelper.GetParent(PinnedItems)) is { } shelf)
        {
            var x = PinnedItems.TranslatePoint(point, shelf).X;
            if (x < 36) shelf.LineLeft(); else if (x > shelf.ActualWidth - 36) shelf.LineRight();
        }
    }

    // Applies the in-progress order to both the list and the pinned row, and dims the item being moved.
    private void ShowReorder(Reorder drag)
    {
        ((ListCollectionView)_desktopView).CustomSort = new ReorderComparer(drag);
        if (drag.Host == DesktopList) DesktopList.SelectedItem = _desktopView.Cast<DesktopItem>().FirstOrDefault(item => item.Id == drag.Item.Id);
        UpdateEmptyState();
        DesktopList.UpdateLayout(); PinnedItems.UpdateLayout();
        SetReorderFade(drag, 0.45);
    }

    private void SetReorderFade(Reorder drag, double opacity)
    {
        var item = _desktop.FirstOrDefault(entry => entry.Id == drag.Item.Id) ?? drag.Item;
        if (DesktopList.ItemContainerGenerator.ContainerFromItem(item) is UIElement tile) tile.Opacity = opacity;
        if (PinnedItems.ItemContainerGenerator.ContainerFromItem(item) is UIElement pin) pin.Opacity = opacity;
    }

    private void EndReorder(Reorder drag)
    {
        Mouse.OverrideCursor = null;
        SetReorderFade(drag, 1);
        if (drag.Host.IsMouseCaptured) drag.Host.ReleaseMouseCapture();
    }

    /// <summary>Saves a drag that was in progress; returns false when the press was an ordinary click.</summary>
    private bool FinishReorder()
    {
        _holdTimer.Stop();
        var drag = _reorder;
        _reorder = null;
        if (drag is not { Active: true }) return false;
        EndReorder(drag);
        if (drag.Order.SequenceEqual(drag.Original))
        {
            // Nothing moved (e.g. dropped on another column): keep the current sort instead of switching to custom.
            ((ListCollectionView)_desktopView).CustomSort = drag.Saved ?? _desktopPreferences;
            UpdateEmptyState(); SetStatus("");
            return true;
        }
        ((ListCollectionView)_desktopView).CustomSort = _desktopPreferences;
        var result = _desktopPreferences.SetOrder(drag.Order);
        ApplyDesktopPreference(result);
        if (result.Success) SetStatus(Loc.T("Status.ReorderSaved"));
        return true;
    }

    private void CancelReorder()
    {
        _holdTimer.Stop();
        var drag = _reorder;
        _reorder = null;
        if (drag is not { Active: true }) return;
        // The button is usually still held after Esc; releasing it must not open the item underneath.
        _suppressClick = Mouse.LeftButton == MouseButtonState.Pressed;
        EndReorder(drag);
        ((ListCollectionView)_desktopView).CustomSort = drag.Saved ?? _desktopPreferences;
        UpdateEmptyState();
        SetStatus(Loc.T("Status.ReorderCancelled"));
    }

    // LostMouseCapture bubbles from children; only the host itself losing capture ends a drag.
    private void Reorder_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e) { if (_reorder is { Active: true } drag && drag.Host == sender && e.OriginalSource == drag.Host && !drag.Host.IsMouseCaptured) CancelReorder(); }
    private void Pinned_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { if (FinishReorder()) e.Handled = true; }

    private IReadOnlyList<ProcessIdentity> RunningProcesses(DesktopItem item)
    {
        try
        {
            var path = item.Location ?? item.ParsingName;
            var target = Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? path : FileLocationService.ResolveTarget(path);
            return target != null && Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? _processService.FindByImage(target) : Array.Empty<ProcessIdentity>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        { return Array.Empty<ProcessIdentity>(); }
    }

    private void RequestDesktopTermination(DesktopItem item, IReadOnlyList<ProcessIdentity> processes)
    {
        if (processes.Count == 0) return;
        _pendingApp = null; _windowSelection = false; _pendingProcesses = processes;
        DialogTitle.Text = Loc.F("Dialog.EndItemTitle", item.Name);
        DialogDescription.Text = Loc.T("Dialog.UnsavedLost");
        DialogDetail.Text = processes.Count == 1
            ? Loc.F("Dialog.ProcessDetail", item.Name, processes[0].ProcessId)
            : Loc.F("Dialog.ProcessesDetail", item.Name, processes.Count, string.Join(Loc.T("Category.ListSeparator"), processes.Select(process => process.ProcessId)));
        WindowPicker.Visibility = Visibility.Collapsed;
        DialogConfirm.Content = Loc.T("Template.EndProcess"); DialogConfirm.SetResourceReference(Control.BackgroundProperty, "DangerBrush");
        ShowOverlay();
    }

    // Choose the closest item in the requested visual direction, across sub-columns and groups.
    private bool MoveVisualSelection(ListBox list, Key key)
    {
        list.UpdateLayout();
        if (list.SelectedItem is null) { if (list.Items.Count > 0) list.SelectedIndex = 0; return list.Items.Count > 0; }
        var current = list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as ListBoxItem;
        if (current is null) return false;
        var origin = current.TranslatePoint(new Point(current.ActualWidth / 2, current.ActualHeight / 2), list);
        var candidates = list.Items.Cast<object>().Select(item => (Item: item, Container: list.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem))
            .Where(pair => pair.Container is not null && pair.Container != current)
            .Select(pair => { var box = pair.Container!; var point = box.TranslatePoint(new Point(box.ActualWidth / 2, box.ActualHeight / 2), list); return (pair.Item, X: point.X - origin.X, Y: point.Y - origin.Y); });
        var horizontal = key is Key.Left or Key.Right;
        var sign = key is Key.Left or Key.Up ? -1 : 1;
        var next = candidates.Where(pair => (horizontal ? pair.X : pair.Y) * sign > 2)
            .OrderBy(pair => Math.Abs(horizontal ? pair.Y : pair.X) * 1000 + Math.Abs(horizontal ? pair.X : pair.Y)).FirstOrDefault();
        if (next.Item is null) return false;
        list.SelectedItem = next.Item;
        return true;
    }

    private sealed class DesktopGroup(Func<DesktopItem, string> name) : GroupDescription
    {
        public override object GroupNameFromItem(object item, int level, CultureInfo culture) => name((DesktopItem)item);
    }

    private sealed class AppGroup : GroupDescription
    {
        public override object GroupNameFromItem(object item, int level, CultureInfo culture) => ((RunningApp)item).IsTray ? Loc.T("Group.Tray") : Loc.T("Group.Windows");
    }
}
