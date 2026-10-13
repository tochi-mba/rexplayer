using Rex.Media.Diagnostics;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Diagnostics;

public sealed class MilestonesTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EachMomentIsOneTimingLineWithTheTimeSinceTheProcessStarted()
    {
        var log = RexLog.InMemory();
        var clock = new ManualTimeProvider(Start);
        var milestones = new Milestones(log, Start, clock: clock);

        clock.Advance(TimeSpan.FromMilliseconds(123.45));
        milestones.Reach("first-paint");
        clock.Advance(TimeSpan.FromSeconds(1));
        milestones.Reach("first-frame");

        Assert.Equal(["first-paint at 123.5 ms", "first-frame at 1123.5 ms"], log.Tail(10).Select(entry => entry.Message));
        Assert.All(log.Tail(10), entry => Assert.Equal((LogLevel.Info, Milestones.LogSource), (entry.Level, entry.Source)));
        Assert.Equal(1123.45, milestones.ElapsedMilliseconds, 3);
    }

    [Fact]
    public void WithoutAnExitConditionNothingExits()
    {
        var exits = 0;
        var milestones = new Milestones(RexLog.InMemory(), Start, exitAfter: null, exit: () => exits++);

        milestones.Reach("first-frame");
        milestones.Reach("first-frame");

        Assert.Equal(0, exits);
    }

    [Fact]
    public void TheExitComesOnceEveryAwaitedMomentHasBeenReachedOftenEnough()
    {
        var exits = 0;
        var milestones = new Milestones(RexLog.InMemory(), Start, "first-frame*2 + first-audio", () => exits++);

        milestones.Reach("first-frame");
        milestones.Reach("first-audio");
        milestones.Reach("seek");
        Assert.Equal(0, exits);

        milestones.Reach("first-frame");
        Assert.Equal(1, exits);

        milestones.Reach("first-frame");
        milestones.Reach("first-audio");
        Assert.Equal(1, exits);
    }

    [Fact]
    public void WithoutAnExitActionReachingTheMomentDoesNothingMore()
    {
        var log = RexLog.InMemory();
        var milestones = new Milestones(log, Start, "interactive");

        milestones.Reach("interactive");
        milestones.Reach("interactive");

        Assert.Equal(2, log.Tail(10).Count);
    }

    [Theory]
    [InlineData("interactive", "interactive=1")]
    [InlineData(" first-frame * 3 ", "first-frame=3")]
    [InlineData("first-frame+first-audio*2", "first-audio=2;first-frame=1")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    [InlineData("first-frame*0", "")]
    [InlineData("first-frame*x", "")]
    [InlineData("first-frame*-1", "")]
    [InlineData("*2", "")]
    [InlineData("a*2*3", "")]
    [InlineData("first-frame+", "")]
    public void ExitConditionsAreReadStrictly(string? text, string expected)
    {
        var parsed = Milestones.ParseExitAfter(text);

        Assert.Equal(expected, string.Join(";", parsed.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}")));
    }

    [Fact]
    public void AMistypedConditionNeverExits()
    {
        var exits = 0;
        var milestones = new Milestones(RexLog.InMemory(), Start, "first-frame*zero", () => exits++);

        milestones.Reach("first-frame");

        Assert.Equal(0, exits);
    }

    [Fact]
    public void BadArgumentsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new Milestones(null!, Start));
        var milestones = new Milestones(RexLog.InMemory(), Start);
        Assert.ThrowsAny<ArgumentException>(() => milestones.Reach(""));
        Assert.Equal("open at 0.0 ms", Milestones.Format("open", 0.04));
    }
}
