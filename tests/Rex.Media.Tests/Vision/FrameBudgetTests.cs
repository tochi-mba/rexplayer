using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class FrameBudgetTests
{
    private static FrameBudget Measured(params double[] milliseconds)
    {
        var budget = new FrameBudget();
        budget.Record(TimeSpan.FromSeconds(5));
        foreach (var value in milliseconds)
        {
            budget.Record(TimeSpan.FromMilliseconds(value));
        }

        return budget;
    }

    [Fact]
    public void TheFirstCallIsWarmUpAndAnUnmeasuredModelIsOnDemand()
    {
        var budget = Measured();

        Assert.Equal(0, budget.Samples);
        Assert.Null(budget.Slow);
        Assert.Equal(ModelUse.OnDemand, budget.Decide(24));
    }

    [Theory]
    [InlineData(10, 24, ModelUse.PerFrame)]
    [InlineData(20.8, 24, ModelUse.PerFrame)]
    [InlineData(21, 24, ModelUse.OnDemand)]
    [InlineData(12, 60, ModelUse.OnDemand)]
    [InlineData(1900, 24, ModelUse.OnDemand)]
    [InlineData(2100, 24, ModelUse.Unsuitable)]
    public void HalfAFrameIsTheBudgetAndTwoSecondsTheLimit(double milliseconds, double framesPerSecond, ModelUse expected)
    {
        Assert.Equal(expected, Measured(milliseconds, milliseconds).Decide(framesPerSecond));
    }

    [Fact]
    public void TheSlowCallsDecideNotTheAverage()
    {
        // Of ten calls, the ninth slowest decides: two slow calls count, one alone does not.
        Assert.Equal(300, Measured(5, 5, 5, 5, 5, 5, 5, 5, 300, 300).Slow);
        Assert.Equal(5, Measured(5, 5, 5, 5, 5, 5, 5, 5, 5, 300).Slow);
    }

    [Fact]
    public void OnlyRecentCallsCount()
    {
        var budget = Measured([.. Enumerable.Repeat(900.0, 10), .. Enumerable.Repeat(5.0, FrameBudget.Window)]);

        Assert.Equal(FrameBudget.Window, budget.Samples);
        Assert.Equal(ModelUse.PerFrame, budget.Decide(30));
    }

    [Fact]
    public void BadInputsAreRefused()
    {
        var budget = new FrameBudget();

        Assert.Throws<ArgumentOutOfRangeException>(() => budget.Record(TimeSpan.FromMilliseconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.Decide(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.Decide(double.NaN));
    }
}
