using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;

namespace Rex.Media.AppCore.Shell;

/// <summary>A line of rexplayer's jump list: what it says and what rexplayer is started with when it is clicked.</summary>
public sealed record JumpListEntry(string Title, string Arguments);

/// <summary>Everything the jump list shows: recent media, then the tasks.</summary>
public sealed record JumpListContent(IReadOnlyList<JumpListEntry> Recent, IReadOnlyList<JumpListEntry> Tasks);

/// <summary>
/// rexplayer's jump list (WIN-07, LIB-07): right-click the taskbar button or the Start menu entry for
/// the media played most recently and three tasks. A click starts rexplayer with the media or a
/// command, and a launch hands both to the window already open, so a click plays in it rather than
/// opening another. Items the user removed from the list stay off it; the user's own pins are kept
/// by Windows. With history off there are no recent items and nothing to resume.
/// </summary>
public static class JumpListPlan
{
    /// <summary>The most recent items shown, at most; Windows may allow fewer.</summary>
    public const int MaxRecent = 10;

    /// <summary>What a launch's arguments start with to run a command instead of opening files.</summary>
    public const string CommandSwitch = "--command";

    /// <summary>The commands a launch may run: the jump list's tasks.</summary>
    public static IReadOnlyList<(string Command, string Title)> TaskCommands { get; } =
    [
        (CommandCatalog.OpenFile, "Open files"),
        (CommandCatalog.OpenLocation, "Open a location"),
        (CommandCatalog.Resume, "Go back to where I left off"),
    ];

    /// <summary>
    /// The jump list from the <paramref name="recent"/> media (newest first), leaving out what is not
    /// there any more, what the user removed and anything that cannot be passed on a command line.
    /// </summary>
    /// <param name="recent">What played recently, newest first.</param>
    /// <param name="keepHistory">Whether rexplayer remembers what played; when not, the list has no recent items and no Resume.</param>
    /// <param name="removed">The arguments of items the user removed from the list.</param>
    /// <param name="slots">How many recent items Windows has room for.</param>
    /// <param name="exists">Whether a file is still there (web addresses always are).</param>
    public static JumpListContent Plan(IReadOnlyList<string> recent, bool keepHistory, IReadOnlySet<string> removed, int slots, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(exists);
        var items = new List<JumpListEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (keepHistory)
        {
            foreach (var location in recent)
            {
                if (items.Count >= Math.Min(slots, MaxRecent))
                {
                    break;
                }

                if (QuoteArgument(location) is not { } arguments || removed.Contains(arguments) || !seen.Add(location)
                    || (!IsWebAddress(location) && !exists(location)))
                {
                    continue;
                }

                items.Add(new JumpListEntry(PlaylistItem.TitleOf(location), arguments));
            }
        }

        var tasks = TaskCommands
            .Where(task => keepHistory || task.Command != CommandCatalog.Resume)
            .Select(task => new JumpListEntry(task.Title, CommandSwitch + " " + task.Command))
            .ToList();
        return new JumpListContent(items, tasks);
    }

    /// <summary>
    /// <paramref name="location"/> as one command-line argument, quoted the way Windows splits them
    /// back; null for one that cannot be: empty, relative, or holding a quote or a line break.
    /// </summary>
    public static string? QuoteArgument(string location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (location.Length == 0 || location.AsSpan().IndexOfAny("\"\r\n") >= 0 || (!IsWebAddress(location) && !IsWindowsPath(location)))
        {
            return null;
        }

        // Backslashes count as escapes only before a quote: the closing one, so trailing ones are doubled.
        var trailing = location.Length - location.TrimEnd('\\').Length;
        return "\"" + location + new string('\\', trailing) + "\"";
    }

    /// <summary>
    /// What a launch's arguments ask for: files to open, or one of the <see cref="TaskCommands"/>
    /// after <see cref="CommandSwitch"/>. Anything after the command, or a command that is not one of
    /// them, is ignored.
    /// </summary>
    public static (IReadOnlyList<string> Files, string? Command) ParseLaunch(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var at = -1;
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i] == CommandSwitch)
            {
                at = i;
                break;
            }
        }

        // A blank argument (a shortcut or script that passes "") names no file; made absolute it
        // would stop the launch before its window showed.
        if (at < 0)
        {
            return ([.. arguments.Where(argument => !string.IsNullOrWhiteSpace(argument))], null);
        }

        var command = at + 1 < arguments.Count && TaskCommands.Any(task => task.Command == arguments[at + 1]) ? arguments[at + 1] : null;
        return ([.. arguments.Take(at).Where(argument => !string.IsNullOrWhiteSpace(argument))], command);
    }

    private static bool IsWebAddress(string location) =>
        Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    /// <summary>A drive path (C:\...) or a share (\\server\...), whatever system this runs on.</summary>
    private static bool IsWindowsPath(string location) =>
        location.StartsWith(@"\\", StringComparison.Ordinal) || (location.Length >= 3 && char.IsAsciiLetter(location[0]) && location[1] == ':' && location[2] is '\\' or '/');
}
