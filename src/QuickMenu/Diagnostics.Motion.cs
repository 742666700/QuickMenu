using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using QuickMenu.Models;

namespace QuickMenu;

internal static partial class Diagnostics
{
    private static async Task InspectMotion(MainWindow window, Dictionary<string, bool> checks, string output, DesktopItem[] items)
    {
        window.PreviewPage(MenuPage.Desktop);
        window.SelectCategory(DesktopCategory.All);
        if (!window.PinnedItems.Items.Cast<DesktopItem>().Any(item => item.Id == items[5].Id))
        {
            window.ShowDesktopItemMenu(items[5], window.DesktopList);
            ((MenuItem)window.DesktopMenu!.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            window.DesktopMenu.IsOpen = false;
        }
        window.UpdateLayout();
        var current = window.DesktopList.Items.Cast<DesktopItem>().Single(item => item.Id == items[5].Id);
        var tile = (ListBoxItem)window.DesktopList.ItemContainerGenerator.ContainerFromItem(current);
        var pin = (FrameworkElement)window.PinnedItems.ItemContainerGenerator.ContainerFromItem(current);
        var original = new TranslateTransform(2, 1);
        tile.RenderTransform = original;
        var origin = tile.RenderTransformOrigin;
        window.PreviewAnimations(true);
        window.PreviewReorderFeedback(items[5], true);
        var lifted = tile.RenderTransform is TransformGroup group && ReferenceEquals(group.Children[0], original)
            && group.Children[1] is ScaleTransform { ScaleX: 1.03 } && pin.Opacity == 0.45;
        window.PreviewReorderFeedback(items[5], false);
        checks["M0: reorder lift preserves and restores container transforms"] = lifted && ReferenceEquals(tile.RenderTransform, original)
            && tile.RenderTransformOrigin == origin && tile.Opacity == 1 && pin.Opacity == 1;
        tile.ClearValue(UIElement.RenderTransformProperty);

        // Exercise the actual template setters, including switches whose resting values are not zero.
        window.PreviewPage(MenuPage.Settings);
        window.ScrollSettingsTo("SectionGeneral");
        window.SearchFocusCheck.IsChecked = false;
        window.SearchFocusCheck.IsChecked = true;
        var thumb = (FrameworkElement)window.SearchFocusCheck.Template.FindName("Thumb", window.SearchFocusCheck);
        await Task.Delay(45);
        checks["M0: enabled control feedback creates a finite animation"] = thumb.RenderTransform.HasAnimatedProperties;
        window.PreviewAnimations(false);
        checks["M0: disabling motion settles control feedback immediately"] = thumb.RenderTransform is TranslateTransform { X: 16, HasAnimatedProperties: false };
        window.SearchFocusCheck.IsChecked = false;
        checks["M0: disabled switches reach their unchecked resting value"] = thumb.RenderTransform is TranslateTransform { X: 0, HasAnimatedProperties: false };
        window.SearchFocusCheck.IsChecked = true;
        window.ScrollSettingsTo("SectionAbout");
        window.UpdateLayout();
        checks["M0: disabled settings anchor jumps directly to its destination"] = !window.HasAnimatedProperties && window.SettingsPanel.VerticalOffset > 0;

        window.PreviewAnimations(true);
        window.ScrollSettingsTo("SectionGeneral");
        await Task.Delay(70);
        var moving = window.HasAnimatedProperties;
        var bar = Descendants(window.SettingsPanel).OfType<ScrollBar>().First(s => s.Orientation == Orientation.Vertical && s.IsVisible);
        var grip = Descendants(bar).OfType<Thumb>().Single();
        var down = new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent };
        grip.RaiseEvent(down);
        window.SettingsPanel.ScrollToVerticalOffset(123);
        window.SettingsPanel.UpdateLayout();
        var manual = window.SettingsPanel.VerticalOffset;
        await Task.Delay(250);
        checks["M0: scrollbar takes over from anchor scrolling without a late snap"] = moving && !down.Handled && !window.HasAnimatedProperties
            && Math.Abs(window.SettingsPanel.VerticalOffset - manual) < 1 && Math.Abs(manual - 123) < 1;
        File.WriteAllText(Path.Combine(output, "motion-scroll.txt"), $"moving={moving}; handled={down.Handled}; windowClock={window.HasAnimatedProperties}; manual={manual}; final={window.SettingsPanel.VerticalOffset}");

        window.ScrollSettingsTo("SectionAbout");
        await Task.Delay(35);
        window.ScrollSettingsTo("SectionGeneral");
        await Task.Delay(260);
        window.UpdateLayout();
        checks["M0: rapid settings anchors finish at the latest destination"] = !window.HasAnimatedProperties && window.SettingsPanel.VerticalOffset < 1;
        window.ScrollSettingsTo("SectionAbout");
        window.PreviewPage(MenuPage.Desktop);
        checks["M0: leaving settings cancels its scroll clock"] = !window.HasAnimatedProperties;
        await Task.Delay(180);

        window.PreviewCompanion("deepseek-rice");
        window.InvokeMenu();
        window.PreviewStatus("motion self-check");
        window.PreviewPage(MenuPage.Settings);
        window.ScrollSettingsTo("SectionAbout");
        window.SearchFocusCheck.IsChecked = false;
        await Task.Delay(25);
        window.PreviewHide();
        checks["M0: hiding mid-animation removes menu and companion motion"] = !window.Ambient.IsRunning && window.Companion is { HasMotion: false }
            && !HasAnimation(window) && !window.HasAnimatedProperties;
        File.WriteAllLines(Path.Combine(output, "motion-hidden.txt"), new[] { $"ambient={window.Ambient.IsRunning}; companion={window.Companion?.HasMotion}; window={window.HasAnimatedProperties}" }
            .Concat(Descendants(window).OfType<UIElement>().Where(element => element.HasAnimatedProperties || element.RenderTransform.HasAnimatedProperties || element.RenderTransform is TransformGroup group && group.Children.Any(t => t.HasAnimatedProperties))
                .Select(element => $"{element.GetType().Name}/{(element as FrameworkElement)?.Name}: element={element.HasAnimatedProperties}; transform={element.RenderTransform.HasAnimatedProperties}")));
        window.PreviewAnimations(false);
        window.InvokeMenu();
        window.PreviewStatus("motion self-check");
        window.PreviewPage(MenuPage.RunningApps);
        window.PreviewPage(MenuPage.Desktop);
        window.SelectCategory(DesktopCategory.Media);
        window.SelectCategory(DesktopCategory.All);
        window.PreviewTermination(new RunningApp("qa:motion", "motion", null, null, Array.Empty<WindowEntry>(), false, true));
        checks["M0: disabled entrance, page, category, status and dialog feedback rests immediately"] = !HasAnimation(window)
            && window.ContentRoot.Opacity == 1 && window.ContentScale.ScaleX == 1 && window.ContentShift.Y == 0
            && window.PageHost.Opacity == 1 && window.PageShift.X == 0 && window.PageShift.Y == 0
            && window.StatusText.Opacity == 1 && window.StatusShift.Y == 0 && window.Overlay.Opacity == 1
            && window.Companion is { HasMotion: false, Opacity: 1 };
        window.PreviewCancel();
        window.PreviewStatus("");
        window.PreviewCompanion(null);
        Capture(window, Path.Combine(output, "motion-disabled-desktop.png"), 1);
        window.PreviewPage(MenuPage.Settings);
        window.ScrollSettingsTo("SectionGeneral");
        Capture(window, Path.Combine(output, "motion-disabled-settings.png"), 1);
        window.PreviewPage(MenuPage.Desktop);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    // WPF's internal caret blinks independently of the app's motion policy; inspect the controls and template parts we own.
    private static bool HasAnimation(DependencyObject root) => Descendants(root).OfType<FrameworkElement>().Any(element =>
        element.HasAnimatedProperties || element.RenderTransform.HasAnimatedProperties || element.RenderTransform is TransformGroup group && group.Children.Any(t => t.HasAnimatedProperties));
}
