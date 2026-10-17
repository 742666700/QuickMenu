using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using QuickMenu.Models;

namespace QuickMenu.Services;

internal static class DesktopCategoryService
{
    private static readonly ConcurrentDictionary<string, (DateTime Stamp, DesktopCategory Category, string Target)> ShortcutCache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Forgets cached categories after the rules change; targets are read again with the next scan.</summary>
    public static void ClearCache() => ShortcutCache.Clear();
    public static string? GetShortcutTarget(string path) => ShortcutCache.TryGetValue(path, out var cached) ? cached.Target : null;
    public static bool IsAiTool(string path, string name) => DesktopCategories.IsAiTool(path, name, ShortcutCache.TryGetValue(path, out var cached) ? cached.Target : null);
    public static DesktopCategory GetCategory(string path)
    {
        if (Directory.Exists(path)) return DesktopCategory.Other;
        // Internet shortcuts carry their target in the file, e.g. steam://rungameid/….
        if (Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var stamp = File.GetLastWriteTimeUtc(path);
                if (ShortcutCache.TryGetValue(path, out var known) && known.Stamp == stamp) return known.Category;
                var url = File.ReadLines(path).FirstOrDefault(line => line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..].Trim();
                var category = DesktopCategories.Classify(path, url);
                // Cached like a shortcut target, so a desktop link to a Steam game matches the library's entry.
                if (url != null) ShortcutCache[path] = (stamp, category, url);
                return category;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return DesktopCategory.Programs; }
        }
        if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return DesktopCategories.Classify(path);
        object? shell = null, shortcut = null;
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            if (ShortcutCache.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Category;
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            shortcut = ((dynamic)shell!).CreateShortcut(path);
            string target = ((dynamic)shortcut).TargetPath;
            var category = DesktopCategories.Classify(path, target);
            ShortcutCache[path] = (stamp, category, target);
            return category;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or IOException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        { return DesktopCategory.Programs; }
        finally
        {
            if (shortcut != null) Marshal.ReleaseComObject(shortcut);
            if (shell != null) Marshal.ReleaseComObject(shell);
        }
    }
}
