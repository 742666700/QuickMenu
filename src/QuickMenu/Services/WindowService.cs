using System.Diagnostics;
using System.IO;
using System.Text;
using QuickMenu.Interop;
using QuickMenu.Models;

namespace QuickMenu.Services;

public sealed class WindowService
{
    private readonly IconService _icons = new();
    public Task<IReadOnlyList<RunningApp>> GetAppsAsync(nint previousForeground, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<RunningApp>>(() => Enumerate(previousForeground, cancellationToken), cancellationToken);

    private IReadOnlyList<RunningApp> Enumerate(nint foreground, CancellationToken token)
    {
        var windows = new Dictionary<int, List<WindowEntry>>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (token.IsCancellationRequested) return false;
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            var extendedStyle = NativeMethods.GetWindowLongPtr(hwnd, -20).ToInt64();
            if ((extendedStyle & 0x80) != 0 || (NativeMethods.GetWindow(hwnd, 4) != 0 && (extendedStyle & 0x40000) == 0)) return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0 || pid == Environment.ProcessId) return true;
            if (NativeMethods.DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            var className = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            var title = new StringBuilder(32768);
            NativeMethods.GetWindowText(hwnd, title, title.Capacity);
            if (title.Length == 0) return true;
            if (!windows.TryGetValue((int)pid, out var list)) windows[(int)pid] = list = [];
            list.Add(new(hwnd, title.ToString(), (int)pid));
            return true;
        }, 0);
        token.ThrowIfCancellationRequested();
        var result = new List<RunningApp>();
        foreach (var (pid, entries) in windows)
        {
            token.ThrowIfCancellationRequested();
            ProcessIdentity? identity = null;
            string? path = null;
            using (var handle = NativeMethods.OpenProcess(0x1000, false, pid))
            {
                if (!handle.IsInvalid)
                {
                    if (NativeMethods.GetProcessTimes(handle, out var created, out _, out _, out _))
                        identity = new(pid, DateTime.FromFileTimeUtc(created));
                    var image = new StringBuilder(32768);
                    uint size = (uint)image.Capacity;
                    if (NativeMethods.QueryFullProcessImageName(handle, 0, image, ref size)) path = image.ToString();
                }
            }
            var live = entries.Where(x =>
            {
                NativeMethods.GetWindowThreadProcessId(x.Handle, out var owner);
                return owner == pid && NativeMethods.IsWindow(x.Handle);
            }).Select(window => window with { StartTimeUtc = identity?.StartTimeUtc }).ToArray();
            if (live.Length == 0) continue;
            var icon = _icons.GetForWindow(live[0].Handle) ?? (path is null ? null : _icons.GetForPath(path));
            var name = GetFriendlyName(path, live[0].Title);
            var id = identity == null ? $"window:{pid}" : $"process:{pid}:{identity.StartTimeUtc.Ticks}";
            result.Add(new(id, name, icon, identity, live, live.Any(x => x.Handle == foreground)));
        }
        return result.OrderByDescending(x => x.IsForeground).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public OperationResult Activate(WindowEntry window)
    {
        if (window.ProcessId <= 0) return new(false, Loc.T("Window.Unconfirmed"));
        if (!NativeMethods.IsWindow(window.Handle)) return new(false, Loc.T("Window.Closed"));
        NativeMethods.GetWindowThreadProcessId(window.Handle, out var owner);
        if (owner != window.ProcessId) return new(false, Loc.T("Window.ProcessChanged"));
        // Retain the original process object until activation completes and verify its captured creation time.
        using var process = NativeMethods.OpenProcess(0x1000, false, window.ProcessId);
        if (process.IsInvalid || !NativeMethods.GetProcessTimes(process, out var created, out _, out _, out _))
            return new(false, Loc.T("Window.OwnerUnknown"));
        if (window.StartTimeUtc is { } expected && DateTime.FromFileTimeUtc(created) != expected.ToUniversalTime())
            return new(false, Loc.T("Window.OwnerChanged"));
        NativeMethods.GetWindowThreadProcessId(window.Handle, out owner);
        if (owner != window.ProcessId || !NativeMethods.IsWindow(window.Handle))
            return new(false, Loc.T("Window.Gone"));
        if (NativeMethods.IsIconic(window.Handle)) NativeMethods.ShowWindowAsync(window.Handle, 9);
        var activated = NativeMethods.SetForegroundWindow(window.Handle);
        return activated && NativeMethods.GetForegroundWindow() == window.Handle
            ? new(true, Loc.T("Window.Switched"))
            : new(false, Loc.T("Window.Refused"));
    }

    private static string GetFriendlyName(string? path, string fallback)
    {
        if (string.IsNullOrWhiteSpace(path)) return fallback;
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException) { }
        var name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }
}
