using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class PerceptionPlanTests
{
    private static (bool Matte, bool Palm, bool Hands, bool Person, bool Pose) Of(PerceptionPlan plan) =>
        (plan.RunMatte, plan.RunPalm, plan.RunHands, plan.RunPerson, plan.RunPose);

    [Fact]
    public void HandWrittenRunsNoModelAtAll()
    {
        var plan = new PerceptionPlan { Level = PerceptionLevel.HandWritten };
        for (var i = 0; i < 5; i++)
        {
            plan.Next(0, false);
            Assert.Equal((false, false, false, false, false), Of(plan));
        }

        Assert.Equal(4, plan.Frame);
    }

    [Fact]
    public void WithNothingFollowedTheDetectorsRunOftenAndTheRestEveryPicture()
    {
        var full = new PerceptionPlan { Level = PerceptionLevel.Full };
        full.Next(0, false);
        Assert.Equal((true, true, true, true, true), Of(full));
        full.Next(0, false);
        Assert.Equal((true, true, true, false, false), Of(full));
        full.Next(0, false);
        Assert.Equal((true, true, true, true, true), Of(full));

        var light = new PerceptionPlan { Level = PerceptionLevel.Light };
        var runs = new List<(bool, bool, bool, bool, bool)>();
        for (var i = 0; i < 12; i++)
        {
            light.Next(0, false);
            runs.Add(Of(light));
        }

        // The matte and the hands every picture; the palm every other, the person every fourth, the pose every third.
        Assert.All(runs, run => Assert.True(run.Item1 && run.Item3));
        Assert.Equal(6, runs.Count(run => run.Item2));
        Assert.Equal(3, runs.Count(run => run.Item4));
        Assert.Equal(4, runs.Count(run => run.Item5));
    }

    [Fact]
    public void OnceFollowedTheDetectorsOnlySweepNowAndThen()
    {
        var plan = new PerceptionPlan { Level = PerceptionLevel.Full };
        var palms = 0;
        var people = 0;
        for (var i = 0; i < 120; i++)
        {
            plan.Next(2, true);
            palms += plan.RunPalm ? 1 : 0;
            people += plan.RunPerson ? 1 : 0;
            Assert.True(plan.RunHands);
        }

        Assert.Equal(120 / PerceptionPlan.PalmSweepEvery, palms);
        Assert.Equal(120 / PerceptionPlan.PersonSweepEvery, people);

        // One hand missing: the palm detector is back to every picture.
        plan.Next(1, true);
        Assert.True(plan.RunPalm);
    }

    [Fact]
    public void ASlowModelRunsHalfAsOftenAndAHopelessOneNever()
    {
        var plan = new PerceptionPlan { Level = PerceptionLevel.Full, FramesPerSecond = 30 };
        // The pose takes 40 ms (over half a frame at 30, under two seconds): every fourth picture instead of every other.
        plan.PoseBudget.Record(TimeSpan.FromSeconds(1));
        plan.PoseBudget.Record(TimeSpan.FromMilliseconds(40));
        plan.PoseBudget.Record(TimeSpan.FromMilliseconds(40));
        // The matte takes three seconds: never.
        plan.MatteBudget.Record(TimeSpan.FromSeconds(1));
        plan.MatteBudget.Record(TimeSpan.FromSeconds(3));
        plan.MatteBudget.Record(TimeSpan.FromSeconds(3));

        var poses = 0;
        for (var i = 0; i < 12; i++)
        {
            plan.Next(2, true);
            poses += plan.RunPose ? 1 : 0;
            Assert.False(plan.RunMatte);
        }

        Assert.Equal(3, poses);
    }

    [Fact]
    public void AnUnmeasuredModelRunsAtItsFullRate()
    {
        // Before any timing is known a model is "on demand", but it must be tried to be measured.
        var plan = new PerceptionPlan { Level = PerceptionLevel.Light };
        plan.Next(0, false);
        plan.Next(0, false);
        Assert.True(plan.RunMatte);
        Assert.True(plan.RunHands);
    }

    [Theory]
    [InlineData(PerceptionLevel.Full, 3, 6)]
    [InlineData(PerceptionLevel.Light, 4, 8)]
    public void AfterASecondWithNobodyTheDetectorsLookLessOftenUntilSomethingIsFound(PerceptionLevel level, int palmEvery, int personEvery)
    {
        var plan = new PerceptionPlan { Level = level };
        for (var i = 0; i <= PerceptionPlan.Idle; i++)
        {
            plan.Next(0, false);
        }

        var (palms, people) = (0, 0);
        for (var i = 0; i < 48; i++)
        {
            plan.Next(0, false);
            palms += plan.RunPalm ? 1 : 0;
            people += plan.RunPerson ? 1 : 0;
            // The matte goes on every picture: an empty room is still an answer.
            Assert.True(plan.RunMatte);
        }

        Assert.Equal(48 / palmEvery, palms);
        Assert.Equal(48 / personEvery, people);

        // One hand found: the idle count starts again, and the palm detector is back at its searching rate.
        plan.Next(1, false);
        var searching = 0;
        for (var i = 0; i < 4; i++)
        {
            plan.Next(1, false);
            searching += plan.RunPalm ? 1 : 0;
        }

        Assert.Equal(level == PerceptionLevel.Full ? 4 : 2, searching);

        // A body found ends the person detector's idle rate too, and a reset forgets the idle counts.
        plan.Next(0, true);
        plan.Reset();
        plan.Next(0, false);
        Assert.True(plan.RunPalm);
        Assert.True(plan.RunPerson);
    }

    [Fact]
    public void ResettingCountsAgainAndBadCountsAreRefused()
    {
        var plan = new PerceptionPlan { Level = PerceptionLevel.Full };
        plan.Next(2, true);
        plan.Next(2, true);
        plan.Reset();
        plan.Next(2, true);
        Assert.Equal(0, plan.Frame);
        Assert.True(plan.RunPalm);
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.Next(-1, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.Next(3, true));
        Assert.Equal(30, plan.FramesPerSecond);
    }
}
