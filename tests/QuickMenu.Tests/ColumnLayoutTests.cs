using Xunit;

namespace QuickMenu.Tests;

public class ColumnLayoutTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(8, 2)]
    [InlineData(9, 3)]
    public void GroupsSplitAfterFourRows(int count, int expected) => Assert.Equal(expected, ColumnItemsPanel.ColumnsWanted(count));

    private static void AssertPlan(int[] counts, int slots, int[][] stacks, int[] split)
    {
        var plan = DesktopColumnsPanel.Plan(counts, slots);
        Assert.Equal(stacks, plan.Stacks);
        Assert.Equal(split, plan.Slots);
    }

    [Fact]
    public void ShortGroupsStaySideBySide() => AssertPlan(new[] { 4, 3, 1 }, 4, new[] { new[] { 0 }, new[] { 1 }, new[] { 2 } }, new[] { 1, 1, 1 });

    [Fact]
    public void ShortGroupsStackToGiveTheLongGroupRoom() => AssertPlan(new[] { 14, 3, 1 }, 4, new[] { new[] { 0 }, new[] { 1, 2 } }, new[] { 3, 1 });

    [Fact]
    public void LongGroupTakesOnlyTheColumnsItNeeds() => AssertPlan(new[] { 8 }, 4, new[] { new[] { 0 } }, new[] { 2 });

    [Fact]
    public void VeryLongGroupUsesEveryColumn() => AssertPlan(new[] { 14 }, 4, new[] { new[] { 0 } }, new[] { 4 });

    [Fact]
    public void TwoLongGroupsShareTheWidth() => AssertPlan(new[] { 9, 8 }, 4, new[] { new[] { 0 }, new[] { 1 } }, new[] { 2, 2 });

    [Fact]
    public void GroupsStayInOrderWhenSlotsRunOut() => AssertPlan(new[] { 20, 20, 20 }, 3, new[] { new[] { 0 }, new[] { 1 }, new[] { 2 } }, new[] { 1, 1, 1 });

    [Fact]
    public void FewerSlotsThanGroupsStacksTheShortOnes() => AssertPlan(new[] { 4, 3, 1 }, 2, new[] { new[] { 0 }, new[] { 1, 2 } }, new[] { 1, 1 });
}
