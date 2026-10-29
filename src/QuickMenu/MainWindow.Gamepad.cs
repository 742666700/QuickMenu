using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using QuickMenu.Models;
using QuickMenu.Services;

namespace QuickMenu;

/// <summary>
/// Controller input for Windows handhelds and gamepads. Menu + View opens and closes the menu from anywhere. While it
/// is shown: the D-pad or left stick move the selection (or through a right-click menu, a list that is open, a dialog
/// or the settings page), A confirms, B opens the selected item's right-click menu (and closes a menu, list or dialog
/// that is open), LB / RB switch between 桌面 and 运行应用, LT / RT step through the category tabs (or the settings
/// sections), Y opens or leaves settings.
/// </summary>
public partial class MainWindow
{
    private GamepadService? _gamepad;

    private void StartGamepad()
    {
        if (_isQa || _gamepad != null) return;
        _gamepad = new GamepadService { Enabled = _settings.Gamepad };
        _gamepad.Toggle += () => Dispatcher.BeginInvoke(GamepadToggle);
        _gamepad.Pressed += button => Dispatcher.BeginInvoke(() => OnGamepad(button));
        _gamepad.Start();
    }

    private void GamepadToggle()
    {
        if (_disposed) return;
        if (!IsVisible) { InvokeMenu(); return; }
        if (_desktopMenu?.IsOpen == true) _desktopMenu.IsOpen = false;
        HideMenu();
    }

    internal void OnGamepad(GamepadButton button)
    {
        if (_disposed || !IsVisible) return;
        _companion?.NoteActivity();
        var popup = OpenPopupFocus();
        switch (button)
        {
            case GamepadButton.Up or GamepadButton.Down or GamepadButton.Left or GamepadButton.Right:
                var key = button switch { GamepadButton.Up => Key.Up, GamepadButton.Down => Key.Down, GamepadButton.Left => Key.Left, _ => Key.Right };
                // Lists of items and open menus take arrow keys; a dialog or the settings page moves focus instead.
                if (popup != null || (!HasOverlay && _session.Page != MenuPage.Settings)) SendKey(popup ?? this, key);
                else MoveGamepadFocus(button);
                break;
            case GamepadButton.A:
                var target = popup ?? Keyboard.FocusedElement as IInputElement ?? this;
                if (target is ComboBox { IsDropDownOpen: false } combo) combo.IsDropDownOpen = true;
                else SendKey(target, target is ToggleButton ? Key.Space : Key.Enter);
                break;
            case GamepadButton.B:
                if (popup != null) SendKey(popup, Key.Escape);
                else if (HasOverlay) CloseOverlay();
                else if (_session.Page == MenuPage.Settings) SetPage(MenuPage.Desktop);
                else if (_session.Page == MenuPage.Desktop && DesktopList.SelectedItem is DesktopItem item)
                {
                    ShowDesktopItemMenu(item, (FrameworkElement?)DesktopList.ItemContainerGenerator.ContainerFromItem(item) ?? DesktopList);
                    // Start on the first entry, so the D-pad moves from there.
                    Dispatcher.BeginInvoke(() => { if (_desktopMenu is { IsOpen: true } menu && menu.Items.OfType<MenuItem>().FirstOrDefault() is { } first) first.Focus(); }, System.Windows.Threading.DispatcherPriority.Input);
                }
                break;
            case GamepadButton.LeftShoulder or GamepadButton.RightShoulder when popup is null && !HasOverlay:
                if (_session.Page == MenuPage.Settings) SetPage(MenuPage.Desktop);
                else { _session.Cycle(); SetPage(_session.Page); }
                break;
            case GamepadButton.LeftTrigger or GamepadButton.RightTrigger when popup is null && !HasOverlay:
                var step = button == GamepadButton.RightTrigger ? 1 : -1;
                if (_session.Page == MenuPage.Desktop) StepCategoryTab(step);
                else if (_session.Page == MenuPage.Settings) StepSettingsSection(step);
                break;
            case GamepadButton.Y when popup is null && !HasOverlay:
                SetPage(_session.Page == MenuPage.Settings ? MenuPage.Desktop : MenuPage.Settings);
                break;
        }
    }

    // The tabs shown along the top of 桌面, in order (分类⌄ opens a menu, so it is skipped); wraps at either end. A
    // category picked from a menu counts as the tab it sits under: 文档+ for its three, otherwise 全部.
    internal void StepCategoryTab(int step)
    {
        OfficePopup.IsOpen = false;
        var tabs = CategoryBar.Children.OfType<Button>().Where(tab => tab.Tag is string && tab.Visibility == Visibility.Visible).ToArray();
        var current = Array.FindIndex(tabs, tab => (bool)tab.GetValue(System.Windows.Controls.Primitives.Selector.IsSelectedProperty));
        Category_Click(tabs[((Math.Max(current, 0) + step) % tabs.Length + tabs.Length) % tabs.Length], new RoutedEventArgs());
    }

    private void StepSettingsSection(int step)
    {
        var sections = SettingsSections.Children.OfType<Button>().ToArray();
        var current = Array.FindIndex(sections, section => (bool)section.GetValue(System.Windows.Controls.Primitives.Selector.IsSelectedProperty));
        ScrollSettingsTo((string)sections[Math.Clamp(Math.Max(current, 0) + step, 0, sections.Length - 1)].Tag);
    }

    // What an open right-click menu or drop-down list has focused, or the menu / list itself.
    private IInputElement? OpenPopupFocus()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (_desktopMenu is { IsOpen: true } menu) return focused is MenuItem item && ItemsControl.ItemsControlFromItemContainer(item) is { } owner && (owner == menu || owner is MenuItem) ? item : menu;
        if (focused is ComboBoxItem option) return option;
        foreach (var combo in new[] { LanguagePicker, ThemePicker, CharacterPicker, SecondClockPicker })
            if (combo.IsDropDownOpen) return combo;
        return null;
    }

    // In a dialog or the settings page the D-pad moves between controls; the first press lands on the first one.
    private void MoveGamepadFocus(GamepadButton button)
    {
        var scope = HasOverlay ? (FrameworkElement)(CategoryEditor.IsVisible ? CategoryEditor : DialogBorder) : SettingsPanel;
        if (Keyboard.FocusedElement is not DependencyObject focused || !IsInside(focused, scope))
        {
            (HasOverlay ? (CategoryEditor.IsVisible ? CategoryNameBox.IsVisible ? CategoryNameBox : (UIElement)CategoryKeywordsBox : DialogCancel) : LanguagePicker).Focus();
            return;
        }
        var direction = button switch { GamepadButton.Up => FocusNavigationDirection.Up, GamepadButton.Down => FocusNavigationDirection.Down, GamepadButton.Left => FocusNavigationDirection.Left, _ => FocusNavigationDirection.Right };
        if (focused is UIElement element && !element.MoveFocus(new TraversalRequest(direction)))
            element.MoveFocus(new TraversalRequest(button is GamepadButton.Up or GamepadButton.Left ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next));
    }

    private static bool IsInside(DependencyObject child, DependencyObject parent)
    {
        for (var node = child; node != null; node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node))
            if (node == parent) return true;
        return false;
    }

    // A key press delivered straight to the element, as if typed: works whether or not Windows let the menu take the
    // keyboard, and includes the controls' own handling (menus, lists, buttons).
    private void SendKey(IInputElement target, Key key)
    {
        if (PresentationSource.FromDependencyObject(target as DependencyObject ?? this) is not { } source) return;
        var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(preview);
        if (!preview.Handled) target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent });
    }
}
