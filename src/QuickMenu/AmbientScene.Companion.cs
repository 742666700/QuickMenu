using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace QuickMenu;

internal sealed partial class AmbientScene
{
    private readonly List<(UIElement Visual, List<(IAnimatable Target, DependencyProperty Property)> Parts)> _effects = new();
    internal bool HasEffects => _effects.Count > 0;

    internal void PlayCompanionEffect(string effect, Point anchor, Color color)
    {
        if (!IsRunning) return;
        Geometry shape = effect switch
        {
            "zzz" => TextShape("Zz"), "typing" => TextShape("···"), "surprise" => TextShape("!"),
            "sweat" or "tear" or "drool" => Geometry.Parse("M12,0 C9,8 3,13 3,18 C3,29 21,29 21,18 C21,13 15,8 12,0 Z"),
            "rice" => new EllipseGeometry(new Rect(4, 6, 12, 7)),
            "bubble" or "glow" => new EllipseGeometry(new Point(13, 13), 11, 11),
            _ => Characters.Star()
        };
        var visual = new Canvas { Opacity = 0, IsHitTestVisible = false, Width = 32, Height = 32 };
        var slide = new TranslateTransform();
        var pulse = new ScaleTransform(1, 1, 16, 16);
        visual.RenderTransform = new TransformGroup { Children = { pulse, slide } };
        var ink = new SolidColorBrush(effect == "rice" ? Color.FromRgb(255, 245, 219) : color);
        var parts = new List<(IAnimatable Target, DependencyProperty Property)> { (visual, UIElement.OpacityProperty), (slide, TranslateTransform.YProperty) };
        if (effect == "typing")
        {
            for (var i = 0; i < 3; i++)
            {
                var dot = new Ellipse { Width = 5, Height = 5, Fill = ink, Opacity = .2 };
                Canvas.SetLeft(dot, 3 + i * 10); Canvas.SetTop(dot, 14); visual.Children.Add(dot);
                var blink = new DoubleAnimation(.2, 1, TimeSpan.FromSeconds(.22)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2), BeginTime = TimeSpan.FromSeconds(i * .22), FillBehavior = FillBehavior.Stop };
                Timeline.SetDesiredFrameRate(blink, 12); dot.BeginAnimation(UIElement.OpacityProperty, blink); parts.Add((dot, UIElement.OpacityProperty));
            }
        }
        else if (effect == "stars")
        {
            for (var i = 0; i < 4; i++)
            {
                var star = new System.Windows.Shapes.Path { Data = Characters.Star(), Fill = ink, Width = 8, Height = 8, Stretch = Stretch.Uniform };
                var travel = new TranslateTransform(); star.RenderTransform = travel;
                Canvas.SetLeft(star, 12); Canvas.SetTop(star, 12); visual.Children.Add(star);
                var angle = i * Math.PI / 2;
                foreach (var (property, distance) in new[] { (TranslateTransform.XProperty, Math.Cos(angle) * 12), (TranslateTransform.YProperty, Math.Sin(angle) * 12) })
                {
                    var fly = new DoubleAnimation(0, distance, TimeSpan.FromSeconds(.7)) { FillBehavior = FillBehavior.Stop };
                    Timeline.SetDesiredFrameRate(fly, 12); travel.BeginAnimation(property, fly); parts.Add((travel, property));
                }
            }
        }
        else
        {
            var picture = new System.Windows.Shapes.Path { Data = shape, Fill = effect == "bubble" ? Brushes.Transparent : ink, Stroke = effect == "bubble" ? ink : null,
                StrokeThickness = 1.5, Width = 28, Height = 28, Stretch = Stretch.Uniform };
            visual.Children.Add(picture);
        }
        if (effect is "bubble" or "glow")
        {
            foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            {
                var grow = new DoubleAnimation(.75, 1.1, TimeSpan.FromSeconds(.7)) { AutoReverse = true, FillBehavior = FillBehavior.Stop };
                Timeline.SetDesiredFrameRate(grow, 12); pulse.BeginAnimation(property, grow); parts.Add((pulse, property));
            }
        }
        Canvas.SetLeft(visual, Math.Clamp(anchor.X, 2, Math.Max(2, _layer.ActualWidth - 36)));
        Canvas.SetTop(visual, Math.Clamp(anchor.Y, 18, Math.Max(18, _layer.ActualHeight - 36)));
        _layer.Children.Add(visual);
        var entry = ((UIElement)visual, parts); _effects.Add(entry);
        var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1.4), FillBehavior = FillBehavior.Stop };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(.85, KeyTime.FromPercent(.2)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        Timeline.SetDesiredFrameRate(fade, 12);
        fade.Completed += (_, _) =>
        {
            if (!_effects.Remove(entry)) return;
            foreach (var part in parts) part.Target.ApplyAnimationClock(part.Property, null);
            _layer.Children.Remove(visual);
        };
        visual.BeginAnimation(UIElement.OpacityProperty, fade);
        var rise = new DoubleAnimation(0, -14, TimeSpan.FromSeconds(1.4)) { FillBehavior = FillBehavior.Stop };
        Timeline.SetDesiredFrameRate(rise, 12); slide.BeginAnimation(TranslateTransform.YProperty, rise);
    }
    private static Geometry TextShape(string text) => new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        new Typeface("Segoe UI"), 22, Brushes.White, 1).BuildGeometry(new Point());

    internal void StopEffects()
    {
        foreach (var entry in _effects)
        {
            foreach (var part in entry.Parts) part.Target.ApplyAnimationClock(part.Property, null);
            _layer.Children.Remove(entry.Visual);
        }
        _effects.Clear();
    }
}
