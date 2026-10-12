using System.Globalization;

namespace Rex.Media.AppCore.Library;

/// <summary>What a confirmation dialog asks: its title and the words under it.</summary>
public sealed record Prompt(string Title, string Body);

/// <summary>
/// The words of the library's menus and confirmations (LIB-05), so every action reads the same
/// whether one item or many are chosen: "Play", "Play 3 items"; "Delete Sungba.mp3?", "Delete 12
/// files?" with the first few named. Deleting a file is never permanent: it goes to the Recycle
/// Bin, and the confirmation says so.
/// </summary>
public static class LibraryActions
{
    /// <summary>How many names a confirmation lists before "and N more".</summary>
    public const int NamedInPrompt = 5;

    /// <summary>"1 item", "3 items".</summary>
    public static string Count(int count, string noun)
    {
        ArgumentNullException.ThrowIfNull(noun);
        return count == 1 ? "1 " + noun : count.ToString(CultureInfo.CurrentCulture) + " " + noun + "s";
    }

    /// <summary>A menu action's words: just the verb for one item, the count after it for many ("Play next", "Play 3 items next").</summary>
    public static string MenuText(string verb, int count, string noun = "item", string? tail = null)
    {
        ArgumentNullException.ThrowIfNull(verb);
        var text = count <= 1 ? verb : verb + " " + Count(count, noun);
        return tail is null ? text : text + " " + tail;
    }

    /// <summary>The confirmation before files go to the Recycle Bin.</summary>
    public static Prompt DeletePrompt(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var title = names.Count == 1 ? $"Delete {names[0]}?" : $"Delete {Count(names.Count, "file")}?";
        return new Prompt(title, Named(names) + (names.Count == 1 ? "It goes" : "They go") + " to the Recycle Bin, where it can be brought back, and the library forgets " + (names.Count == 1 ? "it." : "them."));
    }

    /// <summary>The confirmation before the library stops showing files that stay on the disk.</summary>
    public static Prompt RemovePrompt(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var title = names.Count == 1 ? $"Remove {names[0]} from the library?" : $"Remove {Count(names.Count, "item")} from the library?";
        return new Prompt(title, Named(names) + (names.Count == 1 ? "The file stays" : "The files stay") + " where " + (names.Count == 1 ? "it is" : "they are") + "; the library stops showing " + (names.Count == 1 ? "it" : "them") + " until the folder is added again.");
    }

    /// <summary>The confirmation before named playlists go.</summary>
    public static Prompt DeletePlaylistPrompt(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var title = names.Count == 1 ? $"Delete {names[0]}?" : $"Delete {Count(names.Count, "playlist")}?";
        return new Prompt(title, Named(names) + (names.Count == 1 ? "The playlist goes" : "The playlists go") + "; the files in " + (names.Count == 1 ? "it stay" : "them stay") + " where they are.");
    }

    /// <summary>The first few names, one a line, then "and N more"; nothing for one name (the title has it).</summary>
    private static string Named(IReadOnlyList<string> names)
    {
        if (names.Count <= 1)
        {
            return "";
        }

        var shown = names.Take(NamedInPrompt).ToList();
        var rest = names.Count - shown.Count;
        return string.Join("\n", shown) + (rest > 0 ? "\nand " + rest.ToString(CultureInfo.CurrentCulture) + " more" : "") + "\n\n";
    }
}
