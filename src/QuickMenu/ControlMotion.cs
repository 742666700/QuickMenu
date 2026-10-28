using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace QuickMenu;

/// <summary>Finite control feedback with explicit resting values and no clocks while hidden or motion is disabled.</summary>
public static class ControlMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ControlMotion), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, PolicyChanged));
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);

    public static readonly DependencyProperty OpacityProperty = Register("Opacity");
    public static readonly DependencyProperty ScaleXProperty = Register("ScaleX");
    public static readonly DependencyProperty ScaleYProperty = Register("ScaleY");
    public static readonly DependencyProperty XProperty = Register("X");
    public static readonly DependencyProperty YProperty = Register("Y");
    public static readonly DependencyProperty AngleProperty = Register("Angle");
    public static DoubleAnimation? GetOpacity(DependencyObject d) => (DoubleAnimation?)d.GetValue(OpacityProperty);
    public static void SetOpacity(DependencyObject d, DoubleAnimation value) => d.SetValue(OpacityProperty, value);
    public static DoubleAnimation? GetScaleX(DependencyObject d) => (DoubleAnimation?)d.GetValue(ScaleXProperty);
    public static void SetScaleX(DependencyObject d, DoubleAnimation value) => d.SetValue(ScaleXProperty, value);
    public static DoubleAnimation? GetScaleY(DependencyObject d) => (DoubleAnimation?)d.GetValue(ScaleYProperty);
    public static void SetScaleY(DependencyObject d, DoubleAnimation value) => d.SetValue(ScaleYProperty, value);
    public static DoubleAnimation? GetX(DependencyObject d) => (DoubleAnimation?)d.GetValue(XProperty);
    public static void SetX(DependencyObject d, DoubleAnimation value) => d.SetValue(XProperty, value);
    public static DoubleAnimation? GetY(DependencyObject d) => (DoubleAnimation?)d.GetValue(YProperty);
    public static void SetY(DependencyObject d, DoubleAnimation value) => d.SetValue(YProperty, value);
    public static DoubleAnimation? GetAngle(DependencyObject d) => (DoubleAnimation?)d.GetValue(AngleProperty);
    public static void SetAngle(DependencyObject d, DoubleAnimation value) => d.SetValue(AngleProperty, value);

    private sealed record Part(Animatable? Transform, DependencyProperty Property, double Original)
    {
        public double Rest { get; set; } = Original;
    }
    private static readonly DependencyProperty PartsProperty = DependencyProperty.RegisterAttached("Parts", typeof(Dictionary<DependencyProperty, Part>), typeof(ControlMotion));
    private static DependencyProperty Register(string name) => DependencyProperty.RegisterAttached(name, typeof(DoubleAnimation), typeof(ControlMotion), new PropertyMetadata(null, Changed));

    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        // Template properties may arrive before RenderTransform has been assigned by the XAML loader.
        if (!element.IsInitialized)
        {
            element.Initialized += (_, _) => Changed(element, new DependencyPropertyChangedEventArgs(e.Property, null, element.GetValue(e.Property)));
            return;
        }
        if (element.GetValue(PartsProperty) is not Dictionary<DependencyProperty, Part> parts)
        {
            parts = new();
            element.SetValue(PartsProperty, parts);
            element.IsVisibleChanged += (_, _) =>
            {
                Settle(element);
                if (!element.IsVisible || !GetEnabled(element)) return;
                // One-shot reveals may be selected before the popup or template part becomes visible.
                foreach (var property in parts.Keys.ToArray())
                    if (element.GetValue(property) is DoubleAnimation { From: not null } reveal)
                        Changed(element, new DependencyPropertyChangedEventArgs(property, null, reveal));
            };
            element.Unloaded += (_, _) => Settle(element);
        }
        if (!parts.TryGetValue(e.Property, out var part))
        {
            var property = UIElement.OpacityProperty;
            Animatable? transform = null;
            if (e.Property != OpacityProperty)
            {
                if (element.RenderTransform.IsFrozen) element.RenderTransform = element.RenderTransform.Clone();
                var transforms = element.RenderTransform is TransformGroup group ? group.Children.ToArray() : new[] { element.RenderTransform };
                if (e.Property == ScaleXProperty || e.Property == ScaleYProperty)
                {
                    transform = transforms.OfType<ScaleTransform>().First();
                    property = e.Property == ScaleXProperty ? ScaleTransform.ScaleXProperty : ScaleTransform.ScaleYProperty;
                }
                else if (e.Property == AngleProperty) { transform = transforms.OfType<RotateTransform>().First(); property = RotateTransform.AngleProperty; }
                else { transform = transforms.OfType<TranslateTransform>().First(); property = e.Property == XProperty ? TranslateTransform.XProperty : TranslateTransform.YProperty; }
            }
            part = new(transform, property, (double)(transform as DependencyObject ?? element).GetValue(property));
            parts.Add(e.Property, part);
        }
        var animation = e.NewValue as DoubleAnimation;
        var target = part.Transform as DependencyObject ?? element;
        var from = (double)target.GetValue(part.Property);
        part.Rest = animation?.To ?? part.Original;
        Apply(element, part, null);
        target.SetCurrentValue(part.Property, part.Rest);
        if (animation is null || !GetEnabled(element) || !element.IsVisible) return;
        var play = animation.Clone();
        play.From ??= from;
        play.FillBehavior = FillBehavior.Stop;
        play.Completed += (_, _) => Apply(element, part, null);
        Apply(element, part, play);
    }

    private static void Apply(FrameworkElement element, Part part, DoubleAnimation? animation)
    {
        if (part.Transform is { } transform) transform.BeginAnimation(part.Property, animation);
        else element.BeginAnimation(part.Property, animation);
    }

    private static void PolicyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && !(bool)e.NewValue) Settle(element);
    }

    private static void Settle(FrameworkElement element)
    {
        if (element.GetValue(PartsProperty) is not Dictionary<DependencyProperty, Part> parts) return;
        foreach (var part in parts.Values)
        {
            Apply(element, part, null);
            (part.Transform as DependencyObject ?? element).SetCurrentValue(part.Property, part.Rest);
        }
    }
}
