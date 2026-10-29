using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickMenu.Models;
using ThemeMode = QuickMenu.Models.ThemeMode;

namespace QuickMenu;

/// <summary>
/// A synchronously decoded pose and its sparkle anchors. Its blink, legacy loop and clips are prepared separately
/// so the window can release all decoded motion pictures when another pose takes over.
/// </summary>
/// <param name="Group">Poses are shown group by group in turn, one invocation each; within a group they are random.</param>
/// <param name="Scale">Size relative to her standing height, for poses such as sitting.</param>
internal sealed record CharacterPose(string File, BitmapSource Body, IReadOnlyList<Point> Glints, int Group = 0, double Scale = 1)
{
    public string Id { get; init; } = System.IO.Path.GetFileNameWithoutExtension(File);
    public bool MotionOnly { get; init; }
    public CompanionCue? AutoCue { get; init; }
    public bool HasBlink { get; init; }
    public bool HasFrames { get; init; }
    public IReadOnlyList<HourWeight> Hours { get; init; } = [];
    public Func<CancellationToken, Task<CharacterMotion>> Prepare { get; init; } = _ => Task.FromResult(CharacterMotion.Empty);
}

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
    public Temperament Temperament => Id switch
    {
        "deepseek-rice" => new(.4, 4, 6, false, 320, 12, 25, 25, EntranceX: 8, EntranceDrop: 6, Sleeps: true),
        "deepseek" => new(.15, 6, 3, false, 280, 15, 30, 40, Nod: true),
        "claude" => new(0, 3, 3, false, 180, 10, 20, 25, Nod: true, HoverMilliseconds: 140, ClickMilliseconds: 140, EntranceX: 12, HoverNod: true),
        "chatgpt" => new(0, 6, 6, false, 300, 10, 20, 25, Float: true, HoverMilliseconds: 300, ClickMilliseconds: 300, EntranceDrop: 6),
        _ => new(0, 10, 10, true, 240, 8, 15, 25)
    };
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
        public CharacterClipSpec[] Clips { get; init; } = [];
        public HourWeight[] Hours { get; init; } = [];
        public string? Id { get; init; }
        public string? Rest { get; init; }
        public bool MotionOnly { get; init; }
        public CompanionCue? AutoCue { get; init; }
    }

    /// <summary>
    /// To add a character, put her pictures in Assets/Characters/&lt;folder&gt;/ (front.png, front-blink.png, side.png,
    /// avatar.png or any other names) and add an entry here. A missing picture is left out; a character with no
    /// pose left simply does not appear.
    /// </summary>
    public static IReadOnlyList<Character> All { get; } = new[]
    {
        Define("claude", "Claude娘", "Claude", ThemeMode.Cream, 1, null,
            new("front.png", "front.blink-1.png", [new(0.86, 0.2), new(0.2, 0.06), new(0.04, 0.62)])
            {
                Clips = [
                    new(CompanionCue.Idle, [("front.read-1.png", .6), ("front.read-2.png", .35), ("front.read-3.png", .35), ("front.png", .5)], "glow"),
                    new(CompanionCue.Click, [("front.confident-1.png", 1.2), ("front.confident-2.png", .15)], "stars"),
                    new(CompanionCue.Hover, [("front.focus-1.png", .9)]),
                    new(CompanionCue.Doze, [("front.tap-1.png", .3), ("front.tap-2.png", .3), ("front.tap-1.png", .3), ("front.tap-2.png", .3), ("front.tap-1.png", .3), ("front.tap-2.png", .3), ("front.focus-1.png", .8)]),
                ]
            },
            new("side.png", null, [new(0.84, 0.22), new(0.18, 0.06), new(0.04, 0.6)])),
        Define("chatgpt", "ChatGPT娘", "ChatGPT", ThemeMode.ChatGpt, 1, Cross(),
            new("front.png", "front.blink-1.png", [new(0.915, 0.225), new(0.61, 0.125), new(0.88, 0.72)])
            {
                Clips = [
                    new(CompanionCue.Idle, [("front.sprite-1.png", .3), ("front.sprite-2.png", .3)], Effects: [new("typing", new(.84, .16))]),
                    new(CompanionCue.Click, [("front.praise-1.png", 1), ("front.praise-2.png", .5)], "stars", Effects: [new("surprise", new(.85, .16))]),
                    new(CompanionCue.Hover, [("front.hover-1.png", 1)]),
                    new(CompanionCue.Doze, [("wait-1.png", .8)], "typing", Loop: true),
                    new(CompanionCue.Idle, [("front.wing-1.png", .25), ("front.wing-2.png", .25), ("front.wing-1.png", .25), ("front.wing-2.png", .25)]),
                ]
            },
            new("side.png", null, [new(0.2, 0.18), new(0.6, 0.13), new(0.86, 0.72)])),
        // The rice-bowl chibi is the default DeepSeek娘 (first of the deep-sea pair, so that theme brings her along):
        // one invocation she stands with her bowl (front or side), the next she sits and eats, scoop, big bite,
        // puffed cheeks, a happy swallow, and round again. The maid in a long dress follows as the elegant version.
        Define("deepseek-rice", "DeepSeek娘", "DeepSeekRice", ThemeMode.DeepSeek, 0.8, Bubble(),
            new("front.png", "front.blink-1.png", [new(0.9, 0.5), new(0.74, 0.07), new(0.06, 0.78)])
            {
                Rest = "front.doze-2.png",
                Clips = [
                    new(CompanionCue.Idle, [("front.yawn-1.png", .5), ("front.yawn-2.png", 1.2), ("front.yawn-3.png", .6)], "tear"),
                    new(CompanionCue.Doze, [("front.doze-1.png", .9), ("front.doze-2.png", 1.1)], "zzz", Loop: true,
                        Effects: [new("bubble", new(.42, .46)), new("drool", new(.48, .48))]),
                    new(CompanionCue.Wake, [("front.wake-1.png", .5)], "surprise", Effects: [new("sweat", new(.65, .4))]),
                    new(CompanionCue.Hover, [("front.hover-1.png", 1)]),
                    new(CompanionCue.Click, [("offer-1.png", 1.4), ("offer.blink-1.png", .6)], "stars"),
                ]
            },
            new("side.png", null, [new(0.08, 0.3), new(0.78, 0.08), new(0.92, 0.72)]),
            new("eat-1.png", null, [new(0.08, 0.3), new(0.74, 0.05), new(0.95, 0.6)])
            {
                Group = 1, Scale = 0.85,
                Hours = [new(new(11, 30), new(13, 0), 3), new(new(17, 30), new(19, 30), 3)],
                Frames = [("eat-1.png", 1.6), ("eat-2.png", 0.7), ("eat-3.png", 0.9), ("eat-4.png", 1.4), ("eat-5.png", 1.5), ("eat-6.png", 0.9)]
            },
            new("eat-1.png", null, [new(.08, .3), new(.74, .05), new(.95, .6)])
            {
                Id = "nap", Group = 2, Scale = .85, MotionOnly = true, AutoCue = CompanionCue.Doze, Rest = "eat-1.doze-3.png",
                Hours = [new(new(13, 0), new(14, 30), 3), new(new(22, 30), new(6, 30), 3)],
                Clips = [
                    new(CompanionCue.Doze, [("eat-1.doze-1.png", 1.1), ("eat-1.doze-2.png", 1.2), ("eat-1.doze-3.png", 1), ("eat-1.doze-4.png", .5)], "zzz", Loop: true,
                        Effects: [new("bubble", new(.44, .48))]),
                    new(CompanionCue.Wake, [("eat-1.doze-4.png", .5)], "surprise", Effects: [new("sweat", new(.65, .44))])
                ]
            }),
        Define("deepseek", "DeepSeek娘 · 优雅", "DeepSeek", ThemeMode.DeepSeek, 0.92, Bubble(),
            new("front.png", "front.blink-1.png", [new(0.65, 0.6), new(0.61, 0.15), new(0.89, 0.65)]),
            new("side.png", null, [new(0.1, 0.21), new(0.51, 0.18), new(0.9, 0.65)])),
        Define("gemini", "Gemini娘", "Gemini", ThemeMode.Gemini, 0.9, Star(),
            new("front.png", "front.blink-1.png", [new(0.85, 0.35), new(0.61, 0.095), new(0.36, 0.78)])
            {
                Clips = [
                    new(CompanionCue.Idle, [("front.ear-1.png", .12), ("front.ear-2.png", .12), ("front.tail-1.png", .34), ("front.tail-2.png", .34), ("front.tail-3.png", .34)]),
                    new(CompanionCue.Click, [("front.wink-1.png", 1)], "stars"),
                    new(CompanionCue.Hover, [("front.curious-1.png", .9)]),
                    new(CompanionCue.Idle, [("front.smirk-1.png", .5)], "stars", Weight: .15),
                    new(CompanionCue.Doze, [("front.wash-1.png", .4), ("front.wash-2.png", .4), ("front.wash-1.png", .4), ("front.wash-2.png", .4), ("front.wash-1.png", .4), ("front.wash-2.png", .4)]),
                ]
            },
            new("side.png", null, [new(0.075, 0.145), new(0.63, 0.13), new(0.8, 0.65)])),
    };

    public static Character? Find(string? id) => All.FirstOrDefault(character => character.Id == id);

    /// <summary>The character a theme brings along, if the theme was made from one.</summary>
    public static Character? ForTheme(ThemeMode theme) => All.FirstOrDefault(character => character.Theme == theme);

    private static Character Define(string id, string name, string folder, ThemeMode theme, double heightScale, Geometry? glint, params Spec[] poses)
    {
        Character? character = null;
        character = new(id, name, folder, theme,
            () =>
            {
                var bodies = poses.Select(p => p.Body).Distinct().ToDictionary(file => file, file => Load(folder, file, FigurePixels));
                return poses.Select(pose => bodies[pose.Body] is { } body
                ? new CharacterPose(pose.Body, body, pose.Glints, pose.Group, pose.Scale)
                {
                    Id = pose.Id ?? Path.GetFileNameWithoutExtension(pose.Body), AutoCue = pose.AutoCue, MotionOnly = pose.MotionOnly,
                    HasBlink = pose.Blink is not null, HasFrames = pose.Frames is { Length: > 1 }, Hours = pose.Hours,
                    Prepare = token => CharacterArt.Prepare(() =>
                    {
                        var loaded = new Dictionary<string, BitmapSource> { [pose.Body] = body };
                        BitmapSource Picture(string file)
                        {
                            token.ThrowIfCancellationRequested();
                            if (loaded.TryGetValue(file, out var image)) return image;
                            var resident = CharacterArt.Bytes(loaded.Values.Concat(character!.Poses.Select(p => p.Body)).Append(character.Avatar!));
                            if (resident + (long)body.PixelWidth * body.PixelHeight * 4 > 40_000_000)
                                throw new InvalidDataException("Character motion exceeds the decoded memory limit.");
                            loaded[file] = image = CharacterArt.Load(folder, file, FigurePixels);
                            if (image.PixelWidth != body.PixelWidth || image.PixelHeight != body.PixelHeight)
                                throw new InvalidDataException("Motion pictures must share their pose canvas.");
                            return image;
                        }
                        var blink = pose.Blink is null ? null : Picture(pose.Blink);
                        var frames = pose.Frames?.Select(f => new CharacterFrame(Picture(f.File), TimeSpan.FromSeconds(f.Seconds), f.File)).ToArray() ?? [];
                        var clips = pose.Clips.Select(c => new CharacterClip(c.Cue, c.Frames.Select(f => new CharacterFrame(Picture(f.File), TimeSpan.FromSeconds(f.Seconds), f.File)).ToArray(), c.Effect, c.Weight, c.Loop, c.Effects, c.Hours)).ToArray();
                        if (clips.Any(c => !CharacterMotion.Valid(c))) throw new InvalidDataException("Invalid character clip timing.");
                        var rest = pose.Rest is null ? null : Picture(pose.Rest);
                        var motion = new CharacterMotion(blink, frames, clips, rest);
                        return motion;
                    }, token)
                } : null).OfType<CharacterPose>().ToArray();
            },
            () => Load(folder, "avatar.png", AvatarPixels), heightScale, glint);
        return character;
    }

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
            return CharacterArt.Load(folder, file, decodeHeight);
        }
        catch (Exception ex) when (ex is IOException or UriFormatException or NotSupportedException or InvalidOperationException) { return null; }
    }
}
