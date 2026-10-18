using System.Windows;

namespace QuickMenu;

/// <summary>Where the menu goes on a monitor's work area, in device pixels, and how big the character beside it may be.</summary>
/// <param name="Width">Menu width in DIPs.</param>
/// <param name="Height">Menu height in DIPs.</param>
/// <param name="CompanionScale">Her size relative to full size; 0 when she does not fit at all.</param>
public readonly record struct MenuPlan(double Width, double Height, int Left, int Top, int PixelWidth, int PixelHeight, double CompanionScale);

public static class MenuPlacement
{
    /// <summary>Gap kept between the menu, or her, and the edges of a small screen, in DIPs.</summary>
    private const double Edge = 12;
    /// <summary>On a small screen the menu may narrow to this before she is made smaller.</summary>
    private const double NarrowestBesideHer = 600;
    /// <summary>Smaller than this she is hard to make out, so she stays away instead.</summary>
    public const double SmallestCompanion = 0.6;

    /// <summary>
    /// The menu is centred when there is room for her to its right (or nobody to make room for). Otherwise, as on
    /// handhelds such as a 1080p 7-inch screen at 150 %, the menu and she are centred together as one group: the
    /// menu moves left, narrows if it must, and she is drawn smaller if she is still too tall or too wide.
    /// </summary>
    /// <param name="companion">Her footprint at full size in DIPs (width right of the menu, height above its bottom), or empty.</param>
    public static MenuPlan Plan(int workLeft, int workTop, int workWidth, int workHeight, double scale, double preferredWidth, Size companion)
    {
        var width = Math.Min(preferredWidth, Math.Max(320, (workWidth - 24) / scale));
        var height = Math.Min(600, Math.Max(280, (workHeight - 24) / scale));
        var centred = Place(width, (workWidth - Pixels(width, scale)) / 2, (workHeight - Pixels(height, scale)) / 2, 0);
        if (companion.Width <= 0 || companion.Height <= 0) return centred;
        var right = (int)Math.Round(companion.Width * scale);
        var above = (int)Math.Round(Math.Max(0, companion.Height - height) * scale);
        if (centred.Left + centred.PixelWidth + right <= workLeft + workWidth && centred.Top - above >= workTop) return centred with { CompanionScale = 1 };

        double roomWide = workWidth / scale - 2 * Edge, roomTall = workHeight / scale - 2 * Edge;
        var size = Math.Min(1, roomTall / companion.Height);
        var shifted = Math.Max(Math.Min(width, NarrowestBesideHer), Math.Min(width, roomWide - size * companion.Width));
        size = Math.Min(size, (roomWide - shifted) / companion.Width);
        if (size < SmallestCompanion) return centred;
        var groupWide = shifted + size * companion.Width;
        var groupTall = Math.Max(height, size * companion.Height);
        return Place(shifted, (workWidth - Pixels(groupWide, scale)) / 2,
            (workHeight - Pixels(groupTall, scale)) / 2 + Pixels(groupTall - height, scale), size);

        MenuPlan Place(double menuWidth, int left, int top, double companionScale) =>
            new(menuWidth, height, workLeft + left, workTop + top,
                Math.Min(workWidth, Pixels(menuWidth, scale)), Math.Min(workHeight, Pixels(height, scale)), companionScale);
    }

    private static int Pixels(double dips, double scale) => (int)Math.Round(dips * scale);
}
