using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickMenu.Models;
using ThemeMode = QuickMenu.Models.ThemeMode;

namespace QuickMenu;

/// <summary>
/// One picture of a character: the full figure (<see cref="File"/> in her folder), an optional copy with the eyes
/// closed for blinking, and where her sparkles glint, as fractions of the picture.
/// </summary>
/// <param name="Group">Poses are shown group by group in turn, one invocation each; within a group they are random.</param>
/// <param name="Scale">Size relative to her standing height, for poses such as sitting.</param>
/// <param name="Frames">An animated pose plays these pictures in turn, each held for its time, instead of blinking.</param>
internal sealed record CharacterPose(string File, BitmapSource Body, BitmapSource? Blink, IReadOnlyList<Point> Glints, int Group = 0, double Scale = 1,
    IReadOnlyList<(BitmapSource Frame, TimeSpan Hold)>? Frames = null);

/// <summary>
/// A character who can stand beside the menu in any theme. Each time the menu opens she appears in another pose,
/// never the same one twice running; her avatar takes the place of the spark next to the greeting.
/// </summary>
internal sealed class Character(string id, string name, string folder, ThemeMode theme, Func<IReadOnlyList<CharacterPose>> poses, Func<BitmapSource?> avatar,
    double heightScale = 1, Geometry? glint = null)
{
    private readonly Lazy<IReadOnlyList<CharacterPose>> _poses = new(poses);
    private readonly Lazy<BitmapSource?> _avatar = new(avatar);
    public string Id { get; } = id;
    // The name from the language tables when it has one (Claude娘 / Claude-chan), else the one given here.
    public string Name => Loc.T("Character." + Id) is var localized && localized != "Character." + Id ? localized : name;
    /// <summary>Her folder under Assets/Characters.</summary>
    public string Folder { get; } = folder;
    /// <summary>The theme made from her colours; choosing it can bring her along (see settings).</summary>
    public ThemeMode Theme { get; } = theme;
    public double HeightScale { get; } = heightScale;
    public Geometry? Glint { get; } = glint;
    /// <summary>Pictures are decoded the first time she is shown, so characters nobody picks cost no memory.</summary>
    public IReadOnlyList<CharacterPose> Poses => _poses.Value;
    public BitmapSource? Avatar => _avatar.Value;
}

internal static class Characters
{
    // Figures are decoded a little above the height they are shown at; the avatar is shown at 22 DIPs.
    private const int FigurePixels = 860, AvatarPixels = 96;

    private sealed record Spec(string Body, string? Blink, Point[] Glints)
    {
        public int Group { get; init; }
        public double Scale { get; init; } = 1;
        public (string File, double Seconds)[]? Frames { get; init; }
    }

    /// <summary>
    /// To add a character, put her pictures in Assets/Characters/&lt;folder&gt;/ (front.png, front-blink.png, side.png,
    /// avatar.png or any other names) and add an entry here. A missing picture is left out; a character with no
    /// pose left simply does not appear.
    /// </summary>
    public static IReadOnlyList<Character> All { get; } = new[]
    {
        Define("claude", "Claude娘", "Claude", ThemeMode.Cream, 1, null,
            new("front.png", "front-blink.png", [new(0.86, 0.2), new(0.2, 0.06), new(0.04, 0.62)]),
            new("side.png", null, [new(0.84, 0.22), new(0.18, 0.06), new(0.04, 0.6)])),
        Define("chatgpt", "ChatGPT娘", "ChatGPT", ThemeMode.ChatGpt, 1, Cross(),
            new("front.png", "front-blink.png", [new(0.915, 0.225), new(0.61, 0.125), new(0.88, 0.72)]),
            new("side.png", null, [new(0.2, 0.18), new(0.6, 0.13), new(0.86, 0.72)])),
        // The rice-bowl chibi is the default DeepSeek娘 (first of the deep-sea pair, so that theme brings her along):
        // one invocation she stands with her bowl (front or side), the next she sits and eats, scoop, big bite,
        // puffed cheeks, a happy swallow, and round again. The maid in a long dress follows as the elegant version.
        Define("deepseek-rice", "DeepSeek娘", "DeepSeekRice", ThemeMode.DeepSeek, 0.8, Bubble(),
            new("front.png", "front-blink.png", [new(0.9, 0.5), new(0.74, 0.07), new(0.06, 0.78)]),
            new("side.png", null, [new(0.08, 0.3), new(0.78, 0.08), new(0.92, 0.72)]),
            new("eat-1.png", null, [new(0.08, 0.3), new(0.74, 0.05), new(0.95, 0.6)])
            {
                Group = 1, Scale = 0.85,
                Frames = [("eat-1.png", 1.6), ("eat-2.png", 0.7), ("eat-3.png", 0.9), ("eat-4.png", 1.4), ("eat-5.png", 1.5), ("eat-6.png", 0.9)]
            }),
        Define("deepseek", "DeepSeek娘 · 优雅", "DeepSeek", ThemeMode.DeepSeek, 0.92, Bubble(),
            new("front.png", "front-blink.png", [new(0.65, 0.6), new(0.61, 0.15), new(0.89, 0.65)]),
            new("side.png", null, [new(0.1, 0.21), new(0.51, 0.18), new(0.9, 0.65)])),
        Define("gemini", "Gemini娘", "Gemini", ThemeMode.Gemini, 0.9, Star(),
            new("front.png", "front-blink.png", [new(0.85, 0.35), new(0.61, 0.095), new(0.36, 0.78)]),
            new("side.png", null, [new(0.075, 0.145), new(0.63, 0.13), new(0.8, 0.65)])),
    };

    public static Character? Find(string? id) => All.FirstOrDefault(character => character.Id == id);

    /// <summary>The character a theme brings along, if the theme was made from one.</summary>
    public static Character? ForTheme(ThemeMode theme) => All.FirstOrDefault(character => character.Theme == theme);

    private static Character Define(string id, string name, string folder, ThemeMode theme, double heightScale, Geometry? glint, params Spec[] poses) =>
        new(id, name, folder, theme,
            () => poses.Select(pose => Load(folder, pose.Body, FigurePixels) is { } body
                    ? new CharacterPose(pose.Body, body, pose.Blink is null ? null : Load(folder, pose.Blink, FigurePixels), pose.Glints, pose.Group, pose.Scale,
                        pose.Frames?.Select(frame => (frame.File == pose.Body ? body : Load(folder, frame.File, FigurePixels), TimeSpan.FromSeconds(frame.Seconds)))
                            .Where(frame => frame.Item1 is not null).Select(frame => (frame.Item1!, frame.Item2)).ToArray())
                    : null)
                .OfType<CharacterPose>().ToArray(),
            () => Load(folder, "avatar.png", AvatarPixels), heightScale, glint);

    private static Geometry Frozen(Geometry shape) { shape.Freeze(); return shape; }
    internal static Geometry Cross() => Frozen(Geometry.Parse("M12,1 Q13,10 23,12 Q13,14 12,23 Q11,14 1,12 Q11,10 12,1 Z"));
    internal static Geometry Star() => Frozen(Geometry.Parse("M12,0 L15.2,8.8 L24,12 L15.2,15.2 L12,24 L8.8,15.2 L0,12 L8.8,8.8 Z"));
    private static Geometry Bubble() => Frozen(new GeometryGroup
    {
        Children = { new EllipseGeometry(new Point(12, 12), 10, 10), new EllipseGeometry(new Point(8, 7), 1.3, 1.3) }
    });

    private static BitmapSource? Load(string folder, string file, int decodeHeight)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri($"pack://application:,,,/Assets/Characters/{folder}/{file}");
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelHeight = decodeHeight;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UriFormatException or NotSupportedException or InvalidOperationException) { return null; }
    }
}
