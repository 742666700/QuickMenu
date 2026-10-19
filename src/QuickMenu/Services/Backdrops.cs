using System.Windows;
using System.Windows.Media;
using QuickMenu.Models;
using ThemeMode = QuickMenu.Models.ThemeMode;

namespace QuickMenu.Services;

/// <summary>Non-solid brushes for themes that need them. Everything else uses the flat palette.</summary>
public static class Backdrops
{
    /// <summary>Window background; null means use SurfaceBrush.</summary>
    public static Brush? Window(ThemeMode mode) => mode switch
    {
        ThemeMode.OledDay => Daylight(), ThemeMode.Cream => Parlour(),
        ThemeMode.ChatGpt => Pearl(), ThemeMode.DeepSeek => DeepSea(), ThemeMode.Gemini => StarNight(), _ => null
    };

    /// <summary>Primary button fill; null means use the palette's ActionBrush.</summary>
    public static Brush? Action(ThemeMode mode)
    {
        if (mode is ThemeMode.ChatGpt or ThemeMode.DeepSeek or ThemeMode.Gemini)
        {
            // The white button label needs at least 4.5:1 against every part of its gradient.
            var stops = mode switch
            {
                ThemeMode.ChatGpt => new[] { "#2A2735", "#121116" },
                ThemeMode.DeepSeek => new[] { "#4762F0", "#2B3EB8" },
                _ => new[] { "#3C5FC2", "#6E45C8", "#A23E84" }
            };
            var action = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            for (var i = 0; i < stops.Length; i++) action.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stops[i]), i / (double)(stops.Length - 1)));
            action.Freeze();
            return action;
        }
        if (mode is not (ThemeMode.OledDay or ThemeMode.Cream)) return null;
        var brush = mode == ThemeMode.Cream
            ? new LinearGradientBrush(Rgb(0xEE, 0x6E, 0x2E), Rgb(0xC2, 0x34, 0x18), new Point(0, 0), new Point(1, 1))
            : new LinearGradientBrush(Rgb(0xCC, 0x44, 0x20), Rgb(0xB8, 0x33, 0x6E), new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }

    // Apricot-to-sky wash with three soft light pools: coral at the top left, pink at the top right, periwinkle at the bottom right.
    private static Brush Daylight()
    {
        var group = new DrawingGroup();
        var bounds = new RectangleGeometry(new Rect(0, 0, 1, 1));
        var wash = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        wash.GradientStops.Add(new GradientStop(Rgb(0xFF, 0xF1, 0xE8), 0));
        wash.GradientStops.Add(new GradientStop(Rgb(0xFC, 0xEF, 0xF5), 0.5));
        wash.GradientStops.Add(new GradientStop(Rgb(0xEA, 0xF0, 0xFF), 1));
        group.Children.Add(new GeometryDrawing(wash, null, bounds));
        group.Children.Add(new GeometryDrawing(Glow(0xFF, 0xB3, 0x8F, 0xCC, new Point(0.06, 0.0), 0.62), null, bounds));
        group.Children.Add(new GeometryDrawing(Glow(0xFF, 0xC6, 0xE3, 0x80, new Point(0.88, 0.04), 0.42), null, bounds));
        group.Children.Add(new GeometryDrawing(Glow(0x8F, 0xB0, 0xFF, 0x99, new Point(1.0, 1.0), 0.7), null, bounds));
        return Finish(group);
    }

    // Cream daisy: cream paper warming to peach toward the right, with a gold haze at the top left.
    private static Brush Parlour()
    {
        var group = new DrawingGroup();
        var bounds = new RectangleGeometry(new Rect(0, 0, 1, 1));
        var wash = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        wash.GradientStops.Add(new GradientStop(Rgb(0xFE, 0xFA, 0xF3), 0));
        wash.GradientStops.Add(new GradientStop(Rgb(0xFC, 0xF4, 0xE8), 0.55));
        wash.GradientStops.Add(new GradientStop(Rgb(0xFB, 0xE6, 0xD6), 1));
        group.Children.Add(new GeometryDrawing(wash, null, bounds));
        group.Children.Add(new GeometryDrawing(Glow(0xF2, 0xD3, 0x8A, 0x55, new Point(0.0, 0.0), 0.5), null, bounds));
        group.Children.Add(new GeometryDrawing(Glow(0xF5, 0x9A, 0x62, 0x4D, new Point(1.0, 0.75), 0.6), null, bounds));
        return Finish(group);
    }

    // Pearl (ChatGPT): porcelain white with a mother-of-pearl sheen across the top right, where pink, lilac and ice
    // blue melt into each other like the inside of a shell, and a cool lilac shade low on the left.
    private static Brush Pearl()
    {
        var group = new DrawingGroup();
        var bounds = new RectangleGeometry(new Rect(0, 0, 1, 1));
        group.Children.Add(new GeometryDrawing(Vertical(("#FDFCFF", 0), ("#F6F4FA", 1)), null, bounds));
        var sheen = new LinearGradientBrush { StartPoint = new Point(0.42, 0), EndPoint = new Point(1, 0.62) };
        foreach (var (color, offset) in new[] { ("#00F7E4EF", 0.0), ("#8CF7E4EF", 0.3), ("#99E7E1FA", 0.52), ("#8CDDEEFA", 0.74), ("#00DDEEFA", 1.0) })
            sheen.GradientStops.Add(new GradientStop(Hex(color), offset));
        group.Children.Add(new GeometryDrawing(sheen, null, bounds));
        group.Children.Add(new GeometryDrawing(Glow(0xD6, 0xCE, 0xEF, 0x48, new Point(0.0, 1.0), 0.6), null, bounds));
        return Finish(group);
    }

    // Deep sea (DeepSeek): clear water seen from just above the surface. White light at the top left deepens toward
    // DeepSeek blue at the bottom right, where three layered swells of translucent blue and cyan roll across the
    // bottom, the middle one edged with a silk-white highlight, and a whale's tail rises from the water at the right.
    // The swells stay pale where the list runs over them, so text keeps its contrast.
    private static Brush DeepSea()
    {
        const double w = 820, h = 600;
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, w, h)) };
        var bounds = new RectangleGeometry(new Rect(0, 0, w, h));
        var wash = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        foreach (var (color, offset) in new[] { ("#FFFFFF", 0.0), ("#F5F8FF", 0.45), ("#E3EBFF", 1.0) }) wash.GradientStops.Add(new GradientStop(Hex(color), offset));
        group.Children.Add(new GeometryDrawing(wash, null, bounds));
        group.Children.Add(new GeometryDrawing(Glow(0x4D, 0x6B, 0xFE, 0x18, new Point(1.05, 1.02), 0.62), null, bounds));
        group.Children.Add(new GeometryDrawing(Glow(0x6F, 0xC3, 0xFF, 0x14, new Point(0.72, 0.9), 0.34), null, bounds));
        group.Children.Add(new GeometryDrawing(Glow(0xB8, 0xC8, 0xFF, 0x30, new Point(0.98, 0.0), 0.36), null, bounds));

        // Swells, back to front. Each fades in from the left so the list's left columns stay on near-white.
        Brush Water(string left, string mid, string right)
        {
            var fill = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            fill.GradientStops.Add(new GradientStop(Hex(left), 0));
            fill.GradientStops.Add(new GradientStop(Hex(mid), 0.55));
            fill.GradientStops.Add(new GradientStop(Hex(right), 1));
            return fill;
        }
        var back = DeepSeaSwell(0, w, h);
        var middle = DeepSeaSwell(1, w, h);
        var front = DeepSeaSwell(2, w, h);
        group.Children.Add(new GeometryDrawing(Water("#004D6BFE", "#0E4D6BFE", "#244D6BFE"), new Pen(Water("#004D6BFE", "#244D6BFE", "#404D6BFE"), 1), back));
        group.Children.Add(new GeometryDrawing(Water("#006FC3FF", "#106FC3FF", "#1E5C8DFF"), null, middle));
        // The silk highlight along the middle swell's crest.
        var crest = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        crest.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.1));
        crest.GradientStops.Add(new GradientStop(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), 0.6));
        crest.GradientStops.Add(new GradientStop(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF), 1));
        group.Children.Add(new GeometryDrawing(null, new Pen(crest, 1.4), middle));

        // The whale's tail, white catching the light, its base hidden by the front swell.
        // Two broad lobes with a notch between them on a thick stalk, in a 60 × 40 box with the stalk at the bottom.
        var fluke = Geometry.Parse("M26,40 C26,32 25,27 22,23 C17,18 7,16 0,10 C7,6 15,5 21,8 C25,10 28,13 30,16 C32,13 35,10 39,8 C45,5 53,6 60,10 C53,16 43,18 38,23 C35,27 34,32 34,40 Z").Clone();
        fluke.Transform = new MatrixTransform(1.05, 0, 0, 1.05, w - 92, h * 0.935 - 34);
        var tail = new LinearGradientBrush(Colors.White, Hex("#D6E1FF"), 90);
        group.Children.Add(new GeometryDrawing(tail, new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x70, 0x4D, 0x6B, 0xFE))), 0.9), fluke));
        group.Children.Add(new GeometryDrawing(Water("#004D6BFE", "#1C4D6BFE", "#344D6BFE"), null, front));
        return Finish(group);
    }

    /// <summary>
    /// The deep sea's swells (0 back, 1 middle, 2 front) in a <paramref name="w"/> × <paramref name="h"/> box: the water
    /// below the crest down to the bottom edge. Shared with the scene so its whale meets the same waterline.
    /// </summary>
    internal static Geometry DeepSeaSwell(int layer, double w, double h)
    {
        var (y0, dip, rise, yEnd) = layer switch
        {
            0 => (h * 0.76, h * 0.06, h * 0.05, h * 0.70),
            1 => (h * 0.86, h * 0.05, h * 0.04, h * 0.81),
            _ => (h * 0.95, h * 0.03, h * 0.025, h * 0.91)
        };
        return Geometry.Parse(FormattableString.Invariant(
            $"M0,{y0} C{w * 0.22},{y0 - rise} {w * 0.42},{y0 + dip} {w * 0.66},{y0} C{w * 0.82},{y0 - rise * 0.6} {w * 0.92},{yEnd - rise * 0.3} {w},{yEnd} L{w},{h} L0,{h} Z"));
    }

    private static Brush Frozen(Brush brush) { brush.Freeze(); return brush; }

    // Starry violet (Gemini): a deep violet night whose top right glows with Gemini's blue, violet and rose like an
    // aurora, and, low in the corner, a large crystal star like the ornament in her hair, cut into light and dark
    // facets with a champagne-gold rim, half sunk below the edge and barely there.
    private static Brush StarNight()
    {
        const double w = 820, h = 600;
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, w, h)) };
        var bounds = new RectangleGeometry(new Rect(0, 0, w, h));
        group.Children.Add(new GeometryDrawing(Vertical(("#140F2C", 0), ("#0E0B22", 0.5), ("#09071A", 1)), null, bounds));
        group.Children.Add(new GeometryDrawing(Glow(0x47, 0x96, 0xE3, 0x3A, new Point(0.98, -0.08), 0.55), null, bounds));
        group.Children.Add(new GeometryDrawing(Glow(0x91, 0x77, 0xC7, 0x34, new Point(0.68, -0.02), 0.42), null, bounds));
        group.Children.Add(new GeometryDrawing(Glow(0xCA, 0x66, 0x73, 0x22, new Point(1.02, 0.34), 0.34), null, bounds));
        group.Children.Add(new GeometryDrawing(Glow(0x5B, 0x49, 0xD6, 0x1C, new Point(0.0, 1.0), 0.5), null, bounds));
        // A violet light behind the emblem so it reads as lit crystal rather than a dark shape.
        group.Children.Add(new GeometryDrawing(Glow(0x9B, 0x7C, 0xFF, 0x26, new Point(0.96, 1.0), 0.3), null, bounds));
        var emblem = CrystalStar(150, 0x2E, 0x9A, 1.4);
        emblem.Transform = new TranslateTransform(w - 44, h + 6);
        group.Children.Add(emblem);
        return Finish(group);
    }

    /// <summary>
    /// A four-pointed crystal star like Gemini娘's hair ornament, centred on (0, 0) with points <paramref name="radius"/>
    /// out: each point is cut into a light and a dark facet in Gemini's colours, edged in champagne gold.
    /// </summary>
    internal static DrawingGroup CrystalStar(double radius, byte facetAlpha = 0xFF, byte rimAlpha = 0xFF, double rimWidth = 1)
    {
        var light = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        light.GradientStops.Add(new GradientStop(Color.FromArgb(facetAlpha, 0xA8, 0xDC, 0xFF), 0));
        light.GradientStops.Add(new GradientStop(Color.FromArgb(facetAlpha, 0xD4, 0xB8, 0xFF), 0.5));
        light.GradientStops.Add(new GradientStop(Color.FromArgb(facetAlpha, 0xFF, 0xC0, 0xDE), 1));
        var dark = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        dark.GradientStops.Add(new GradientStop(Color.FromArgb(facetAlpha, 0x3E, 0x62, 0xD6), 0));
        dark.GradientStops.Add(new GradientStop(Color.FromArgb(facetAlpha, 0x74, 0x47, 0xCF), 0.5));
        dark.GradientStops.Add(new GradientStop(Color.FromArgb(facetAlpha, 0xB8, 0x48, 0x8C), 1));
        var mappingBox = new Rect(-radius, -radius, radius * 2, radius * 2);
        foreach (var brush in new[] { light, dark }) { brush.MappingMode = BrushMappingMode.Absolute; brush.StartPoint = mappingBox.TopLeft; brush.EndPoint = mappingBox.BottomRight; }
        var group = new DrawingGroup();
        var inner = radius * 0.3;
        var outline = new StreamGeometry();
        using (var rim = outline.Open())
        {
            for (var k = 0; k < 4; k++)
            {
                var angle = Math.PI / 2 * k - Math.PI / 2;
                Point At(double a, double r) => new(Math.Cos(a) * r, Math.Sin(a) * r);
                var tip = At(angle, radius);
                var before = At(angle - Math.PI / 4, inner);
                var after = At(angle + Math.PI / 4, inner);
                group.Children.Add(new GeometryDrawing(light, null, Triangle(new Point(), before, tip)));
                group.Children.Add(new GeometryDrawing(dark, null, Triangle(new Point(), tip, after)));
                if (k == 0) rim.BeginFigure(before, false, true);
                rim.LineTo(tip, true, true);
                rim.LineTo(after, true, true);
            }
        }
        var gold = new Pen(new SolidColorBrush(Color.FromArgb(rimAlpha, 0xE8, 0xCB, 0x8A)), rimWidth) { LineJoin = PenLineJoin.Round };
        group.Children.Add(new GeometryDrawing(null, gold, outline));
        return group;
    }

    private static Geometry Triangle(Point a, Point b, Point c)
    {
        var shape = new StreamGeometry();
        using (var draw = shape.Open()) { draw.BeginFigure(a, true, true); draw.LineTo(b, true, false); draw.LineTo(c, true, false); }
        return shape;
    }

    private static LinearGradientBrush Vertical(params (string Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (color, offset) in stops) brush.GradientStops.Add(new GradientStop(Hex(color), offset));
        return brush;
    }

    private static Brush Finish(DrawingGroup group)
    {
        var brush = new DrawingBrush(group) { Stretch = Stretch.Fill };
        // Render the backdrop once and reuse it: without the cache, a small repaint (a sparkle flaring, a bubble
        // rising) re-renders just that strip of the gradient, which can come out a shade different and leave seams.
        RenderOptions.SetCachingHint(brush, CachingHint.Cache);
        RenderOptions.SetCacheInvalidationThresholdMinimum(brush, 0.5);
        RenderOptions.SetCacheInvalidationThresholdMaximum(brush, 2.0);
        brush.Freeze();
        return brush;
    }

    private static Color Hex(string value) => (Color)ColorConverter.ConvertFromString(value.Length == 7 ? value : "#" + value[1..]);

    private static RadialGradientBrush Glow(byte r, byte g, byte b, byte alpha, Point center, double radius)
    {
        // Fade to the same hue at zero alpha so the edge never turns grey.
        var glow = new RadialGradientBrush(Color.FromArgb(alpha, r, g, b), Color.FromArgb(0, r, g, b)) { Center = center, GradientOrigin = center, RadiusX = radius, RadiusY = radius };
        return glow;
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
