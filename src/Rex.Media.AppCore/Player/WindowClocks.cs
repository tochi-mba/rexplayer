namespace Rex.Media.AppCore.Player;

/// <summary>
/// The window's repeating clocks and when each has work to do. A clock that ticks with nothing to
/// do still wakes the processor and the window's thread, so each runs only while it is needed:
/// the resume point is kept while something is open, the sleep timer moves on while it is set, and
/// transcription looks at the computer's state while there is a worker or something to hear. With
/// the library showing and nothing open, none of them ticks.
/// </summary>
public static class WindowClocks
{
    /// <summary>How often where playback is gets saved, so a crash or an update loses at most this much.</summary>
    public static readonly TimeSpan ResumeEvery = TimeSpan.FromSeconds(5);

    /// <summary>How often the sleep timer moves on and fades the sound.</summary>
    public static readonly TimeSpan SleepEvery = TimeSpan.FromSeconds(1);

    /// <summary>How often transcription reads the computer's state and hands the worker what it needs.</summary>
    public static readonly TimeSpan TranscriptionEvery = TimeSpan.FromSeconds(1);

    /// <summary>The resume point changes only while an item is open.</summary>
    public static bool Resume(bool itemOpen) => itemOpen;

    /// <summary>The sleep timer moves on only while it is set, at a time or at the end of the item.</summary>
    public static bool Sleep(bool sleepTimerSet) => sleepTimerSet;

    /// <summary>Transcription is looked after while a worker runs or something is waiting to be heard.</summary>
    public static bool Transcription(bool workerRunning, bool workWanted) => workerRunning || workWanted;

    /// <summary>How many times a minute the clocks that run wake the window, for the given state.</summary>
    public static double TicksPerMinute(bool itemOpen, bool sleepTimerSet, bool workerRunning, bool workWanted) =>
        (Resume(itemOpen) ? PerMinute(ResumeEvery) : 0)
        + (Sleep(sleepTimerSet) ? PerMinute(SleepEvery) : 0)
        + (Transcription(workerRunning, workWanted) ? PerMinute(TranscriptionEvery) : 0);

    private static double PerMinute(TimeSpan every) => TimeSpan.FromMinutes(1) / every;
}
