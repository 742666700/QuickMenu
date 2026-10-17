namespace QuickMenu.Services.GameSources;

/// <summary>
/// A game found by one of the sources. <paramref name="Launch"/> is what starts it (an exe path or a store link such as
/// steam://rungameid/…); <paramref name="Exe"/> and <paramref name="Folder"/> say where it lives on disk, when known,
/// for its icon, "打开文件所在位置" and matching a desktop shortcut to the same game.
/// </summary>
public sealed record GameEntry(string Launch, string Name, string? Exe, string? Folder, string Source);

/// <summary>One place games are recorded: a store's own install records, or folders the user listed.</summary>
public interface IGameSource
{
    string Id { get; }
    IEnumerable<GameEntry> Find(CancellationToken token);
}

internal static class GameFiles
{
    /// <summary>
    /// Picks a game's main executable in its install folder: an exe whose name resembles the game's, otherwise the
    /// largest one, skipping uninstallers, crash reporters and the like. With <paramref name="descend"/> it also
    /// looks one folder down (Binaries, bin, Game…) when the top level has none.
    /// </summary>
    public static string? MainExe(string? folder, string? name = null, bool descend = true)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        try
        {
            var candidates = Executables(folder);
            if (candidates.Length == 0 && descend)
                candidates = Directory.GetDirectories(folder).Where(child => !GameLibraryService.IsSkippedFolder(Path.GetFileName(child))).SelectMany(Executables).ToArray();
            if (candidates.Length == 0) return null;
            var wanted = Simple(name ?? Path.GetFileName(folder));
            bool Resembles(string path) => Simple(Path.GetFileNameWithoutExtension(path)) is { Length: > 2 } stem && wanted.Length > 2 && (wanted.Contains(stem) || stem.Contains(wanted));
            return candidates.OrderByDescending(Resembles).ThenByDescending(path => new FileInfo(path).Length).First();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }

        static string[] Executables(string directory)
        {
            try { return Directory.GetFiles(directory, "*.exe").Where(path => !GameLibraryService.IsSkippedExecutable(Path.GetFileNameWithoutExtension(path))).ToArray(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
        }
        static string Simple(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    /// <summary>The exe in a registry value such as "C:\Game\game.exe",0 or C:\Game\game.exe, if it exists.</summary>
    public static string? ExeFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var text = command.Trim();
        if (text.StartsWith('"')) { var end = text.IndexOf('"', 1); text = end > 0 ? text[1..end] : text.Trim('"'); }
        else
        {
            var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe > 0) text = text[..(exe + 4)];
        }
        text = Environment.ExpandEnvironmentVariables(text);
        return text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Path.IsPathFullyQualified(text) && File.Exists(text) ? text : null;
    }
}
