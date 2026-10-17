using System.Runtime.InteropServices;
using QuickMenu.Interop;
using QuickMenu.Models;

namespace QuickMenu.Services;

public static class FileLocationService
{
    public static string? ResolveTarget(string path)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var depth = 0; depth < 16; depth++)
        {
            if (!Path.IsPathFullyQualified(path) || !visited.Add(path)) return null;
            if (Directory.Exists(path)) return path;
            if (!File.Exists(path)) return null;
            if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                using (new ComScope(2)) path = Environment.ExpandEnvironmentVariables(ShellLink.Target(path));
                continue;
            }
            if (Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                var target = File.ReadLines(path).FirstOrDefault(line => line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..].Trim();
                if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.IsFile) { path = uri.LocalPath; continue; }
                return null;
            }
            return path;
        }
        return null;
    }

    public static OperationResult Open(string path)
    {
        nint pidl = 0;
        try
        {
            var target = ResolveTarget(path);
            if (target == null) return new(false, Loc.T("Location.NotFound"));
            using var apartment = new ComScope(2);
            Marshal.ThrowExceptionForHR(ShellInterop.SHParseDisplayName(target, 0, out pidl, 0, out _));
            Marshal.ThrowExceptionForHR(ShellInterop.SHOpenFolderAndSelectItems(pidl, 0, 0, 0));
            return new(true, Loc.T("Location.Shown"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        { return new(false, Loc.T("Location.Failed") + ex.Message); }
        finally { if (pidl != 0) Marshal.FreeCoTaskMem(pidl); }
    }
}
