using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Xunit;

namespace QuickMenu.Tests.Services;

[CollectionDefinition("原生窗口集成", DisableParallelization = true)]
public sealed class WindowsIntegrationCollection;

internal static class FixtureTestSupport
{
    internal sealed record Ready(int ProcessId, long MainHandle, long MinimizedHandle, long ToolHandle);
    /// <summary>The repository root: the folder holding QuickMenu.slnx.</summary>
    internal static string Workspace
    {
        get
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "QuickMenu.slnx"))) root = root.Parent;
            Assert.NotNull(root);
            return root.FullName;
        }
    }

    /// <summary>A .NET install kept beside the sources (.tools/dotnet), if there is one; otherwise the machine's own is used.</summary>
    internal static string? LocalDotnetRoot => Path.Combine(Workspace, ".tools", "dotnet") is var local && File.Exists(Path.Combine(local, "dotnet.exe")) ? local : null;
    internal static string Dotnet => LocalDotnetRoot is { } local ? Path.Combine(local, "dotnet.exe") : "dotnet";
    internal static string Executable => Path.Combine(Workspace, "tests", "QuickMenu.Fixture", "bin", "Debug", "net10.0-windows", "QuickMenu.Fixture.exe");
    internal static string NewDirectory()
    {
        var folder = Path.Combine(Workspace, "artifacts", "test-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
    internal static async Task<Ready> WaitForReadyAsync(string directory, Process? child = null)
    {
        var deadline = Stopwatch.StartNew();
        var path = Path.Combine(directory, "handles-ready.json");
        while (deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (child != null) Assert.False(child.HasExited, "受控测试进程过早退出。");
            if (File.Exists(path))
            {
                try
                {
                    var ready = JsonSerializer.Deserialize<Ready>(await File.ReadAllTextAsync(path));
                    if (ready is { ProcessId: > 0, MainHandle: not 0, MinimizedHandle: not 0, ToolHandle: not 0 }) return ready;
                }
                catch (Exception ex) when (ex is IOException or JsonException) { }
            }
            await Task.Delay(40);
        }
        throw new TimeoutException("受控测试进程没有按参数写入指定输出目录。");
    }
    internal static async Task RequestExitAsync(Process child, string directory)
    {
        if (!child.HasExited)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "exit.request"), "测试完成");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
