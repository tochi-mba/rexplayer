using Rex.Media.AppCore.Player;

namespace Rex.Media.Tests.AppCore;

/// <summary>The window's repeating clocks run only while they have work, so an idle window does not wake every second.</summary>
public sealed class WindowClocksTests
{
    [Fact]
    public void An_idle_window_with_the_library_showing_runs_no_clock()
    {
        // Before: the resume clock (every 5 s), the sleep clock (every second) and, once the
        // launch settled, the transcription clock (every second) all ran from the start: 132
        // wake-ups a minute with nothing open, no sleep timer and nothing to transcribe.
        Assert.Equal(132.0, PerMinute(WindowClocks.ResumeEvery) + PerMinute(WindowClocks.SleepEvery) + PerMinute(WindowClocks.TranscriptionEvery));

        Assert.Equal(0.0, WindowClocks.TicksPerMinute(itemOpen: false, sleepTimerSet: false, workerRunning: false, workWanted: false));
    }

    [Fact]
    public void Each_clock_runs_while_it_has_work()
    {
        Assert.Equal(12.0, WindowClocks.TicksPerMinute(itemOpen: true, sleepTimerSet: false, workerRunning: false, workWanted: false));
        Assert.Equal(72.0, WindowClocks.TicksPerMinute(itemOpen: true, sleepTimerSet: true, workerRunning: false, workWanted: false));
        Assert.Equal(60.0, WindowClocks.TicksPerMinute(itemOpen: false, sleepTimerSet: false, workerRunning: true, workWanted: false));
        Assert.Equal(60.0, WindowClocks.TicksPerMinute(itemOpen: false, sleepTimerSet: false, workerRunning: false, workWanted: true));
        Assert.Equal(132.0, WindowClocks.TicksPerMinute(itemOpen: true, sleepTimerSet: true, workerRunning: true, workWanted: true));
    }

    [Fact]
    public void A_sleep_timer_runs_its_clock_even_with_nothing_open()
    {
        Assert.True(WindowClocks.Sleep(sleepTimerSet: true));
        Assert.False(WindowClocks.Sleep(sleepTimerSet: false));
        Assert.True(WindowClocks.Resume(itemOpen: true));
        Assert.False(WindowClocks.Resume(itemOpen: false));
        Assert.True(WindowClocks.Transcription(workerRunning: true, workWanted: false));
        Assert.False(WindowClocks.Transcription(workerRunning: false, workWanted: false));
    }

    private static double PerMinute(TimeSpan every) => TimeSpan.FromMinutes(1) / every;
}
