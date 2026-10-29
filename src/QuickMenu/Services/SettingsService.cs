using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using QuickMenu.Models;

namespace QuickMenu.Services;

public sealed class SettingsService
{
    private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValue = "QuickMenu";
    private readonly string _path;
    private readonly bool _manageStartup;
    public SettingsService(string? storagePath = null, bool manageStartup = true)
    {
        _path = storagePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickMenu", "settings.json");
        _manageStartup = manageStartup;
    }

    /// <summary>
    /// Reads the settings. A field that is out of range goes back to its default and the rest are kept; a file that
    /// cannot be read at all gives the defaults. Either way a copy of the file is kept beside it and the log says why,
    /// since the next save replaces it.
    /// </summary>
    public AppSettings Load()
    {
        AppSettings? settings;
        var invalidMotion = false;
        try
        {
            if (!File.Exists(_path)) return new() { Version = AppSettings.CurrentVersion };
            var text = File.ReadAllText(_path);
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("CharacterMotion", out var motion)
                && motion.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                var repaired = JsonNode.Parse(text)!;
                repaired["CharacterMotion"] = true;
                text = repaired.ToJsonString(); invalidMotion = true;
            }
            settings = JsonSerializer.Deserialize<AppSettings>(text);
        }
        catch (JsonException ex) { SetAside($"Settings could not be read ({ex.Message}); the defaults are used."); return Fallback(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.Log(ex); return Fallback(); }
        if (settings == null) { SetAside("Settings were empty; the defaults are used."); return Fallback(); }
        Migrate(settings);
        var reset = Repair(settings);
        if (invalidMotion) reset.Add("CharacterMotion");
        if (reset.Count > 0) SetAside($"Settings had an invalid {string.Join(" and ", reset)}; only that was reset.");
        return settings;
    }

    private static AppSettings Fallback() => new() { Version = AppSettings.CurrentVersion, WelcomeShown = true };

    /// <summary>Keeps a copy of the settings file next to it (settings.broken-&lt;time&gt;.json) and logs why.</summary>
    private void SetAside(string reason)
    {
        try
        {
            var copy = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_path))!,
                $"{Path.GetFileNameWithoutExtension(_path)}.broken-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(_path)}");
            File.Copy(_path, copy, true);
            App.Log(new InvalidDataException($"{reason} The file as it was is kept at {copy}."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.Log(new InvalidDataException(reason, ex)); }
    }

    /// <summary>Puts each invalid field back to its default and names what was reset.</summary>
    private static List<string> Repair(AppSettings settings)
    {
        var reset = new List<string>();
        var defaults = new AppSettings();
        if (!ValidHotkey(settings)) { settings.HotkeyKey = defaults.HotkeyKey; settings.HotkeyModifiers = defaults.HotkeyModifiers; reset.Add("hotkey"); }
        if (!Enum.IsDefined(settings.Theme)) { settings.Theme = defaults.Theme; reset.Add("theme"); }
        return reset;
    }

    public OperationResult Save(AppSettings settings)
    {
        if (!Validate(settings)) return new(false, Loc.T("SettingsFile.Invalid"));
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        RegistryKey? registry = null;
        object? previousValue = null;
        RegistryValueKind previousKind = RegistryValueKind.String;
        bool registryChanged = false;
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(_path))!;
            Directory.CreateDirectory(folder);
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            if (_manageStartup)
            {
                registry = Registry.CurrentUser.CreateSubKey(StartupKey, true);
                previousValue = registry.GetValue(StartupValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (previousValue != null) previousKind = registry.GetValueKind(StartupValue);
                if (settings.StartWithWindows)
                {
                    var executable = Environment.ProcessPath;
                    if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                        return new(false, Loc.T("SettingsFile.NoPath"));
                    registry.SetValue(StartupValue, $"\"{executable}\" --background", RegistryValueKind.String);
                }
                else registry.DeleteValue(StartupValue, false);
                registryChanged = true;
            }
            // Replacement occurs on the same volume and preserves the previous complete JSON on failure.
            File.Move(temporary, _path, true);
            return new(true, Loc.T("Status.SettingsSaved"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            if (registryChanged && registry != null)
            {
                try
                {
                    if (previousValue == null) registry.DeleteValue(StartupValue, false);
                    else registry.SetValue(StartupValue, previousValue, previousKind);
                }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                { return new(false, Loc.T("SettingsFile.RollbackFailed") + rollback.Message); }
            }
            return new(false, Loc.T("SettingsFile.Failed") + ex.Message);
        }
        finally
        {
            registry?.Dispose();
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
        }
    }

    private static void Migrate(AppSettings settings)
    {
        settings.Theme = settings.Theme switch { ThemeMode.Light => ThemeMode.OledDay, ThemeMode.Dark => ThemeMode.Oled, var theme => theme };
        if (settings.ShowCompanion == false) settings.Character = "";
        settings.ShowCompanion = null;
        settings.Character ??= "";
        // 1.x always showed the US (local) clock and Beijing time, and its users have seen the menu already.
        if (settings.Version < 2)
        {
            settings.ClockLabel = "美国时间"; settings.SecondClockZone = "China Standard Time"; settings.SecondClockLabel = "北京时间";
            settings.WelcomeShown = true;
        }
        settings.Version = AppSettings.CurrentVersion;
        settings.ClockLabel ??= ""; settings.SecondClockLabel ??= "";
    }

    private static bool ValidHotkey(AppSettings settings) => settings.HotkeyKey is > 0 and <= 254 && settings.HotkeyModifiers is > 0 and <= 15;
    private static bool Validate(AppSettings settings) => ValidHotkey(settings) && Enum.IsDefined(settings.Theme);
}
