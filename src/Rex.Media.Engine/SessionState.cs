namespace Rex.Media.Engine;

/// <summary>Where a media session is in its life.</summary>
public enum SessionState
{
    Idle,
    Opening,
    Ready,
    Playing,
    Paused,
    Buffering,
    Seeking,
    Ended,
    Stopping,
    Faulted,
}

/// <summary>
/// The legal transitions (ADR-007), kept as data so one test can check every pair. A move the table
/// does not allow is a bug in the engine, so <see cref="Ensure"/> throws rather than drifting into a
/// state nobody designed.
/// </summary>
public static class SessionStateMachine
{
    private static readonly Dictionary<SessionState, SessionState[]> Allowed = new()
    {
        [SessionState.Idle] = [SessionState.Opening],
        [SessionState.Opening] = [SessionState.Ready, SessionState.Playing, SessionState.Stopping, SessionState.Faulted],
        [SessionState.Ready] = [SessionState.Playing, SessionState.Paused, SessionState.Buffering, SessionState.Seeking, SessionState.Stopping, SessionState.Faulted],
        [SessionState.Playing] = [SessionState.Paused, SessionState.Buffering, SessionState.Seeking, SessionState.Ended, SessionState.Stopping, SessionState.Faulted],
        [SessionState.Paused] = [SessionState.Playing, SessionState.Seeking, SessionState.Ended, SessionState.Stopping, SessionState.Faulted],
        [SessionState.Buffering] = [SessionState.Playing, SessionState.Paused, SessionState.Seeking, SessionState.Stopping, SessionState.Faulted],
        [SessionState.Seeking] = [SessionState.Playing, SessionState.Paused, SessionState.Buffering, SessionState.Seeking, SessionState.Ended, SessionState.Stopping, SessionState.Faulted],
        [SessionState.Ended] = [SessionState.Playing, SessionState.Seeking, SessionState.Stopping, SessionState.Faulted],
        [SessionState.Stopping] = [SessionState.Idle],
        [SessionState.Faulted] = [SessionState.Stopping],
    };

    public static bool CanMove(SessionState from, SessionState to) => Allowed[from].Contains(to);

    public static void Ensure(SessionState from, SessionState to)
    {
        if (!CanMove(from, to))
        {
            throw new InvalidOperationException($"A media session cannot go from {from} to {to}.");
        }
    }
}
