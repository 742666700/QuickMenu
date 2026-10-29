namespace QuickMenu.Models;

public enum CompanionCue { Idle, Hover, Click, Doze, Wake, Watch, Typing }
public enum CompanionState { Resting, Playing, Dozing, Waiting, Quiescent, Stopped }
public sealed record Temperament(double HoverDelay, double HoverHeight, double ClickHeight, bool DoubleHop,
    double EntranceMilliseconds, double IdleMinSeconds, double IdleMaxSeconds, double DozeSeconds,
    bool Nod = false, bool Float = false, double HoverMilliseconds = 340, double ClickMilliseconds = 360,
    double EntranceX = 0, double EntranceDrop = 0, bool HoverNod = false, bool Sleeps = false);
public sealed record HourWeight(TimeOnly Start, TimeOnly End, double Weight)
{
    public bool Contains(TimeOnly time) => Start <= End ? time >= Start && time < End : time >= Start || time < End;
}
public sealed record PoseChoice(int Group, IReadOnlyList<HourWeight>? Hours = null);

/// <summary>Input, cooldowns and pose decisions use injected time and randomness, independently of WPF.</summary>
public sealed class CompanionSchedule(Func<DateTimeOffset> clock, Func<double> random, Temperament temperament,
    Func<DateTimeOffset, DateTimeOffset, DateTimeOffset?>? alignIdle = null, bool diagnostic = false)
{
    private DateTimeOffset _activity, _idleDue, _hoverDue, _automaticDue;
    private bool _dozed;
    public CompanionState State { get; private set; } = CompanionState.Stopped;
    public bool Spontaneous { get; private set; } = true;
    public DateTimeOffset NextIdleAt => _idleDue;
    public static readonly TimeSpan QuietAfter = TimeSpan.FromMinutes(2);

    public void Start(bool spontaneous)
    {
        State = CompanionState.Resting; _dozed = false;
        _activity = clock(); _hoverDue = DateTimeOffset.MinValue;
        _automaticDue = DateTimeOffset.MinValue;
        Spontaneous = spontaneous; NextIdle();
    }
    public void Stop() { State = CompanionState.Stopped; _dozed = false; }
    public void SetSpontaneous(bool enabled)
    {
        Spontaneous = enabled;
        if (!enabled && State is CompanionState.Dozing or CompanionState.Waiting or CompanionState.Playing) { State = CompanionState.Resting; _dozed = false; }
        NextIdle();
    }
    public CompanionCue? NoteActivity()
    {
        if (State == CompanionState.Stopped) return null;
        var wake = _dozed; _dozed = false;
        _activity = clock();
        if (State is CompanionState.Quiescent or CompanionState.Dozing or CompanionState.Waiting) { State = CompanionState.Resting; NextIdle(); }
        return wake ? CompanionCue.Wake : null;
    }
    public CompanionCue? Tick(bool canDoze, bool canIdle)
    {
        if (State is CompanionState.Stopped or CompanionState.Quiescent) return null;
        var now = clock();
        if (now - _activity >= QuietAfter) { State = CompanionState.Quiescent; return null; }
        if (!Spontaneous || State is CompanionState.Playing or CompanionState.Dozing or CompanionState.Waiting) return null;
        if (canDoze && now - _activity >= TimeSpan.FromSeconds(temperament.DozeSeconds))
        { State = temperament.Sleeps ? CompanionState.Dozing : CompanionState.Waiting; _dozed = temperament.Sleeps; return CompanionCue.Doze; }
        if (canIdle && now >= _idleDue && now >= _automaticDue) { State = CompanionState.Playing; NextIdle(); return CompanionCue.Idle; }
        return null;
    }
    public CompanionCue? Trigger(CompanionCue cue)
    {
        if (State == CompanionState.Stopped) return null;
        if (cue is CompanionCue.Watch or CompanionCue.Typing)
        {
            if (!Spontaneous || State is CompanionState.Quiescent or CompanionState.Dozing or CompanionState.Waiting
                || cue == CompanionCue.Watch && clock() < _automaticDue) return null;
            State = CompanionState.Playing; return cue;
        }
        if (cue == CompanionCue.Hover && clock() < _hoverDue) return null;
        var wake = NoteActivity();
        if (wake is not null) return wake;
        if (cue == CompanionCue.Hover) _hoverDue = clock().AddSeconds(4);
        State = CompanionState.Playing;
        return cue;
    }
    public void Finish()
    {
        if (State == CompanionState.Playing) { State = CompanionState.Resting; NextIdle(); }
    }
    public void BeginClip(CompanionCue cue)
    {
        if (State == CompanionState.Stopped) return;
        State = cue == CompanionCue.Doze ? (temperament.Sleeps ? CompanionState.Dozing : CompanionState.Waiting) : CompanionState.Playing;
        _dozed = cue == CompanionCue.Doze && temperament.Sleeps;
        if (cue is CompanionCue.Idle or CompanionCue.Watch) _automaticDue = clock().AddSeconds(8);
    }
    private void NextIdle()
    {
        var now = clock();
        var minimum = Math.Max(diagnostic ? 5 : 8, temperament.IdleMinSeconds);
        var maximum = Math.Max(minimum, temperament.IdleMaxSeconds);
        var candidate = now.AddSeconds(minimum + Math.Clamp(random(), 0, 1) * (maximum - minimum));
        // Reuse an existing sparkle burst without changing the temperament's allowed interval.
        _idleDue = alignIdle?.Invoke(candidate, now.AddSeconds(maximum))
            ?? alignIdle?.Invoke(now.AddSeconds(minimum), candidate) ?? candidate;
    }

    public static int SelectPose(IReadOnlyList<PoseChoice> poses, int previous, TimeOnly time, Func<double> random)
    {
        if (poses.Count == 0) throw new ArgumentException("A pose is required.", nameof(poses));
        var groups = poses.Select(p => p.Group).Distinct().Where(g => previous < 0 || poses[previous].Group != g).ToArray();
        if (groups.Length == 0) groups = [poses[0].Group];
        var weights = groups.Select(g => poses.Where(p => p.Group == g).Max(p =>
            p.Hours?.Where(h => h.Contains(time)).Select(h => h.Weight).DefaultIfEmpty(1).Max() ?? 1)).ToArray();
        var groupIndex = WeightedIndex(weights, random());
        var choices = Enumerable.Range(0, poses.Count).Where(i => poses[i].Group == groups[groupIndex] && i != previous).ToArray();
        if (choices.Length == 0) return previous;
        return choices[Math.Min(choices.Length - 1, (int)(Math.Clamp(random(), 0, 1) * choices.Length))];
    }
    public static int WeightedIndex(IReadOnlyList<double> weights, double random)
    {
        var total = weights.Sum(w => double.IsFinite(w) && w > 0 ? w : 0);
        if (total <= 0) throw new ArgumentException("A positive finite weight is required.", nameof(weights));
        var draw = Math.Clamp(random, 0, Math.BitDecrement(1.0)) * total;
        for (var i = 0; i < weights.Count; i++)
            if (double.IsFinite(weights[i]) && weights[i] > 0 && (draw -= weights[i]) < 0) return i;
        return weights.Count - 1;
    }
}
