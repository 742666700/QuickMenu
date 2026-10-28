using System.IO;
using System.Windows.Media;
using QuickMenu.Services;
using Xunit;

namespace QuickMenu.Tests.Services;

public class IconServiceTests
{
    [Fact]
    public async Task ConcurrentShellRequestsReturnFrozenReusableIcons()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var paths = new[] { "shell32.dll", "notepad.exe", "cmd.exe", "imageres.dll" }.Select(file => Path.Combine(system, file)).ToArray();
        using var start = new ManualResetEventSlim();
        var requests = Enumerable.Range(0, 8).Select(_ =>
        {
            var completed = new TaskCompletionSource<ImageSource?[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { start.Wait(); completed.SetResult(paths.Select(path => new IconService().GetForPath(path)).ToArray()); }
                catch (Exception ex) { completed.SetException(ex); }
            }) { IsBackground = true };
            // Match the app's concurrent STA shell-query workers.
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return completed.Task;
        }).ToArray();
        start.Set();
        var batches = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(20));
        // SHGetFileInfo may fail during a cold concurrent lookup; the service deliberately returns null then.
        // Every path must still produce a usable icon, and every successful result must cross threads frozen.
        for (var i = 0; i < paths.Length; i++) Assert.Contains(batches, batch => batch[i] is not null);
        Assert.All(batches.SelectMany(batch => batch).OfType<ImageSource>(), image => Assert.True(image.IsFrozen));
        var service = new IconService();
        foreach (var path in paths)
        {
            var cached = service.GetForPath(path);
            Assert.NotNull(cached);
            Assert.Same(cached, service.GetForPath(path));
        }
    }
}
