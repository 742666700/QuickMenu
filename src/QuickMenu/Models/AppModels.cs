using System.Text.Json.Serialization;
using System.Windows.Media;

namespace QuickMenu.Models;

public sealed record DesktopItem(string Id, string Name, ImageSource? Icon, string ParsingName, DesktopCategory Category = DesktopCategory.Other, bool IsAiTool = false, DateTime? ModifiedUtc = null, bool IsPinned = false)
{
    public DesktopCategory? DisplayCategory { get; init; }
    /// <summary>For a game from the library: its exe or install folder (for "打开文件所在位置" and 结束进程).</summary>
    public string? Location { get; init; }
    /// <summary>For a game from the library: the source that found it (Steam, Epic, Folders…), so it can be hidden there.</summary>
    public string? GameSource { get; init; }
    public string DisplayType => (DisplayCategory ?? Category) switch
    {
        DesktopCategory.Games => Loc.T("Type.Game"),
        DesktopCategory.Documents => Loc.T("Type.Document"), DesktopCategory.Spreadsheets => Loc.T("Type.Spreadsheet"),
        DesktopCategory.Presentations => Loc.T("Type.Presentation"), DesktopCategory.Office => Loc.T("Type.Office"),
        DesktopCategory.Media => Loc.T("Type.Media"), DesktopCategory.Programs => IsAiTool ? Loc.T("Type.AiTool") : Loc.T("Type.Program"),
        _ => ParsingName.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || ParsingName.StartsWith("::{", StringComparison.Ordinal) ? Loc.T("Type.System") : Loc.T("Type.File")
    };
}
public sealed record WindowEntry(nint Handle, string Title, int ProcessId = 0, DateTime? StartTimeUtc = null);
public sealed record ProcessIdentity(int ProcessId, DateTime StartTimeUtc);
public sealed record RunningApp(string Id, string Name, ImageSource? Icon, ProcessIdentity? Identity,
    IReadOnlyList<WindowEntry> Windows, bool IsForeground, bool IsTray = false)
{
    public string Subtitle => IsTray ? Loc.T("App.TrayUnverified") : Windows.Count == 1 ? Windows[0].Title : Loc.F("App.WindowsSummary", Windows.Count, Windows.FirstOrDefault()?.Title);
    public bool CanTerminate => Identity is { ProcessId: > 4 } && Identity.ProcessId != Environment.ProcessId;
}
// Oled is the pure-black night theme; values are persisted as numbers, so new modes go at the end.
// Light and Dark were retired in 1.6.0: saved settings move to OLED day and OLED night (see SettingsService).
public enum ThemeMode { System, Light, Dark, Oled, OledDay, Cream, ChatGpt, DeepSeek, Gemini }
public sealed class AppSettings
{
    /// <summary>Alt (1) by default, so Alt + Space opens the menu.</summary>
    public uint HotkeyModifiers { get; set; } = 1;
    public uint HotkeyKey { get; set; } = 32;
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public bool StartWithWindows { get; set; }
    /// <summary>The character standing beside the menu, an id from <c>Characters</c>; empty for none. Any theme goes with any character.</summary>
    public string Character { get; set; } = "claude";
    /// <summary>Choosing a theme made from a character's colours brings that character along (still changeable by hand).</summary>
    public bool CharacterFollowsTheme { get; set; } = true;
    /// <summary>The greeting also shows the time: the local clock, and optionally a second time zone.</summary>
    public bool ShowWorldClock { get; set; } = true;
    /// <summary>What the local clock is called in the greeting ("美国时间"); empty shows just the time.</summary>
    public string? ClockLabel { get; set; } = "";
    /// <summary>A Windows time zone id for a second clock (e.g. "China Standard Time"), or null for none.</summary>
    public string? SecondClockZone { get; set; }
    public string? SecondClockLabel { get; set; } = "";
    /// <summary>Interface language: "zh-CN", "en", or empty to follow Windows.</summary>
    public string Language { get; set; } = "";
    /// <summary>A controller can open the menu (Menu + View) and move through it.</summary>
    public bool Gamepad { get; set; } = true;
    /// <summary>The first-run welcome was shown.</summary>
    public bool WelcomeShown { get; set; }
    /// <summary>Settings format: files without it (0) come from 1.x and are migrated once (see SettingsService).</summary>
    public int Version { get; set; }
    public const int CurrentVersion = 2;
    public AppSettings Clone() => (AppSettings)MemberwiseClone();
    /// <summary>Early 1.6.0 builds only had an on/off switch for Claude娘; read once into <see cref="Character"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ShowCompanion { get; set; }
}
public sealed record OperationResult(bool Success, string Message);
