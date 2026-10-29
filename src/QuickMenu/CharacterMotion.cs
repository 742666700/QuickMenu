using System.Windows.Media.Imaging;
using System.Windows;
using QuickMenu.Models;

namespace QuickMenu;

internal sealed record CharacterFrame(BitmapSource Picture, TimeSpan Hold, string? File = null);
internal sealed record CharacterEffect(string Name, Point Anchor);
internal sealed record CharacterClip(CompanionCue Cue, IReadOnlyList<CharacterFrame> Frames, string? Effect = null,
    double Weight = 1, bool Loop = false, IReadOnlyList<CharacterEffect>? Effects = null, IReadOnlyList<HourWeight>? Hours = null);
internal sealed record CharacterClipSpec(CompanionCue Cue, (string File, double Seconds)[] Frames, string? Effect = null,
    double Weight = 1, bool Loop = false, CharacterEffect[]? Effects = null, HourWeight[]? Hours = null);
internal sealed record CharacterMotion(BitmapSource? Blink, IReadOnlyList<CharacterFrame> Frames, IReadOnlyList<CharacterClip> Clips, BitmapSource? Rest = null)
{
    public static CharacterMotion Empty { get; } = new(null, [], []);
    public IEnumerable<BitmapSource> Pictures => Frames.Select(f => f.Picture).Concat(Clips.SelectMany(c => c.Frames).Select(f => f.Picture))
        .Concat(Blink is null ? [] : new[] { Blink }).Concat(Rest is null ? [] : new[] { Rest }).Distinct();
    public static bool Valid(CharacterClip clip) => clip.Frames.Count > 0 && clip.Weight > 0 && double.IsFinite(clip.Weight)
        && clip.Frames.All(f => f.Hold >= TimeSpan.FromMilliseconds(80))
        && clip.Frames.Average(f => f.Hold.TotalSeconds) >= (clip.Loop ? .5 : .25)
        && clip.Frames.Sum(f => f.Hold.TotalSeconds) <= 4;
}
