using Rex.Media.AppCore.Player;

namespace Rex.Media.Tests.AppCore;

/// <summary>The window closes every part in order, once, and one broken part never stops the rest.</summary>
public sealed class WindowTeardownTests
{
    [Fact]
    public void Every_step_runs_in_order_and_a_failure_is_reported_not_thrown()
    {
        var ran = new List<string>();
        var failed = new List<string>();
        var teardown = new WindowTeardown((name, ex) => failed.Add(name + ": " + ex.Message));
        Assert.False(teardown.Started);

        var failures = teardown.Run(
        [
            ("timers", () => ran.Add("timers")),
            ("presenter", () => throw new InvalidOperationException("device lost")),
            ("player", () => ran.Add("player")),
        ]);

        Assert.True(teardown.Started);
        Assert.Equal(1, failures);
        Assert.Equal(["timers", "player"], ran);
        Assert.Equal(["presenter: device lost"], failed);
    }

    [Fact]
    public void A_second_close_does_nothing()
    {
        var count = 0;
        var teardown = new WindowTeardown((_, _) => { });
        Assert.Equal(0, teardown.Run([("count", () => count++)]));
        Assert.Equal(-1, teardown.Run([("count", () => count++)]));
        Assert.Equal(1, count);
    }
}
