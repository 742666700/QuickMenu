using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;
using QuickMenu.Models;
using ThemeMode = QuickMenu.Models.ThemeMode;

namespace QuickMenu;

/// <summary>
/// Slow background motion behind the menu content, one scene per theme. Clocks exist only while the menu is
/// shown: hiding removes them, so WPF's frame timer stops and a hidden menu costs nothing, and showing again
/// recreates them seeked to where the scene had got to. Without animation (system setting, software rendering, preview captures) the same scene is
/// drawn as a still frame.
/// </summary>
/// <remarks>
/// Every running clock keeps WPF drawing frames, even while the value it drives is standing still. So brief events
/// (a shooting star, a sparkle flaring, a bubble rising) are bursts: a timer starts their clocks when they are due
/// and the clocks end with the event, leaving nothing to tick in between. Only truly continuous motion keeps a clock
/// running, at the lowest frame rate that still looks smooth for its speed.
/// </remarks>
internal sealed partial class AmbientScene
{
    // Drifting lights and clouds move a pixel or two a second and look the same at 6 fps; falling petals and twinkles
    // need 12; only the shooting star asks for 24.
    private const int SlowFrameRate = 6, FrameRate = 12, FastFrameRate = 24;
    private sealed record Motion(IAnimatable Target, DependencyProperty Property, AnimationTimeline Timeline, double Phase);
    /// <summary>
    /// Clocks that play together once every <see cref="Period"/>; between plays, every target rests at its base value.
    /// With a <see cref="StartDelay"/> the burst opens every showing that long after it starts, instead of keeping its rhythm.
    /// </summary>
    private sealed record Burst(TimeSpan Period, double Phase, List<(IAnimatable Target, DependencyProperty Property, AnimationTimeline Timeline)> Parts, TimeSpan? StartDelay = null);

    private readonly Canvas _layer;
    private readonly RotateTransform _sparkTurn = new();
    private readonly List<Motion> _motions = new();
    private readonly List<Burst> _bursts = new();
    private readonly List<DispatcherTimer> _timers = new();
    private readonly Dictionary<Burst, DateTimeOffset> _burstDue = new();
    private readonly List<(IAnimatable Target, DependencyProperty Property, AnimationClock Clock)> _clocks = new();
    private Random _random = new(7);
    private Burst? _moment;
    private TranslateTransform? _whaleRise;
    private TimeSpan _elapsed;
    private DateTime _startedAt;

    public bool IsRunning { get; private set; }
    internal bool HasBursts => _bursts.Count > 0;
    public event Action<ThemeMode>? MomentStarted;
    internal event Action? BurstStarted;
    internal DateTimeOffset? AlignBurst(DateTimeOffset earliest, DateTimeOffset latest)
    {
        DateTimeOffset? next = null;
        foreach (var (burst, due) in _burstDue)
        {
            var candidate = due;
            if (candidate < earliest)
                candidate += TimeSpan.FromTicks(((earliest - candidate).Ticks + burst.Period.Ticks - 1) / burst.Period.Ticks * burst.Period.Ticks);
            if (candidate <= latest && (next is null || candidate < next)) next = candidate;
        }
        return next;
    }
    internal void ResetForPerformance() { Stop(); _elapsed = TimeSpan.Zero; _random = new Random(7); }
    public ThemeMode Mode { get; private set; }

    public AmbientScene(Canvas layer, FrameworkElement? greetingSpark = null)
    {
        _layer = layer;
        if (greetingSpark is null) return;
        greetingSpark.RenderTransformOrigin = new Point(0.5, 0.5);
        greetingSpark.RenderTransform = _sparkTurn;
    }

    /// <summary>
    /// Replaces the scene for a theme. The scene starts still; call <see cref="Start"/> to animate it.
    /// <paramref name="spinSpark"/> is false while a character's avatar stands in for the greeting spark.
    /// </summary>
    public void Build(ThemeMode mode, Size area, bool spinSpark = true)
    {
        Clear();
        Mode = mode;
        var width = Math.Max(area.Width, 400);
        var height = Math.Max(area.Height, 300);
        // The brand spark in the greeting turns once every 24 s in every theme.
        if (spinSpark) Add(_sparkTurn, RotateTransform.AngleProperty, Slow(new DoubleAnimation(0, 360, TimeSpan.FromSeconds(24)) { RepeatBehavior = RepeatBehavior.Forever }), 0);
        switch (mode)
        {
            case ThemeMode.Oled: BuildNightSky(width, height); break;
            case ThemeMode.Cream: BuildPetals(width, height); break;
            case ThemeMode.ChatGpt: BuildPearl(width, height); break;
            case ThemeMode.DeepSeek: BuildDeepSea(width, height); break;
            case ThemeMode.Gemini: BuildStarViolet(width, height); break;
            default: BuildDaylight(width, height); break;
        }
    }

    /// <summary>Only sparks glinting at <paramref name="spots"/> (fractions of <paramref name="figure"/>), for the window a character stands in.</summary>
    public void BuildGlints(Rect figure, IReadOnlyList<Point> spots, Color color, int count, byte haloAlpha, Geometry? shape = null)
    {
        Clear();
        AddGlints(figure, spots, color, count, haloAlpha, shape);
    }

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _startedAt = DateTime.UtcNow;
        foreach (var motion in _motions)
        {
            if (Timeline.GetDesiredFrameRate(motion.Timeline) is null) Timeline.SetDesiredFrameRate(motion.Timeline, FrameRate);
            var clock = motion.Timeline.CreateClock();
            motion.Target.ApplyAnimationClock(motion.Property, clock);
            var offset = _elapsed;
            if (motion.Phase > 0 && motion.Timeline.Duration.HasTimeSpan) offset += TimeSpan.FromTicks((long)(motion.Timeline.Duration.TimeSpan.Ticks * motion.Phase));
            if (offset > TimeSpan.Zero) clock.Controller?.Seek(offset, TimeSeekOrigin.BeginTime);
            _clocks.Add((motion.Target, motion.Property, clock));
        }
        foreach (var burst in _bursts)
        {
            // Keep each burst's place in its rhythm across hide and show, unless it opens every showing.
            var due = burst.StartDelay?.Ticks ?? burst.Period.Ticks * burst.Phase - _elapsed.Ticks % burst.Period.Ticks;
            if (due < 0) due += burst.Period.Ticks;
            var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromTicks(Math.Max((long)due, TimeSpan.TicksPerMillisecond)) };
            _burstDue[burst] = DateTimeOffset.UtcNow + timer.Interval;
            timer.Tick += (_, _) => { timer.Interval = burst.Period; _burstDue[burst] = DateTimeOffset.UtcNow + burst.Period; Play(burst); };
            timer.Start();
            _timers.Add(timer);
        }
    }

    public void Stop()
    {
        StopEffects();
        if (!IsRunning) return;
        IsRunning = false;
        _elapsed += DateTime.UtcNow - _startedAt;
        RemoveClocks();
    }

    /// <summary>The DeepSeek whale's rise: at rest she is under the water (null in other themes).</summary>
    internal TranslateTransform? WhaleRise => _whaleRise;
    internal void PreviewMomentStart() { if (IsRunning && _moment is { } moment) Play(moment); }

    /// <summary>For self-check captures: holds the theme's signature moment at <paramref name="at"/> into it until <see cref="Stop"/>.</summary>
    internal bool PreviewMoment(TimeSpan at)
    {
        if (_moment is null) return false;
        IsRunning = true;
        _startedAt = DateTime.UtcNow;
        foreach (var (target, property, timeline) in _moment.Parts)
        {
            var clock = timeline.CreateClock();
            target.ApplyAnimationClock(property, clock);
            clock.Controller!.Pause();
            clock.Controller.SeekAlignedToLastTick(at, TimeSeekOrigin.BeginTime);
            _clocks.Add((target, property, clock));
        }
        return true;
    }

    private void Play(Burst burst)
    {
        BurstStarted?.Invoke();
        if (!IsRunning) return;
        if (ReferenceEquals(burst, _moment)) MomentStarted?.Invoke(Mode);
        foreach (var (target, property, timeline) in burst.Parts)
        {
            var clock = timeline.CreateClock();
            var entry = (target, property, clock);
            // When the event is over, drop the clock so nothing is left ticking until the next one.
            clock.Completed += (_, _) => { if (_clocks.Remove(entry)) target.ApplyAnimationClock(property, null); };
            target.ApplyAnimationClock(property, clock);
            _clocks.Add(entry);
        }
    }

    private void RemoveClocks()
    {
        foreach (var timer in _timers) timer.Stop();
        _timers.Clear();
        _burstDue.Clear();
        foreach (var (target, property, clock) in _clocks.ToArray()) { clock.Controller?.Stop(); target.ApplyAnimationClock(property, null); }
        _clocks.Clear();
    }

    private void Clear()
    {
        StopEffects();
        RemoveClocks();
        _motions.Clear();
        _bursts.Clear();
        _moment = null;
        _whaleRise = null;
        _layer.Children.Clear();
        IsRunning = false;
        _sparkTurn.Angle = 0;
    }

    private void Add(IAnimatable target, DependencyProperty property, AnimationTimeline timeline, double phase) => _motions.Add(new(target, property, timeline, phase));

    private Burst NewBurst(double periodSeconds, double phase, double? startDelaySeconds = null)
    {
        var burst = new Burst(TimeSpan.FromSeconds(periodSeconds), phase, new(), startDelaySeconds is { } delay ? TimeSpan.FromSeconds(delay) : null);
        _bursts.Add(burst);
        return burst;
    }

    private static AnimationTimeline Slow(AnimationTimeline timeline) { Timeline.SetDesiredFrameRate(timeline, SlowFrameRate); return timeline; }

    private double Between(double low, double high) => low + _random.NextDouble() * (high - low);

    private static SineEase Sine => new() { EasingMode = EasingMode.EaseInOut };

    /// <summary>A steady glide from 0 to <paramref name="to"/>, played once for a burst.</summary>
    private static DoubleAnimation Linear(int frameRate, double seconds, double to)
    {
        var animation = new DoubleAnimation(0, to, TimeSpan.FromSeconds(seconds)) { FillBehavior = FillBehavior.Stop };
        Timeline.SetDesiredFrameRate(animation, frameRate);
        return animation;
    }

    // OLED night: a clear night sky along the top of the menu. Pinpoint stars twinkle, Claude's spark glints
    // like the brightest stars and now and then a small shooting star slips across. Nothing moves over the
    // icons, and nearly every pixel stays #000000: on black, light has to be small and bright to look like
    // light; large and dim, it only looks brown. Now and then a sleepy moon looks in (AddSleepyMoon).
    private void BuildNightSky(double width, double height)
    {
        var header = new Rect(width * 0.55, 0, width * 0.36, 40);

        // Stars: most along the top band, a few scattered thinly lower down. Every fourth one twinkles on its
        // own, so each repaint touches a few pixels instead of the whole sky.
        var tints = new[] { Color.FromRgb(0xFF, 0xF7, 0xF0), Color.FromRgb(0xFF, 0xD8, 0xC2), Color.FromRgb(0xD8, 0xD2, 0xFF) };
        var sky = new Canvas();
        _layer.Children.Add(sky);
        for (var i = 0; i < 40; i++)
        {
            var area = i < 30 ? new Rect(0, 0, width, 64) : new Rect(0, 64, width, height - 110);
            var size = i % 9 == 0 ? Between(2.2, 2.8) : Between(1.1, 1.9);
            var fill = new SolidColorBrush(tints[i % 3]) { Opacity = i % 9 == 0 ? 1 : Between(0.45, 0.9) };
            fill.Freeze();
            var star = new Ellipse { Width = size, Height = size, Fill = fill };
            Canvas.SetLeft(star, area.X + _random.NextDouble() * area.Width);
            Canvas.SetTop(star, area.Y + _random.NextDouble() * area.Height);
            sky.Children.Add(star);
            if (i % 4 == 0)
                Add(star, UIElement.OpacityProperty, new DoubleAnimation(Between(0.15, 0.3), 1, TimeSpan.FromSeconds(Between(1.6, 3.4))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Sine }, _random.NextDouble());
        }

        AddGlints(header, [new(0.86, 0.2), new(0.2, 0.06), new(0.04, 0.62)], Color.FromRgb(0xFF, 0xE6, 0xD6), 3, 0x70);

        // Two small shooting stars on different rhythms, so they never feel like a loop.
        AddMeteor(new Point(width * 0.72, 18), 13, 0.55);
        AddMeteor(new Point(width * 0.38, 10), 19, 0.1);
        AddSleepyMoon(width, height);
    }

    /// <summary>A short, faint streak that slides down-left for 0.7 s once every <paramref name="seconds"/>.</summary>
    private void AddMeteor(Point head, double seconds, double phase)
    {
        const double length = 90, travelX = -210, travelY = 84;
        var trail = new LinearGradientBrush(Colors.Transparent, Color.FromRgb(0xFF, 0xF4, 0xEC), 0);
        trail.Freeze();
        var meteor = new Rectangle { Width = length, Height = 1.2, Fill = trail, RadiusX = 0.6, RadiusY = 0.6, Opacity = 0 };
        var travel = new TranslateTransform();
        // The bright end leads; turning by the travel angle (about 158°) puts the faint tail behind it, up and to the right.
        meteor.RenderTransformOrigin = new Point(1, 0.5);
        meteor.RenderTransform = new TransformGroup { Children = { new RotateTransform(Math.Atan2(travelY, travelX) * 180 / Math.PI), travel } };
        Canvas.SetLeft(meteor, head.X - length);
        Canvas.SetTop(meteor, head.Y);
        _layer.Children.Add(meteor);
        var burst = NewBurst(seconds, phase);
        burst.Parts.Add((travel, TranslateTransform.XProperty, Once(FastFrameRate, (0, 0), (0.7, travelX))));
        burst.Parts.Add((travel, TranslateTransform.YProperty, Once(FastFrameRate, (0, 0), (0.7, travelY))));
        burst.Parts.Add((meteor, UIElement.OpacityProperty, Once(FastFrameRate, (0, 0), (0.1, 0.75), (0.45, 0.6), (0.7, 0))));
    }

    /// <summary>Claude's spark as a star that flares, turns a little and fades, in turn with its neighbours.</summary>
    private void AddGlints(Rect figure, IReadOnlyList<Point> spots, Color color, int count, byte haloAlpha, Geometry? shape = null)
    {
        var brush = new SolidColorBrush(color); brush.Freeze();
        var halo = new RadialGradientBrush(Color.FromArgb(haloAlpha, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B)); halo.Freeze();
        const double period = 7.5;
        for (var i = 0; i < Math.Min(count, spots.Count); i++)
        {
            var size = i == 0 ? 15 : 11;
            var holder = new Grid { Width = size * 3, Height = size * 3, RenderTransformOrigin = new Point(0.5, 0.5), Opacity = 0.25 };
            holder.Children.Add(new Ellipse { Fill = halo });
            var spark = shape is null ? Spark(brush, size) : new Path { Data = shape, Stroke = brush, StrokeLineJoin = PenLineJoin.Round, Stretch = Stretch.Uniform, Width = size, Height = size };
            spark.StrokeThickness = 2;
            holder.Children.Add(spark);
            var scale = new ScaleTransform(0.2, 0.2);
            var turn = new RotateTransform(0);
            holder.RenderTransform = new TransformGroup { Children = { scale, turn } };
            Canvas.SetLeft(holder, figure.X + figure.Width * spots[i].X - size * 1.5);
            Canvas.SetTop(holder, figure.Y + figure.Height * spots[i].Y - size * 1.5);
            _layer.Children.Add(holder);
            var burst = NewBurst(period, i / (double)count);
            var flare = Once(FrameRate, (0, 0.2), (0.35, 1), (1.4, 0.2));
            burst.Parts.Add((scale, ScaleTransform.ScaleXProperty, flare));
            burst.Parts.Add((scale, ScaleTransform.ScaleYProperty, flare.Clone()));
            burst.Parts.Add((turn, RotateTransform.AngleProperty, Once(FrameRate, (0, 0), (1.4, 45))));
            burst.Parts.Add((holder, UIElement.OpacityProperty, Once(FrameRate, (0, 0.25), (0.35, 1), (1.4, 0.25))));
        }
    }

    /// <summary>
    /// Eased key frames at (seconds, value), played once for a burst. When it ends the property goes back to its base
    /// value, so the last frame should match it.
    /// </summary>
    private static DoubleAnimationUsingKeyFrames Once(int frameRate, params (double Seconds, double Value)[] frames) =>
        Keyed(frameRate, frames.Select(frame => (frame.Seconds, frame.Value, Eased)).ToArray());

    /// <summary>Like <see cref="Once"/>, with the curve into each key frame given.</summary>
    private static DoubleAnimationUsingKeyFrames Keyed(int frameRate, params (double Seconds, double Value, KeySpline Curve)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(frames[^1].Seconds), FillBehavior = FillBehavior.Stop };
        Timeline.SetDesiredFrameRate(animation, frameRate);
        foreach (var (seconds, value, curve) in frames)
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)), curve));
        return animation;
    }

    // Eased in and out; thrown up (fast, then slowing at the top); falling (slow, then faster); and steady.
    private static readonly KeySpline Eased = Frozen(new KeySpline(0.4, 0, 0.2, 1)), Thrown = Frozen(new KeySpline(0.25, 0.6, 0.45, 1)),
        Falling = Frozen(new KeySpline(0.55, 0, 0.85, 0.45)), Steady = Frozen(new KeySpline(0, 0, 1, 1));
    private static KeySpline Frozen(KeySpline curve) { curve.Freeze(); return curve; }

    // OLED day: extra light pools float over the gradient backdrop and breathe, and now and then a paper plane
    // loops across the top (AddPaperPlane).
    private void BuildDaylight(double width, double height)
    {
        AddGlow(Color.FromArgb(0x66, 0xFF, 0xD2, 0xB8), width * 0.7, new Point(width * 0.2, height * 0.15), new Vector(width * 0.2, height * 0.12), 26, 21);
        AddGlow(Color.FromArgb(0x55, 0xFF, 0xC8, 0xE6), width * 0.55, new Point(width * 0.85, height * 0.2), new Vector(width * 0.15, height * 0.18), 23, 29);
        AddGlow(Color.FromArgb(0x55, 0xB4, 0xC8, 0xFF), width * 0.75, new Point(width * 0.75, height * 0.95), new Vector(width * 0.2, height * 0.1), 29, 24);
        AddPaperPlane(width, height);
    }

    // Pearl (ChatGPT): two little clouds like ChatGPT娘's companion drift along the empty band above the search box,
    // and three soft sparkles breathe beside them; the larger cloud now and then makes a rainbow (AddRainbowCloud).
    // Nothing moves over the list.
    private void BuildPearl(double width, double height)
    {
        // White on top with her wings' lilac underneath, so the clouds read on the pearl backdrop without an outline.
        var fill = new LinearGradientBrush(Colors.White, Color.FromRgb(0xD9, 0xD2, 0xF0), 90); fill.Freeze();
        var cloudShape = Geometry.Parse("M7,16 A5,5 0 0 1 6.5,6.4 A7.5,7.5 0 0 1 20,4.6 A5.5,5.5 0 0 1 28.6,9.2 A3.6,3.6 0 0 1 28,16 Z"); cloudShape.Freeze();
        AddRainbowCloud(width);
        foreach (var (x, y, size, seconds, phase) in new[] { (0.78, 22.0, 24.0, 46.0, 0.65) })
        {
            var cloud = new Path { Data = cloudShape, Fill = fill, Width = size, Height = size * 0.5, Stretch = Stretch.Uniform };
            var drift = new TranslateTransform();
            cloud.RenderTransform = drift;
            Canvas.SetLeft(cloud, width * x);
            Canvas.SetTop(cloud, y);
            _layer.Children.Add(cloud);
            Add(drift, TranslateTransform.XProperty, Slow(new DoubleAnimation(-width * 0.05, width * 0.05, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Sine }), phase);
        }
        AddTwinkles(Characters.Cross(), new SolidColorBrush(Color.FromRgb(0xA8, 0x9F, 0xD6)), new[] { new Point(width * 0.56, 22), new Point(width * 0.71, 8), new Point(width * 0.9, 18) }, 9, 6.5);
    }

    // Deep sea (DeepSeek): light on the water. A glint runs along the crest of the back swell, drifting and
    // brightening slowly; a cyan light breathes under the surface; and every few seconds a tiny luminous bubble
    // rises from the water at the right and fades. The swells and the big whale's tail sit still in the backdrop,
    // and beside the tail a little whale comes up now and then to spout (see AddWhale).
    private void BuildDeepSea(double width, double height)
    {
        AddGlow(Color.FromArgb(0x1C, 0x6F, 0xC3, 0xFF), width * 0.5, new Point(width * 0.82, height * 0.92), new Vector(width * 0.05, height * 0.03), 31, 23);

        var y0 = height * 0.76;
        var glint = new Path
        {
            Data = Geometry.Parse(FormattableString.Invariant($"M{width * 0.34},{y0 + height * 0.035} C{width * 0.5},{y0 + height * 0.05} {width * 0.58},{y0 + height * 0.02} {width * 0.66},{y0} C{width * 0.76},{y0 - height * 0.02} {width * 0.86},{y0 - height * 0.045} {width * 0.98},{height * 0.70 + 2}")),
            StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Opacity = 0.5
        };
        var shine = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0));
        shine.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0.45));
        shine.GradientStops.Add(new GradientStop(Color.FromArgb(0xD0, 0xBF, 0xE6, 0xFF), 0.7));
        shine.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xBF, 0xE6, 0xFF), 1));
        shine.Freeze();
        glint.Stroke = shine;
        var slide = new TranslateTransform();
        glint.RenderTransform = slide;
        _layer.Children.Add(glint);
        Add(slide, TranslateTransform.XProperty, Slow(new DoubleAnimation(-18, 18, TimeSpan.FromSeconds(17)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Sine }), 0);
        Add(glint, UIElement.OpacityProperty, Slow(new DoubleAnimation(0.35, 0.95, TimeSpan.FromSeconds(7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Sine }), 0.4);

        // Luminous bubbles: white cores with a blue halo, rising from the water along the right.
        var core = new RadialGradientBrush { GradientOrigin = new Point(0.4, 0.35) };
        core.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0));
        core.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0xC9, 0xE8, 0xFF), 0.45));
        core.GradientStops.Add(new GradientStop(Color.FromArgb(0x40, 0x4D, 0x6B, 0xFE), 0.8));
        core.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x4D, 0x6B, 0xFE), 1));
        core.Freeze();
        foreach (var (x, size, seconds, phase) in new[] { (0.9, 7.0, 6.0, 0.1), (0.95, 5.0, 8.0, 0.45), (0.86, 6.0, 9.5, 0.75) })
        {
            var bubble = new Ellipse { Width = size, Height = size, Fill = core, Opacity = 0 };
            var rise = new TranslateTransform();
            bubble.RenderTransform = rise;
            Canvas.SetLeft(bubble, width * x - size / 2);
            Canvas.SetTop(bubble, height * 0.93);
            _layer.Children.Add(bubble);
            var burst = NewBurst(seconds, phase);
            burst.Parts.Add((rise, TranslateTransform.YProperty, Once(FrameRate, (0, 0), (3.2, -height * 0.16))));
            burst.Parts.Add((rise, TranslateTransform.XProperty, Once(FrameRate, (0, 0), (1.1, 3), (2.2, -2), (3.2, 1))));
            burst.Parts.Add((bubble, UIElement.OpacityProperty, Once(FrameRate, (0, 0), (0.5, 0.9), (2.4, 0.7), (3.2, 0))));
        }

        AddWhale(width, height);
    }

    // Starry violet (Gemini): faint, still pinpoint stars in the empty bands; crystal stars like the ornaments in
    // Gemini娘's hair flare one after another with a thin cross of light; and a little charm of two crystal stars
    // hangs on a gold chain from the top right corner and sways now and then. Twin stars come out of the big crystal
    // star to play now and then (AddTwinStars).
    private void BuildStarViolet(double width, double height)
    {
        var dust = new GeometryGroup();
        var dice = new Random(11);
        for (var i = 0; i < 26; i++)
        {
            var top = i < 18;
            var at = top ? new Point(width * (0.4 + dice.NextDouble() * 0.52), 3 + dice.NextDouble() * 34) : new Point(width - 8 - dice.NextDouble() * 18, height * (0.3 + dice.NextDouble() * 0.6));
            var r = 0.5 + dice.NextDouble() * 0.7;
            dust.Children.Add(new EllipseGeometry(at, r, r));
        }
        dust.Freeze();
        var dustFill = new SolidColorBrush(Color.FromArgb(0x8C, 0xE4, 0xDD, 0xFF)); dustFill.Freeze();
        _layer.Children.Add(new Path { Data = dust, Fill = dustFill });

        var crystal = new DrawingImage(Services.Backdrops.CrystalStar(12)); crystal.Freeze();
        var violet = Color.FromRgb(0xC2, 0xA8, 0xFF);
        var halo = new RadialGradientBrush(Color.FromArgb(0x55, violet.R, violet.G, violet.B), Color.FromArgb(0, violet.R, violet.G, violet.B)); halo.Freeze();
        var streak = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        streak.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xE8, 0xDE, 0xFF), 0));
        streak.GradientStops.Add(new GradientStop(Color.FromArgb(0xE6, 0xF4, 0xF0, 0xFF), 0.5));
        streak.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xE8, 0xDE, 0xFF), 1));
        streak.Freeze();
        var spots = new[]
        {
            new Point(width * 0.47, 14), new Point(width * 0.63, 27), new Point(width * 0.8, 12),
            new Point(width - 16, height * 0.42), new Point(width - 20, height * 0.7), new Point(width - 14, height * 0.92)
        };
        const double seconds = 8.5;
        for (var i = 0; i < spots.Length; i++)
        {
            var size = i % 3 == 0 ? 14.0 : 10.0;
            var box = size * 3;
            var holder = new Grid { Width = box, Height = box, RenderTransformOrigin = new Point(0.5, 0.5), Opacity = 0.35 };
            holder.Children.Add(new Ellipse { Fill = halo, Margin = new Thickness(size * 0.6) });
            holder.Children.Add(new Rectangle { Fill = streak, Height = 1, Width = box, VerticalAlignment = VerticalAlignment.Center });
            holder.Children.Add(new Rectangle { Fill = streak, Height = 1, Width = box * 0.7, VerticalAlignment = VerticalAlignment.Center, LayoutTransform = new RotateTransform(90) });
            holder.Children.Add(new Image { Source = crystal, Width = size, Height = size });
            var scale = new ScaleTransform(0.7, 0.7);
            holder.RenderTransform = scale;
            Canvas.SetLeft(holder, spots[i].X - box / 2);
            Canvas.SetTop(holder, spots[i].Y - box / 2);
            _layer.Children.Add(holder);
            var burst = NewBurst(seconds, i / (double)spots.Length);
            var flare = Once(FrameRate, (0, 0.7), (0.4, 1.15), (1.5, 0.7));
            burst.Parts.Add((scale, ScaleTransform.ScaleXProperty, flare));
            burst.Parts.Add((scale, ScaleTransform.ScaleYProperty, flare.Clone()));
            burst.Parts.Add((holder, UIElement.OpacityProperty, Once(FrameRate, (0, 0.35), (0.4, 1), (1.5, 0.35))));
        }

        // The charm: a gold chain from the top edge with a small and a larger crystal star.
        // Short enough that both stars hang above the close button's row.
        var charm = new Canvas { Width = 20, Height = 50 };
        var chain = new SolidColorBrush(Color.FromArgb(0xC0, 0xE8, 0xCB, 0x8A)); chain.Freeze();
        charm.Children.Add(new Rectangle { Width = 1, Height = 32, Fill = chain, Margin = new Thickness(9.5, 0, 0, 0) });
        foreach (var (top, size) in new[] { (9.0, 11.0), (27.0, 18.0) })
        {
            var star = new Image { Source = crystal, Width = size, Height = size };
            Canvas.SetLeft(star, 10 - size / 2);
            Canvas.SetTop(star, top);
            charm.Children.Add(star);
        }
        var sway = new RotateTransform(0, 10, 0);
        charm.RenderTransform = sway;
        // Left of the settings and close buttons in the top-right corner, above the search box.
        Canvas.SetLeft(charm, width - 112);
        Canvas.SetTop(charm, -2);
        _layer.Children.Add(charm);
        var swing = NewBurst(11, 0.3);
        swing.Parts.Add((sway, RotateTransform.AngleProperty, Once(FrameRate, (0, 0), (0.5, 7), (1.1, -4.5), (1.7, 2.5), (2.3, -1), (2.9, 0))));
        AddTwinStars(width, height);
    }

    /// <summary>Small shapes with a soft halo that flare and turn a little, one after another, then rest.</summary>
    private void AddTwinkles(Geometry shape, Brush fill, IReadOnlyList<Point> spots, double size, double seconds)
    {
        if (!fill.IsFrozen) fill.Freeze();
        var color = fill is SolidColorBrush solid ? solid.Color : ((GradientBrush)fill).GradientStops[1].Color;
        var halo = new RadialGradientBrush(Color.FromArgb(0x50, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B)); halo.Freeze();
        for (var i = 0; i < spots.Count; i++)
        {
            var scaled = size * (i % 3 == 0 ? 1 : 0.75);
            var holder = new Grid { Width = scaled * 3, Height = scaled * 3, RenderTransformOrigin = new Point(0.5, 0.5), Opacity = 0.3 };
            holder.Children.Add(new Ellipse { Fill = halo });
            holder.Children.Add(new Path { Data = shape, Fill = fill, Width = scaled, Height = scaled, Stretch = Stretch.Uniform });
            var scale = new ScaleTransform(0.7, 0.7);
            var turn = new RotateTransform();
            holder.RenderTransform = new TransformGroup { Children = { scale, turn } };
            Canvas.SetLeft(holder, spots[i].X - scaled * 1.5);
            Canvas.SetTop(holder, spots[i].Y - scaled * 1.5);
            _layer.Children.Add(holder);
            var burst = NewBurst(seconds, i / (double)spots.Count);
            var flare = Once(FrameRate, (0, 0.7), (0.5, 1.1), (1.6, 0.7));
            burst.Parts.Add((scale, ScaleTransform.ScaleXProperty, flare));
            burst.Parts.Add((scale, ScaleTransform.ScaleYProperty, flare.Clone()));
            burst.Parts.Add((turn, RotateTransform.AngleProperty, Once(FrameRate, (0, 0), (1.6, 30))));
            burst.Parts.Add((holder, UIElement.OpacityProperty, Once(FrameRate, (0, 0.3), (0.5, 1), (1.6, 0.3))));
        }
    }

    // Cream daisy: orange daisies from Claude娘's outfit drift down across the window, turning and swaying gently as
    // they fall; in the corner a bud opens for a bee and a butterfly now and then (AddBeeGarden).
    private void BuildPetals(double width, double height)
    {
        AddGlow(Color.FromArgb(0x2C, 0xF0, 0x7A, 0x3A), width * 0.7, new Point(width * 0.92, height * 0.55), new Vector(width * 0.06, height * 0.1), 19, 27);
        AddGlow(Color.FromArgb(0x26, 0xF2, 0xC9, 0x6B), width * 0.55, new Point(width * 0.12, height * 0.05), new Vector(width * 0.12, height * 0.06), 33, 25);
        var petals = new SolidColorBrush(Color.FromRgb(0xF0, 0x7D, 0x3C)); petals.Freeze();
        var light = new SolidColorBrush(Color.FromRgb(0xF6, 0xA2, 0x63)); light.Freeze();
        var heart = new SolidColorBrush(Color.FromRgb(0x9A, 0x3A, 0x12)); heart.Freeze();
        for (var i = 0; i < 10; i++)
        {
            var size = Between(10, 19);
            var flower = Daisy(i % 3 == 0 ? light : petals, heart, size);
            var turn = new RotateTransform(Between(0, 72));
            var sway = new TranslateTransform();
            var fall = new TranslateTransform();
            flower.RenderTransformOrigin = new Point(0.5, 0.5);
            flower.RenderTransform = new TransformGroup { Children = { turn, sway, fall } };
            // Most flowers fall on the right half, where she stands; the ones over the left columns stay softer so text stays clear.
            Canvas.SetLeft(flower, i < 7 ? Between(width * 0.45, width) : Between(0, width * 0.45));
            Canvas.SetTop(flower, -30);
            var rawOpacity = Between(0.35, 0.75);
            flower.Opacity = i < 7 ? rawOpacity * 0.78 : rawOpacity * 0.45;
            _layer.Children.Add(flower);
            var phase = _random.NextDouble();
            var drift = -Between(60, 160);
            var duration = TimeSpan.FromSeconds(Between(14, 26));
            fall.Y = (height + 60) * phase; fall.X = drift * phase;
            Add(fall, TranslateTransform.YProperty, new DoubleAnimation(0, height + 60, duration) { RepeatBehavior = RepeatBehavior.Forever }, phase);
            Add(fall, TranslateTransform.XProperty, new DoubleAnimation(0, drift, duration) { RepeatBehavior = RepeatBehavior.Forever }, phase);
            Add(sway, TranslateTransform.XProperty, Slow(new DoubleAnimation(-9, 9, TimeSpan.FromSeconds(3.8 + i % 4 * 0.65)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Sine }), phase);
            Add(turn, RotateTransform.AngleProperty, new DoubleAnimation(turn.Angle, turn.Angle + (i % 2 == 0 ? 360 : -360), TimeSpan.FromSeconds(Between(8, 16))) { RepeatBehavior = RepeatBehavior.Forever }, 0);
        }
        AddBeeGarden(width, height);
    }

    /// <summary>A soft radial light that wanders on a slow Lissajous path (different X and Y periods) and breathes.</summary>
    private void AddGlow(Color color, double diameter, Point center, Vector reach, double secondsX, double secondsY, bool compact = false)
    {
        var clear = Color.FromArgb(0, color.R, color.G, color.B);
        var fill = compact
            ? new RadialGradientBrush(new GradientStopCollection { new(color, 0), new(Color.FromArgb((byte)(color.A * 0.35), color.R, color.G, color.B), 0.4), new(clear, 0.75), new(clear, 1) })
            : new RadialGradientBrush(color, clear);
        fill.Freeze();
        var glow = new Ellipse { Width = diameter, Height = diameter, Fill = fill };
        var move = new TranslateTransform();
        glow.RenderTransform = move;
        Canvas.SetLeft(glow, center.X - diameter / 2);
        Canvas.SetTop(glow, center.Y - diameter / 2);
        _layer.Children.Add(glow);
        Add(move, TranslateTransform.XProperty, Slow(new DoubleAnimation(-reach.X, reach.X, TimeSpan.FromSeconds(secondsX)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Sine }), 0.5);
        Add(move, TranslateTransform.YProperty, Slow(new DoubleAnimation(-reach.Y, reach.Y, TimeSpan.FromSeconds(secondsY)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Sine }), 0.5);
        Add(glow, UIElement.OpacityProperty, Slow(new DoubleAnimation(0.6, 1, TimeSpan.FromSeconds(secondsX / 3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Sine }), 0.3);
    }

    /// <summary>The app's eight-ray spark, from the shared geometry in Themes/Tokens.xaml.</summary>
    private static Path Spark(Brush stroke, double size) => new()
    {
        Data = new GeometryGroup { Children = { (Geometry)Application.Current.FindResource("SparkMajor"), (Geometry)Application.Current.FindResource("SparkMinor") } },
        Stroke = stroke, StrokeThickness = 2.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        Stretch = Stretch.Uniform, Width = size, Height = size
    };

    /// <summary>An eight-petal daisy like the ornaments on her dress.</summary>
    private static FrameworkElement Daisy(Brush petals, Brush heart, double size)
    {
        var ring = new GeometryGroup();
        for (var i = 0; i < 8; i++) ring.Children.Add(new EllipseGeometry(new Point(12, 6.2), 2.4, 5.4, new RotateTransform(i * 45, 12, 12)));
        ring.Freeze();
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new Path { Data = ring, Fill = petals });
        canvas.Children.Add(new Path { Data = new EllipseGeometry(new Point(12, 12), 3, 3), Fill = heart });
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }
}
