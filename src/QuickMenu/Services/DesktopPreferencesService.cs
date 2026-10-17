using System.Collections;
using System.Text.Json;
using QuickMenu.Models;

namespace QuickMenu.Services;

// Custom is the order the user set by dragging; values are persisted as numbers, so new modes go at the end.
public enum DesktopSort { Frequent, Modified, Type, Custom }
// Category is a built-in category chosen by hand, Custom the id of a custom one; AiTool overrides AI-tool detection.
public sealed record DesktopPreference(bool Pinned = false, long LaunchCount = 0, DateTime? LastLaunchUtc = null, DesktopCategory? Category = null, string? Custom = null, bool? AiTool = null);
public sealed class DesktopPreferencesService : IComparer
{
    private sealed class Store
    {
        public DesktopSort Sort { get; set; }
        public List<string> Order { get; set; } = new();
        public Dictionary<string, DesktopPreference> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
    private Store _store = new();
    private Dictionary<string, int> _orderIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _path;
    public DesktopSort Sort => _store.Sort;
    public IReadOnlyList<string> Order => _store.Order;
    public DesktopPreferencesService(string? path = null, bool ephemeral = false)
    {
        _path = ephemeral ? null : path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickMenu", "desktop-preferences.json");
        if (_path == null) return;
        try
        {
            if (!File.Exists(_path)) return;
            var loaded = JsonSerializer.Deserialize<Store>(File.ReadAllText(_path));
            if (loaded == null || !Enum.IsDefined(loaded.Sort) || loaded.Items == null || loaded.Items.Any(pair => pair.Value == null || pair.Value.LaunchCount < 0 || pair.Value.Category is { } category && (!Enum.IsDefined(category) || category is DesktopCategory.All or DesktopCategory.AiTools))) return;
            loaded.Items = new(loaded.Items, StringComparer.OrdinalIgnoreCase);
            loaded.Order ??= new();
            _store = loaded;
            IndexOrder();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
    }
    public DesktopPreference Get(string id) => _store.Items.GetValueOrDefault(id) ?? new();
    public DesktopCategory Category(DesktopItem item) => Get(item.Id).Category ?? item.Category;
    public OperationResult SetSort(DesktopSort sort) => Enum.IsDefined(sort) ? Change(store => store.Sort = sort) : new(false, Loc.T("Prefs.InvalidSort"));
    public OperationResult SetPinned(string id, bool pinned) => Change(store => store.Items[id] = Get(id) with { Pinned = pinned });
    /// <summary>Marks an item with a built-in category, or with null returns it to automatic recognition (AI tool mark included).</summary>
    public OperationResult SetCategory(string id, DesktopCategory? category) => category is { } value && (!Enum.IsDefined(value) || value is DesktopCategory.All or DesktopCategory.AiTools) ? new(false, Loc.T("Category.Invalid"))
        : Change(store => store.Items[id] = Get(id) with { Category = category, Custom = null, AiTool = category is null ? null : Get(id).AiTool });
    public OperationResult SetCustom(string id, string? custom) => Change(store => store.Items[id] = Get(id) with { Custom = custom, Category = null });
    public OperationResult SetAiTool(string id, bool? aiTool) => Change(store => store.Items[id] = Get(id) with { AiTool = aiTool });
    /// <summary>Saves a dragged arrangement (item ids, first to last) and switches to the custom sort.</summary>
    public OperationResult SetOrder(IEnumerable<string> ids) => Change(store => { store.Sort = DesktopSort.Custom; store.Order = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList(); });
    public OperationResult RecordLaunch(string id) => Change(store => store.Items[id] = Get(id) with { LaunchCount = Math.Min(Get(id).LaunchCount, long.MaxValue - 1) + 1, LastLaunchUtc = DateTime.UtcNow });
    private OperationResult Change(Action<Store> update)
    {
        var previous = _store;
        _store = new Store { Sort = previous.Sort, Order = new(previous.Order), Items = new(previous.Items, StringComparer.OrdinalIgnoreCase) };
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            update(_store);
            if (_path != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                File.WriteAllText(temporary, JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, _path, true);
            }
            IndexOrder();
            return new(true, Loc.T("Prefs.Saved"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { _store = previous; IndexOrder(); return new(false, Loc.T("Prefs.SaveFailed") + ex.Message); }
        finally { try { if (_path != null && File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    public int Compare(object? x, object? y)
    {
        if (x is not DesktopItem a || y is not DesktopItem b) return 0;
        var left = Get(a.Id); var right = Get(b.Id);
        var result = right.Pinned.CompareTo(left.Pinned);
        if (result != 0) return result;
        result = Sort switch
        {
            DesktopSort.Modified => Nullable.Compare(b.ModifiedUtc, a.ModifiedUtc),
            DesktopSort.Type => StringComparer.CurrentCulture.Compare(TypeName(a), TypeName(b)),
            // Items never dragged (new on the desktop) follow the arranged ones, by name.
            DesktopSort.Custom => _orderIndex.GetValueOrDefault(a.Id, int.MaxValue).CompareTo(_orderIndex.GetValueOrDefault(b.Id, int.MaxValue)),
            _ => right.LaunchCount.CompareTo(left.LaunchCount)
        };
        if (result == 0 && Sort == DesktopSort.Frequent) result = Nullable.Compare(right.LastLaunchUtc, left.LastLaunchUtc);
        if (result == 0) result = StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name);
        return result == 0 ? StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id) : result;
    }
    private void IndexOrder()
    {
        _orderIndex = new(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _store.Order.Count; i++) _orderIndex.TryAdd(_store.Order[i], i);
    }
    private string TypeName(DesktopItem item) => $"{(int)Category(item):D2}:{Path.GetExtension(item.ParsingName).ToLowerInvariant()}";
}
