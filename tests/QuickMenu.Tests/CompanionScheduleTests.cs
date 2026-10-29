using QuickMenu.Models;
using Xunit;

namespace QuickMenu.Tests;

public class CompanionScheduleTests
{
    private static readonly Temperament Tempo = new(.4, 4, 6, false, 320, 12, 25, 25, Sleeps: true);
    private DateTimeOffset _now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private CompanionSchedule Create(bool spontaneous = true)
    {
        var schedule = new CompanionSchedule(() => _now, () => 0, Tempo);
        schedule.Start(spontaneous); return schedule;
    }

    [Fact]
    public void ThemeWatchSharesTheEightSecondAutonomousCooldown()
    {
        var schedule = Create();
        schedule.BeginClip(CompanionCue.Idle);
        _now += TimeSpan.FromSeconds(2); schedule.Finish();
        Assert.Null(schedule.Trigger(CompanionCue.Watch));
        Assert.Equal(CompanionCue.Click, schedule.Trigger(CompanionCue.Click));
        schedule.Finish();
        _now += TimeSpan.FromSeconds(6);
        Assert.Equal(CompanionCue.Watch, schedule.Trigger(CompanionCue.Watch));
        schedule.BeginClip(CompanionCue.Watch); schedule.Finish();
        _now += TimeSpan.FromMilliseconds(7999);
        Assert.Null(schedule.Trigger(CompanionCue.Watch));
        _now += TimeSpan.FromMilliseconds(1);
        Assert.Equal(CompanionCue.Watch, schedule.Trigger(CompanionCue.Watch));
    }

    [Fact]
    public void FinishedNonSleepingGestureResumesIdleWithoutRepeatingTheWaitCue()
    {
        var schedule = new CompanionSchedule(() => _now, () => 0, Tempo with { Sleeps = false });
        schedule.Start(true); _now += TimeSpan.FromSeconds(25);
        Assert.Equal(CompanionCue.Doze, schedule.Tick(true, false));
        schedule.BeginClip(CompanionCue.Doze);
        _now += TimeSpan.FromSeconds(3); schedule.Finish();
        Assert.Equal(CompanionState.Resting, schedule.State);
        _now += TimeSpan.FromSeconds(12);
        Assert.Equal(CompanionCue.Idle, schedule.Tick(true, true));
        schedule.Finish(); schedule.NoteActivity();
        _now += TimeSpan.FromSeconds(25);
        Assert.Equal(CompanionCue.Doze, schedule.Tick(true, false));
    }

    [Fact]
    public void WaitingCharacterStopsOnInputWithoutInventingAWake()
    {
        var schedule = new CompanionSchedule(() => _now, () => 0, Tempo with { Sleeps = false });
        schedule.Start(true); _now += TimeSpan.FromSeconds(25);
        Assert.Equal(CompanionCue.Doze, schedule.Tick(true, false));
        Assert.Equal(CompanionState.Waiting, schedule.State);
        Assert.Null(schedule.NoteActivity());
        Assert.Equal(CompanionState.Resting, schedule.State);
    }

    [Fact]
    public void IdleAlignmentStaysWithinTemperamentAndPreservesMinimumGap()
    {
        var origin = _now;
        var schedule = new CompanionSchedule(() => _now, () => .5, Tempo, (first, last) => first.AddSeconds(1) <= last ? first.AddSeconds(1) : null);
        schedule.Start(true);
        Assert.InRange((schedule.NextIdleAt - origin).TotalSeconds, 12, 25);
        _now = schedule.NextIdleAt;
        Assert.Equal(CompanionCue.Idle, schedule.Tick(false, true));
        _now += TimeSpan.FromSeconds(4); schedule.Finish();
        Assert.InRange((schedule.NextIdleAt - _now).TotalSeconds, 12, 25);
    }

    [Fact]
    public void DozeWakesOnInputAndQuietSleepStillWakes()
    {
        var schedule = Create();
        _now += TimeSpan.FromSeconds(25);
        Assert.Equal(CompanionCue.Doze, schedule.Tick(true, false));
        Assert.Equal(CompanionState.Dozing, schedule.State);
        Assert.Equal(CompanionCue.Wake, schedule.NoteActivity());
        _now += TimeSpan.FromSeconds(25);
        Assert.Equal(CompanionCue.Doze, schedule.Tick(true, false));
        _now += TimeSpan.FromSeconds(96);
        Assert.Null(schedule.Tick(true, true));
        Assert.Equal(CompanionState.Quiescent, schedule.State);
        Assert.Equal(CompanionCue.Wake, schedule.NoteActivity());
        Assert.Equal(CompanionState.Resting, schedule.State);
    }

    [Fact]
    public void InterruptedLoopRemainsPlayingAndBlocksAutonomousClips()
    {
        var schedule = Create();
        schedule.Trigger(CompanionCue.Click);
        schedule.Finish();
        schedule.BeginClip(CompanionCue.Typing);
        _now += TimeSpan.FromSeconds(30);
        Assert.Null(schedule.Tick(true, true));
        Assert.Equal(CompanionState.Playing, schedule.State);
        schedule.Finish();
        Assert.Equal(CompanionState.Resting, schedule.State);
    }

    [Fact]
    public void MissingDozeArtworkDoesNotInventASleepState()
    {
        var schedule = Create();
        _now += TimeSpan.FromSeconds(30);
        Assert.Null(schedule.Tick(false, false));
        Assert.Equal(CompanionState.Resting, schedule.State);
    }

    [Fact]
    public void HoverHasFourSecondCooldownAndClickCanInterrupt()
    {
        var schedule = Create();
        Assert.Equal(CompanionCue.Hover, schedule.Trigger(CompanionCue.Hover));
        Assert.Null(schedule.Trigger(CompanionCue.Hover));
        Assert.Equal(CompanionCue.Click, schedule.Trigger(CompanionCue.Click));
        _now += TimeSpan.FromMilliseconds(3999);
        Assert.Null(schedule.Trigger(CompanionCue.Hover));
        _now += TimeSpan.FromMilliseconds(1);
        Assert.Equal(CompanionCue.Hover, schedule.Trigger(CompanionCue.Hover));
    }

    [Fact]
    public void DisabledSpontaneousMotionKeepsExplicitReactions()
    {
        var schedule = Create(false);
        _now += TimeSpan.FromSeconds(40);
        Assert.Null(schedule.Tick(true, true));
        Assert.Null(schedule.Trigger(CompanionCue.Watch));
        Assert.Null(schedule.Trigger(CompanionCue.Typing));
        Assert.Equal(CompanionCue.Click, schedule.Trigger(CompanionCue.Click));
        schedule.Finish();
        Assert.Equal(CompanionCue.Hover, schedule.Trigger(CompanionCue.Hover));
    }

    [Fact]
    public void TwoMinutesWithoutInputQuiescesEvenWithSpontaneousMotionOff()
    {
        var schedule = Create(false);
        _now += TimeSpan.FromSeconds(119);
        schedule.Tick(false, true);
        Assert.NotEqual(CompanionState.Quiescent, schedule.State);
        _now += TimeSpan.FromSeconds(1);
        schedule.Tick(false, true);
        Assert.Equal(CompanionState.Quiescent, schedule.State);
        Assert.Null(schedule.NoteActivity());
        Assert.Equal(CompanionState.Resting, schedule.State);
    }

    [Fact]
    public void AmbientAndTypingCuesDoNotCountAsUserInput()
    {
        var schedule = Create();
        _now += TimeSpan.FromSeconds(119);
        Assert.Equal(CompanionCue.Watch, schedule.Trigger(CompanionCue.Watch));
        _now += TimeSpan.FromSeconds(1);
        schedule.Tick(false, false);
        Assert.Equal(CompanionState.Quiescent, schedule.State);
        Assert.Null(schedule.Trigger(CompanionCue.Watch));
    }

    [Fact]
    public void IdleIntervalsRestartAfterCompletionAndNeverRunWhileStopped()
    {
        var schedule = Create();
        _now += TimeSpan.FromSeconds(12);
        Assert.Equal(CompanionCue.Idle, schedule.Tick(false, true));
        _now += TimeSpan.FromSeconds(4);
        Assert.Null(schedule.Tick(false, true));
        schedule.Finish();
        _now += TimeSpan.FromSeconds(11);
        Assert.Null(schedule.Tick(false, true));
        _now += TimeSpan.FromSeconds(1);
        Assert.Equal(CompanionCue.Idle, schedule.Tick(false, true));
        schedule.Stop();
        Assert.Null(schedule.Trigger(CompanionCue.Click));
        Assert.Null(schedule.NoteActivity());
        Assert.Null(schedule.Tick(true, true));
    }

    [Theory]
    [InlineData(23, 0, true)]
    [InlineData(5, 0, true)]
    [InlineData(6, 30, false)]
    [InlineData(22, 30, true)]
    [InlineData(12, 0, false)]
    public void HourWeightsCrossMidnightWithExclusiveEnd(int hour, int minute, bool expected) =>
        Assert.Equal(expected, new HourWeight(new(22, 30), new(6, 30), 3).Contains(new(hour, minute)));

    [Fact]
    public void TimeWeightChangesGroupSelectionWithoutAdjacentGroupRepeats()
    {
        PoseChoice[] poses = [new(0), new(1, [new(new(22, 30), new(6, 30), 3)]), new(2)];
        Assert.Equal(1, CompanionSchedule.SelectPose(poses, 0, new(23, 0), () => .6));
        Assert.Equal(2, CompanionSchedule.SelectPose(poses, 0, new(12, 0), () => .6));
        for (var i = 0; i < poses.Length; i++)
            Assert.NotEqual(poses[i].Group, poses[CompanionSchedule.SelectPose(poses, i, new(23, 0), () => .1)].Group);
    }

    [Fact]
    public void TwoGroupsAlternateAndSingleGroupDoesNotRepeatItsPose()
    {
        PoseChoice[] poses = [new(0), new(0), new(1)];
        Assert.Equal(2, CompanionSchedule.SelectPose(poses, 0, new(12, 0), () => .9));
        Assert.NotEqual(2, CompanionSchedule.SelectPose(poses, 2, new(12, 0), () => .2));
        Assert.Equal(1, CompanionSchedule.SelectPose(poses[..2], 0, new(12, 0), () => .9));
    }

    [Fact]
    public void WeightedBoundaryAndInvalidWeightsAreHandled()
    {
        Assert.Equal(0, CompanionSchedule.WeightedIndex([1, 3], 0));
        Assert.Equal(1, CompanionSchedule.WeightedIndex([1, 3], .25));
        Assert.Equal(1, CompanionSchedule.WeightedIndex([1, 3], 1));
        Assert.Throws<ArgumentException>(() => CompanionSchedule.WeightedIndex([0, double.NaN], .5));
    }
}
