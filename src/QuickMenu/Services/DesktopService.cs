using System.Runtime.InteropServices;
using QuickMenu.Interop;
using QuickMenu.Models;

namespace QuickMenu.Services;

public sealed class DesktopService
{
    private readonly IconService _icons = new();
    private readonly GameLibraryService _games = new();
    public GameLibraryService Games => _games;
    public void RefreshGames() => _games.Invalidate();
    public Task<IReadOnlyList<DesktopItem>> GetItemsAsync(CancellationToken cancellationToken = default) =>
        StaWorker.Run<IReadOnlyList<DesktopItem>>(() => Enumerate(cancellationToken), cancellationToken);

    private IReadOnlyList<DesktopItem> Enumerate(CancellationToken cancellationToken)
    {
        var items = new List<DesktopItem>();
        IShellFolder? desktop = null;
        IEnumIDList? enumerator = null;
        try
        {
            using var apartment = new ComScope(2);
            Marshal.ThrowExceptionForHR(ShellInterop.SHGetDesktopFolder(out desktop));
            Marshal.ThrowExceptionForHR(desktop.EnumObjects(0, 0x20 | 0x40, out enumerator));
            if (enumerator == null) return items;
            while (enumerator.Next(1, out var pidl, out var fetched) == 0 && fetched == 1)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var parsing = GetName(pidl, 0x80028000);
                    var name = GetName(pidl, 0);
                    if (string.IsNullOrWhiteSpace(parsing) || string.IsNullOrWhiteSpace(name)) continue;
                    if (DesktopCategories.IsAiFolder(name) && Directory.Exists(parsing)) { AddAiFolder(parsing, items, cancellationToken); continue; }
                    items.Add(new(parsing, name, _icons.GetForParsingName(parsing), parsing, DesktopCategoryService.GetCategory(parsing), DesktopCategoryService.IsAiTool(parsing, name), ReadModifiedUtc(parsing)));
                }
                catch (Exception ex) when (ex is COMException or ArgumentException) { /* An unavailable item must not hide other items. */ }
                finally { Marshal.FreeCoTaskMem(pidl); }
            }
        }
        finally
        {
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            if (desktop != null) Marshal.ReleaseComObject(desktop);
        }
        AddGames(items, cancellationToken);
        return items.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    // Launchers kept in the desktop's AI folder appear directly in the menu, always tagged as AI tools; the folder itself is not listed.
    private void AddAiFolder(string folder, List<DesktopItem> items, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder).Where(DesktopCategories.IsLauncher))
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(new(path, Path.GetFileNameWithoutExtension(path), _icons.GetForParsingName(path), path, DesktopCategoryService.GetCategory(path), IsAiTool: true, ReadModifiedUtc(path)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* An unreadable folder must not hide the rest of the desktop. */ }
    }

    // Games from the library join the desktop list. A desktop shortcut to the same game (same exe, same store link,
    // or anything inside its install folder) is kept with its own name and icon and marked as a game instead of
    // being listed twice.
    private void AddGames(List<DesktopItem> items, CancellationToken cancellationToken)
    {
        string Target(DesktopItem item) => DesktopCategoryService.GetShortcutTarget(item.ParsingName) ?? item.ParsingName;
        foreach (var game in _games.GetGames(cancellationToken))
        {
            var folder = IsInstallFolder(game.Folder) ? game.Folder!.TrimEnd('\\') + "\\" : null;
            bool Same(DesktopItem item) => item.GameSource is null && Target(item) is var target
                && (string.Equals(target, game.Launch, StringComparison.OrdinalIgnoreCase) || string.Equals(target, game.Exe, StringComparison.OrdinalIgnoreCase)
                    || (folder != null && target.StartsWith(folder, StringComparison.OrdinalIgnoreCase)));
            var matched = false;
            for (var index = 0; index < items.Count; index++)
                if (Same(items[index])) { items[index] = items[index] with { Category = DesktopCategory.Games }; matched = true; }
            if (matched) continue;
            var icon = game.Exe ?? (game.Launch.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase) ? game.Launch : null) ?? game.Folder ?? game.Launch;
            items.Add(new(game.Launch, game.Name, _icons.GetForParsingName(icon), game.Launch, DesktopCategory.Games, ModifiedUtc: ReadModifiedUtc(game.Exe ?? game.Launch))
                { Location = game.Exe ?? game.Folder, GameSource = game.Source });
        }
    }

    // A game's own folder, not a drive root or a shared folder like Program Files that holds other programs too.
    private static bool IsInstallFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) return false;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (Path.GetPathRoot(full) is { } root && string.Equals(Path.TrimEndingDirectorySeparator(root), full, StringComparison.OrdinalIgnoreCase)) return false;
        var shared = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.Desktop }
            .Select(Environment.GetFolderPath).Where(path => path.Length > 0);
        return !shared.Any(path => string.Equals(Path.TrimEndingDirectorySeparator(path), full, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetName(nint pidl, uint type)
    {
        if (ShellInterop.SHGetNameFromIDList(pidl, type, out var text) < 0) return null;
        try { return Marshal.PtrToStringUni(text); }
        finally { Marshal.FreeCoTaskMem(text); }
    }

    private static DateTime? ReadModifiedUtc(string path)
    {
        try { return File.Exists(path) || Directory.Exists(path) ? File.GetLastWriteTimeUtc(path) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { return null; }
    }

    /// <summary>The kinds of link the game sources build: Steam, Epic, Ubisoft and Xbox (packaged app) games.</summary>
    private static readonly string[] StoreLinks = ["steam://rungameid/", "com.epicgames.launcher://apps/", "uplay://launch/", @"shell:AppsFolder\"];
    public static bool IsStoreLink(string link) => StoreLinks.Any(prefix => link.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public OperationResult Open(DesktopItem item)
    {
        // Store games start through their store's link (steam://, uplay://, the Epic launcher, shell:AppsFolder\…). Only
        // those kinds of link are started: anything else in game-library.json that is not a full path is refused.
        if (item.GameSource != null && !Path.IsPathFullyQualified(item.ParsingName))
        {
            if (!IsStoreLink(item.ParsingName)) return new(false, Loc.T("Games.StartFailed") + Loc.T("Games.UnknownLink"));
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.ParsingName) { UseShellExecute = true })?.Dispose();
                return new(true, Loc.T("Games.Started"));
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException) { return new(false, Loc.T("Games.StartFailed") + ex.Message); }
        }
        // Many games only find their data when started from their own folder.
        if (item.Category == DesktopCategory.Games && Path.GetExtension(item.ParsingName).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.ParsingName) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(item.ParsingName)! })?.Dispose();
                return new(true, Loc.T("Games.Started"));
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException) { return new(false, Loc.T("Games.StartFailed") + ex.Message); }
        }
        nint pidl = 0;
        try
        {
            using var apartment = new ComScope(0);
            if (string.IsNullOrWhiteSpace(item.ParsingName) || ShellInterop.SHParseDisplayName(item.ParsingName, 0, out pidl, 0, out _) < 0)
                return new(false, Loc.T("Desktop.Stale"));
            var info = new ShellInterop.SHELLEXECUTEINFO
            {
                Size = Marshal.SizeOf<ShellInterop.SHELLEXECUTEINFO>(), Mask = 0x4 | 0x100 | 0x400,
                IdList = pidl, Show = 1
            };
            return ShellInterop.ShellExecuteEx(ref info) ? new(true, Loc.T("Desktop.Opened")) : new(false, Loc.T("Desktop.OpenFailed"));
        }
        catch (Exception ex) when (ex is COMException or ArgumentException) { return new(false, Loc.T("Desktop.OpenError") + ex.Message); }
        finally { if (pidl != 0) Marshal.FreeCoTaskMem(pidl); }
    }
}
