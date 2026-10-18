using System.Windows;
using Xunit;

namespace QuickMenu.Tests;

public class MenuPlacementTests
{
    // Claude娘 standing, at full size: 395 DIPs right of the menu, 666 above its bottom.
    private static readonly Size Claude = new(395, 666);

    [Fact]
    public void WideScreenKeepsTheMenuCentredAndHerFullSize()
    {
        // 2560 × 1440 at 100 %, 48 px taskbar.
        var plan = MenuPlacement.Plan(0, 0, 2560, 1392, 1, 820, Claude);
        Assert.Equal(1, plan.CompanionScale);
        Assert.Equal((2560 - 820) / 2, plan.Left);
        Assert.Equal(820, plan.Width);
    }

    [Fact]
    public void NobodyBesideItKeepsTheMenuCentred()
    {
        var plan = MenuPlacement.Plan(0, 0, 1920, 1008, 1.5, 820, default);
        Assert.Equal(0, plan.CompanionScale);
        Assert.Equal((1920 - 1230) / 2, plan.Left);
    }

    [Fact]
    public void HandheldMovesTheMenuLeftSoSheFits()
    {
        // ROG Ally X: 1920 × 1080 at 150 %, 72 px taskbar, so 1280 × 672 DIPs of work area.
        var plan = MenuPlacement.Plan(0, 0, 1920, 1008, 1.5, 820, Claude);
        Assert.InRange(plan.CompanionScale, 0.9, 1);
        Assert.Equal(820, plan.Width);
        Assert.True(plan.Left < (1920 - 1230) / 2, "the menu moves left of centre");
        AssertOnScreen(plan, 1920, 1008, 1.5, Claude);
    }

    [Fact]
    public void ShorterScreenShrinksHerAndNarrowsTheMenu()
    {
        // 1920 × 1080 at 175 %: about 1097 × 576 DIPs. Her height decides her size; the menu gives up the rest.
        var plan = MenuPlacement.Plan(0, 0, 1920, 1008, 1.75, 820, Claude);
        Assert.InRange(plan.Width, 600, 819);
        Assert.InRange(plan.CompanionScale, MenuPlacement.SmallestCompanion, 0.9);
        AssertOnScreen(plan, 1920, 1008, 1.75, Claude);
    }

    [Fact]
    public void TooSmallForHerLeavesTheMenuCentredWithoutHer()
    {
        // 800 × 480 at 100 %.
        var plan = MenuPlacement.Plan(0, 0, 800, 480, 1, 820, Claude);
        Assert.Equal(0, plan.CompanionScale);
        Assert.Equal((800 - plan.PixelWidth) / 2, plan.Left);
    }

    [Fact]
    public void SecondMonitorKeepsItsOffset()
    {
        var plan = MenuPlacement.Plan(-1920, 0, 1920, 1008, 1.5, 820, Claude);
        Assert.True(plan.CompanionScale > 0);
        Assert.InRange(plan.Left, -1920, -1);
    }

    private static void AssertOnScreen(MenuPlan plan, int workWidth, int workHeight, double scale, Size companion)
    {
        Assert.True(plan.Left >= 0);
        Assert.True(plan.Left + plan.PixelWidth + companion.Width * plan.CompanionScale * scale <= workWidth, "she fits to the right");
        Assert.True(plan.Top + plan.PixelHeight - companion.Height * plan.CompanionScale * scale >= 0, "her head stays on screen");
        Assert.True(plan.Top + plan.PixelHeight <= workHeight);
    }
}
