using System.Windows;
using QuickMenu.Models;
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
internal sealed partial class CompanionWindow : Window
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
    private Random _random = new();
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
        _figure.MouseEnter += (_, _) => RequestCue(CompanionCue.Hover);
        _figure.MouseMove += (_, _) => NoteActivity();
        _figure.MouseLeave += (_, _) => CancelHover();
        _figure.MouseLeftButtonDown += (_, _) => Clicked?.Invoke();
        Content = new Grid { Children = { _figure, _glintLayer } };
        _glints = new AmbientScene(_glintLayer);
        _blinkTimer.Tick += (_, _) => Blink();
        _frameTimer.Tick += (_, _) => NextFrame();
        SourceInitialized += (_, _) => HostNative.MakeNonActivating(this);
        InitializeDirector();
        IsVisibleChanged += (_, _) => { if (!IsVisible) { StopMotion(); ReleaseMotion(); } };
    }

    /// <summary>She was clicked. Her window never activates itself, so the menu decides what a click means.</summary>
    public event Action? Clicked;

    public bool CanBlink => _blink.Source is not null;
    public ImageSource? Pose => _body.Source;
    public bool IsAnimating => _animated && _glints.IsRunning;
    internal bool HasMotion => _animated || _glints.IsRunning || _blinkTimer.IsEnabled || _frameTimer.IsEnabled
        || _directorTimer.IsEnabled || _clipTimer.IsEnabled || _reactionTimer.IsEnabled
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
    public void Present(Character character, CharacterPose pose, double figureHeight, Color glint, byte glintHalo, int glintCount, Geometry? shape = null)
    {
        var running = _animated;
        StopMotion();
        if (_pose != pose) ReleaseMotion();
        _character = character;
        Title = character.Name;
        _body.Source = pose.Body;
        _blink.Source = _motion?.Blink;
        _frames = _motion?.Frames is { Count: > 1 } frames ? frames.Select(f => (f.Picture, f.Hold)).ToArray() : null;
        _frame = 0;
        _pose = pose;
        _figureHeight = figureHeight;
        _figureWidth = pose.Body.PixelWidth / (double)pose.Body.PixelHeight * figureHeight;
        _glint = (glint, glintHalo, glintCount, shape);
        Layout();
        EnsurePrepared();
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
        if (!_animated) return;
        var tempo = _character!.Temperament;
        var duration = TimeSpan.FromMilliseconds(tempo.EntranceMilliseconds);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        _hop.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(tempo.EntranceX, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        if (tempo.DoubleHop) Move(-tempo.HoverHeight, true, duration.TotalMilliseconds);
        else _hop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(tempo.EntranceDrop, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }

    public void PlayClickReaction() => RequestCue(CompanionCue.Click);

    /// <summary>
    /// Blinking, the glints and the hop. She does not breathe or float continuously: her window is see-through,
    /// and WPF repaints such windows in software, so a figure that never stops moving costs a few percent of a
    /// core the whole time the menu is open.
    /// </summary>
    public void StartMotion()
    {
        if (_animated) return;
        _animated = true;
        if (_pose is not null) _body.Source = _pose.Body;
        _blinkCancellation = new CancellationTokenSource();
        _blinkTimer.Interval = TimeSpan.FromSeconds(3 + _random.NextDouble() * 4);
        if (CanBlink) _blinkTimer.Start();
        if (_frames is not null) { _frameTimer.Interval = _frames[0].Hold; _frameTimer.Start(); }
        _glints.Start();
        StartDirector();
        EnsurePrepared();
    }

    public bool IsPlayingFrames => _frameTimer.IsEnabled;

    private void NextFrame()
    {
        if (_frames is null) { _frameTimer.Stop(); return; }
        _frame = (_frame + 1) % _frames.Count;
        SetPicture(_frames[_frame].Frame);
        _frameTimer.Interval = _frames[_frame].Hold;
    }

    private void SetPicture(ImageSource picture)
    {
        var previous = _body.Source;
        _body.Source = picture;
        if (_animated && previous is not null && !ReferenceEquals(previous, picture))
        {
            _fadeBody.Source = previous;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(90)) { FillBehavior = FillBehavior.Stop };
            Timeline.SetDesiredFrameRate(fade, 12);
            fade.Completed += (_, _) => { _fadeBody.BeginAnimation(OpacityProperty, null); _fadeBody.Source = null; _fadeBody.Clip = null; };
            _fadeBody.BeginAnimation(OpacityProperty, fade);
        }
    }

    public void StopMotion()
    {
        _animated = false;
        StopDirector();
        _blinkCancellation?.Cancel();
        _blinkCancellation?.Dispose();
        _blinkCancellation = null;
        _blinking = false;
        BeginAnimation(OpacityProperty, null);
        _hop.BeginAnimation(TranslateTransform.XProperty, null);
        _hop.BeginAnimation(TranslateTransform.YProperty, null);
        _fadeBody.BeginAnimation(OpacityProperty, null); _fadeBody.Source = null; _fadeBody.Clip = null;
        _blinkTimer.Stop();
        _blink.Opacity = 0;
        _frameTimer.Stop();
        // An animated pose starts from its first picture again next time.
        if (_frames is not null && _frame != 0) { _frame = 0; _body.Source = _frames[0].Frame; }
        _glints.Stop();
    }

    private async void Blink()
    {
        if (!_animated || _playing is not null || _blinking || _blinkCancellation is not { } cancellation) return;
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

}
