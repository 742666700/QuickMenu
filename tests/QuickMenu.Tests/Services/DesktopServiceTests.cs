using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using QuickMenu.Models;
using QuickMenu.Services;
using Xunit;

namespace QuickMenu.Tests.Services;

// Needs a signed-in Windows desktop; CI skips it (dotnet test --filter Category!=Interactive).
[Trait("Category", "Interactive")]
[Collection("原生窗口集成")]
public class DesktopServiceTests
{
    [Fact]
    public void EmptyParsingNameIsRejectedWithoutLaunchingAnything()
    {
        Assert.False(new DesktopService().Open(new DesktopItem("invalid", "无效项目", null, "")).Success);
    }

    [Fact]
    public async Task DesktopNamespaceReturnsStableUniqueParsingNames()
    {
        var service = new DesktopService();
        var first = await service.GetItemsAsync();
        var second = await service.GetItemsAsync();
        Assert.NotEmpty(first);
        Assert.All(first, item => Assert.False(string.IsNullOrWhiteSpace(item.ParsingName)));
        Assert.Equal(first.Count, first.Select(x => x.ParsingName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(first.Select(x => x.Id).Order(), second.Select(x => x.Id).Order());
    }

    [Fact]
    public async Task OpeningWorkspaceShortcutPreservesItsArguments()
    {
        var folder = FixtureTestSupport.NewDirectory();
        var output = Path.Combine(folder, "目标输出目录 带空格");
        Directory.CreateDirectory(output);
        var shortcutPath = Path.Combine(folder, "受控快捷方式.lnk");
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                QuickMenu.Interop.ShellLink.Create(shortcutPath, FixtureTestSupport.Executable, $"--output \"{output}\" --wait", folder);
                completed.SetResult(true);
            }
            catch (Exception ex) { completed.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var oldRuntimeRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        Process? child = null;
        try
        {
            if (FixtureTestSupport.LocalDotnetRoot is { } runtime) Environment.SetEnvironmentVariable("DOTNET_ROOT", runtime);
            var result = new DesktopService().Open(new(shortcutPath, "受控快捷方式", null, shortcutPath));
            Assert.True(result.Success, result.Message);
            var ready = await FixtureTestSupport.WaitForReadyAsync(output);
            child = Process.GetProcessById(ready.ProcessId);
            Assert.Equal(Path.GetFullPath(FixtureTestSupport.Executable), child.MainModule!.FileName, StringComparer.OrdinalIgnoreCase);
            Assert.False(child.HasExited);
            Assert.True(File.Exists(Path.Combine(output, "handles-ready.json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", oldRuntimeRoot);
            if (child != null)
            {
                await FixtureTestSupport.RequestExitAsync(child, output);
                child.Dispose();
            }
        }
    }
}
