using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class OneEuroFilterTests
{
    private const float Frame = 1 / 30f;

    [Fact]
    public void TheFirstValueIsTakenAsItIs()
    {
        var filter = new OneEuroFilter();

        Assert.Equal(0, filter.Value);
        Assert.Equal(0.7f, filter.Filter(0.7f, Frame));
        Assert.Equal(0.7f, filter.Value);
    }

    [Fact]
    public void JitterAtRestIsSmoothedAway()
    {
        var filter = new OneEuroFilter(minCutoff: 1, beta: 0);
        var random = new Random(7);
        filter.Filter(0.5f, Frame);
        var furthest = 0f;
        var rawFurthest = 0f;
        for (var i = 0; i < 300; i++)
        {
            var noisy = 0.5f + ((float)random.NextDouble() - 0.5f) * 0.1f;
            rawFurthest = Math.Max(rawFurthest, Math.Abs(noisy - 0.5f));
            furthest = Math.Max(furthest, Math.Abs(filter.Filter(noisy, Frame) - 0.5f));
        }

        // At a 1 Hz cutoff and thirty frames a second about a third of the jitter's amplitude passes.
        Assert.InRange(rawFurthest, 0.045f, 0.05f);
        Assert.InRange(furthest, 0, rawFurthest * 0.6f);
    }

    [Fact]
    public void FastMotionIsFollowedWithLittleLag()
    {
        var steady = new OneEuroFilter(minCutoff: 1, beta: 0);
        var quick = new OneEuroFilter(minCutoff: 1, beta: 20);
        steady.Filter(0, Frame);
        quick.Filter(0, Frame);
        var position = 0f;
        for (var i = 0; i < 15; i++)
        {
            // A hand crossing a third of the picture in half a second.
            position += 1f / 45;
            steady.Filter(position, Frame);
            quick.Filter(position, Frame);
        }

        var lagSteady = position - steady.Value;
        var lagQuick = position - quick.Value;
        Assert.True(lagQuick < lagSteady / 4, $"beta should cut the lag ({lagQuick} against {lagSteady}).");
        Assert.InRange(lagQuick, 0, 0.02f);
    }

    [Fact]
    public void AStepSettlesOnTheNewValue()
    {
        var filter = new OneEuroFilter(minCutoff: 2, beta: 1);
        filter.Filter(0, Frame);
        var value = 0f;
        for (var i = 0; i < 60; i++)
        {
            value = filter.Filter(1, Frame);
        }

        Assert.Equal(1, value, 2);
    }

    [Fact]
    public void ResettingStartsAgainFromTheNextValue()
    {
        var filter = new OneEuroFilter();
        filter.Filter(0.2f, Frame);
        filter.Filter(0.25f, Frame);
        filter.Reset();

        Assert.Equal(0, filter.Value);
        Assert.Equal(0.9f, filter.Filter(0.9f, Frame));
    }

    [Fact]
    public void AMissingOrBadIntervalRestartsAndABadValueIsIgnored()
    {
        var filter = new OneEuroFilter();
        filter.Filter(0.2f, Frame);
        Assert.Equal(0.8f, filter.Filter(0.8f, 0));
        Assert.Equal(0.1f, filter.Filter(0.1f, float.NaN));
        Assert.Equal(0.1f, filter.Filter(float.NaN, Frame));
        Assert.Equal(0.1f, filter.Filter(float.PositiveInfinity, Frame));
    }

    [Fact]
    public void TheParametersAreChecked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OneEuroFilter(minCutoff: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OneEuroFilter(beta: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OneEuroFilter(speedCutoff: float.NaN));
        var filter = new OneEuroFilter(0.5f, 2, 3);
        Assert.Equal((0.5f, 2f, 3f), (filter.MinCutoff, filter.Beta, filter.SpeedCutoff));
    }

    [Fact]
    public void AlphaIsTheShareOfANewSampleALowPassTakes()
    {
        // A cutoff far above the frame rate takes nearly the whole sample; one far below, almost none.
        Assert.InRange(OneEuroFilter.Alpha(1000, Frame), 0.99f, 1f);
        Assert.InRange(OneEuroFilter.Alpha(0.01f, Frame), 0, 0.01f);
    }

    [Fact]
    public void ASmootherSmoothsEveryPointAndRestartsWhenThePartIsLost()
    {
        var smoother = new LandmarkSmoother(2, minCutoff: 1, beta: 0);
        Assert.Equal(2, smoother.Points);
        var points = new Landmark[] { new(0, 0, 0, 1), new(1, 1, 1, 1) };
        smoother.Smooth(points, present: true, Frame);
        Assert.Equal(new Landmark(0, 0, 0, 1), points[0]);

        points[0] = new Landmark(1, 1, 1, 1);
        smoother.Smooth(points, present: true, Frame);
        // Smoothed: part of the way from 0 towards 1, confidence untouched.
        Assert.InRange(points[0].X, 0.01f, 0.99f);
        Assert.Equal(points[0].X, points[0].Y);
        Assert.Equal(points[0].X, points[0].Z);
        Assert.Equal(1, points[0].Confidence);

        // Lost, then found far away: the old position is forgotten.
        smoother.Smooth(points, present: false, Frame);
        smoother.Smooth(points, present: false, Frame);
        points[0] = new Landmark(0.9f, 0.9f, 0.9f, 1);
        smoother.Smooth(points, present: true, Frame);
        Assert.Equal(0.9f, points[0].X);
    }

    [Fact]
    public void ASmootherChecksItsInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LandmarkSmoother(0));
        var smoother = new LandmarkSmoother(3);
        Assert.Throws<ArgumentNullException>(() => smoother.Smooth(null!, true, Frame));
        Assert.Throws<ArgumentException>(() => smoother.Smooth(new Landmark[2], true, Frame));
    }
}
