using System.IO;
using System.Text.Json;
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

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new() { Version = AppSettings.CurrentVersion };
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path));
            if (settings != null) Migrate(settings);
            return settings != null && Validate(settings) ? settings : new() { Version = AppSettings.CurrentVersion, WelcomeShown = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new() { Version = AppSettings.CurrentVersion, WelcomeShown = true }; }
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

    private static bool Validate(AppSettings settings) => settings.HotkeyKey is > 0 and <= 254 &&
        settings.HotkeyModifiers is > 0 and <= 15 && Enum.IsDefined(settings.Theme);
}
