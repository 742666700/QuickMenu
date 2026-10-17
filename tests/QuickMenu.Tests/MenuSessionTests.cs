using QuickMenu.Models;
using Xunit;

namespace QuickMenu.Tests;

public class MenuSessionTests
{
    [Fact]
    public void NewInvocationResetsPageSearchAndSelection()
    {
        var state = new MenuSession();
        state.Navigate(MenuPage.RunningApps);
        state.Searches[MenuPage.Desktop] = "微信";
        state.Searches[MenuPage.RunningApps] = "浏览器";
        state.Selections[MenuPage.RunningApps] = 5;
        state.Begin();
        Assert.Equal(MenuPage.Desktop, state.Page);
        Assert.Equal("", state.Searches[MenuPage.Desktop]);
        Assert.Equal("", state.Searches[MenuPage.RunningApps]);
        Assert.Equal(0, state.Selections[MenuPage.RunningApps]);
    }

    [Fact]
    public void CyclingKeepsEachPagesSearchAndSelection()
    {
        var state = new MenuSession();
        state.Begin();
        state.Searches[MenuPage.Desktop] = "资料";
        state.Selections[MenuPage.Desktop] = 7;
        state.Cycle();
        Assert.Equal(MenuPage.RunningApps, state.Page);
        state.Cycle(true);
        Assert.Equal(MenuPage.Desktop, state.Page);
        Assert.Equal("资料", state.Searches[MenuPage.Desktop]);
        Assert.Equal(7, state.Selections[MenuPage.Desktop]);
    }

    [Theory]
    [InlineData(0, 0, 1, -1)]
    [InlineData(-1, 8, 1, 0)]
    [InlineData(0, 8, -1, 0)]
    [InlineData(7, 8, 6, 7)]
    [InlineData(1, 18, 6, 7)]
    public void NavigationStaysWithinAvailableItems(int current, int count, int delta, int expected)
        => Assert.Equal(expected, MenuSession.MoveSelection(current, count, delta));
}
