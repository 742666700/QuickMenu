namespace QuickMenu.Models;

public enum MenuPage { Desktop, RunningApps, Settings }

public sealed class MenuSession
{
    public MenuPage Page { get; private set; }
    public DesktopCategory DesktopCategory { get; set; } = DesktopCategory.All;
    /// <summary>The id of the custom category shown, or null when a built-in one is.</summary>
    public string? CustomCategory { get; set; }
    public Dictionary<MenuPage, string> Searches { get; } = new();
    public Dictionary<MenuPage, int> Selections { get; } = new();
    public void Begin()
    {
        Page = MenuPage.Desktop;
        DesktopCategory = DesktopCategory.All;
        CustomCategory = null;
        foreach (var page in Enum.GetValues<MenuPage>())
        {
            Searches[page] = "";
            Selections[page] = 0;
        }
    }
    public void Navigate(MenuPage page) => Page = page;
    public void Cycle(bool reverse = false)
    {
        if (Page == MenuPage.Settings) return;
        Page = Page == MenuPage.Desktop ? MenuPage.RunningApps : MenuPage.Desktop;
    }
    public static int MoveSelection(int current, int count, int delta)
        => count == 0 ? -1 : current < 0 ? 0 : Math.Clamp(current + delta, 0, count - 1);
}
