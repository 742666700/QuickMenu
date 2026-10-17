using Microsoft.Win32;
using QuickMenu.Models;

namespace QuickMenu.Services;

public static class ThemeService
{
    /// <summary>The theme actually drawn: following the system means OLED day or OLED night.</summary>
    public static ThemeMode Resolve(ThemeMode mode) => mode switch
    {
        ThemeMode.System => IsDark(ThemeMode.System) ? ThemeMode.Oled : ThemeMode.OledDay,
        ThemeMode.Light => ThemeMode.OledDay,
        ThemeMode.Dark => ThemeMode.Oled,
        _ => mode
    };

    public static bool IsDark(ThemeMode mode)
    {
        if (mode is ThemeMode.Dark or ThemeMode.Oled or ThemeMode.Gemini) return true;
        if (mode is ThemeMode.Light or ThemeMode.OledDay or ThemeMode.Cream or ThemeMode.ChatGpt or ThemeMode.DeepSeek) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }
}
