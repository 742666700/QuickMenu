using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using QuickMenu.Models;
using QuickMenu.Services;
using Xunit;

namespace QuickMenu.Tests.Services;

// Needs a signed-in Windows desktop; CI skips it (dotnet test --filter Category!=Interactive).
[Trait("Category", "Interactive")]
[Collection("原生窗口集成")]
public class ProcessServiceTests
{
    [Fact]
    public void RejectsOwnProcess()
    {
        using var self = Process.GetCurrentProcess();
        var result = new ProcessService().Terminate(new(self.Id, self.StartTime.ToUniversalTime()));
        Assert.False(result.Success);
        Assert.False(self.HasExited);
    }

    [Fact]
    public void RejectsReusedPidIdentityWithoutTerminatingTarget()
    {
        using var child = StartControlledProcess();
        try
        {
            var result = new ProcessService().Terminate(new(child.Id, child.StartTime.ToUniversalTime().AddSeconds(-1)));
            Assert.False(result.Success);
            Assert.False(child.HasExited);
        }
        finally { if (!child.HasExited) child.Kill(); }
    }

    [Fact]
    public void TerminatesOnlyTheVerifiedControlledProcess()
    {
        using var child = StartControlledProcess();
        using var sibling = StartControlledProcess();
        try
        {
            var result = new ProcessService().Terminate(new(child.Id, child.StartTime.ToUniversalTime()));
            Assert.True(result.Success, result.Message);
            Assert.True(child.WaitForExit(5000));
            Assert.False(sibling.HasExited);
        }
        finally
        {
            if (!child.HasExited) child.Kill();
            if (!sibling.HasExited) sibling.Kill();
        }
    }

    [Fact]
    public async Task PermissionDeniedDoesNotTerminateTheControlledFixture()
    {
        var folder = FixtureTestSupport.NewDirectory();
        var start = new ProcessStartInfo(FixtureTestSupport.Executable) { UseShellExecute = false, CreateNoWindow = true };
        if (FixtureTestSupport.LocalDotnetRoot is { } runtime) start.Environment["DOTNET_ROOT"] = runtime;
        start.ArgumentList.Add("--output"); start.ArgumentList.Add(folder);
        start.ArgumentList.Add("--wait"); start.ArgumentList.Add("--deny-terminate");
        using var child = Process.Start(start)!;
        try
        {
            var ready = await FixtureTestSupport.WaitForReadyAsync(folder, child);
            Assert.Equal(child.Id, ready.ProcessId);
            var identity = new ProcessIdentity(child.Id, child.StartTime.ToUniversalTime());
            // The executor has debug privilege enabled; disable it only on a duplicated, thread-scoped token.
            using var currentIdentity = WindowsIdentity.GetCurrent();
            Assert.True(DuplicateTokenEx(currentIdentity.AccessToken, 0x2e, 0, 2, 2, out var token));
            using (token)
            {
                Assert.True(LookupPrivilegeValue(null, "SeDebugPrivilege", out var debugPrivilege));
                var privileges = new TokenPrivileges { Count = 1, Luid = debugPrivilege, Attributes = 0 };
                Assert.True(AdjustTokenPrivileges(token, false, ref privileges, 0, 0, 0));
                var result = WindowsIdentity.RunImpersonated(token, () =>
                {
                    using var deniedHandle = OpenProcess(1, false, child.Id);
                    var error = Marshal.GetLastWin32Error();
                    Assert.True(deniedHandle.IsInvalid);
                    Assert.Equal(5, error);
                    return new ProcessService().Terminate(identity);
                });
                Assert.False(result.Success);
                Assert.Contains("权限", result.Message);
            }
            Assert.False(child.HasExited);
        }
        finally { await FixtureTestSupport.RequestExitAsync(child, folder); }
        Assert.Equal(0, child.ExitCode);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(SafeAccessTokenHandle existing, uint access, nint attributes, int level, int type, out SafeAccessTokenHandle duplicate);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, bool disableAll, ref TokenPrivileges privileges, uint length, nint previous, nint returned);
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges { public uint Count; public long Luid; public uint Attributes; }

    [Fact]
    public void FindsRunningCopiesByFullImagePathOnly()
    {
        using var child = StartControlledProcess();
        try
        {
            var service = new ProcessService();
            var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            var found = service.FindByImage(cmd);
            Assert.Contains(found, process => process.ProcessId == child.Id && process.StartTimeUtc == child.StartTime.ToUniversalTime());
            Assert.Empty(service.FindByImage(Path.Combine(Path.GetTempPath(), "cmd.exe")));
            Assert.Empty(service.FindByImage(Environment.ProcessPath!));
        }
        finally { if (!child.HasExited) child.Kill(); }
    }

    private static Process StartControlledProcess() => Process.Start(new ProcessStartInfo
    {
        FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
        Arguments = "/d /q /c \"pause >nul\"", UseShellExecute = false,
        CreateNoWindow = true, RedirectStandardInput = true
    })!;
}
