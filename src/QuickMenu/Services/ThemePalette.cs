using QuickMenu.Models;

namespace QuickMenu.Services;

// Warm "paper and clay" palette. Keys are the application-wide brush resource names.
public static class ThemePalette
{
    public static readonly string[] Keys =
    {
        "SurfaceBrush", "SidebarBrush", "TextBrush", "MutedBrush", "BorderBrush", "HoverBrush", "SelectedBrush", "AccentBrush", "InputBrush", "DangerBrush",
        "ChipBrush", "PopupBrush", "CardBrush", "SelectionBorderBrush", "IconPlateBrush", "IconEdgeBrush", "DangerSoftBrush", "ActionBrush",
        "OnActionBrush", "AccentTextBrush", "OverlayBrush", "ToggleOffBrush"
    };

    // Light and Dark now only colour the tray menu, which is drawn by Windows Forms and cannot show the themes' gradients.
    private static readonly string[] Light =
    {
        "#FFFAF9F5", "#FFF0EEE6", "#FF141413", "#FF6B6A64", "#FFE5E2D9", "#FFF0EEE6", "#FFF5E6DD", "#FFC96442", "#FFFFFFFF", "#FFB53333",
        "#00000000", "#FFFDFCF9", "#FFF0EEE6", "#FFC96442", "#FFECE9E0", "#00000000", "#FFF7E1DE", "#FFB5573A",
        "#FFFFFFFF", "#FF9C4A2B", "#73141413", "#FFD3CFC4"
    };

    private static readonly string[] Dark =
    {
        "#FF262624", "#FF1F1E1D", "#FFF5F4ED", "#FFA6A39B", "#FF3D3C38", "#FF30302E", "#FF3A2E28", "#FFD97757", "#FF1F1E1D", "#FFE5736B",
        "#00000000", "#FF2B2A27", "#FF30302E", "#FFD97757", "#FF3A3936", "#00000000", "#FF3D2422", "#FFD97757",
        "#FF1F1E1D", "#FFE08A6D", "#8C000000", "#FF4A4843"
    };

    // OLED night: every surface is #000000 so those pixels switch off; white text and an emissive clay accent give maximum contrast.
    private static readonly string[] OledNight =
    {
        "#FF000000", "#FF000000", "#FFFFFFFF", "#FFB5B2AA", "#FF242424", "#FF121212", "#FF22130C", "#FFFF7A45", "#FF000000", "#FFFF5A5A",
        "#00000000", "#FF000000", "#FF000000", "#FFFF7A45", "#FF000000", "#FF2A2A2A", "#FF2A0808", "#FFFF7A45",
        "#FF000000", "#FFFF9468", "#D9000000", "#FF2E2E2E"
    };

    // OLED day: luminous gradient backdrop (see Backdrops) under translucent white layers, with saturated accents.
    private static readonly string[] OledDay =
    {
        "#FFFBF5F3", "#66FFFFFF", "#FF17131F", "#FF46424F", "#1F17131F", "#73FFFFFF", "#33F2643A", "#FFF0582E", "#D9FFFFFF", "#FFD02F3F",
        "#00000000", "#F7FFFFFF", "#8CFFFFFF", "#FFF0582E", "#A6FFFFFF", "#1417131F", "#29D02F3F", "#FFCC4420",
        "#FFFFFFFF", "#FF8A2A0B", "#5917131F", "#4017131F"
    };

    // Cream daisy (Claude colours): taken from Claude娘's outfit. Cream dress for surfaces, vivid orange hair for
    // the accent, ribbon black for text, and the gold trim for hairlines and the selection edge.
    private static readonly string[] Cream =
    {
        "#FFFCF7EF", "#FFF4EADB", "#FF1C1512", "#FF7A6455", "#FFE7D3B0", "#FFF8EBDA", "#FFFCE1CC", "#FFE8642C", "#FFFFFDF8", "#FFB3261E",
        "#00000000", "#FFFFFCF6", "#FFF6ECDD", "#FFC9A24A", "#FFF3E6D3", "#00000000", "#FFF9DEDA", "#FFD9531E",
        "#FFFFFFFF", "#FFA8431A", "#661C1512", "#FFDCCDB8"
    };

    // Pearl (ChatGPT): taken from ChatGPT娘, the white dragon girl. Porcelain surfaces with a breath of lilac, ink text,
    // an ink-iris accent and ChatGPT's black primary button; the silver-lilac of her wings edges the selection.
    private static readonly string[] Pearl =
    {
        "#FFFCFBFE", "#FFF4F2F8", "#FF15141B", "#FF5C5868", "#FFE7E3EF", "#FFF2EFF7", "#FFEBE6F6", "#FF3B3556", "#FFFFFFFF", "#FFC2363F",
        "#00000000", "#FFFFFFFF", "#FFF7F5FB", "#FF9087BE", "#FFF0EDF6", "#00000000", "#FFF9E3E5", "#FF1D1B24",
        "#FFFFFFFF", "#FF3B3556", "#5915141B", "#FFCFCADB"
    };

    // Deep sea (DeepSeek): clear water in DeepSeek blue. Light, cool surfaces, deep navy text, DeepSeek blue for the
    // accent and the selection, and a blue gradient on the primary button.
    private static readonly string[] DeepSea =
    {
        "#FFFBFCFF", "#FFEEF2FB", "#FF131B3B", "#FF424B6C", "#FFDCE2F1", "#FFEEF2FD", "#FFE0E7FF", "#FF4D6BFE", "#FFFFFFFF", "#FFC9354A",
        "#00000000", "#FFFFFFFF", "#FFF3F6FD", "#FF4D6BFE", "#FFEBF0FB", "#00000000", "#FFFCE5E8", "#FF1E2A5E",
        "#FFFFFFFF", "#FF2F45C4", "#66131B3B", "#FFC5CDE3"
    };

    // Starry violet (Gemini): taken from Gemini娘's crystal star hair ornaments. A deep night violet, a neon violet
    // accent, and the ornaments' champagne-gold rim on the selection; Gemini's blue-violet-rose on the primary button.
    private static readonly string[] StarViolet =
    {
        "#FF0E0B1F", "#FF0A0817", "#FFF5F2FF", "#FFAFA8D2", "#FF2A2450", "#FF1B1638", "#FF2C2168", "#FFB69CFF", "#FF161230", "#FFFF6E84",
        "#00000000", "#FF171332", "#FF15112D", "#FFE3C27E", "#FF1E1940", "#FF3A2F6E", "#FF3B1734", "#FF5B49D6",
        "#FFFFFFFF", "#FFC9B8FF", "#B3060410", "#FF3A3463"
    };

    /// <summary>Colours for a resolved theme (see <see cref="ThemeService.Resolve"/>).</summary>
    public static IReadOnlyList<string> Colors(ThemeMode mode) => mode switch
    {
        ThemeMode.Oled => OledNight, ThemeMode.Cream => Cream, ThemeMode.ChatGpt => Pearl,
        ThemeMode.DeepSeek => DeepSea, ThemeMode.Gemini => StarViolet, _ => OledDay
    };

    public static bool IsOled(ThemeMode mode) => mode is ThemeMode.Oled or ThemeMode.OledDay;

    public static string Color(string key, bool dark) => (dark ? Dark : Light)[Array.IndexOf(Keys, key)];

    public static System.Drawing.Color Drawing(string key, bool dark)
    {
        var hex = Color(key, dark);
        return System.Drawing.Color.FromArgb(Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16), Convert.ToInt32(hex[7..9], 16));
    }
}
