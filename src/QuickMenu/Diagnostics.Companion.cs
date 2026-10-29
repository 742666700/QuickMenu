using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickMenu.Models;
using QuickMenu.Services;
using ThemeMode = QuickMenu.Models.ThemeMode;

namespace QuickMenu;

internal static partial class Diagnostics
{
    private static async Task InspectCompanions(string output)
    {
        var window = new MainWindow(isQa: true) { Width = 820, Height = 600 };
        var checks = new Dictionary<string, bool>();
        try
        {
            window.Show(); window.InvokeMenu();
            await InspectCompanionEngine(window, checks, output);
            await File.WriteAllTextAsync(Path.Combine(output, "ui-report.json"), JsonSerializer.Serialize(new { Checks = checks, AllChecksPassed = checks.Values.All(pass => pass) }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { window.DisposeServices(); window.Close(); }
    }

    private static async Task InspectCompanionEngine(MainWindow window, Dictionary<string, bool> checks, string output)
    {
        var memory = new List<object>();
        var catalog = new List<object>();
        foreach (var character in Characters.All)
        {
            var strip = new List<(BitmapSource Image, string Label)>();
            foreach (var pose in character.Poses)
            {
                var motion = await pose.Prepare(CancellationToken.None);
                catalog.Add(new { Character = character.Id, character.Folder, Pose = pose.Id, Base = pose.File,
                    Clips = motion.Clips.Select((c, index) => new { Index = index, Cue = c.Cue.ToString(), c.Loop, c.Weight, c.Effect, c.Effects, c.Hours,
                        Frames = c.Frames.Select(f => new { f.File, Seconds = f.Hold.TotalSeconds }) }) });
                var bytes = CharacterArt.Bytes(motion.Pictures.Concat(character.Poses.Select(p => p.Body)).Append(character.Avatar!));
                memory.Add(new { character.Id, Pose = pose.Id, DecodedBytes = bytes });
                checks[$"M1: {character.Id}/{pose.Id} decoded memory <= 40 MB"] = bytes <= 40_000_000;
                checks[$"M1: {character.Id}/{pose.Id} frozen, same-canvas frames and valid clips"] = motion.Pictures.All(p => p.IsFrozen
                    && p.PixelWidth == pose.Body.PixelWidth && p.PixelHeight == pose.Body.PixelHeight) && motion.Clips.All(CharacterMotion.Valid);
                strip.Add((pose.Body, pose.Id + " / still"));
                if (motion.Blink is { } blink) strip.Add((blink, "blink / 0.12s (existing)"));
                strip.AddRange(motion.Frames.Select((f, i) => (f.Picture, $"eat-{i+1} / {f.Hold.TotalSeconds:0.##}s")));
                foreach (var authored in motion.Clips)
                    strip.AddRange(authored.Frames.Select(f => (f.Picture, $"{authored.Cue} / {f.Hold.TotalSeconds:0.##}s")));
            }
            CaptureClips(strip, Path.Combine(output, $"clips-{character.Id}.png"));
            foreach (var (file, patch) in CharacterArt.Patches(character.Folder))
            {
                var basis = ReadArt(character.Folder, patch.Base);
                var composed = ReadArt(character.Folder, file);
                checks[$"M1: {character.Id}/{file} patch composes on its base canvas"] = composed.PixelWidth == basis.PixelWidth && composed.PixelHeight == basis.PixelHeight && patch.X >= 0 && patch.Y >= 0;
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "decoded-memory.json"), JsonSerializer.Serialize(memory, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(output, "character-clips.json"), JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }));
        using (var resources = typeof(App).Assembly.GetManifestResourceStream("QuickMenu.g.resources"))
        using (var reader = new System.Resources.ResourceReader(resources!))
        {
            long embeddedBytes = 0;
            foreach (System.Collections.DictionaryEntry entry in reader)
                if (entry.Key is string key && key.StartsWith("assets/characters/", StringComparison.OrdinalIgnoreCase) && entry.Value is Stream image) embeddedBytes += image.Length;
            checks["M1: embedded character resources remain under 40 MB"] = embeddedBytes > 0 && embeddedBytes <= 40_000_000;
            File.WriteAllText(Path.Combine(output, "character-resource-bytes.txt"), embeddedBytes.ToString(CultureInfo.InvariantCulture));
        }

        window.PreviewPage(MenuPage.Desktop);
        window.PreviewAnimations(true); window.InvokeMenu();
        foreach (var character in Characters.All)
        {
            window.PreviewCompanion(character.Id);
            for (var i = 0; i < character.Poses.Count; i++)
            {
                window.PreviewPoseIndex(i);
                var actor = window.Companion!; await actor.MotionReady;
                var clips = actor.PreparedMotion.Clips.ToArray();
                actor.PreviewClips();
                foreach (var (authored, index) in clips.Select((clip, index) => (clip, index)))
                {
                    actor.NoteActivity(); actor.PlayClip(authored with { Loop = false });
                    await Task.Delay(40);
                    var pausedBlink = !actor.IsBlinking && actor.IsPlayingClip;
                    CaptureWithCompanion(window, actor, Path.Combine(output, $"clip-{character.Id}-{character.Poses[i].Id}-{authored.Cue}-{index}.png"));
                    await Task.Delay(TimeSpan.FromSeconds(authored.Frames.Sum(f => f.Hold.TotalSeconds)) + TimeSpan.FromMilliseconds(250));
                    checks[$"clips: {character.Id}/{character.Poses[i].Id}/{authored.Cue}-{index} pauses blink and returns to pose"] = pausedBlink && !actor.IsPlayingClip && actor.IsAtPose;
                    if (authored.Loop)
                    {
                        actor.NoteActivity(); actor.PlayClip(authored);
                        await Task.Delay(TimeSpan.FromSeconds(authored.Frames.Sum(f => f.Hold.TotalSeconds)) + TimeSpan.FromMilliseconds(80));
                        var held = actor.IsPlayingClip;
                        actor.StopCue(authored.Cue); actor.NoteActivity();
                        checks[$"clips: {character.Id}/{character.Poses[i].Id}/{authored.Cue}-{index} loops until its cue ends"] = held && !actor.IsPlayingClip && actor.IsAtPose;
                    }
                }
            }
        }
        window.ApplyTheme(ThemeMode.DeepSeek);
        window.PreviewCompanion("deepseek-rice");
        window.PreviewPoseIndex(Characters.Find("deepseek-rice")!.Poses.ToList().FindIndex(p => p.Id == "nap"));
        await window.Companion!.MotionReady;
        window.Companion.PlayClip(window.Companion.PreparedMotion.Clips.First(c => c.Cue == CompanionCue.Doze));
        for (var i = 0; i < 16; i++)
        {
            await Task.Delay(250);
            CaptureWithCompanion(window, window.Companion, Path.Combine(output, $"motion-deepseek-rice-{i:00}.png"));
        }
        window.PreviewAnimations(false);

        window.PreviewPage(MenuPage.Desktop);
        window.PreviewCompanion("claude"); window.PreviewPoseIndex(0);
        window.PreviewAnimations(true); window.InvokeMenu(); window.PreviewPoseIndex(0);
        var companion = window.Companion!;
        await companion.MotionReady;
        window.ApplyTheme(ThemeMode.Cream); window.PreviewPoseIndex(0); await companion.MotionReady;
        window.Ambient.PreviewMomentStart();
        checks["P2: a matching theme moment starts the declared Watch clip"] = companion.PlayingCue == CompanionCue.Watch;
        window.ApplyTheme(ThemeMode.Oled); window.PreviewPoseIndex(0); await companion.MotionReady;
        window.Ambient.PreviewMomentStart();
        checks["P2: an unrelated theme moment does not trigger Watch"] = !companion.IsPlayingClip;
        window.SearchBox.Text = "motion";
        checks["P2: Claude holds Typing while search has text"] = companion.PlayingCue == CompanionCue.Typing;
        window.SearchBox.Text = "";
        checks["P2: clearing search ends Typing and returns to pose"] = !companion.IsPlayingClip && companion.IsAtPose;
        var idleBefore = companion.IdlePlays; var alignedBefore = companion.AlignedIdlePlays;
        companion.PreviewIdleClip(); companion.PreviewSampleActions();
        await Task.Delay(9000);
        checks["motion: autonomous clips start in an actual sparkle dispatcher burst"] = companion.IdlePlays > idleBefore && companion.AlignedIdlePlays > alignedBefore;
        var poseBody = window.CurrentPose!.Body;
        var frame = companion.PreparedMotion.Blink!;
        var focused = Keyboard.FocusedElement;
        var clip = new CharacterClip(CompanionCue.Click, [new(frame, TimeSpan.FromSeconds(.3)), new(poseBody, TimeSpan.FromSeconds(.3)), new(frame, TimeSpan.FromSeconds(.3))], "surprise");
        companion.PlayClip(clip);
        await Task.Delay(60);
        checks["M1: frames and cross-fade stay inside the decoded budget"] = companion.DecodedBytes <= 40_000_000;
        await Task.Delay(90);
        checks["M1: clip pauses blinking and does not change keyboard focus"] = companion.IsPlayingClip && !companion.IsBlinking && ReferenceEquals(focused, Keyboard.FocusedElement) && !companion.IsActive;
        await Task.Delay(1000);
        checks["M1: one-shot clip returns to the pose"] = !companion.IsPlayingClip && ReferenceEquals(companion.Pose, poseBody) && companion.IsAtPose;
        foreach (var effect in new[] { "zzz", "typing", "surprise", "sweat", "tear", "rice", "stars", "bubble", "glow" })
        {
            companion.PreviewEffect(effect);
            checks[$"M1: vector effect {effect} starts without focus"] = companion.HasEffect && !companion.IsActive;
            await Task.Delay(35);
        }
        await Task.Delay(1800);
        checks["M1: vector bursts remove their clocks and visuals"] = !companion.HasEffect;
        checks["M1: invalid frame timing, loop cadence and clip length are rejected"] = !CharacterMotion.Valid(clip with { Frames = [new(frame, TimeSpan.FromMilliseconds(70))] })
            && !CharacterMotion.Valid(clip with { Frames = [new(frame, TimeSpan.FromSeconds(.1))] })
            && !CharacterMotion.Valid(clip with { Frames = [new(frame, TimeSpan.FromSeconds(.3))], Loop = true })
            && !CharacterMotion.Valid(clip with { Frames = [new(frame, TimeSpan.FromSeconds(4.5))] });
        companion.PlayEntrance();
        companion.PreviewAdvance(TimeSpan.FromSeconds(121));
        checks["M1: quiescence also removes an interrupted entrance opacity clock"] = !companion.HasNonBlinkMotion;
        companion.NoteActivity();

        window.PreviewCompanion("deepseek-rice"); window.PreviewPoseIndex(0);
        companion = window.Companion!; await companion.MotionReady;
        poseBody = window.CurrentPose!.Body; frame = companion.PreparedMotion.Blink!;
        clip = clip with { Frames = [new(frame, TimeSpan.FromSeconds(.3)), new(poseBody, TimeSpan.FromSeconds(.3)), new(frame, TimeSpan.FromSeconds(.3))] };
        var doze = new CharacterClip(CompanionCue.Doze, [new(poseBody, TimeSpan.FromSeconds(.5)), new(frame, TimeSpan.FromSeconds(.5))], "zzz", Loop: true);
        companion.PreviewClips(doze, clip with { Cue = CompanionCue.Wake });
        companion.PreviewAdvance(TimeSpan.FromSeconds(26));
        checks["M1: inactivity selects a declared doze clip"] = companion.DirectorState == CompanionState.Dozing && companion.IsPlayingClip;
        companion.PreviewAdvance(TimeSpan.FromSeconds(96));
        checks["M1: two minutes stop loops and clocks on the last sleeping frame"] = companion.DirectorState == CompanionState.Quiescent
            && !companion.HasNonBlinkMotion && ReferenceEquals(companion.Pose, frame) && !window.Ambient.IsRunning;
        companion.NoteActivity();
        checks["M1: input wakes a sleeping companion before any other reaction"] = companion.IsPlayingClip && companion.DirectorState != CompanionState.Quiescent;
        await Task.Delay(1000);

        foreach (var input in new[] { "keyboard", "keyboard-tab", "mouse", "controller" })
        {
            companion.PreviewClips();
            companion.PreviewAdvance(TimeSpan.FromSeconds(121));
            if (input.StartsWith("keyboard", StringComparison.Ordinal)) RaiseKey(window, input == "keyboard-tab" ? Key.Tab : Key.Right);
            else if (input == "mouse") window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.PreviewMouseMoveEvent });
            else window.OnGamepad(GamepadButton.Right);
            checks[$"M1: {input} activity resumes the director"] = companion.DirectorState != CompanionState.Quiescent && window.Ambient.IsRunning;
            window.PreviewPage(MenuPage.Desktop);
        }

        window.PreviewPage(MenuPage.Settings);
        window.CharacterMotionCheck.IsChecked = false;
        window.CharacterMotionCheck.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        companion.PreviewClips(clip, doze, clip with { Cue = CompanionCue.Idle });
        companion.PreviewAdvance(TimeSpan.FromSeconds(30));
        var quiet = !companion.IsPlayingClip;
        companion.RequestCue(CompanionCue.Click);
        checks["M1: idle setting stops autonomous clips but preserves explicit clicks"] = quiet && companion.IsPlayingClip;
        window.CharacterMotionCheck.IsChecked = true;
        window.CharacterMotionCheck.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.PreviewPage(MenuPage.Desktop);
        window.PreviewAnimations(false);
        companion.PlayClip(clip);
        checks["M1: disabled animations do not start clips"] = !companion.IsPlayingClip && !companion.HasMotion && ReferenceEquals(companion.Pose, window.CurrentPose!.Body);

        window.PreviewAnimations(true);
        window.PreviewCompanion("deepseek-rice"); window.PreviewPoseIndex(0);
        companion = window.Companion!; companion.StartMotion(); await companion.MotionReady;
        companion.RequestCue(CompanionCue.Hover);
        var delayed = companion.HasDelayedReaction && companion.ReactionY == 0;
        await Task.Delay(200);
        checks["M1: rice-bowl hover is delayed rather than jumping immediately"] = delayed && companion.ReactionY == 0;
        await Task.Delay(650);
        companion.RequestCue(CompanionCue.Click);
        window.PreviewHide();
        checks["M1: hidden companion releases prepared frames and stops all motion"] = !companion.HasMotion && !companion.PreparedMotion.Pictures.Any() && !window.Ambient.IsRunning;
        window.PreviewAnimations(false);
        window.InvokeMenu();
        await companion.MotionReady;
        checks["M1: preparation finishes without a UI-thread bitmap or memory error"] = companion.PreparationError is null;
        window.PreviewCompanion(null);
        window.PreviewPage(MenuPage.Settings);
        window.ScrollSettingsTo("SectionAppearance");
        Capture(window, Path.Combine(output, "settings-character-motion.png"), 1);
        window.PreviewPage(MenuPage.Desktop);
    }

    private static void CaptureClips(IReadOnlyList<(BitmapSource Image, string Label)> frames, string file)
    {
        const int width = 150, height = 260;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, width * frames.Count, height));
            for (var i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                var scale = Math.Min((width-12) / (double)f.Image.PixelWidth, 216.0 / f.Image.PixelHeight);
                drawing.DrawImage(f.Image, new Rect(i*width+(width-f.Image.PixelWidth*scale)/2, 8, f.Image.PixelWidth*scale, f.Image.PixelHeight*scale));
                drawing.DrawText(new FormattedText(f.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Brushes.Black, 1), new Point(i*width+4, 230));
            }
        }
        var bitmap = new RenderTargetBitmap(width * frames.Count, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(file); encoder.Save(stream);
    }
}
