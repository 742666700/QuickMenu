using QuickMenu.Services;
using Xunit;
using Xunit.Abstractions;

namespace QuickMenu.Tests.Services;

// Needs a signed-in Windows desktop; CI skips it (dotnet test --filter Category!=Interactive).
[Trait("Category", "Interactive")]
public class TrayServiceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task AccessibleTrayItemsNeverUseTheProviderProcessAsTheirOwner()
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var items = await new TrayService().GetItemsAsync();
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(4));
        output.WriteLine($"当前可访问通知区域项目：{items.Count}");
        Assert.NotEmpty(items); // Interactive Windows 11 verification machine has visible third-party notification icons.
        Assert.All(items, item =>
        {
            Assert.True(item.IsTray);
            Assert.Null(item.Identity);
            Assert.Empty(item.Windows);
            Assert.False(item.CanTerminate);
        });
    }

    [Fact]
    public async Task CancelledQueryDoesNotWaitForAnAccessibilityProvider()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TrayService().GetItemsAsync(cancellation.Token));
    }
}
