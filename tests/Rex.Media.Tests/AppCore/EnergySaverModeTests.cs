using Rex.Media.AppCore.Performance;
using Rex.Media.Settings;

namespace Rex.Media.Tests.AppCore;

public sealed class EnergySaverModeTests
{
    private static readonly DeviceFacts Capable = new() { Processors = 8, MemoryBytes = 32L * 1024 * 1024 * 1024, Graphics = GraphicsKind.Discrete };

    [Theory]
    [InlineData(PerformanceChoice.BestQuality, "Best quality")]
    [InlineData(PerformanceChoice.Balanced, "Balanced")]
    [InlineData(PerformanceChoice.Fastest, "Fastest")]
    public void EnergySaverPutsAutomaticInPlaceOfTheChoiceAndGivesItBackWithANoticeEachWay(PerformanceChoice chosen, string name)
    {
        var start = EnergySaverMode.Start(chosen, allowed: true);
        Assert.False(start.Overriding);
        Assert.Equal(chosen, start.Effective);

        var (on, started) = start.PowerChanged(true);
        Assert.True(on.Overriding);
        Assert.Equal(PerformanceChoice.Automatic, on.Effective);
        Assert.Equal(chosen, on.Chosen);
        Assert.Equal(EnergySaverMode.StartedNotice, started);

        var (off, ended) = on.PowerChanged(false);
        Assert.False(off.Overriding);
        Assert.Equal(chosen, off.Effective);
        Assert.Equal("Energy Saver is off, back to " + name, ended);
    }

    [Fact]
    public void TheSameStatusAgainChangesNothingAndSaysNothing()
    {
        var start = EnergySaverMode.Start(PerformanceChoice.BestQuality, allowed: true);
        Assert.Equal((start, (string?)null), start.PowerChanged(false));
        var (on, _) = start.PowerChanged(true);
        Assert.Equal((on, (string?)null), on.PowerChanged(true));
    }

    [Fact]
    public void AutomaticAlreadyChosenOrTheOverrideNotAllowedChangesNothingAndSaysNothing()
    {
        foreach (var start in new[] { EnergySaverMode.Start(PerformanceChoice.Automatic, allowed: true), EnergySaverMode.Start(PerformanceChoice.Fastest, allowed: false) })
        {
            var (on, started) = start.PowerChanged(true);
            Assert.False(on.Overriding);
            Assert.Equal(start.Chosen, on.Effective);
            Assert.Null(started);
            var (off, ended) = on.PowerChanged(false);
            Assert.False(off.Overriding);
            Assert.Null(ended);
        }
    }

    [Fact]
    public void AModeChosenWhileOverridingWinsUntilEnergySaverChangesAgain()
    {
        var (on, _) = EnergySaverMode.Start(PerformanceChoice.BestQuality, allowed: true).PowerChanged(true);

        // Saving the same choice keeps the override; a new choice is what the user wants now.
        Assert.True(on.WithChoice(PerformanceChoice.BestQuality, allowed: true).Overriding);
        var held = on.WithChoice(PerformanceChoice.Balanced, allowed: true);
        Assert.True(held.Held);
        Assert.False(held.Overriding);
        Assert.Equal(PerformanceChoice.Balanced, held.Effective);

        // Energy saver going off ends nothing (the override was already off), so it is not announced.
        var (off, ended) = held.PowerChanged(false);
        Assert.Null(ended);
        Assert.False(off.Held);
        Assert.Equal(PerformanceChoice.Balanced, off.Effective);

        // Back on, the held choice gives way again.
        var (again, started) = off.PowerChanged(true);
        Assert.Equal(EnergySaverMode.StartedNotice, started);
        Assert.Equal(PerformanceChoice.Automatic, again.Effective);

        // Turning the preference off while overriding ends the override quietly and holds nothing.
        var disallowed = on.WithChoice(PerformanceChoice.BestQuality, allowed: false);
        Assert.False(disallowed.Overriding);
        Assert.False(disallowed.Held);
        Assert.Equal(PerformanceChoice.BestQuality, disallowed.Effective);
    }

    [Fact]
    public void DescribeSaysWhyAutomaticIsInEffect()
    {
        var start = EnergySaverMode.Start(PerformanceChoice.Fastest, allowed: true);
        Assert.Equal("Fastest, as chosen.", start.Describe(Capable));
        var (on, _) = start.PowerChanged(true);
        var described = on.Describe(Capable);
        Assert.StartsWith("Automatic, because Energy Saver is on. Automatic: ", described, StringComparison.Ordinal);
        Assert.Equal(DeviceProfile.Describe(PerformanceChoice.Automatic, Capable), described["Automatic, because Energy Saver is on. ".Length..]);
    }
}
