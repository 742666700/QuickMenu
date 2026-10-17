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
    private bool _animated;

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
        RenderOptions.SetBitmapScalingMode(_blink, BitmapScalingMode.HighQuality);
        _figure.Children.Add(_body);
        _figure.Children.Add(_blink);
        _figure.RenderTransform = _hop;
        _figure.Margin = new Thickness(0, 0, Pad, 0);
        _figure.MouseEnter += (_, _) => Hop();
        Content = new Grid { Children = { _figure, _glintLayer } };
        _glints = new AmbientScene(_glintLayer);
        _blinkTimer.Tick += (_, _) => Blink();
        _frameTimer.Tick += (_, _) => NextFrame();
        SourceInitialized += (_, _) => HostNative.MakeNonActivating(this);
    }

    public bool CanBlink => _blink.Source is not null;
    public ImageSource? Pose => _body.Source;
    public bool IsAnimating => _animated && _glints.IsRunning;

    /// <summary>The space she takes beyond the menu: width right of its edge, height above its bottom.</summary>
    public Size Footprint => new(Width - Overlap, Height - FeetDrop);

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
        var figureWidth = pose.Body.PixelWidth / (double)pose.Body.PixelHeight * figureHeight;
        _figure.Width = figureWidth;
        _figure.Height = figureHeight;
        Width = figureWidth + Pad;
        Height = figureHeight + Pad;
        _glints.BuildGlints(new Rect(0, Pad, figureWidth, figureHeight), pose.Glints, glint, glintCount, glintHalo, shape);
        if (running) StartMotion();
    }

    public void PlaceBeside(Window menu) => HostNative.PlaceAtRightEdge(this, menu, Overlap, FeetDrop);

    /// <summary>
    /// Blinking, the glints and the hop. She does not breathe or float continuously: her window is see-through,
    /// and WPF repaints such windows in software, so a figure that never stops moving costs a few percent of a
    /// core the whole time the menu is open.
    /// </summary>
    public void StartMotion()
    {
        if (_animated) return;
        _animated = true;
        _blinkTimer.Interval = TimeSpan.FromSeconds(3 + _random.NextDouble() * 4);
        if (CanBlink) _blinkTimer.Start();
        if (_frames is not null) { _frameTimer.Interval = _frames[0].Hold; _frameTimer.Start(); }
        _glints.Start();
    }

    public bool IsPlayingFrames => _frameTimer.IsEnabled;

    private void NextFrame()
    {
        if (_frames is null) { _frameTimer.Stop(); return; }
        _frame = (_frame + 1) % _frames.Count;
        _body.Source = _frames[_frame].Frame;
        _frameTimer.Interval = _frames[_frame].Hold;
    }

    public void StopMotion()
    {
        _animated = false;
        _hop.BeginAnimation(TranslateTransform.YProperty, null);
        _blinkTimer.Stop();
        _blink.Opacity = 0;
        _frameTimer.Stop();
        // An animated pose starts from its first picture again next time.
        if (_frames is not null && _frame != 0) { _frame = 0; _body.Source = _frames[0].Frame; }
        _glints.Stop();
    }

    private async void Blink()
    {
        _blinkTimer.Interval = TimeSpan.FromSeconds(3 + _random.NextDouble() * 4);
        var times = _random.NextDouble() < 0.2 ? 2 : 1;
        for (var i = 0; i < times && _animated; i++)
        {
            _blink.Opacity = 1;
            await Task.Delay(120);
            _blink.Opacity = 0;
            if (i + 1 < times) await Task.Delay(160);
        }
    }

    // Pointing at her gets a small, happy hop.
    private void Hop()
    {
        if (!_animated) return;
        _hop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -10, TimeSpan.FromMilliseconds(170)) { AutoReverse = true, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }
}
