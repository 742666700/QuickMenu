using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using QuickMenu.Models;
using QuickMenu.Services;
using Xunit;
using Xunit.Abstractions;

namespace QuickMenu.Tests.Services;

// Needs a signed-in Windows desktop; CI skips it (dotnet test --filter Category!=Interactive).
[Trait("Category", "Interactive")]
[Collection("原生窗口集成")]
public class WindowServiceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ControlledWindowsAreGroupedIncludingMinimizedAndExcludingToolWindows()
    {
        using var fixture = await FixtureProcess.StartAsync();
        var service = new WindowService();
        var timer = Stopwatch.StartNew();
        var apps = await service.GetAppsAsync((nint)fixture.Ready.MinimizedHandle);
        output.WriteLine($"窗口列表刷新耗时：{timer.ElapsedMilliseconds} 毫秒");
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(apps, app => app.Identity?.ProcessId == Environment.ProcessId);
        var app = Assert.Single(apps, app => app.Identity?.ProcessId == fixture.Process.Id);
        Assert.Equal(2, app.Windows.Count);
        Assert.Contains(app.Windows, window => window.Handle == (nint)fixture.Ready.MainHandle);
        Assert.Contains(app.Windows, window => window.Handle == (nint)fixture.Ready.MinimizedHandle);
        Assert.DoesNotContain(app.Windows, window => window.Handle == (nint)fixture.Ready.ToolHandle);
        Assert.True(IsIconic((nint)fixture.Ready.MinimizedHandle));
        Assert.True(app.IsForeground);
        Assert.Equal(fixture.Process.StartTime.ToUniversalTime(), app.Identity!.StartTimeUtc);
        Assert.All(app.Windows, window =>
        {
            Assert.Equal(fixture.Process.Id, window.ProcessId);
            Assert.Equal(app.Identity.StartTimeUtc, window.StartTimeUtc);
        });
        var version = FileVersionInfo.GetVersionInfo(fixture.Process.MainModule!.FileName);
        var expectedName = string.IsNullOrWhiteSpace(version.FileDescription)
            ? Path.GetFileNameWithoutExtension(fixture.Process.MainModule.FileName) : version.FileDescription.Trim();
        Assert.Equal(expectedName, app.Name);
        Assert.Contains(app.Windows[0].Title, app.Subtitle);
    }

    [Fact]
    public async Task ActivatingControlledMinimizedWindowRestoresItAndReportsFocusAccurately()
    {
        using var fixture = await FixtureProcess.StartAsync();
        var hwnd = (nint)fixture.Ready.MinimizedHandle;
        Assert.True(IsIconic(hwnd));
        var service = new WindowService();
        var app = Assert.Single(await service.GetAppsAsync(0), item => item.Identity?.ProcessId == fixture.Process.Id);
        var selected = Assert.Single(app.Windows, item => item.Handle == hwnd);
        var result = service.Activate(selected);
        var wait = Stopwatch.StartNew();
        while (IsIconic(hwnd) && wait.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(20);
        Assert.False(IsIconic(hwnd));
        output.WriteLine($"切换结果：{result.Success}；{result.Message}");
        if (result.Success) Assert.Equal(hwnd, GetForegroundWindow());
        else Assert.Contains("未允许", result.Message);
        Assert.False(new WindowService().Activate(new WindowEntry(0, "失效窗口")).Success);
    }

    [Fact]
    public async Task MismatchedWindowProcessDoesNotRestoreOrActivateTheHandle()
    {
        using var fixture = await FixtureProcess.StartAsync();
        var hwnd = (nint)fixture.Ready.MinimizedHandle;
        var before = GetForegroundWindow();
        var result = new WindowService().Activate(new(hwnd, "错误进程身份", Environment.ProcessId,
            fixture.Process.StartTime.ToUniversalTime()));
        await Task.Delay(150);
        Assert.False(result.Success);
        Assert.Equal(before, GetForegroundWindow());
        Assert.True(IsIconic(hwnd));
    }

    [Fact]
    public async Task MismatchedWindowStartTimeDoesNotRestoreOrActivateTheHandle()
    {
        using var fixture = await FixtureProcess.StartAsync();
        var hwnd = (nint)fixture.Ready.MinimizedHandle;
        var before = GetForegroundWindow();
        var result = new WindowService().Activate(new(hwnd, "错误启动时间", fixture.Process.Id,
            fixture.Process.StartTime.ToUniversalTime().AddSeconds(-1)));
        await Task.Delay(150);
        Assert.False(result.Success);
        Assert.Equal(before, GetForegroundWindow());
        Assert.True(IsIconic(hwnd));
    }

    [Fact]
    public async Task WindowWithoutCapturedProcessIdentityCannotBeActivated()
    {
        using var fixture = await FixtureProcess.StartAsync();
        var hwnd = (nint)fixture.Ready.MinimizedHandle;
        var before = GetForegroundWindow();
        var result = new WindowService().Activate(new WindowEntry(hwnd, "未确认身份的窗口"));
        await Task.Delay(150);
        Assert.False(result.Success);
        Assert.Contains("身份尚未确认", result.Message);
        Assert.Equal(before, GetForegroundWindow());
        Assert.True(IsIconic(hwnd));
    }

    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();

    private sealed record FixtureReady(int ProcessId, long MainHandle, long MinimizedHandle, long ToolHandle);
    private sealed class FixtureProcess(Process process, FixtureReady ready) : IDisposable
    {
        public Process Process { get; } = process;
        public FixtureReady Ready { get; } = ready;

        public static async Task<FixtureProcess> StartAsync()
        {
            var root = new DirectoryInfo(FixtureTestSupport.Workspace);
            var helperFolder = Path.Combine(root.FullName, "tests", "QuickMenu.Fixture", "bin", "Debug", "net10.0-windows");
            var executable = Path.Combine(helperFolder, "QuickMenu.Fixture.exe");
            var assembly = Path.Combine(helperFolder, "QuickMenu.Fixture.dll");
            Assert.True(File.Exists(executable) || File.Exists(assembly), "先构建 tests/QuickMenu.Fixture 受控窗口夹具。");
            var folder = Path.Combine(root.FullName, "artifacts", "fixture-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var start = new ProcessStartInfo
            {
                FileName = File.Exists(executable) ? executable : FixtureTestSupport.Dotnet,
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = root.FullName
            };
            if (FixtureTestSupport.LocalDotnetRoot is { } runtime) start.Environment["DOTNET_ROOT"] = runtime;
            if (!File.Exists(executable)) start.ArgumentList.Add(assembly);
            start.ArgumentList.Add("--output");
            start.ArgumentList.Add(folder);
            start.ArgumentList.Add("--wait");
            var child = Process.Start(start)!;
            try
            {
                var deadline = Stopwatch.StartNew();
                var path = Path.Combine(folder, "handles-ready.json");
                while (deadline.Elapsed < TimeSpan.FromSeconds(10))
                {
                    Assert.False(child.HasExited, "受控窗口夹具过早退出。");
                    if (File.Exists(path))
                    {
                        try
                        {
                            var ready = JsonSerializer.Deserialize<FixtureReady>(await File.ReadAllTextAsync(path),
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (ready is { MainHandle: not 0, MinimizedHandle: not 0, ToolHandle: not 0 })
                            {
                                Assert.Equal(child.Id, ready.ProcessId);
                                return new(child, ready);
                            }
                        }
                        catch (Exception ex) when (ex is IOException or JsonException) { }
                    }
                    await Task.Delay(40);
                }
                throw new TimeoutException("受控窗口夹具没有生成有效窗口句柄。");
            }
            catch { if (!child.HasExited) child.Kill(); child.Dispose(); throw; }
        }

        public void Dispose()
        {
            if (!Process.HasExited)
            {
                var result = new ProcessService().Terminate(new(Process.Id, Process.StartTime.ToUniversalTime()));
                if (!result.Success && !Process.HasExited) Process.Kill();
                Process.WaitForExit(5000);
            }
            Process.Dispose();
        }
    }
}
