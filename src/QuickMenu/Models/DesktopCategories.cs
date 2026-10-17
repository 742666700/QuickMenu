using System.Text.RegularExpressions;

namespace QuickMenu.Models;

// Values are persisted as numbers in desktop preferences, so new categories go at the end.
public enum DesktopCategory { All, Documents, Spreadsheets, Presentations, Media, Programs, Other, Office, AiTools, Games }

/// <summary>
/// The automatic classification. Its word and extension lists have defaults here and can be changed in settings
/// (<see cref="Configure"/>); custom categories are applied on top, in the menu.
/// </summary>
public static class DesktopCategories
{
    public static readonly IReadOnlyList<string> DefaultAiKeywords =
    [
        "ChatGPT", "Codex", "Claude", "Cursor", "Grok", "DeepSeek", "OpenCode", "Qoder", "QoderCN", "Trae", "Antigravity", "Gemini",
        "Copilot", "Aider", "Kimi", "Doubao", "Chatbox", "Cherry Studio", "豆包", "通义千问", "元宝", "文心一言", "智谱清言"
    ];
    public static readonly IReadOnlyList<string> DefaultGameFolders = ["steamapps\\common", "Games", "SteamLibrary", "WeGameApps"];
    public static readonly IReadOnlyDictionary<DesktopCategory, IReadOnlyList<string>> DefaultExtensions = new Dictionary<DesktopCategory, IReadOnlyList<string>>
    {
        [DesktopCategory.Documents] = ["doc", "docx", "docm", "pdf", "txt", "rtf", "md", "odt", "wps"],
        [DesktopCategory.Spreadsheets] = ["xls", "xlsx", "xlsm", "xlsb", "csv", "ods", "et", "ett"],
        [DesktopCategory.Presentations] = ["ppt", "pptx", "pptm", "pps", "ppsx", "odp", "dps"],
        [DesktopCategory.Media] = ["png", "jpg", "jpeg", "gif", "bmp", "webp", "heic", "tif", "tiff", "svg", "ico", "avif", "mp4", "mkv", "avi", "mov", "wmv", "webm", "m4v", "mpeg", "mpg"]
    };
    private static readonly HashSet<string> ProgramExtensions = [".lnk", ".url", ".exe", ".msi", ".appref-ms", ".bat", ".cmd", ".ps1"];

    private sealed record Rules(Regex? Ai, Regex? GameFolder, IReadOnlyDictionary<string, DesktopCategory> Extensions);
    private static volatile Rules _rules = Build(null);

    /// <summary>Applies the rules from settings; null or missing lists mean the defaults.</summary>
    public static void Configure(CategorySettings? settings) => _rules = Build(settings);

    private static Rules Build(CategorySettings? settings)
    {
        var extensions = new Dictionary<string, DesktopCategory>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, defaults) in DefaultExtensions)
            foreach (var extension in settings?.Extensions?.GetValueOrDefault(category.ToString()) ?? defaults)
                extensions.TryAdd("." + extension.Trim().TrimStart('.'), category);
        return new(WordPattern(settings?.AiKeywords ?? DefaultAiKeywords), FolderPattern(settings?.GameFolders ?? DefaultGameFolders), extensions);
    }

    // Latin words must stand alone ("Trae" matches "Trae CN" but not "Traefik"); a space may also be a hyphen or nothing.
    // Words in other scripts match anywhere.
    private static Regex? WordPattern(IEnumerable<string> words)
    {
        var parts = words.Select(word => word.Trim()).Where(word => word.Length > 0).Select(word =>
        {
            var pattern = string.Join(@"[\s-]?", word.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));
            return word.All(c => c < 128) ? $@"(?<![\p{{L}}\p{{N}}]){pattern}(?![\p{{L}}\p{{N}}])" : pattern;
        }).ToArray();
        return parts.Length == 0 ? null : new Regex(string.Join("|", parts), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static Regex? FolderPattern(IEnumerable<string> folders)
    {
        var parts = folders.Select(folder => folder.Trim().Trim('\\', '/').Replace('/', '\\')).Where(folder => folder.Length > 0).Select(Regex.Escape).ToArray();
        return parts.Length == 0 ? null : new Regex($@"\\({string.Join("|", parts)})\\", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static bool Matches(DesktopCategory filter, DesktopCategory item, bool isAiTool = false) => filter switch
    {
        DesktopCategory.All => true,
        DesktopCategory.Office => item is DesktopCategory.Office or DesktopCategory.Documents or DesktopCategory.Spreadsheets or DesktopCategory.Presentations,
        DesktopCategory.AiTools => isAiTool,
        _ => filter == item
    };

    public static bool IsAiTool(string path, string name, string? target = null)
    {
        if (!IsLauncher(path) || _rules.Ai is not { } pattern) return false;
        return pattern.IsMatch(name + " " + Path.GetFileNameWithoutExtension(target ?? path));
    }
    /// <summary>A desktop folder named like "AI工具" / "AI Tools" is unpacked: its launchers are listed as AI tools.</summary>
    public static bool IsAiFolder(string name) => Regex.IsMatch(name, @"^\s*AI\s*(工具|tools?)?\s*$", RegexOptions.IgnoreCase);

    public static bool IsLauncher(string path) => Path.GetExtension(path).ToLowerInvariant() is ".lnk" or ".url" or ".exe" or ".appref-ms" or ".ps1" or ".cmd" or ".bat";

    /// <summary>Launchers pointing into a game library folder, or at a Steam / Epic game link.</summary>
    private static bool IsGameLauncher(string extension, string target)
    {
        if (extension is not (".exe" or ".lnk" or ".url" or ".bat" or ".cmd")) return false;
        if (target.StartsWith("steam:", StringComparison.OrdinalIgnoreCase) || target.StartsWith("com.epicgames.launcher:", StringComparison.OrdinalIgnoreCase)) return true;
        // Folder names only count on disk, so a web link like https://…/games/ stays a normal shortcut.
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && !uri.IsFile) return false;
        return _rules.GameFolder?.IsMatch(target.Replace('/', '\\')) == true;
    }

    public static DesktopCategory Classify(string parsingName, string? shortcutTarget = null, bool isFolder = false)
    {
        if (isFolder || string.IsNullOrWhiteSpace(parsingName)) return DesktopCategory.Other;
        var extension = Path.GetExtension(parsingName).ToLowerInvariant();
        if (IsGameLauncher(extension, shortcutTarget ?? parsingName) && (string.IsNullOrWhiteSpace(shortcutTarget) || Classify(shortcutTarget) is DesktopCategory.Programs or DesktopCategory.Games or DesktopCategory.Other)) return DesktopCategory.Games;
        if (extension == ".lnk" && !string.IsNullOrWhiteSpace(shortcutTarget))
        {
            var target = Classify(shortcutTarget);
            if (target is DesktopCategory.Documents or DesktopCategory.Spreadsheets or DesktopCategory.Presentations or DesktopCategory.Media) return target;
        }
        if (_rules.Extensions.TryGetValue(extension, out var category)) return category;
        return ProgramExtensions.Contains(extension) ? DesktopCategory.Programs : DesktopCategory.Other;
    }
}
