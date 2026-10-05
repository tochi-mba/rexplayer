using Rex.Media.Primitives;

namespace Rex.Media.Tests.Primitives;

public sealed class RationalTests
{
    [Fact]
    public void AFractionIsStoredInLowestTermsWithAPositiveDenominator()
    {
        var value = new Rational(6, -4);

        Assert.Equal(-3, value.Numerator);
        Assert.Equal(2, value.Denominator);
        Assert.Equal(-1.5, value.Value);
        Assert.Equal("-3/2", value.ToString());
    }

    [Fact]
    public void ZeroOverAnythingIsZeroOverOne()
    {
        var value = new Rational(0, 7);

        Assert.Equal(0, value.Numerator);
        Assert.Equal(1, value.Denominator);
    }

    [Fact]
    public void AZeroDenominatorIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rational(1, 0));
    }

    [Theory]
    [InlineData(90_000, 1, 90_000, 1, 10_000_000, 10_000_000)]
    [InlineData(1, 1, 3, 1, 1, 0)]
    [InlineData(2, 1, 3, 1, 1, 1)]
    [InlineData(-2, 1, 3, 1, 1, -1)]
    [InlineData(-1, 1, 3, 1, 1, 0)]
    [InlineData(1, 1001, 30_000, 1, 10_000_000, 333_667)]
    public void RescaleRoundsToTheNearestUnitWithHalvesAwayFromZero(long value, long fromNum, long fromDen, long toNum, long toDen, long expected)
    {
        Assert.Equal(expected, Rational.Rescale(value, new Rational(fromNum, fromDen), new Rational(toNum, toDen)));
    }

    [Fact]
    public void RescaleHandlesANegativeTarget()
    {
        Assert.Equal(-4, Rational.Rescale(2, new Rational(2, 1), new Rational(-1, 1)));
    }

    [Fact]
    public void RescaleSurvivesAVeryLongNinetyKilohertzClock()
    {
        var thirtyDays = 90_000L * 60 * 60 * 24 * 30;

        var ticks = Rational.Rescale(thirtyDays, new Rational(1, 90_000), new Rational(1, 10_000_000));

        Assert.Equal(10_000_000L * 60 * 60 * 24 * 30, ticks);
    }

    [Fact]
    public void RescaleRefusesAZeroTarget()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rational.Rescale(1, new Rational(1, 1), default));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rational.Rescale(1, default, new Rational(1, 1)));
    }

    [Fact]
    public void RescaleThrowsWhenTheResultDoesNotFitALong()
    {
        Assert.Throws<OverflowException>(() => Rational.Rescale(long.MaxValue, new Rational(2, 1), new Rational(1, 1)));
    }
}
