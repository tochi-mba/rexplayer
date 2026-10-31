using Rex.Media.Settings;

namespace Rex.Media.AppCore.Player;

/// <summary>
/// An app the visualisations could listen to: its process, its program's name (what is remembered),
/// the title of its window (what the user recognises), and whether and how loud it plays now.
/// </summary>
public readonly record struct SoundApp(uint ProcessId, string Name, string Title, bool Playing, float Peak)
{
    /// <summary>What the menu and the chip call it: its window's title when short enough, else its program's name.</summary>
    public string Label => Title.Length is > 0 and <= 40 ? Title : ListenChooser.Pretty(Name);
}

/// <summary>What the chip over the stage says while the visualisations listen elsewhere.</summary>
public enum ListenState
{
    /// <summary>Listening, and the sound is coming.</summary>
    Listening,

    /// <summary>Listening, but the app has gone quiet.</summary>
    Quiet,

    /// <summary>The remembered app is not running; listening starts when it does.</summary>
    Waiting,

    /// <summary>rexplayer is playing, so its own sound is drawn until it stops.</summary>
    PlayerTookOver,
}

/// <summary>
/// The "Listen to" choice for the visualisations (AU-25): which apps to offer and in what order, how
/// a remembered app is found again, and what the chip over the stage says. Apps are remembered by
/// their program's name, never their process, so the choice survives the app and rexplayer restarting.
/// </summary>
public static class ListenChooser
{
    /// <summary>
    /// The apps to offer: those with sound on the default output, one per program (its loudest
    /// process), never rexplayer itself, playing ones first and louder before quieter.
    /// </summary>
    public static IReadOnlyList<SoundApp> Offered(IEnumerable<SoundApp> sessions, uint ownProcess)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        return [.. sessions
            .Where(app => app.ProcessId != ownProcess && app.Name.Length > 0)
            .GroupBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(app => app.Playing).ThenByDescending(app => app.Peak).ThenByDescending(app => app.Title.Length).First())
            .OrderByDescending(app => app.Playing)
            .ThenByDescending(app => app.Peak)
            .ThenBy(app => app.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The process to listen to for the remembered program <paramref name="name"/>: one of its
    /// processes that has sound, else one with a window, else any; null when it is not running.
    /// </summary>
    public static SoundApp? Find(IEnumerable<SoundApp> running, string name)
    {
        ArgumentNullException.ThrowIfNull(running);
        var matches = running.Where(app => string.Equals(app.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 0
            ? null
            : matches.OrderByDescending(app => app.Playing).ThenByDescending(app => app.Peak).ThenByDescending(app => app.Title.Length > 0).First();
    }

    /// <summary>
    /// Whose sound the visualisations draw now: rexplayer's whenever it plays (its own music always
    /// comes first), else the chosen source's.
    /// </summary>
    public static bool DrawsOtherSound(ListenTo choice, bool playerPlaying) => choice != ListenTo.Player && !playerPlaying;

    /// <summary>The state the chip shows, from what is chosen, whether the app was found, whether sound comes, and whether rexplayer plays.</summary>
    public static ListenState State(ListenTo choice, bool found, bool quiet, bool playerPlaying) =>
        playerPlaying ? ListenState.PlayerTookOver
        : choice == ListenTo.App && !found ? ListenState.Waiting
        : quiet ? ListenState.Quiet
        : ListenState.Listening;

    /// <summary>The chip's words; empty while the visualisations listen to rexplayer.</summary>
    public static string Chip(ListenTo choice, string label, ListenState state)
    {
        if (choice == ListenTo.Player)
        {
            return "";
        }

        var who = choice == ListenTo.Everything ? "everything" : label;
        return state switch
        {
            ListenState.PlayerTookOver => $"Listening to rexplayer ({who} again when it stops)",
            ListenState.Waiting => $"Waiting for {who} to play",
            ListenState.Quiet => choice == ListenTo.Everything ? "Nothing is playing" : $"{label} is quiet",
            _ => $"Listening to {who}",
        };
    }

    /// <summary>A program's name as people say it: "spotify" becomes Spotify.</summary>
    public static string Pretty(string name) =>
        string.IsNullOrEmpty(name) ? "the app" : char.ToUpperInvariant(name[0]) + name[1..];
}
