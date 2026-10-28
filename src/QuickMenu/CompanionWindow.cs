using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace QuickMenu;

/// <summary>
/// The character (Claude娘 for now) in a see-through window of her own, standing across the menu's right edge: her hair and skirt reach
/// over the frame and her head rises above it. The window never takes focus, so pointing at her or clicking her
/// leaves the menu active; clicks on the transparent parts fall through to whatever is behind.
/// </summary>
internal sealed class CompanionWindow : Window
{
    /// <summary>How far she reaches over the menu's right edge, in DIPs.</summary>
    public const double Overlap = 30;
    /// <summary>Her feet stand this far below the menu's bottom edge.</summary>
    public const double FeetDrop = 8;
    private const double Pad = 24;           // room above and to the right for the glints

    private readonly Grid _figure = new() { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent };
    private readonly Image _body = new() { Stretch = Stretch.Uniform };
    private readonly Image _fadeBody = new() { Stretch = Stretch.Uniform, Opacity = 0, IsHitTestVisible = false };
    private readonly Image _blink = new() { Stretch = Stretch.Uniform, Opacity = 0 };
    private readonly Canvas _glintLayer = new() { IsHitTestVisible = false };
    private readonly AmbientScene _glints;
    private readonly TranslateTransform _hop = new();
    private readonly DispatcherTimer _blinkTimer = new();
    // Plays an animated pose: each picture swaps in once its hold time is up, so the see-through window repaints
    // about once a second rather than on every frame.
    private readonly DispatcherTimer _frameTimer = new();
    private IReadOnlyList<(BitmapSource Frame, TimeSpan Hold)>? _frames;
    private int _frame;
    private readonly Random _random = new();
    private DateTime _lastHopUtc;
    private bool _animated;
    private bool _blinking;
    private CancellationTokenSource? _blinkCancellation;
    private CharacterPose? _pose;
    private double _figureWidth, _figureHeight, _scale = 1;
    private (Color Color, byte Halo, int Count, Geometry? Shape) _glint;

    public CompanionWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        RenderOptions.SetBitmapScalingMode(_body, BitmapScalingMode.HighQuality);
        RenderOptions.SetBitmapScalingMode(_fadeBody, BitmapScalingMode.HighQuality);
        RenderOptions.SetBitmapScalingMode(_blink, BitmapScalingMode.HighQuality);
        _figure.Children.Add(_body);
        _figure.Children.Add(_fadeBody);
        _figure.Children.Add(_blink);
        _figure.RenderTransform = _hop;
        _figure.Margin = new Thickness(0, 0, Pad, 0);
        _figure.MouseEnter += (_, _) => Hop();
        _figure.MouseLeftButtonDown += (_, _) => Clicked?.Invoke();
        Content = new Grid { Children = { _figure, _glintLayer } };
        _glints = new AmbientScene(_glintLayer);
        _blinkTimer.Tick += (_, _) => Blink();
        _frameTimer.Tick += (_, _) => NextFrame();
        SourceInitialized += (_, _) => HostNative.MakeNonActivating(this);
    }

    /// <summary>She was clicked. Her window never activates itself, so the menu decides what a click means.</summary>
    public event Action? Clicked;

    public bool CanBlink => _blink.Source is not null;
    public ImageSource? Pose => _body.Source;
    public bool IsAnimating => _animated && _glints.IsRunning;
    internal bool HasMotion => _animated || _glints.IsRunning || _blinkTimer.IsEnabled || _frameTimer.IsEnabled
        || HasAnimatedProperties || _hop.HasAnimatedProperties || _fadeBody.HasAnimatedProperties;

    /// <summary>The space she takes beyond the menu at full size: width right of its edge, height above its bottom.</summary>
    public Size Footprint => _pose is null ? default : new(_figureWidth + Pad - Overlap, _figureHeight + Pad - FeetDrop);

    /// <summary>Her size relative to full size; screens with too little room beside the menu show her smaller.</summary>
    public double Scale
    {
        get => _scale;
        set { if (_scale == value) return; _scale = value; if (_pose is not null) Layout(); }
    }

    /// <summary>Shows her in <paramref name="pose"/>, glinting in the theme's colour, and sizes the window to a figure <paramref name="figureHeight"/> DIPs tall.</summary>
    public void Present(string name, CharacterPose pose, double figureHeight, Color glint, byte glintHalo, int glintCount, Geometry? shape = null)
    {
        var running = _animated;
        StopMotion();
        Title = name;
        _body.Source = pose.Body;
        _blink.Source = pose.Blink;
        _frames = pose.Frames is { Count: > 1 } frames ? frames : null;
        _frame = 0;
        _pose = pose;
        _figureHeight = figureHeight;
        _figureWidth = pose.Body.PixelWidth / (double)pose.Body.PixelHeight * figureHeight;
        _glint = (glint, glintHalo, glintCount, shape);
        Layout();
        if (running) StartMotion();
    }

    private void Layout()
    {
        var running = _animated;
        StopMotion();
        double width = _figureWidth * _scale, height = _figureHeight * _scale;
        _figure.Width = width;
        _figure.Height = height;
        Width = width + Pad;
        Height = height + Pad;
        _glints.BuildGlints(new Rect(0, Pad, width, height), _pose!.Glints, _glint.Color, _glint.Count, _glint.Halo, _glint.Shape);
        if (running) StartMotion();
    }

    public void PlaceBeside(Window menu) => HostNative.PlaceAtRightEdge(this, menu, Overlap, FeetDrop);

    /// <summary>Gentle slide-and-fade entrance when she first appears beside the menu.</summary>
    public void PlayEntrance()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        _hop.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        _hop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }

    /// <summary>Cheerful double-hop and blink when clicked while the menu is active.</summary>
    public void PlayClickReaction()
    {
        if (!_animated) return;
        _lastHopUtc = DateTime.UtcNow;
        var bounce = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(360), FillBehavior = FillBehavior.Stop };
        var outEase = new KeySpline(0.25, 0.8, 0.4, 1);
        var inEase = new KeySpline(0.5, 0, 0.85, 0.6);
        bounce.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        bounce.KeyFrames.Add(new SplineDoubleKeyFrame(-11, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100)), outEase));
        bounce.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(195)), inEase));
        bounce.KeyFrames.Add(new SplineDoubleKeyFrame(-5.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(275)), outEase));
        bounce.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360)), inEase));
        _hop.BeginAnimation(TranslateTransform.YProperty, bounce);
        if (CanBlink) Blink();
    }

    /// <summary>
    /// Blinking, the glints and the hop. She does not breathe or float continuously: her window is see-through,
    /// and WPF repaints such windows in software, so a figure that never stops moving costs a few percent of a
    /// core the whole time the menu is open.
    /// </summary>
    public void StartMotion()
    {
        if (_animated) return;
        _animated = true;
        _blinkCancellation = new CancellationTokenSource();
        _blinkTimer.Interval = TimeSpan.FromSeconds(3 + _random.NextDouble() * 4);
        if (CanBlink) _blinkTimer.Start();
        if (_frames is not null) { _frameTimer.Interval = _frames[0].Hold; _frameTimer.Start(); }
        _glints.Start();
    }

    public bool IsPlayingFrames => _frameTimer.IsEnabled;

    private void NextFrame()
    {
        if (_frames is null) { _frameTimer.Stop(); return; }
        var previous = _body.Source;
        _frame = (_frame + 1) % _frames.Count;
        _body.Source = _frames[_frame].Frame;
        _frameTimer.Interval = _frames[_frame].Hold;
        if (_animated && previous is not null && !ReferenceEquals(previous, _body.Source))
        {
            _fadeBody.Source = previous;
            var crossFade = new DoubleAnimation(0.85, 0, TimeSpan.FromMilliseconds(90)) { FillBehavior = FillBehavior.Stop };
            crossFade.Completed += (_, _) => _fadeBody.Source = null;
            _fadeBody.BeginAnimation(OpacityProperty, crossFade);
        }
    }

    public void StopMotion()
    {
        _animated = false;
        _blinkCancellation?.Cancel();
        _blinkCancellation?.Dispose();
        _blinkCancellation = null;
        _blinking = false;
        BeginAnimation(OpacityProperty, null);
        _hop.BeginAnimation(TranslateTransform.XProperty, null);
        _hop.BeginAnimation(TranslateTransform.YProperty, null);
        _fadeBody.BeginAnimation(OpacityProperty, null);
        _fadeBody.Opacity = 0;
        _fadeBody.Source = null;
        _blinkTimer.Stop();
        _blink.Opacity = 0;
        _frameTimer.Stop();
        // An animated pose starts from its first picture again next time.
        if (_frames is not null && _frame != 0) { _frame = 0; _body.Source = _frames[0].Frame; }
        _glints.Stop();
    }

    private async void Blink()
    {
        if (!_animated || _blinking || _blinkCancellation is not { } cancellation) return;
        _blinking = true;
        var token = cancellation.Token;
        _blinkTimer.Interval = TimeSpan.FromSeconds(3 + _random.NextDouble() * 4);
        var times = _random.NextDouble() < 0.2 ? 2 : 1;
        try
        {
            for (var i = 0; i < times; i++)
            {
                token.ThrowIfCancellationRequested();
                _blink.Opacity = 1;
                await Task.Delay(120, token);
                _blink.Opacity = 0;
                if (i + 1 < times) await Task.Delay(160, token);
            }
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_blinkCancellation, cancellation)) { _blink.Opacity = 0; _blinking = false; } }
    }

    // Pointing at her gets a small, happy hop, debounced so brushing across the edge does not jitter.
    private void Hop()
    {
        if (!_animated || (DateTime.UtcNow - _lastHopUtc).TotalMilliseconds < 280) return;
        _lastHopUtc = DateTime.UtcNow;
        _hop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -10, TimeSpan.FromMilliseconds(170)) { AutoReverse = true, FillBehavior = FillBehavior.Stop, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }
}
