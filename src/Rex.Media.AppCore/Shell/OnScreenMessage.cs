namespace Rex.Media.AppCore.Shell;

/// <summary>
/// How a message over the picture behaves. A problem (something could not be done) is always shown,
/// even with on-screen messages turned off, because otherwise it reaches only the log; and every
/// message stays long enough to be read, longer for a longer one.
/// </summary>
public static class OnScreenMessage
{
    /// <summary>How long a short message stays.</summary>
    public static readonly TimeSpan Shortest = TimeSpan.FromSeconds(1.5);

    /// <summary>How long a problem stays, at the least.</summary>
    public static readonly TimeSpan ShortestProblem = TimeSpan.FromSeconds(4);

    /// <summary>How long any message stays, at the most.</summary>
    public static readonly TimeSpan Longest = TimeSpan.FromSeconds(8);

    // About fifteen characters a second: a comfortable reading pace for a line glanced at.
    private const double SecondsPerCharacter = 1 / 15.0;

    private static readonly string[] ProblemWords =
    [
        "could not", "couldn't", "cannot", "can't", "failed", "not found", "no longer",
        "missing", "unavailable", "unable", "not shown", "not supported",
    ];

    /// <summary>Whether <paramref name="text"/> says something could not be done.</summary>
    public static bool IsProblem(string text) =>
        ProblemWords.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether to show <paramref name="text"/>, given the on-screen messages setting.</summary>
    public static bool Shows(string text, bool messagesOn) => messagesOn || IsProblem(text);

    /// <summary>How long to show <paramref name="text"/>.</summary>
    public static TimeSpan Duration(string text)
    {
        var reading = TimeSpan.FromSeconds(text.Length * SecondsPerCharacter);
        var shortest = IsProblem(text) ? ShortestProblem : Shortest;
        return reading < shortest ? shortest : reading > Longest ? Longest : reading;
    }
}
