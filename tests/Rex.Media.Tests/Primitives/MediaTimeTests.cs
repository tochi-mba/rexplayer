using Rex.Media.Primitives;

namespace Rex.Media.Tests.Primitives;

public sealed class MediaTimeTests
{
    [Fact]
    public void SecondsMillisecondsAndTicksAgree()
    {
        Assert.Equal(MediaTime.FromSeconds(1.5), MediaTime.FromMilliseconds(1500));
        Assert.Equal(15_000_000, MediaTime.FromSeconds(1.5).Ticks);
        Assert.Equal(1.5, MediaTime.FromMilliseconds(1500).TotalSeconds);
        Assert.Equal(TimeSpan.FromSeconds(1.5), MediaTime.FromMilliseconds(1500).ToTimeSpan());
    }

    [Fact]
    public void ATimebaseValueBecomesTicks()
    {
        var time = MediaTime.FromTimebase(180_000, new Rational(1, 90_000));

        Assert.Equal(MediaTime.FromSeconds(2), time);
        Assert.Equal(180_000, time.ToTimebase(new Rational(1, 90_000)));
    }

    [Fact]
    public void SamplesRoundTripAtCommonRates()
    {
        foreach (var rate in new[] { 8000, 22_050, 44_100, 48_000, 96_000, 192_000 })
        {
            for (long sample = 0; sample < 200_000; sample += 7919)
            {
                Assert.Equal(sample, MediaTime.FromSamples(sample, rate).ToSamples(rate));
            }
        }
    }

    [Fact]
    public void SampleConversionRejectsANonPositiveRate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MediaTime.FromSamples(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MediaTime.Zero.ToSamples(-1));
    }

    [Fact]
    public void ArithmeticAndOrderingWork()
    {
        var one = MediaTime.FromSeconds(1);
        var two = MediaTime.FromSeconds(2);

        Assert.Equal(MediaTime.FromSeconds(3), one + two);
        Assert.Equal(one, two - one);
        Assert.True(one < two);
        Assert.True(two > one);
        var alsoOne = MediaTime.FromSeconds(1);
        Assert.True(one <= alsoOne);
        Assert.True(one >= alsoOne);
        Assert.False(two <= one);
        Assert.False(one >= two);
        Assert.Equal(one, MediaTime.Min(one, two));
        Assert.Equal(two, MediaTime.Max(one, two));
        Assert.Equal(one, MediaTime.Min(two, one));
        Assert.Equal(two, MediaTime.Max(two, one));
        Assert.True(one.CompareTo(two) < 0);
    }

    [Fact]
    public void AdditionThatOverflowsThrows()
    {
        Assert.Throws<OverflowException>(() => MediaTime.MaxValue + MediaTime.FromSeconds(1));
    }

    [Fact]
    public void UnknownIsNotKnown()
    {
        Assert.False(MediaTime.Unknown.IsKnown);
        Assert.True(MediaTime.Zero.IsKnown);
        Assert.Equal("unknown", MediaTime.Unknown.ToString());
        Assert.Equal("--:--", MediaTime.Unknown.ToClock());
    }

    [Theory]
    [InlineData(0, false, "0:00")]
    [InlineData(65.25, false, "1:05")]
    [InlineData(65.25, true, "1:05.250")]
    [InlineData(3725, false, "1:02:05")]
    [InlineData(-5, false, "-0:05")]
    [InlineData(360_000, false, "100:00:00")]
    public void TheClockFormatDependsOnLength(double seconds, bool milliseconds, string expected)
    {
        Assert.Equal(expected, MediaTime.FromSeconds(seconds).ToClock(milliseconds));
    }

    [Fact]
    public void ToStringIsTheClockWithMilliseconds()
    {
        Assert.Equal("0:01.500", MediaTime.FromMilliseconds(1500).ToString());
    }

    [Theory]
    [InlineData("90", 90)]
    [InlineData("12.5", 12.5)]
    [InlineData("1:30", 90)]
    [InlineData("90:00", 5400)]
    [InlineData(" 1:02:03.5 ", 3723.5)]
    [InlineData("0:00", 0)]
    public void TheClockParserReadsEveryAcceptedForm(string text, double seconds)
    {
        Assert.True(MediaTime.TryParseClock(text, out var time));
        Assert.Equal(MediaTime.FromSeconds(seconds), time);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1:2:3:4")]
    [InlineData("abc")]
    [InlineData("1:60")]
    [InlineData("1.5:00")]
    [InlineData("-5")]
    [InlineData("1:-5")]
    public void TheClockParserRejectsEverythingElse(string? text)
    {
        Assert.False(MediaTime.TryParseClock(text, out var time));
        Assert.Equal(MediaTime.Unknown, time);
    }
}
