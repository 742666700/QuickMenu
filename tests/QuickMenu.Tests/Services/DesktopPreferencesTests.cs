using System.IO;
using QuickMenu.Models;
using QuickMenu.Services;
using Xunit;

namespace QuickMenu.Tests.Services;

public class DesktopPreferencesTests
{
    private static DesktopItem Item(string id, DateTime? modified = null, DesktopCategory category = DesktopCategory.Other) => new(id, id, null, id, category, ModifiedUtc: modified);
    [Fact]
    public void PersistsPinsUsageCategoriesAndSort()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var preferences = new DesktopPreferencesService(path);
            Assert.True(preferences.SetPinned("A", true).Success);
            Assert.True(preferences.RecordLaunch("A").Success);
            Assert.True(preferences.SetCategory("A", DesktopCategory.Office).Success);
            Assert.True(preferences.SetSort(DesktopSort.Modified).Success);
            var loaded = new DesktopPreferencesService(path);
            Assert.True(loaded.Get("a").Pinned);
            Assert.Equal(1, loaded.Get("a").LaunchCount);
            Assert.NotNull(loaded.Get("a").LastLaunchUtc);
            Assert.Equal(DesktopSort.Modified, loaded.Sort);
            Assert.True(DesktopCategories.Matches(DesktopCategory.Office, loaded.Category(Item("a"))));
            Assert.True(loaded.SetCategory("a", null).Success);
            Assert.Equal(DesktopCategory.Other, loaded.Category(Item("a")));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void AllSortsPrioritizePinsAndUnknownDatesSortLast()
    {
        var preferences = new DesktopPreferencesService(ephemeral: true);
        var pinned = Item("pin"); var used = Item("used");
        preferences.SetPinned(pinned.Id, true); preferences.RecordLaunch(used.Id);
        Assert.True(preferences.Compare(used, Item("unused")) < 0);
        foreach (var sort in Enum.GetValues<DesktopSort>())
        {
            preferences.SetSort(sort);
            Assert.True(preferences.Compare(pinned, used) < 0);
        }
        preferences.SetSort(DesktopSort.Modified);
        Assert.True(preferences.Compare(Item("new", DateTime.UtcNow), Item("unknown")) < 0);
        Assert.True(preferences.Compare(Item("new", DateTime.UtcNow), Item("old", DateTime.UnixEpoch)) < 0);
        preferences.SetSort(DesktopSort.Type);
        Assert.True(preferences.Compare(Item("doc", category: DesktopCategory.Documents), Item("app", category: DesktopCategory.Programs)) < 0);
    }
    [Fact]
    public void CorruptionFallsBackAndFailedWritesRollBack()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "invalid json");
            Assert.Equal(DesktopSort.Frequent, new DesktopPreferencesService(path).Sort);
            var preferences = new DesktopPreferencesService(Path.Combine(path, "blocked.json"));
            Assert.False(preferences.SetPinned("A", true).Success);
            Assert.False(preferences.Get("A").Pinned);
            Assert.False(preferences.SetSort(DesktopSort.Modified).Success);
            Assert.Equal(DesktopSort.Frequent, preferences.Sort);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CustomOrderPersistsAndPlacesNewItemsAfterArrangedOnes()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var preferences = new DesktopPreferencesService(path);
            Assert.True(preferences.SetPinned("B", true).Success);
            Assert.True(preferences.SetOrder(new[] { "C", "A", "B", "C" }).Success);
            var loaded = new DesktopPreferencesService(path);
            Assert.Equal(DesktopSort.Custom, loaded.Sort);
            Assert.Equal(new[] { "C", "A", "B" }, loaded.Order);
            var items = new System.Collections.Generic.List<DesktopItem> { Item("A"), Item("Z"), Item("B"), Item("C"), Item("D") };
            items.Sort((x, y) => loaded.Compare(x, y));
            // Pinned first, then the dragged order, then never-arranged items by name.
            Assert.Equal(new[] { "B", "C", "A", "D", "Z" }, items.Select(item => item.Id));
            Assert.True(loaded.RecordLaunch("A").Success);
            Assert.Equal(new[] { "C", "A", "B" }, new DesktopPreferencesService(path).Order);
        }
        finally { File.Delete(path); }
    }
}
