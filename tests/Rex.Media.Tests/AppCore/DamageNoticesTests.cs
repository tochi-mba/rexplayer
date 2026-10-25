using Rex.Media.AppCore.Player;

namespace Rex.Media.Tests.AppCore;

public sealed class DamageNoticesTests
{
    [Fact]
    public void ADamagedFileSaysSoPlainly() =>
        Assert.Equal("This file is damaged. Playing what can be read.", DamageNotices.Damaged);

    [Theory]
    [InlineData(0.2, "Skipped 1 second that could not be read.")]
    [InlineData(1.4, "Skipped 1 second that could not be read.")]
    [InlineData(12, "Skipped 12 seconds that could not be read.")]
    [InlineData(119, "Skipped 119 seconds that could not be read.")]
    [InlineData(170, "Skipped 3 minutes that could not be read.")]
    public void WhatWasSkippedIsSaidInSecondsOrMinutes(double seconds, string expected) =>
        Assert.Equal(expected, DamageNotices.Skipped(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void AFileThatCannotPlaySaysWhyInASentence()
    {
        Assert.Equal("This file could not be played: The MP4 file has no movie header.", DamageNotices.Failed(null, "The MP4 file has no movie header."));
        Assert.Equal("Sungba could not be played: Reading the media failed: hostile.", DamageNotices.Failed("Sungba", "Reading the media failed: IndexOutOfRangeException: hostile"));
        Assert.Equal("This file could not be played: First sentence.", DamageNotices.Failed(" ", "First sentence. And the rest, for the log."));
    }

    [Fact]
    public void AReasonIsKeptShort()
    {
        var reason = new string('x', 300);

        var shown = DamageNotices.Short(reason);

        Assert.Equal(DamageNotices.LongestReason + 3, shown.Length);
        Assert.EndsWith("...", shown, StringComparison.Ordinal);
        Assert.Equal("it could not be read.", DamageNotices.Short(null));
        Assert.Equal("it could not be read.", DamageNotices.Short("NullReferenceException: "));
    }
}
