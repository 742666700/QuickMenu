using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuickMenu.Models;

namespace QuickMenu;

internal sealed partial class CompanionWindow
{
    private Character? _character;
    private CharacterMotion? _motion;
    private CancellationTokenSource? _prepareCancellation;
    private readonly DispatcherTimer _directorTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _clipTimer = new();
    private readonly DispatcherTimer _reactionTimer = new();
    private CompanionSchedule? _schedule;
    private CharacterClip? _playing;
    private int _clipFrame, _preparation, _idlePlays;
    private int _alignedIdlePlays;
    private bool _inBurst;
    private bool _sampleTracing;
    private readonly List<string> _sampleTrace = new();
    private bool _spontaneous = true;
    private bool _typingRequested;
    private TimeSpan _previewOffset;
    private Random _scheduleRandom = new();
    internal bool IsAtPose => _pose is not null && ReferenceEquals(_body.Source, _pose.Body);
    internal long DecodedBytes => CharacterArt.Bytes((_character?.Poses.Select(p => p.Body) ?? [])
        .Concat(_motion?.Pictures ?? []).Concat(_character?.Avatar is { } avatar ? new[] { avatar } : []));
    internal Task MotionReady { get; private set; } = Task.CompletedTask;
    internal Exception? PreparationError { get; private set; }
    internal CharacterMotion PreparedMotion => _motion ?? CharacterMotion.Empty;
    internal CompanionState DirectorState => _schedule?.State ?? CompanionState.Stopped;
    internal bool IsPlayingClip => _playing is not null;
    internal CompanionCue? PlayingCue => _playing?.Cue;
    internal bool IsBlinking => _blinking;
    internal int IdlePlays => _idlePlays;
    internal int AlignedIdlePlays => _alignedIdlePlays;
    internal IReadOnlyList<string> SampleTrace => _sampleTrace;
    private void TraceSample(string message)
    {
        if (_sampleTracing && _sampleTrace.Count < 100)
            _sampleTrace.Add($"{Now():O} {message}; state={DirectorState}; due={_schedule?.NextIdleAt:O}");
    }
    internal bool HasNonBlinkMotion => _directorTimer.IsEnabled || _clipTimer.IsEnabled || _frameTimer.IsEnabled || _reactionTimer.IsEnabled
        || _glints.IsRunning || _glints.HasEffects || _hop.HasAnimatedProperties || _fadeBody.HasAnimatedProperties || HasAnimatedProperties;
    internal double ReactionY => _hop.Y;
    internal bool HasDelayedReaction => _reactionTimer.IsEnabled;
    internal bool HasEffect => _glints.HasEffects;
    internal void PreviewEffect(string effect) => _glints.PlayCompanionEffect(effect,
        new Point(_figureWidth * _scale * .65, Pad + _figureHeight * _scale * .22), _glint.Color);
    public event Action<bool>? QuiescenceChanged;
    private DateTimeOffset Now() => DateTimeOffset.UtcNow + _previewOffset;

    private void InitializeDirector()
    {
        _directorTimer.Tick += (_, _) => Direct();
        _glints.BurstStarted += () =>
        {
            TraceSample("sparkle burst");
            _inBurst = true;
            try
            {
                var previous = _playing;
                Direct(withBurst: true);
                if (_playing is { Loop: true } clip && ReferenceEquals(previous, clip))
                { _glints.StopEffects(); PlayEffects(clip); }
            }
            finally { _inBurst = false; }
        };
        _clipTimer.Tick += (_, _) => AdvanceClip();
        _reactionTimer.Tick += (_, _) => { _reactionTimer.Stop(); var action = _pendingReaction; _pendingReaction = null; action?.Invoke(); };
    }
    private void StartDirector()
    {
        _schedule = new CompanionSchedule(Now, _scheduleRandom.NextDouble, _character!.Temperament, _glints.AlignBurst);
        _schedule.Start(_spontaneous);
        _directorTimer.Start();
        QuiescenceChanged?.Invoke(false);
        if (_motion is not null && _spontaneous && _pose?.AutoCue is { } cue) ExecuteCue(cue);
        ResumeSearch();
    }
    private void StopDirector()
    {
        _directorTimer.Stop(); _clipTimer.Stop(); _reactionTimer.Stop();
        _playing = null; _schedule?.Stop();
        _pendingReaction = null;
        _glints.StopEffects();
        if (_pose is not null) _body.Source = _pose.Body;
    }
    private void EnsurePrepared()
    {
        if (_pose is null || _motion is not null || _prepareCancellation is not null) return;
        _prepareCancellation = new CancellationTokenSource();
        MotionReady = Prepare(_pose, ++_preparation, _prepareCancellation.Token);
    }
    private async Task Prepare(CharacterPose pose, int generation, CancellationToken token)
    {
        try
        {
            var motion = await pose.Prepare(token);
            if (token.IsCancellationRequested || generation != _preparation) return;
            var held = CharacterArt.Bytes(motion.Pictures.Concat(_character!.Poses.Select(p => p.Body)).Append(_character.Avatar!));
            if (held > 40_000_000) throw new InvalidOperationException("Character decoded pictures exceed 40 MB.");
            _motion = motion; _blink.Source = motion.Blink;
            if (_playing is null) _body.Source = pose.Body;
            _frames = motion.Frames.Count > 1 ? motion.Frames.Select(f => (f.Picture, f.Hold)).ToArray() : null;
            if (_animated && DirectorState != CompanionState.Quiescent) ResumePictures();
            if (_animated && _spontaneous && pose.AutoCue is { } cue) ExecuteCue(cue);
            ResumeSearch();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested || generation != _preparation) return;
            PreparationError = ex; App.Log(ex);
        }
    }
    private void ReleaseMotion()
    {
        _preparation++;
        _prepareCancellation?.Cancel(); _prepareCancellation?.Dispose(); _prepareCancellation = null;
        _motion = null; _frames = null; _blink.Source = null;
        if (_pose is not null) _body.Source = _pose.Body;
        _fadeBody.BeginAnimation(OpacityProperty, null); _fadeBody.Source = null; _fadeBody.Clip = null; PreparationError = null;
        MotionReady = Task.CompletedTask;
    }
    private void ResumePictures()
    {
        if (_playing is not null) return;
        if (CanBlink) _blinkTimer.Start();
        if (_frames is { Count: > 1 }) { _frameTimer.Interval = _frames[_frame].Hold; _frameTimer.Start(); }
    }
    public void SetSpontaneous(bool enabled)
    {
        if (_spontaneous == enabled) return;
        _spontaneous = enabled; _schedule?.SetSpontaneous(enabled);
        if (!enabled && _playing?.Cue is CompanionCue.Idle or CompanionCue.Doze or CompanionCue.Watch or CompanionCue.Typing) FinishClip();
        if (enabled) ResumeSearch();
    }
    public void NoteActivity()
    {
        if (!_animated || _schedule is null) return;
        var quiet = DirectorState == CompanionState.Quiescent;
        var waiting = DirectorState == CompanionState.Waiting;
        var cue = _schedule.NoteActivity();
        if (quiet)
        {
            _directorTimer.Start(); _glints.Start();
            FinishClip(); QuiescenceChanged?.Invoke(false);
        }
        if (cue is { } wake) ExecuteCue(wake);
        else if (waiting) FinishClip();
        if (quiet || waiting) ResumeSearch();
    }
    private void Direct(bool withBurst = false)
    {
        if (!_animated || _schedule is null) return;
        // Start idle pictures and effects in the sparkle's actual dispatcher callback. Independent clocks
        // started a few milliseconds apart can otherwise make a transparent window repaint twice per frame.
        var cue = _schedule.Tick(HasCue(CompanionCue.Doze), (withBurst || !_glints.HasBursts) && HasCue(CompanionCue.Idle));
        TraceSample($"tick inBurst={withBurst} cue={cue}");
        if (DirectorState == CompanionState.Quiescent)
        {
            _directorTimer.Stop(); _clipTimer.Stop(); _frameTimer.Stop(); _reactionTimer.Stop();
            BeginAnimation(OpacityProperty, null);
            _glints.Stop(); _glints.StopEffects();
            _hop.BeginAnimation(TranslateTransform.XProperty, null); _hop.BeginAnimation(TranslateTransform.YProperty, null);
            _fadeBody.BeginAnimation(OpacityProperty, null); _fadeBody.Source = null; _fadeBody.Clip = null;
            if (_playing?.Cue == CompanionCue.Doze && _character!.Temperament.Sleeps) _body.Source = _motion?.Rest ?? _playing.Frames[^1].Picture;
            else { _playing = null; _frame = 0; _body.Source = _pose!.Body; if (CanBlink) _blinkTimer.Start(); }
            QuiescenceChanged?.Invoke(true);
        }
        else if (cue is { } due) ExecuteCue(due);
        if (_directorTimer.IsEnabled)
        {
            var untilIdle = _schedule.NextIdleAt - Now();
            _directorTimer.Interval = DirectorState == CompanionState.Resting && HasCue(CompanionCue.Idle) && untilIdle > TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(Math.Clamp(untilIdle.TotalMilliseconds, 1, 500)) : TimeSpan.FromMilliseconds(500);
        }
    }
    private bool HasCue(CompanionCue cue) => _motion?.Clips.Any(c => c.Cue == cue) == true;
    public void RequestCue(CompanionCue cue)
    {
        if (!_animated || _schedule is null) return;
        if (cue is CompanionCue.Watch or CompanionCue.Typing && !HasCue(cue)) return;
        var quiet = DirectorState == CompanionState.Quiescent;
        var request = _schedule.Trigger(cue);
        if (request is null) return;
        if (quiet) { _directorTimer.Start(); _glints.Start(); QuiescenceChanged?.Invoke(false); }
        ExecuteCue(request.Value);
    }
    private void ExecuteCue(CompanionCue cue)
    {
        _reactionTimer.Stop(); _pendingReaction = null;
        _hop.BeginAnimation(TranslateTransform.YProperty, null);
        if (_playing is not null) FinishClip();
        var time = TimeOnly.FromDateTime(DateTime.Now);
        var choices = _motion?.Clips.Where(c => c.Cue == cue && (c.Hours is null || c.Hours.Any(h => h.Contains(time)))).ToArray() ?? [];
        if (choices.Length > 0)
        {
            var clip = choices[CompanionSchedule.WeightedIndex(choices.Select(c => c.Weight).ToArray(), _random.NextDouble())];
            void ReactClip()
            {
                if (cue is CompanionCue.Hover or CompanionCue.Click)
                {
                    var tempo = _character!.Temperament;
                    Move((cue == CompanionCue.Hover ? tempo.HoverNod : tempo.Nod) ? (cue == CompanionCue.Hover ? tempo.HoverHeight : tempo.ClickHeight)
                        : -(cue == CompanionCue.Hover ? tempo.HoverHeight : tempo.ClickHeight), tempo.DoubleHop,
                        cue == CompanionCue.Hover ? tempo.HoverMilliseconds : tempo.ClickMilliseconds);
                }
                PlayClip(clip);
            }
            if (cue == CompanionCue.Hover && _character!.Temperament.HoverDelay > 0)
            { _pendingReaction = ReactClip; _reactionTimer.Interval = TimeSpan.FromSeconds(_character.Temperament.HoverDelay); _reactionTimer.Start(); }
            else ReactClip();
            return;
        }
        if (cue is CompanionCue.Hover or CompanionCue.Click)
        {
            var tempo = _character!.Temperament;
            void React()
            {
                var height = cue == CompanionCue.Hover ? tempo.HoverHeight : tempo.ClickHeight;
                var nod = cue == CompanionCue.Hover ? tempo.HoverNod : tempo.Nod;
                Move(nod ? height : -height, tempo.DoubleHop, cue == CompanionCue.Hover ? tempo.HoverMilliseconds : tempo.ClickMilliseconds);
                if (cue == CompanionCue.Click && CanBlink) Blink();
                _schedule?.Finish();
            }
            if (cue == CompanionCue.Hover && tempo.HoverDelay > 0)
            {
                _reactionTimer.Interval = TimeSpan.FromSeconds(tempo.HoverDelay);
                _pendingReaction = React; _reactionTimer.Start();
            }
            else React();
        }
        else _schedule?.Finish();
    }
    private Action? _pendingReaction;
    private void CancelHover()
    {
        if (_pendingReaction is null) return;
        _reactionTimer.Stop(); _pendingReaction = null; _schedule?.Finish();
    }
    internal void StopCue(CompanionCue cue) { if (_playing?.Cue == cue) FinishClip(); }
    internal void SetTyping(bool enabled)
    {
        _typingRequested = enabled;
        if (enabled) ResumeSearch(); else StopCue(CompanionCue.Typing);
    }
    private void ResumeSearch()
    {
        if (_typingRequested && _spontaneous && _playing is null) RequestCue(CompanionCue.Typing);
    }
    private void Move(double height, bool doubleHop, double milliseconds)
    {
        var move = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(milliseconds), FillBehavior = FillBehavior.Stop };
        var ease = new KeySpline(.25, .8, .4, 1);
        move.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        move.KeyFrames.Add(new SplineDoubleKeyFrame(height, KeyTime.FromPercent(doubleHop ? .28 : .5), ease));
        if (doubleHop)
        {
            move.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromPercent(.54), ease));
            move.KeyFrames.Add(new SplineDoubleKeyFrame(height / 2, KeyTime.FromPercent(.77), ease));
        }
        move.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromPercent(1), ease));
        move.Completed += (_, _) => _hop.BeginAnimation(TranslateTransform.YProperty, null);
        _hop.BeginAnimation(TranslateTransform.YProperty, move);
    }
    internal void PlayClip(CharacterClip clip)
    {
        if (!_animated || !CharacterMotion.Valid(clip)) return;
        TraceSample($"play {clip.Cue}");
        if (_playing is not null) FinishClip();
        _playing = clip; _clipFrame = 0; _frameTimer.Stop(); _blinkTimer.Stop();
        _schedule?.BeginClip(clip.Cue);
        _blinkCancellation?.Cancel(); _blinkCancellation?.Dispose();
        _blinkCancellation = new CancellationTokenSource(); _blinking = false; _blink.Opacity = 0;
        if (clip.Cue == CompanionCue.Idle) { _idlePlays++; if (_inBurst) _alignedIdlePlays++; }
        ShowClipFrame();
        PlayEffects(clip);
    }
    private void PlayEffects(CharacterClip clip)
    {
        if (clip.Effect is { } effect) _glints.PlayCompanionEffect(effect, new Point(_figureWidth * _scale * .65, Pad + _figureHeight * _scale * .22), _glint.Color);
        foreach (var extra in clip.Effects ?? [])
            _glints.PlayCompanionEffect(extra.Name, new Point(_figureWidth * _scale * extra.Anchor.X, Pad + _figureHeight * _scale * extra.Anchor.Y), _glint.Color);
    }
    private void ShowClipFrame()
    {
        var frame = _playing!.Frames[_clipFrame];
        SetPicture(frame.Picture); _clipTimer.Interval = frame.Hold; _clipTimer.Start();
    }
    private void AdvanceClip()
    {
        if (_playing is null) { _clipTimer.Stop(); return; }
        _clipFrame++;
        if (_clipFrame >= _playing.Frames.Count)
        {
            if (!_playing.Loop) { FinishClip(); ResumeSearch(); return; }
            _clipFrame = 0;
            if (_playing.Cue == CompanionCue.Doze) _idlePlays++;
        }
        ShowClipFrame();
    }
    private void FinishClip()
    {
        _clipTimer.Stop(); _playing = null; _frame = 0; _glints.StopEffects();
        if (_pose is not null) SetPicture(_pose.Body);
        _schedule?.Finish();
        if (_animated && DirectorState != CompanionState.Quiescent) ResumePictures();
    }
    internal void PreviewAdvance(TimeSpan elapsed) { _previewOffset += elapsed; Direct(); }
    internal void PreviewIdleClip()
    {
        if (_motion?.Clips.Any(c => c.Cue == CompanionCue.Idle) == true) return;
        var picture = _motion?.Blink ?? _pose!.Body;
        _motion = ( _motion ?? CharacterMotion.Empty) with { Clips = [new(CompanionCue.Idle, [new(picture, TimeSpan.FromSeconds(.5)), new(_pose!.Body, TimeSpan.FromSeconds(.5))], "stars")] };
    }
    internal void PreviewClips(params CharacterClip[] clips) => _motion = (_motion ?? CharacterMotion.Empty) with { Clips = clips, Rest = null };
    internal void PreviewSampleActions()
    {
        _sampleTracing = true; _sampleTrace.Clear();
        if (_playing is not null) FinishClip();
        _schedule = new CompanionSchedule(Now, () => 0, _character!.Temperament with { IdleMinSeconds = 5, IdleMaxSeconds = 8 }, diagnostic: true);
        _schedule.Start(true); _directorTimer.Start();
        TraceSample("sample start");
    }
    internal void ResetForPerformance()
    {
        StopMotion(); _random = new Random(7); _scheduleRandom = new Random(71);
        _glints.ResetForPerformance(); Layout();
    }
}
