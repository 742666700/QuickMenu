using System.Text.Json;
using QuickMenu.Models;

namespace QuickMenu.Services;

/// <summary>Reads and writes categories.json: the user's own categories and any built-in rules they changed.</summary>
public sealed class CategoryService(string? path = null)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickMenu", "categories.json");
    private readonly string _path = path ?? DefaultPath;

    public CategorySettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new();
            var settings = JsonSerializer.Deserialize<CategorySettings>(File.ReadAllText(_path)) ?? new();
            settings.Custom = (settings.Custom ?? []).Where(category => !string.IsNullOrWhiteSpace(category.Id) && !string.IsNullOrWhiteSpace(category.Name))
                .GroupBy(category => category.Id).Select(group => group.First()).ToList();
            foreach (var category in settings.Custom) { category.Keywords ??= []; category.Extensions ??= []; category.PathContains ??= []; }
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { App.Log(ex); return new(); }
    }

    public OperationResult Save(CategorySettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            File.Move(temporary, _path, overwrite: true);
            return new(true, Loc.T("Category.Saved"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.Log(ex); return new(false, Loc.T("Category.SaveFailed") + ex.Message); }
    }

    /// <summary>Splits what was typed into a rule box: commas (either width), 、, semicolons or new lines separate entries.</summary>
    public static List<string> SplitList(string text, bool spacesSeparate = false)
    {
        var separators = new[] { ',', '，', '、', ';', '；', '\n', '\r' };
        var parts = spacesSeparate ? text.Split(separators.Append(' ').Append('\t').ToArray(), StringSplitOptions.RemoveEmptyEntries) : text.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        return parts.Select(part => part.Trim()).Where(part => part.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
