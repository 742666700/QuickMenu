using System.Globalization;
using System.Text.Json;
using System.Windows;

namespace QuickMenu;

/// <summary>
/// Interface text in Simplified Chinese or English, from Strings/&lt;language&gt;.json embedded in the program. Code
/// asks <see cref="T"/> / <see cref="F"/>; XAML uses {DynamicResource S.&lt;key&gt;}, which <see cref="Use"/> keeps current,
/// so switching language updates an open window. A key missing in English falls back to Chinese, then to the key.
/// </summary>
public static class Loc
{
    public const string Chinese = "zh-CN", English = "en";
    private static readonly IReadOnlyDictionary<string, string> Fallback = Load(Chinese);
    private static volatile IReadOnlyDictionary<string, string> _strings = Fallback;
    private static ResourceDictionary? _resources;

    public static string Current { get; private set; } = Chinese;
    public static bool IsChinese => Current == Chinese;
    public static event Action? Changed;

    /// <summary>"zh-CN" or "en" as chosen; anything else follows Windows: Chinese for a Chinese UI, English otherwise.</summary>
    public static string Resolve(string? preference) => preference is Chinese or English ? preference
        : CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? Chinese : English;

    public static void Use(string? preference)
    {
        var language = Resolve(preference);
        _strings = language == Chinese ? Fallback : Load(language);
        Current = language;
        if (Application.Current is { } app)
        {
            var resources = new ResourceDictionary();
            foreach (var key in Fallback.Keys) resources["S." + key] = T(key);
            if (_resources != null) app.Resources.MergedDictionaries.Remove(_resources);
            app.Resources.MergedDictionaries.Add(_resources = resources);
        }
        Changed?.Invoke();
    }

    public static string T(string key) => _strings.TryGetValue(key, out var text) ? text : Fallback.GetValueOrDefault(key, key);
    public static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    /// <summary>Every key of a language's table, for the self-check.</summary>
    public static IReadOnlyCollection<string> Keys(string language) => (language == Chinese ? Fallback : Load(language)).Keys.ToArray();

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"Strings.{language}.json");
        return stream is null ? new Dictionary<string, string>() : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
