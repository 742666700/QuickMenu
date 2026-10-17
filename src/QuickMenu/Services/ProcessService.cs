using System.IO;
using System.Security.Principal;
using System.Text;
using QuickMenu.Interop;
using QuickMenu.Models;

namespace QuickMenu.Services;

public sealed class ProcessService
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "winlogon", "dwm", "csrss", "services", "smss", "lsass", "svchost",
        "fontdrvhost", "sihost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost"
    };

    /// <summary>Running copies of one program, matched by full image path so a same-named exe elsewhere is not included.</summary>
    public IReadOnlyList<ProcessIdentity> FindByImage(string imagePath)
    {
        var found = new List<ProcessIdentity>();
        string expected;
        try { expected = Path.GetFullPath(imagePath); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return found; }
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(expected)))
        {
            using (process)
            {
                if (process.Id <= 4 || process.Id == Environment.ProcessId) continue;
                using var handle = NativeMethods.OpenProcess(0x1000, false, process.Id);
                if (handle.IsInvalid) continue;
                var path = new StringBuilder(32768);
                uint length = (uint)path.Capacity;
                if (!NativeMethods.QueryFullProcessImageName(handle, 0, path, ref length) || !string.Equals(path.ToString(), expected, StringComparison.OrdinalIgnoreCase)) continue;
                if (NativeMethods.GetProcessTimes(handle, out var created, out _, out _, out _)) found.Add(new(process.Id, DateTime.FromFileTimeUtc(created)));
            }
        }
        return found;
    }

    public OperationResult Terminate(ProcessIdentity identity)
    {
        if (identity.ProcessId <= 4 || identity.ProcessId == Environment.ProcessId)
            return new(false, Loc.T("Process.Self"));
        try
        {
            // The same kernel handle is used for identity verification and termination, so PID reuse cannot change the target.
            using var handle = NativeMethods.OpenProcess(0x1001, false, identity.ProcessId);
            if (handle.IsInvalid) return new(false, Loc.T("Process.Gone"));
            if (!NativeMethods.GetProcessTimes(handle, out var created, out _, out _, out _) ||
                DateTime.FromFileTimeUtc(created) != identity.StartTimeUtc.ToUniversalTime())
                return new(false, Loc.T("Process.Changed"));
            if (!NativeMethods.IsProcessCritical(handle, out var critical) || critical)
                return new(false, Loc.T("Process.Unsafe"));
            var path = new StringBuilder(32768);
            uint length = (uint)path.Capacity;
            if (!NativeMethods.QueryFullProcessImageName(handle, 0, path, ref length) ||
                ProtectedNames.Contains(Path.GetFileNameWithoutExtension(path.ToString())))
                return new(false, Loc.T("Process.System"));
            if (!NativeMethods.OpenProcessToken(handle, 8, out var token))
                return new(false, Loc.T("Process.UnknownOwner"));
            using (token)
            using (var owner = new WindowsIdentity(token.DangerousGetHandle()))
            {
                if (owner.User is null || owner.User.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
                    owner.User.IsWellKnown(WellKnownSidType.LocalServiceSid) || owner.User.IsWellKnown(WellKnownSidType.NetworkServiceSid))
                    return new(false, Loc.T("Process.Service"));
            }
            return NativeMethods.TerminateProcess(handle, 1)
                ? new(true, Loc.T("Process.Requested")) : new(false, Loc.T("Process.Failed"));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or UnauthorizedAccessException or ArgumentException)
        {
            return new(false, Loc.T("Process.Error") + ex.Message);
        }
    }
}
