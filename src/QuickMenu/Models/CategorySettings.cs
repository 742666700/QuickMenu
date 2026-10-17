namespace QuickMenu.Models;

/// <summary>
/// A category the user made: items whose name or file matches any of its rules land in it, as do items marked by hand.
/// </summary>
public sealed class CustomCategory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public bool ShowAsTab { get; set; } = true;
    /// <summary>Words found in the item's name or file name, ignoring case.</summary>
    public List<string> Keywords { get; set; } = [];
    /// <summary>File extensions of the item or of a shortcut's target, without the dot.</summary>
    public List<string> Extensions { get; set; } = [];
    /// <summary>Text found in the item's path or a shortcut's target path, e.g. a folder name.</summary>
    public List<string> PathContains { get; set; } = [];

    public bool HasRules => Keywords.Count + Extensions.Count + PathContains.Count > 0;

    public bool Matches(string name, string path, string? target)
    {
        var files = new[] { path, target }.OfType<string>().Where(file => file.Length > 0).ToArray();
        if (Keywords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase) || files.Any(file => SafeFileName(file).Contains(word, StringComparison.OrdinalIgnoreCase)))) return true;
        if (Extensions.Any(extension => files.Any(file => SafeExtension(file).Equals(extension, StringComparison.OrdinalIgnoreCase)))) return true;
        return PathContains.Any(fragment => files.Any(file => file.Replace('/', '\\').Contains(fragment.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase)));
    }

    private static string SafeFileName(string path) { try { return Path.GetFileName(path); } catch (ArgumentException) { return path; } }
    private static string SafeExtension(string path) { try { return Path.GetExtension(path).TrimStart('.'); } catch (ArgumentException) { return ""; } }
}

/// <summary>
/// The user's categories and the built-in rules they changed, saved in categories.json. A built-in rule left null
/// uses the default list in <see cref="DesktopCategories"/>, so improved defaults reach everyone who never edited it.
/// </summary>
public sealed class CategorySettings
{
    public List<CustomCategory> Custom { get; set; } = [];
    public bool ShowGamesTab { get; set; } = true;
    public bool ShowAiTab { get; set; } = true;
    public List<string>? AiKeywords { get; set; }
    public List<string>? GameFolders { get; set; }
    /// <summary>Built-in category name (Documents, Spreadsheets, Presentations, Media) → its extensions.</summary>
    public Dictionary<string, List<string>>? Extensions { get; set; }
}
