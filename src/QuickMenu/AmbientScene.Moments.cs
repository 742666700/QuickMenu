using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace QuickMenu;

/// <summary>
/// Each theme's signature moment: a little character who comes out each time the menu opens, about a second in, and
/// every 25 s after, plays for five or six seconds and leaves nothing showing (or ticking) until the next time. They
/// were drawn as SVG first; the shapes are the same path data, the timings the same key frames.
/// </summary>
internal sealed partial class AmbientScene
{
    private const double MomentPeriod = 25, MomentDelay = 0.8;
    // Gliding (eased both ways, gently) and leaving (setting off slowly, then faster) along a route.
    private static readonly KeySpline Glide = Frozen(new KeySpline(0.35, 0, 0.65, 1)), Leave = Frozen(new KeySpline(0.5, 0, 0.9, 0.6));

    /// <summary>A part of a sprite that scales and turns about its own origin, then moves.</summary>
    private sealed record Piece(Canvas Box, ScaleTransform Scale, RotateTransform Turn, TranslateTransform Move);

    private static Piece Part(Panel parent, double x, double y, params UIElement[] children)
    {
        var box = new Canvas();
        foreach (var child in children) box.Children.Add(child);
        var scale = new ScaleTransform(1, 1, x, y);
        var turn = new RotateTransform(0, x, y);
        var move = new TranslateTransform();
        box.RenderTransform = new TransformGroup { Children = { scale, turn, move } };
        parent.Children.Add(box);
        return new(box, scale, turn, move);
    }

    /// <summary>A canvas holding <paramref name="children"/> drawn <paramref name="scale"/> times larger, at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    private static Canvas Group(double x, double y, double scale, params UIElement[] children)
    {
        var group = new Canvas { RenderTransform = new ScaleTransform(scale, scale) };
        foreach (var child in children) group.Children.Add(child);
        Canvas.SetLeft(group, x);
        Canvas.SetTop(group, y);
        return group;
    }

    /// <summary>
    /// A canvas for a moment drawn in the backdrop's 820 × 600 design coordinates, shifted so that
    /// <paramref name="anchor"/> lands where the stretched backdrop puts it; the drawing itself is not stretched.
    /// </summary>
    private Canvas Stage(Point anchor, double width, double height)
    {
        var stage = new Canvas();
        Canvas.SetLeft(stage, anchor.X * (width / 820 - 1));
        Canvas.SetTop(stage, anchor.Y * (height / 600 - 1));
        _layer.Children.Add(stage);
        return stage;
    }

    private static Color Hex(string value) => (Color)ColorConverter.ConvertFromString(value);
    private static Brush Solid(string color) { var brush = new SolidColorBrush(Hex(color)); brush.Freeze(); return brush; }
    private static Brush Gradient(GradientBrush brush, params (string Color, double Offset)[] stops)
    {
        foreach (var (color, offset) in stops) brush.GradientStops.Add(new GradientStop(Hex(color), offset));
        brush.Freeze();
        return brush;
    }
    private static Brush Radial(Point center, double radius, params (string, double)[] stops) =>
        Gradient(new RadialGradientBrush { Center = center, GradientOrigin = center, RadiusX = radius, RadiusY = radius }, stops);
    private static Brush Linear(Point start, Point end, params (string, double)[] stops) => Gradient(new LinearGradientBrush { StartPoint = start, EndPoint = end }, stops);
    private static Brush Vertical(bool upward, params (string, double)[] stops) => Linear(new Point(0, upward ? 1 : 0), new Point(0, upward ? 0 : 1), stops);
    private static Path Shape(Geometry data, Brush? fill, Brush? stroke = null, double thickness = 0, double opacity = 1) => new()
    {
        Data = data, Fill = fill, Stroke = stroke, StrokeThickness = thickness, Opacity = opacity,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
    };
    private static Path Drawn(string data, Brush? fill, Brush? stroke = null, double thickness = 0, double opacity = 1) => Shape(Geometry.Parse(data), fill, stroke, thickness, opacity);
    private static Path Oval(double x, double y, double rx, double ry, Brush? fill, Brush? stroke = null, double thickness = 0, double opacity = 1) =>
        Shape(new EllipseGeometry(new Point(x, y), rx, ry), fill, stroke, thickness, opacity);
    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    // Key frames for a moment's burst, at the frame rate quick small motion needs.
    private static void Key(Burst burst, IAnimatable target, DependencyProperty property, params (double Seconds, double Value)[] frames) =>
        burst.Parts.Add((target, property, Once(FastFrameRate, frames)));
    private static void Fade(Burst burst, UIElement target, params (double, double)[] frames) => Key(burst, target, UIElement.OpacityProperty, frames);
    private static void Fade(Burst burst, Piece piece, params (double, double)[] frames) => Fade(burst, piece.Box, frames);
    private static void Grow(Burst burst, Piece piece, params (double, double)[] frames)
    {
        Key(burst, piece.Scale, ScaleTransform.ScaleXProperty, frames);
        Key(burst, piece.Scale, ScaleTransform.ScaleYProperty, frames);
    }
    private static void Spin(Burst burst, Piece piece, params (double, double)[] frames) => Key(burst, piece.Turn, RotateTransform.AngleProperty, frames);
    private static void Slide(Burst burst, Piece piece, DependencyProperty axis, params (double, double, KeySpline)[] frames) =>
        burst.Parts.Add((piece.Move, axis, Keyed(FastFrameRate, frames)));

    /// <summary>Beats a wing between 1 and <paramref name="low"/> every <paramref name="half"/> seconds, after <paramref name="from"/> and up to <paramref name="to"/>.</summary>
    private static void Beat(List<(double, double, KeySpline)> frames, double from, double to, double half, double low)
    {
        for (var k = 1; from + k * half <= to + 1e-9; k++) frames.Add((from + k * half, k % 2 == 1 ? low : 1, Steady));
    }

    /// <summary>Legs of travel along paths, each from one time to another; between legs, and before and after them, the traveller waits at the nearest end.</summary>
    private sealed class Route((string Path, double From, double To, KeySpline Curve)[] legs)
    {
        private readonly (PathGeometry Path, double From, double To, KeySpline Curve)[] _legs =
            legs.Select(leg => (PathGeometry.CreateFromGeometry(Geometry.Parse(leg.Path)), leg.From, leg.To, leg.Curve)).ToArray();
        public double End => _legs[^1].To;

        public Point At(double seconds, out Vector heading)
        {
            var (path, progress) = (_legs[0].Path, 0.0);
            foreach (var leg in _legs)
                if (seconds >= leg.From) (path, progress) = (leg.Path, seconds >= leg.To ? 1 : leg.Curve.GetSplineProgress((seconds - leg.From) / (leg.To - leg.From)));
            path.GetPointAtFractionLength(progress, out var point, out var tangent);
            heading = new Vector(tangent.X, tangent.Y);
            return point;
        }
    }

    private enum Heading { Upright, Along, Facing }

    /// <summary>
    /// Carries <paramref name="piece"/> (drawn around its origin) along <paramref name="route"/>: it stays upright, turns
    /// to point along the way, or faces the way it is going with a little tilt (for things drawn facing right).
    /// </summary>
    private static void Ride(Burst burst, Piece piece, Route route, Heading heading)
    {
        DoubleAnimationUsingKeyFrames Track()
        {
            var track = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(route.End), FillBehavior = FillBehavior.Stop };
            Timeline.SetDesiredFrameRate(track, FastFrameRate);
            return track;
        }
        DoubleAnimationUsingKeyFrames x = Track(), y = Track(), angle = Track(), facing = Track();
        double? last = null;
        var direction = 1.0;
        for (var frame = 0; ; frame++)
        {
            var seconds = Math.Min(route.End, frame / (double)FastFrameRate);
            var key = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds));
            var point = route.At(seconds, out var way);
            x.KeyFrames.Add(new LinearDoubleKeyFrame(point.X, key));
            y.KeyFrames.Add(new LinearDoubleKeyFrame(point.Y, key));
            if (heading == Heading.Along)
            {
                // Unwound, so a loop turns all the way round instead of snapping back.
                var degrees = Math.Atan2(way.Y, way.X) * 180 / Math.PI;
                if (last is { } previous) degrees = previous + Math.IEEERemainder(degrees - previous, 360);
                last = degrees;
                angle.KeyFrames.Add(new LinearDoubleKeyFrame(degrees, key));
            }
            else if (heading == Heading.Facing)
            {
                if (Math.Abs(way.X) > 0.02) direction = Math.Sign(way.X);
                var tilt = Math.Clamp(Math.Atan2(way.Y, Math.Abs(way.X) + 1e-6) * 180 / Math.PI, -28, 28);
                angle.KeyFrames.Add(new LinearDoubleKeyFrame(tilt * direction, key));
                facing.KeyFrames.Add(new DiscreteDoubleKeyFrame(direction, key));
            }
            if (seconds >= route.End) break;
        }
        burst.Parts.Add((piece.Move, TranslateTransform.XProperty, x));
        burst.Parts.Add((piece.Move, TranslateTransform.YProperty, y));
        if (heading != Heading.Upright) burst.Parts.Add((piece.Turn, RotateTransform.AngleProperty, angle));
        if (heading == Heading.Facing) burst.Parts.Add((piece.Scale, ScaleTransform.ScaleXProperty, facing));
    }

    /// <summary>A figure eight lying on its side through (<paramref name="cx"/>, <paramref name="cy"/>), setting off left (-1) or right (1).</summary>
    private static string FigureEight(double cx, double cy, double a, double b, int direction)
    {
        var path = new System.Text.StringBuilder();
        for (var i = 0; i <= 96; i++)
        {
            var theta = i / 96.0 * Math.PI * 2;
            path.Append(Invariant($"{(i == 0 ? "M" : "L")}{cx + direction * a * Math.Sin(theta):0.##},{cy + b * Math.Sin(theta) * Math.Cos(theta):0.##} "));
        }
        return path.ToString();
    }

    /// <summary>The theme's signature burst, which opens every showing; null until a theme with one is built.</summary>
    private Burst NewMoment() => _moment = NewBurst(MomentPeriod, 0, MomentDelay);

    /// <summary>
    /// A little round whale, DeepSeek blue, who pops up from the front swell just left of the big tail each time the
    /// menu opens and every 25 s after: she squeezes, spouts a fountain with her eyes shut happily, waves a flipper,
    /// sends up a small heart, blinks and sinks again, about 5 s in all. She is drawn facing left in a box whose
    /// waterline is y = 34; everything below the swell is clipped away, so between spouts she rests out of sight.
    /// </summary>
    private void AddWhale(double width, double height)
    {
        const double hidden = 40;
        // Where she surfaces in the backdrop's 820 × 600 design box, which is stretched over the window.
        double sx = width / 820, sy = height / 600;
        var at = new Point(650 * sx, 524 * sy);
        var stage = new Canvas();
        Canvas.SetLeft(stage, at.X);
        Canvas.SetTop(stage, at.Y);
        _layer.Children.Add(stage);

        var body = Radial(new Point(0.32, 0.22), 0.85, ("#A9B8FF", 0), ("#6A84FF", 0.38), ("#4D6BFE", 0.75), ("#3A52D8", 1));
        var fluke = Vertical(false, ("#7F95FF", 0), ("#4560EE", 1));
        var belly = Vertical(false, ("#FFFFFF", 0), ("#DCE5FF", 1));
        var jet = Vertical(true, ("#F4F8FF", 0), ("#C4DCFF", 0.55), ("#9CC8FF", 1));
        var drop = Radial(new Point(0.38, 0.32), 0.65, ("#FFFFFF", 0), ("#D2E6FF", 0.45), ("#76A6FF", 1));
        var heart = Radial(new Point(0.35, 0.3), 0.8, ("#FFC4D6", 0), ("#FF7EA6", 1));
        var outline = Solid("#732F47C9");
        var spray = Solid("#8C86B2FF");
        var ink = Solid("#1A2156");
        var white = Solid("#FFFFFF");

        // Rings that spread on the water as she comes up and as she goes down.
        var rings = new[] { 0, 1 }.Select(_ =>
        {
            var ring = Part(stage, 28, 34, Oval(28, 34, 34, 4.2, null, white, 1.3));
            ring.Box.Opacity = 0;
            ring.Scale.ScaleX = ring.Scale.ScaleY = 0.25;
            return ring;
        }).ToArray();

        // Everything above the front swell, in her own coordinates.
        var swell = Services.Backdrops.DeepSeaSwell(2, 820, 600).Clone();
        swell.Transform = new MatrixTransform(sx, 0, 0, sy, -at.X, -at.Y);
        var above = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(-at.X, -at.Y, width, height)), swell);
        above.Freeze();
        var water = new Canvas { Clip = above };
        stage.Children.Add(water);

        var whale = Part(water, 28, 34);
        whale.Move.Y = hidden;
        _whaleRise = whale.Move;
        // The fountain: a jet from the blowhole that opens into two curls and a little puff, with a drop on each curl.
        var fountain = Part(whale.Box, 22, 10, Drawn("M20.4,10 C19.7,2 19.8,-6 21.6,-14 C23.4,-6 23.5,2 22.8,10 Z", jet, spray, 0.6));
        fountain.Box.Opacity = 0;
        fountain.Scale.ScaleY = 0;
        var crown = Part(whale.Box, 21.6, -14,
            Drawn("M21.6,-14 C17,-20 9.5,-19.5 6.5,-12 C10,-16 15,-16.8 21.6,-12.2 Z", jet, spray, 0.6),
            Drawn("M21.6,-14 C26.2,-20 33.7,-19.5 36.7,-12 C33.2,-16 28.2,-16.8 21.6,-12.2 Z", jet, spray, 0.6),
            Drawn("M21.6,-13 C19.6,-17 20.4,-21 21.6,-23 C22.8,-21 23.6,-17 21.6,-13 Z", jet, spray, 0.6),
            Oval(6.2, -10.2, 1.7, 1.7, drop), Oval(37, -10.2, 1.7, 1.7, drop));
        crown.Box.Opacity = 0;
        crown.Scale.ScaleX = crown.Scale.ScaleY = 0;
        var tail = Part(whale.Box, 56, 34, Drawn("M52.5,34 C55.5,31 58,27 59,22.5 C56,21.8 53.4,19.4 52.8,15.6 C56.4,15.8 59.2,17.8 60.7,20.6 C61.8,17.4 64.6,14.6 68.6,14.2 C68.2,18 65.6,21.2 62.2,22.8 C61.4,27.4 59.8,31.2 58,34 Z", fluke, outline, 0.7));
        // A round mochi of a body with a pale, grooved belly, a rim of light along the back and a shine on the head.
        foreach (var piece in new UIElement[]
        {
            Drawn("M2.5,34 C0.5,21.5 9.5,9.5 24.5,9 C38,8.5 48,15.5 52.5,26 C53.6,28.6 54.4,31.4 54.8,34 Z", body, outline, 0.7),
            Drawn("M2.6,34 C2.7,30.4 3.9,27.4 5.8,25.2 C10.6,29.6 18,32.8 29,34 Z", belly),
            Drawn("M6.4,28.6 C10.4,31.4 15.4,33.2 20.6,34 M8.6,26.9 C12.4,29.6 16.8,31.4 21.8,32.4", null, Solid("#C3D0FF"), 0.7),
            Drawn("M31,10.6 C39,11.4 46,16.2 49.6,23", null, Solid("#73FFFFFF"), 1.2),
            Shape(new EllipseGeometry(new Point(17.5, 14.6), 5.4, 2.3, new RotateTransform(-18, 17.5, 14.6)), white, opacity: 0.55),
            Oval(24.6, 12.6, 1, 1, white, opacity: 0.8),
            Drawn("M20.4,10.2 Q21.6,9.2 22.8,10.2", null, Solid("#992F47C9"), 0.7)
        }) whale.Box.Children.Add(piece);
        // A sparkling eye, and the same eye shut in a happy curve while she spouts.
        var eye = Part(whale.Box, 14, 21.6,
            Oval(14, 21.6, 2.9, 3.4, ink), Oval(14, 23.4, 2, 1.1, Solid("#CC3D4FA8")), Oval(15.1, 20.2, 1.25, 1.25, white), Oval(13.1, 23.1, 0.55, 0.55, white));
        var shut = Part(whale.Box, 14, 21.6, Drawn("M11.1,22.2 Q14,18.8 16.9,22.2", null, ink, 1.35));
        shut.Box.Opacity = 0;
        whale.Box.Children.Add(Oval(9.4, 27, 3.3, 1.8, Solid("#FF8FB3"), opacity: 0.55));
        whale.Box.Children.Add(Drawn("M8,26.2 l-0.8,1.5 M9.8,26.2 l-0.8,1.5 M11.6,26.2 l-0.8,1.5", null, Solid("#99FF6E9C"), 0.5));
        whale.Box.Children.Add(Drawn("M3.9,26.2 Q5.7,28.2 7.5,26.6", null, ink, 0.95));
        var flipper = Part(whale.Box, 25, 30.5, Drawn("M25,30.5 C28.4,29.2 32.6,30.4 34.6,33.4 C31,34.3 27.2,33.4 25,30.5 Z", Solid("#4562F0"), outline, 0.6));

        // Drops flung from the curls and the puff (start, sideways throw, height, radius); they fall back through the waterline.
        var drops = new[] { (6.2, -10.0, -9.0, 3.0, 1.5), (37, -10, 9, 3, 1.5), (21.6, -22, -6, 6, 1.3), (21.6, -22, 6, 6, 1.3), (21.6, -22, 0, 8, 1.1), (10, -15, -12, 4, 1.2), (33, -15, 12, 4, 1.2) }
            .Select(spec =>
            {
                var (x, y, _, _, r) = spec;
                var flung = Part(water, x, y, Oval(x, y, r, r, drop));
                flung.Box.Opacity = 0;
                return (flung, spec);
            }).ToArray();

        // Above the water: a sparkle by the fountain, a small heart, and foam where she meets the water.
        var sparkle = Part(stage, 38, -22, Drawn("M38,-27 Q38.7,-22.7 43,-22 Q38.7,-21.3 38,-17 Q37.3,-21.3 33,-22 Q37.3,-22.7 38,-27 Z", white, Solid("#8DB6FF"), 0.6));
        sparkle.Scale.ScaleX = sparkle.Scale.ScaleY = 0;
        var love = Part(stage, 8, 6, Drawn("M8,4.6 C8,2.2 4.6,1.6 4.6,4.2 C4.6,6.4 7.4,7.8 8,9 C8.6,7.8 11.4,6.4 11.4,4.2 C11.4,1.6 8,2.2 8,4.6 Z", heart));
        love.Box.Opacity = 0;
        love.Scale.ScaleX = love.Scale.ScaleY = 0.4;
        var foam = Part(stage, 28, 34, Drawn("M1,34 q3,-1.3 6,0 t6,0 t6,0 t6,0 t6,0 t6,0 t6,0 t6,0 t6,0", null, Solid("#D9FFFFFF"), 1.1));
        foam.Box.Opacity = 0;
        foam.Scale.ScaleX = 0.6;

        // Every part ends at its base value (hidden, shut or still), so nothing is left showing between spouts.
        var burst = NewMoment();
        void Key(IAnimatable target, DependencyProperty property, params (double Seconds, double Value)[] frames) => burst.Parts.Add((target, property, Once(FastFrameRate, frames)));
        void Scale(ScaleTransform target, params (double Seconds, double Value)[] frames) { Key(target, ScaleTransform.ScaleXProperty, frames); Key(target, ScaleTransform.ScaleYProperty, frames); }
        var fade = UIElement.OpacityProperty;

        // Up with a little overshoot, squash and stretch for the spout, a happy sway, then down.
        Key(whale.Move, TranslateTransform.YProperty, (0, hidden), (0.85, -2), (1.1, 0), (3.9, 0), (5.0, hidden));
        Key(whale.Turn, RotateTransform.AngleProperty, (0, 0), (0.85, -4), (1.4, 1), (2.2, -2), (3.0, 2), (3.9, -1), (5.0, 0));
        Key(whale.Scale, ScaleTransform.ScaleXProperty, (0, 1), (1.1, 1), (1.25, 1.08), (1.5, 0.96), (1.75, 1));
        Key(whale.Scale, ScaleTransform.ScaleYProperty, (0, 1), (1.1, 1), (1.25, 0.9), (1.5, 1.06), (1.75, 1));
        Key(tail.Turn, RotateTransform.AngleProperty, (0, 0), (0.9, -14), (1.5, 10), (2.1, -10), (2.7, 7), (3.3, -4), (3.9, 0));
        Key(flipper.Turn, RotateTransform.AngleProperty, (0, 0), (1.8, 0), (2.05, -28), (2.3, 4), (2.55, -22), (2.8, 0));
        // Eyes shut happily while she spouts, then a blink.
        Key(eye.Box, fade, (0, 1), (1.12, 1), (1.18, 0), (2.6, 0), (2.66, 1));
        Key(shut.Box, fade, (0, 0), (1.12, 0), (1.18, 1), (2.6, 1), (2.66, 0));
        Key(eye.Scale, ScaleTransform.ScaleYProperty, (0, 1), (3.2, 1), (3.28, 0.1), (3.36, 1));
        // The fountain.
        Key(fountain.Scale, ScaleTransform.ScaleYProperty, (0, 0), (1.2, 0), (1.5, 1), (2.4, 0.9), (2.85, 0));
        Key(fountain.Box, fade, (0, 0), (1.2, 0), (1.28, 1), (2.45, 0.9), (2.85, 0));
        Key(crown.Scale, ScaleTransform.ScaleXProperty, (0, 0), (1.42, 0), (1.68, 1.15), (2.45, 1.28), (2.6, 1.3));
        Key(crown.Scale, ScaleTransform.ScaleYProperty, (0, 0), (1.42, 0), (1.68, 1.15), (2.45, 1.2), (2.6, 1.22));
        Key(crown.Box, fade, (0, 0), (1.42, 0), (1.5, 1), (2.25, 0.9), (2.6, 0));
        Scale(sparkle.Scale, (0, 0), (1.65, 0), (1.9, 1.15), (2.4, 0));
        Key(sparkle.Turn, RotateTransform.AngleProperty, (0, 0), (1.65, 0), (2.4, 60));
        for (var i = 0; i < drops.Length; i++)
        {
            var (flung, (_, y, dx, rise, _)) = drops[i];
            double t0 = 1.7 + i % 4 * 0.08, top = t0 + 0.28, end = t0 + 0.95;
            burst.Parts.Add((flung.Move, TranslateTransform.XProperty, Keyed(FastFrameRate, (0, 0, Eased), (t0, 0, Eased), (end, dx, Steady))));
            burst.Parts.Add((flung.Move, TranslateTransform.YProperty, Keyed(FastFrameRate, (0, 0, Eased), (t0, 0, Eased), (top, -rise, Thrown), (end, 42 - y, Falling))));
            Key(flung.Box, fade, (0, 0), (t0, 0), (t0 + 0.05, 1), (t0 + 0.65, 0.95), (end, 0));
        }
        // A heart floats up after the spout.
        Key(love.Move, TranslateTransform.YProperty, (0, 0), (2.75, 0), (3.9, -20));
        Key(love.Move, TranslateTransform.XProperty, (0, 0), (2.75, 0), (3.3, -2), (3.9, 1));
        Scale(love.Scale, (0, 0.4), (2.75, 0.4), (3.05, 1.1), (3.3, 1), (3.9, 1));
        Key(love.Box, fade, (0, 0), (2.75, 0), (2.95, 1), (3.5, 1), (3.9, 0));
        // The water.
        Key(foam.Box, fade, (0, 0), (0.55, 0), (0.9, 0.85), (3.9, 0.8), (4.6, 0));
        Key(foam.Scale, ScaleTransform.ScaleXProperty, (0, 0.6), (0.55, 0.6), (1.0, 1), (1.25, 1.06), (1.6, 1), (4.6, 0.7));
        foreach (var (ring, start, end) in new[] { (rings[0], 0.35, 1.7), (rings[1], 4.0, 5.2) })
        {
            Scale(ring.Scale, (0, 0.25), (start, 0.25), (end, 1));
            Key(ring.Box, fade, (0, 0), (start, 0), (start + 0.2, 0.8), (end, 0));
        }
    }

    /// <summary>
    /// OLED night: a sleepy crescent moon in an orange nightcap (Claude's colour) fades in along the top band, sways
    /// with her eye shut and lets out a few z's. When a shooting star slips past she opens one eye and follows it, then
    /// dozes off and fades. Lit only for those few seconds, so no pixel stays on.
    /// </summary>
    private void AddSleepyMoon(double width, double height)
    {
        var stage = Stage(new Point(540, 26), width, height);
        var ink = Solid("#5B3424");
        var cream = Solid("#FFF3E8");

        var tail = new LinearGradientBrush(Hex("#FFFFF6EE"), Hex("#00FFF6EE"), new Point(652, 2), new Point(682, -10)) { MappingMode = BrushMappingMode.Absolute };
        tail.Freeze();
        var meteor = Part(stage, 0, 0, Shape(new LineGeometry(new Point(652, 2), new Point(682, -10)), null, tail, 1.3), Oval(652, 2, 1.3, 1.3, Solid("#FFF8F2")));
        meteor.Box.Opacity = 0;

        var face = new Canvas();
        Canvas.SetLeft(face, 540);
        Canvas.SetTop(face, 26);
        foreach (var piece in new UIElement[]
        {
            Oval(0, 0, 24, 24, Radial(new Point(0.5, 0.5), 0.5, ("#42FFE6D6", 0), ("#00FFE6D6", 1))),
            Drawn("M3,-11.6 A12,12 0 1 0 3,11.6 A13,13 0 0 1 3,-11.6 Z", Radial(new Point(0.3, 0.3), 0.8, ("#FFF9F0", 0), ("#FFE4C8", 0.6), ("#F2B98A", 1))),
            Drawn("M-6.5,-10 C-5,-17 3,-21 10,-19 C7,-17 4.5,-14 2.6,-11.4 C-0.5,-10.4 -3.8,-9.8 -6.5,-10 Z", Linear(new Point(0, 1), new Point(1, 0), ("#F59A62", 0), ("#D9602C", 1))),
            Drawn("M-7,-10.1 C-3.8,-9.2 -0.4,-10 2.9,-11.7", null, cream, 2),
            Oval(10.4, -19, 2.1, 2.1, cream),
            Oval(-6.3, 1.8, 1.8, 1, Solid("#FF9A7A"), opacity: 0.7),
            Drawn("M-10.4,4.6 Q-9.6,5.5 -8.8,4.7", null, ink, 0.8)
        }) face.Children.Add(piece);
        var asleep = Part(face, 0, 0, Drawn("M-10.2,-2.4 Q-8.4,-0.6 -6.6,-2.4", null, ink, 1));
        var peek = Part(face, -8.4, -2.2, Oval(-8.4, -2.2, 1.35, 1.6, ink), Oval(-7.9, -2.8, 0.5, 0.5, Solid("#FFFFFF")));
        peek.Box.Opacity = 0;
        var moon = Part(stage, 540, 26, face);
        moon.Box.Opacity = 0;
        moon.Scale.ScaleX = moon.Scale.ScaleY = 0.85;

        var zs = new[] { 0, 1, 2 }.Select(i =>
        {
            double x = 554 + i * 2, y = 10 - i * 2, s = 1 - i * 0.15;
            var z = Part(stage, x + 2, y + 2, Drawn(Invariant($"M{x},{y} H{x + 4 * s} L{x},{y + 4 * s} H{x + 4 * s}"), null, Solid("#FFE6D6"), 1.1));
            z.Box.Opacity = 0;
            z.Scale.ScaleX = z.Scale.ScaleY = 0.5;
            return z;
        }).ToArray();

        var burst = NewMoment();
        Fade(burst, moon, (0, 0), (0.8, 1), (4.6, 1), (5.6, 0));
        Grow(burst, moon, (0, 0.85), (0.8, 1), (5.6, 0.9));
        Spin(burst, moon, (0, 0), (1.0, -6), (2.2, 5), (3.4, -4), (4.6, 4), (5.6, 0));
        foreach (var (start, z) in new[] { (0.9, zs[0]), (1.5, zs[1]), (3.9, zs[2]) })
        {
            Fade(burst, z, (0, 0), (start, 0), (start + 0.2, 1), (start + 1.4, 0));
            Slide(burst, z, TranslateTransform.XProperty, (0, 0, Eased), (start, 0, Eased), (start + 1.4, 8, Eased));
            Slide(burst, z, TranslateTransform.YProperty, (0, 0, Eased), (start, 0, Eased), (start + 1.4, -10, Eased));
            Grow(burst, z, (0, 0.5), (start, 0.5), (start + 1.4, 1.2));
        }
        Fade(burst, meteor, (0, 0), (2.5, 0), (2.6, 1), (3.1, 0.8), (3.3, 0));
        Slide(burst, meteor, TranslateTransform.XProperty, (0, 0, Eased), (2.5, 0, Eased), (3.3, -96, Steady));
        Slide(burst, meteor, TranslateTransform.YProperty, (0, 0, Eased), (2.5, 0, Eased), (3.3, 38, Steady));
        // One eye opens to watch the shooting star go by.
        Fade(burst, asleep, (0, 1), (2.55, 1), (2.6, 0), (3.75, 0), (3.8, 1));
        Fade(burst, peek, (0, 0), (2.55, 0), (2.6, 1), (3.75, 1), (3.8, 0));
        Slide(burst, peek, TranslateTransform.XProperty, (0, 0.9, Eased), (2.6, 0.9, Eased), (3.3, -0.9, Eased), (3.8, -0.9, Eased));
    }

    /// <summary>
    /// OLED day: a white paper plane glides in along the top band right of the greeting, leaving a dotted coral trail,
    /// loops the loop (with a sparkle at the top) and climbs out before the settings button.
    /// </summary>
    private void AddPaperPlane(double width, double height)
    {
        var stage = Stage(new Point(530, 20), width, height);
        var coral = Solid("#F08A6A");
        var edge = Solid("#E39C83");
        var route = new Route([("M330,38 C390,26 450,42 505,34 C540,29 560,18 552,9 C544,1 526,6 528,18 C530,32 560,40 600,34 C650,27 690,22 732,-14", 0, 4.4, Glide)]);
        var burst = NewMoment();

        for (var k = 1; k <= 30; k++)
        {
            var seconds = k / 31.0 * 4.4;
            var point = route.At(seconds, out _);
            var dot = Oval(point.X, point.Y, 1.1, 1.1, coral, opacity: 0);
            stage.Children.Add(dot);
            Fade(burst, dot, (0, 0), (seconds, 0), (seconds + 0.05, 0.6), (seconds + 1.1, 0));
        }
        var spark = Part(stage, 541, 3, Drawn("M541,-2 Q541.6,2.4 546,3 Q541.6,3.6 541,8 Q540.4,3.6 536,3 Q540.4,2.4 541,-2 Z", Solid("#FFFFFF"), coral, 0.7));
        spark.Scale.ScaleX = spark.Scale.ScaleY = 0;
        var plane = Part(stage, 0, 0, Group(0, 0, 1.15,
            Drawn("M12,0 L-10,-7.5 L-4.5,0 L-10,6 Z", Solid("#FFFFFF"), edge, 0.7),
            Drawn("M12,0 L-4.5,0 L-10,6 Z", Solid("#FBE0D4"), edge, 0.7),
            Drawn("M12,0 L-4.5,0", null, edge, 0.5)));
        plane.Box.Opacity = 0;

        Ride(burst, plane, route, Heading.Along);
        Fade(burst, plane, (0, 0), (0.3, 1), (4.0, 1), (4.4, 0));
        Grow(burst, spark, (0, 0), (2.0, 0), (2.25, 1.2), (2.7, 0));
        Spin(burst, spark, (0, 0), (2.0, 0), (2.7, 60));
    }

    /// <summary>
    /// Cream daisy: a daisy bud waits in the bottom right corner. It trembles and opens; a bee loops over and lands on
    /// it for the nectar, puffing up pollen, while a little butterfly comes to rest on the leaf tip, slowly fanning its
    /// wings. They leave one after the other and the flower closes into a bud again.
    /// </summary>
    private void AddBeeGarden(double width, double height)
    {
        var stage = Stage(new Point(726, 574), width, height);
        var petal = Radial(new Point(0.5, 0.9), 0.9, ("#F9B27A", 0), ("#EE7434", 1));
        var leaf = Solid("#8DBA63");
        var brown = Solid("#5A3B26");
        var white = Solid("#FFFFFF");

        stage.Children.Add(Drawn("M726,606 C727,597 724.5,588 726,578", null, Solid("#7FA75A"), 2));
        stage.Children.Add(Drawn("M726,595 C731,588 738,587 742,589 C737,594 731,596 726,595 Z", leaf));
        stage.Children.Add(Drawn("M725.5,598 C721,593 715,593 712,595 C716,599 721,600 725.5,598 Z", Solid("#9CC873")));
        var bud = Part(stage, 726, 574, Oval(726, 570, 3.4, 5, petal),
            Drawn("M720.6,577 C720,572 722.4,568.6 724.6,567.4 C724.4,571 725,574.6 726,577.6 C727,574.6 727.6,571 727.4,567.4 C729.6,568.6 732,572 731.4,577 C728.6,579.4 723.4,579.4 720.6,577 Z", leaf));
        var petalEdge = Solid("#59E0672B");
        var bloom = Part(stage, 726, 574, Enumerable.Range(0, 10)
            .Select(i => (UIElement)Shape(new EllipseGeometry(new Point(726, 566.6), 2.7, 6.2, new RotateTransform(i * 36, 726, 574)), petal, petalEdge, 0.4))
            .Append(Oval(726, 574, 3.8, 3.8, Radial(new Point(0.35, 0.3), 0.8, ("#E0884A", 0), ("#8F3410", 1))))
            .Append(Oval(724.8, 572.8, 1, 1, Solid("#F2A867"), opacity: 0.8)).ToArray());
        bloom.Box.Opacity = 0;
        bloom.Scale.ScaleX = bloom.Scale.ScaleY = 0.2;
        var pollen = new[] { 1.3, 1, 1.3, 1 }.Select(r =>
        {
            var grain = Part(stage, 726, 572, Oval(726, 572, r, r, Solid("#F7C548")));
            grain.Box.Opacity = 0;
            return grain;
        }).ToArray();

        // The butterfly, seen from above: peach wings with cream spots that fold and open.
        var wingEdge = Solid("#99D9653A");
        var upper = Radial(new Point(0.15, 0.75), 1, ("#FFE3CC", 0), ("#FFB28C", 0.55), ("#F08A5D", 1));
        var lower = Radial(new Point(0.2, 0.2), 1, ("#FFF0DC", 0), ("#FFC59A", 1));
        var spot = Solid("#FFF6EC");
        var flyer = Part(stage, 0, 0);
        flyer.Box.Opacity = 0;
        var tilt = Part(flyer.Box, 0, 0);
        var flyBody = Group(0, 0, 1.05);
        tilt.Box.Children.Add(flyBody);
        var wings = Part(flyBody, 0, 0,
            Drawn("M0,0 C-3,-7 -11,-9.5 -11.5,-3.6 C-11.8,0.4 -5,1 0,0 Z", upper, wingEdge, 0.5),
            Drawn("M0,0 C3,-7 11,-9.5 11.5,-3.6 C11.8,0.4 5,1 0,0 Z", upper, wingEdge, 0.5),
            Drawn("M0,0.6 C-2,5.4 -8.4,7.6 -8.6,3.4 C-8.7,1.2 -3.4,0.6 0,0.6 Z", lower, wingEdge, 0.5),
            Drawn("M0,0.6 C2,5.4 8.4,7.6 8.6,3.4 C8.7,1.2 3.4,0.6 0,0.6 Z", lower, wingEdge, 0.5),
            Oval(-7.6, -4.4, 1.25, 1.25, spot), Oval(7.6, -4.4, 1.25, 1.25, spot), Oval(-5.2, 3.6, 0.8, 0.8, spot), Oval(5.2, 3.6, 0.8, 0.8, spot));
        foreach (var piece in new UIElement[]
        {
            Oval(0, 0.6, 1, 4.4, brown), Oval(0, -3.6, 1.5, 1.5, brown),
            Drawn("M-0.5,-4.8 C-1.2,-7 -2.6,-8.2 -3.8,-8.4 M0.5,-4.8 C1.2,-7 2.6,-8.2 3.8,-8.4", null, brown, 0.55),
            Oval(-3.9, -8.4, 0.7, 0.7, brown), Oval(3.9, -8.4, 0.7, 0.7, brown)
        }) flyBody.Children.Add(piece);

        // The bee, drawn facing right.
        var gauze = Solid("#D9FFFFFF");
        var gauzeEdge = Solid("#E2C29E");
        var beeBody = Group(0, 0, 1.1);
        var bee = Part(stage, 0, 0, beeBody);
        bee.Box.Opacity = 0;
        var beeWings = Part(beeBody, 1, -4, Oval(-1, -6.2, 3.8, 5.4, gauze, gauzeEdge, 0.5), Oval(2.6, -5.6, 3, 4.4, gauze, gauzeEdge, 0.5));
        foreach (var piece in new UIElement[]
        {
            Drawn("M-6.8,0.2 L-9.4,0.9 L-6.8,1.8 Z", brown),
            Oval(0, 0.6, 7, 5.4, Radial(new Point(0.4, 0.3), 0.8, ("#FFE48A", 0), ("#F5B622", 1)), Solid("#C98A1E"), 0.5),
            Drawn("M-2.6,-4.4 C-3.6,-1.4 -3.6,2.8 -2.6,5.6 M1.2,-4.7 C0.2,-1.4 0.2,2.8 1.2,5.8", null, brown, 1.5),
            Drawn("M4.2,-4 C5,-7.4 6.6,-8.8 8.4,-8.8", null, brown, 0.7),
            Oval(8.6, -8.8, 0.9, 0.9, brown),
            Oval(4.4, -0.6, 1.1, 1.1, Solid("#3A2416")), Oval(4.8, -1, 0.4, 0.4, white),
            Oval(4.6, 2, 1.3, 0.7, Solid("#FF8A6A"), opacity: 0.7)
        }) beeBody.Children.Add(piece);

        var burst = NewMoment();
        // The bud trembles and opens first.
        Spin(burst, bud, (0, 0), (0.2, -6), (0.35, 5), (0.5, -3), (0.6, 0));
        Fade(burst, bud, (0, 1), (0.55, 1), (0.8, 0), (5.6, 0), (6.1, 1));
        Fade(burst, bloom, (0, 0), (0.55, 0), (0.8, 1), (5.6, 1), (6.2, 0));
        Grow(burst, bloom, (0, 0.2), (0.55, 0.2), (0.95, 1.15), (1.15, 0.92), (1.3, 1.04), (1.5, 1), (3.0, 1), (3.1, 0.93), (3.25, 1.03), (3.4, 1), (5.6, 1), (6.2, 0.25));
        Spin(burst, bloom, (0, -40), (0.55, -40), (1.1, 0), (5.6, 0), (6.2, -30));
        // Then the bee comes for the nectar.
        Ride(burst, bee, new Route([
            ("M548,530 C590,512 630,552 662,540 C688,530 680,512 666,518 C650,526 668,552 700,556 C712,558 720,560 726,562", 1.0, 3.0, Glide),
            ("M726,562 C744,546 776,536 806,528 C820,524 832,518 846,510", 4.2, 5.4, Leave)]), Heading.Facing);
        Fade(burst, bee, (0, 0), (1.0, 0), (1.2, 1), (5.2, 1), (5.4, 0));
        var buzz = new List<(double, double, KeySpline)> { (0, 1, Eased), (1.0, 1, Eased) };
        Beat(buzz, 1.0, 5.42, 0.06, 0.45);
        burst.Parts.Add((beeWings.Scale, ScaleTransform.ScaleYProperty, Keyed(FastFrameRate, buzz.ToArray())));
        foreach (var (grain, i) in pollen.Select((grain, i) => (grain, i)))
        {
            var (dx, dy) = new[] { (-8.0, -11.0), (-2, -14), (4, -13), (9, -9) }[i];
            var start = 3.15 + i * 0.05;
            Fade(burst, grain, (0, 0), (start, 0), (start + 0.08, 1), (start + 0.9, 0));
            Slide(burst, grain, TranslateTransform.XProperty, (0, 0, Eased), (start, 0, Eased), (start + 0.9, dx, Thrown));
            Slide(burst, grain, TranslateTransform.YProperty, (0, 0, Eased), (start, 0, Eased), (start + 0.9, dy, Thrown));
        }
        // And a butterfly flutters over to rest on the leaf tip before both leave.
        Ride(burst, flyer, new Route([
            ("M584,516 C612,498 634,538 662,528 C690,518 694,546 718,546 C736,546 746,566 744,582", 0.9, 2.9, Glide),
            ("M744,582 C752,560 772,548 790,538 C806,530 820,518 842,506", 4.5, 5.7, Leave)]), Heading.Upright);
        Fade(burst, flyer, (0, 0), (0.9, 0), (1.1, 1), (5.5, 1), (5.7, 0));
        Spin(burst, tilt, (0, 0), (1.4, -12), (1.9, 10), (2.4, -8), (2.9, 0), (4.5, 0), (5.0, 14), (5.7, 0));
        var fan = new List<(double, double, KeySpline)> { (0, 1, Eased), (0.9, 1, Eased) };
        Beat(fan, 0.9, 2.9, 0.08, 0.25);
        fan.AddRange([(3.4, 1, Eased), (3.9, 0.55, Eased), (4.4, 1, Eased)]);
        Beat(fan, 4.4, 5.7, 0.08, 0.25);
        burst.Parts.Add((wings.Scale, ScaleTransform.ScaleXProperty, Keyed(FastFrameRate, fan.ToArray())));
    }

    /// <summary>
    /// Pearl: the larger of the two drifting clouds, with a face that shows only during its moment: it blinks, squeezes
    /// out a little drizzle, a pearly rainbow rises behind it and it shuts its eyes happily; then the rainbow and the
    /// face fade. Everything drifts with the cloud.
    /// </summary>
    private void AddRainbowCloud(double width)
    {
        // Drawn in the design box less (488, 5), the cloud's own corner.
        var stage = new Canvas();
        Canvas.SetLeft(stage, width * 0.6 - 4);
        Canvas.SetTop(stage, 5);
        var drift = new TranslateTransform();
        stage.RenderTransform = drift;
        _layer.Children.Add(stage);
        Add(drift, TranslateTransform.XProperty, Slow(new DoubleAnimation(-width * 0.05, width * 0.05, TimeSpan.FromSeconds(38)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Sine }), 0.15);

        var bands = new[] { "#F5B8D0", "#F9D2A8", "#F2E9A6", "#BFE8C9", "#B9DAF7", "#CFC3F2" };
        var rainbow = Part(stage, 39, 19, bands.Select((color, i) =>
        {
            var r = 18 - i * 1.8;
            return (UIElement)Drawn(Invariant($"M{57 - r},19 A{r},{r} 0 0 1 {57 + r},19"), null, Solid(color), 1.9);
        }).ToArray());
        rainbow.Box.Opacity = 0;
        rainbow.Scale.ScaleX = rainbow.Scale.ScaleY = 0.3;
        var twinkles = new[]
        {
            Part(stage, 81, 4, Drawn("M81,1 Q81.4,3.6 84,4 Q81.4,4.4 81,7 Q80.6,4.4 78,4 Q80.6,3.6 81,1 Z", Solid("#FFFFFF"), Solid("#C9BDF0"), 0.5)),
            Part(stage, 72, -1, Drawn("M72,-3 Q72.3,-1.3 74,-1 Q72.3,-0.7 72,1 Q71.7,-0.7 70,-1 Q71.7,-1.3 72,-3 Z", Solid("#FFFFFF"), Solid("#F2B8CF"), 0.5))
        };
        foreach (var twinkle in twinkles) twinkle.Scale.ScaleX = twinkle.Scale.ScaleY = 0;
        var rainBrush = Solid("#B9D6F7");
        var rain = new[] { 10, 16, 22, 28, 34 }.Select(x =>
        {
            var drop = Part(stage, 0, 0, Drawn(Invariant($"M{x},22 C{x + 1.3},24 {x + 1.3},25.6 {x},25.6 C{x - 1.3},25.6 {x - 1.3},24 {x},22 Z"), rainBrush));
            drop.Box.Opacity = 0;
            return drop;
        }).ToArray();

        // The cloud's own shape and face, 1.4 times the size of its 30 × 16 drawing.
        var ink = Solid("#4B4466");
        var blush = Solid("#F4A6C6");
        var drawing = Group(0, 0, 1.4, Drawn("M7,16 A5,5 0 0 1 6.5,6.4 A7.5,7.5 0 0 1 20,4.6 A5.5,5.5 0 0 1 28.6,9.2 A3.6,3.6 0 0 1 28,16 Z", Vertical(false, ("#FFFFFF", 0), ("#D9D2F0", 1))));
        var face = Part(drawing, 0, 0,
            Oval(11.6, 12.2, 1.15, 0.65, blush, opacity: 0.75), Oval(20.4, 12.2, 1.15, 0.65, blush, opacity: 0.75),
            Drawn("M15.3,12 Q16,12.8 16.7,12", null, ink, 0.5));
        face.Box.Opacity = 0;
        var eyes = Part(face.Box, 16, 10.4, Oval(13.2, 10.4, 0.85, 0.85, ink), Oval(18.8, 10.4, 0.85, 0.85, ink), Oval(13.5, 10.1, 0.3, 0.3, Solid("#FFFFFF")), Oval(19.1, 10.1, 0.3, 0.3, Solid("#FFFFFF")));
        var happy = Part(face.Box, 0, 0, Drawn("M12.3,10.8 Q13.2,9.5 14.1,10.8 M17.9,10.8 Q18.8,9.5 19.7,10.8", null, ink, 0.55));
        happy.Box.Opacity = 0;
        var cloud = Part(stage, 22, 22, drawing);

        var burst = NewMoment();
        Fade(burst, face, (0, 0), (0.5, 1), (4.8, 1), (5.4, 0));
        Key(burst, eyes.Scale, ScaleTransform.ScaleYProperty, (0, 1), (0.95, 1), (1.03, 0.1), (1.11, 1));
        Fade(burst, eyes, (0, 1), (2.55, 1), (2.6, 0), (4.4, 0), (4.45, 1));
        Fade(burst, happy, (0, 0), (2.55, 0), (2.6, 1), (4.4, 1), (4.45, 0));
        Key(burst, cloud.Scale, ScaleTransform.ScaleXProperty, (0, 1), (1.3, 1), (1.45, 1.08), (1.65, 0.97), (1.85, 1));
        Key(burst, cloud.Scale, ScaleTransform.ScaleYProperty, (0, 1), (1.3, 1), (1.45, 0.92), (1.65, 1.04), (1.85, 1));
        Key(burst, cloud.Move, TranslateTransform.YProperty, (0, 0), (2.6, 0), (3.2, -1.5), (3.8, 0.5), (4.4, 0));
        foreach (var (drop, i) in rain.Select((drop, i) => (drop, i)))
        {
            var start = 1.5 + new[] { 0, 0.18, 0.06, 0.24, 0.12 }[i];
            Fade(burst, drop, (0, 0), (start, 0), (start + 0.08, 1), (start + 0.55, 0.8), (start + 0.75, 0));
            Slide(burst, drop, TranslateTransform.YProperty, (0, 0, Eased), (start, 0, Eased), (start + 0.75, 18, Falling));
        }
        Fade(burst, rainbow, (0, 0), (2.2, 0), (2.8, 0.95), (4.2, 0.9), (5.0, 0));
        Grow(burst, rainbow, (0, 0.3), (2.2, 0.3), (2.9, 1), (5.0, 1));
        foreach (var (start, twinkle) in new[] { (2.9, twinkles[0]), (3.2, twinkles[1]) })
        {
            Grow(burst, twinkle, (0, 0), (start, 0), (start + 0.3, 1.2), (start + 0.9, 0));
            Spin(burst, twinkle, (0, 0), (start, 0), (start + 0.9, 45));
        }
    }

    /// <summary>
    /// Starry violet: two little star twins with faces, one blue and one pink, fly out of the tip of the big crystal star
    /// in the corner, chase each other round a figure eight trailing sparkles, high-five with a flash and fly home,
    /// where the tip glints.
    /// </summary>
    private void AddTwinStars(double width, double height)
    {
        var stage = Stage(new Point(776, 456), width, height);
        var gold = Solid("#E8CB8A");
        var ink = Solid("#2A2350");
        var white = Solid("#FFFFFF");
        var blush = Solid("#FF8FB8");
        var burst = NewMoment();
        var routes = new Dictionary<int, Route>
        {
            [-1] = new([("M776,468 C770,500 712,532 673,556", 0.3, 1.2, Eased), (FigureEight(673, 556, 46, 38, -1), 1.2, 3.8, Glide), ("M673,556 C700,536 768,512 776,470", 4.1, 5.1, Eased)]),
            [1] = new([("M776,468 C790,512 676,520 687,556", 0.3, 1.2, Eased), (FigureEight(687, 556, 46, 38, 1), 1.2, 3.8, Glide), ("M687,556 C680,520 784,508 776,470", 4.1, 5.1, Eased)])
        };
        foreach (var (side, sparkle) in new[] { (-1, "#BFE4FF"), (1, "#FFD0E6") })
        {
            var trail = Solid(sparkle);
            for (var i = 0; i < 22; i++)
            {
                var seconds = 1.25 + i * 0.12;
                var point = routes[side].At(seconds, out _);
                var r = i % 3 == 0 ? 1.2 : 0.8;
                var dot = Oval(point.X, point.Y, r, r, trail, opacity: 0);
                stage.Children.Add(dot);
                Fade(burst, dot, (0, 0), (seconds, 0), (seconds + 0.05, 0.9), (seconds + 0.8, 0));
            }
        }
        foreach (var (side, light, mid, deep) in new[] { (-1, "#F2FAFF", "#A8DCFF", "#6E8BFF"), (1, "#FFF4FA", "#FFC0DE", "#B86BE0") })
        {
            var face = Group(0, 0, 1.5,
                Drawn("M0,-8.5 C2,-3.5 3.5,-2 8.5,0 C3.5,2 2,3.5 0,8.5 C-2,3.5 -3.5,2 -8.5,0 C-3.5,-2 -2,-3.5 0,-8.5 Z", Radial(new Point(0.4, 0.35), 0.75, (light, 0), (mid, 0.45), (deep, 1)), gold, 0.5),
                Oval(-1.25, -0.5, 0.62, 0.62, ink), Oval(1.25, -0.5, 0.62, 0.62, ink), Oval(-1.05, -0.72, 0.22, 0.22, white), Oval(1.45, -0.72, 0.22, 0.22, white),
                Oval(-2.1, 0.75, 0.75, 0.42, blush, opacity: 0.75), Oval(2.1, 0.75, 0.75, 0.42, blush, opacity: 0.75),
                Drawn("M-0.5,0.7 Q0,1.2 0.5,0.7", null, ink, 0.32));
            var star = Part(stage, 0, 0);
            var wobble = Part(star.Box, 0, 0, face);
            star.Box.Opacity = 0;
            star.Scale.ScaleX = star.Scale.ScaleY = 0.4;
            Ride(burst, star, routes[side], Heading.Upright);
            Fade(burst, star, (0, 0), (0.3, 0), (0.5, 1), (4.9, 1), (5.1, 0));
            Grow(burst, star, (0, 0.4), (0.3, 0.4), (0.9, 1), (4.6, 1), (5.1, 0.4));
            Spin(burst, wobble, (0, 0), (1.2, 18 * side), (2.0, -14 * side), (2.8, 14 * side), (3.6, -10 * side), (4.4, 0));
        }
        var clap = Part(stage, 680, 556, Oval(680, 556, 9, 9, null, Solid("#F4F0FF"), 0.9),
            Drawn("M680,546 Q681.2,554.8 690,556 Q681.2,557.2 680,566 Q678.8,557.2 670,556 Q678.8,554.8 680,546 Z", white));
        clap.Box.Opacity = 0;
        clap.Scale.ScaleX = clap.Scale.ScaleY = 0;
        var tip = Part(stage, 776, 457, Drawn("M776,449 Q776.9,456.1 784,457 Q776.9,457.9 776,465 Q775.1,457.9 768,457 Q775.1,456.1 776,449 Z", white, gold, 0.6));
        tip.Scale.ScaleX = tip.Scale.ScaleY = 0;
        Grow(burst, clap, (0, 0), (3.8, 0), (3.95, 1.4), (4.35, 0));
        Fade(burst, clap, (0, 0), (3.8, 0), (3.85, 1), (4.35, 0));
        Grow(burst, tip, (0, 0), (5.0, 0), (5.2, 1.2), (5.6, 0));
        Spin(burst, tip, (0, 0), (5.0, 0), (5.6, 45));
    }
}
