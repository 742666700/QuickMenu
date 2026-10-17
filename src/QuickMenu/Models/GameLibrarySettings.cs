namespace QuickMenu.Models;

/// <summary>
/// What the game library reads, saved in game-library.json: which stores to read install records from, extra folders
/// to scan, games added by hand, and games hidden from the menu (by launch target).
/// </summary>
public sealed class GameLibrarySettings
{
    /// <summary>Store id → on/off. A store missing from the map is on, so stores added later start enabled.</summary>
    public Dictionary<string, bool> Sources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Folders { get; set; } = [];
    public List<string> Added { get; set; } = [];
    public List<string> Hidden { get; set; } = [];

    public bool IsEnabled(string source) => !Sources.TryGetValue(source, out var enabled) || enabled;
}
