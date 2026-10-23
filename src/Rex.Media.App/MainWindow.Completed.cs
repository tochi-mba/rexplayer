using Rex.Media.AppCore.Library;
using Rex.Media.Library;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// Media known to have reached its end. A resume point always wins over a play count, so a
    /// replay left part-way does not look completed. Audiobooks use their explicit whole-book
    /// state rather than treating one completed chapter as a completed book.
    /// </summary>
    private (IReadOnlyList<LibraryGroup> Books, IReadOnlyList<LibraryEntry> Videos,
        IReadOnlyList<LibraryEntry> Podcasts, IReadOnlyList<LibraryEntry> Audio) CompletedMedia(IReadOnlyList<LibraryEntry> entries)
    {
        var listening = Listening();
        var bookParts = listening.Audiobooks.SelectMany(book => book.Entries).Select(entry => entry.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var podcastEpisodes = listening.Podcasts.SelectMany(show => show.Entries).Select(entry => entry.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var finished = entries.Where(entry => LibraryArrangement.StateOf(entry, _player.LeftAt) == WatchState.Finished).ToList();
        return (
            listening.Audiobooks.Where(Audiobooks.Finished).ToList(),
            finished.Where(entry => entry.Kind == LibraryKind.Video).ToList(),
            finished.Where(entry => podcastEpisodes.Contains(entry.Path)).ToList(),
            finished.Where(entry => entry.Kind == LibraryKind.Music && !bookParts.Contains(entry.Path) && !podcastEpisodes.Contains(entry.Path)).ToList());
    }

    private (string Kicker, string Title, string Subtitle, List<LibraryRowGroup> Sections, bool GroupsView) CompletedView(
        IReadOnlyList<LibraryEntry> entries)
    {
        var completed = CompletedMedia(entries);
        var sections = new List<LibraryRowGroup>
        {
            new("Audiobooks", completed.Books.Select(BookRow)),
            new("Movies and episodes", completed.Videos.Select(EntryRow)),
            new("Podcast episodes", completed.Podcasts.Select(PodcastEpisodeRow)),
            new("Music and recordings", completed.Audio.Select(EntryRow)),
        };
        sections.RemoveAll(section => section.Count == 0);
        var count = completed.Books.Count + completed.Videos.Count + completed.Podcasts.Count + completed.Audio.Count;
        return ("LIBRARY", "Completed", LibraryActions.Count(count, "item"), sections, false);
    }

    private IReadOnlyList<(string Title, LibrarySource Source, IReadOnlyList<LibraryRow> Rows, bool Wide)> CompletedShelves(
        IReadOnlyList<LibraryEntry> entries)
    {
        var completed = CompletedMedia(entries);
        return [
            ("Finished audiobooks", LibrarySource.Audiobooks, [.. completed.Books.Select(BookRow)], false),
            ("Watched movies and episodes", LibrarySource.Completed, [.. completed.Videos.Select(EntryRow)], true),
            ("Played podcast episodes", LibrarySource.Podcasts, [.. completed.Podcasts.Select(PodcastEpisodeRow)], false),
            ("Played music and recordings", LibrarySource.Completed, [.. completed.Audio.Select(EntryRow)], false),
        ];
    }
}
