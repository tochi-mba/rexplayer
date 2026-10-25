namespace Rex.Media.App.Tests;

public sealed partial class AppWindowTests
{
    /// <summary>
    /// A failure in the window's own code where Windows calls it back is written down with its
    /// stack, and the player carries on. Work queued from another thread and a clock's tick are
    /// called by Windows directly: an exception escaping them used to end the process with
    /// 0xC000027B without a trace. A task nobody waits for used to fail unseen.
    /// </summary>
    [Theory]
    [InlineData("queued")]
    [InlineData("clock")]
    [InlineData("task")]
    [InlineData("handler")]
    public void AFailureWhereWindowsCallsTheWindowIsWrittenDownAndThePlayerCarriesOn(string where)
    {
        using var app = AppProcess.Start(environment: new Dictionary<string, string> { ["REXPLAYER_FAKE_FAULTS"] = "1" });

        app.Fault(where);

        Wait.For(() => app.LogText.Contains($"A test's {where} failure.", StringComparison.Ordinal), "the failure to be written down", () => app.LogText);
        Assert.Contains("at Rex.Media.App.MainWindow", app.LogText, StringComparison.Ordinal);
        Assert.Equal("Idle", app.Status().State);
        Assert.Equal(0, app.Close());
    }
}
