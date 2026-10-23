using Rex.Media.AppCore.Library;
using Rex.Media.Library;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    private void UpdateLibrarySearchHint()
    {
        var hint = _librarySource switch
        {
            LibrarySource.Movies => "Search titles, genres, actors, directors or writers",
            LibrarySource.TvShows => "Search shows, genres, actors or creators",
            LibrarySource.Audiobooks or LibrarySource.Authors => "Search books, authors or chapters",
            LibrarySource.Podcasts => "Search podcasts, shows or episodes",
            _ => "Search the library",
        };
        LibrarySearch.PlaceholderText = hint;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(LibrarySearch, hint);
    }

    private IReadOnlyList<LibraryGroup> Groups(LibrarySource source, IReadOnlyList<LibraryEntry> entries) => source switch
    {
        LibrarySource.Albums => LibraryViews.Albums(entries),
        LibrarySource.Artists => LibraryViews.Artists(entries),
        LibrarySource.Genres => LibraryViews.Genres(entries, entry => entry.Album is { } album
            ? MetadataAlbum(entry.FiledArtist, album)?.Genres ?? [] : []),
        LibrarySource.Authors => ListeningViews.Authors(Listening().Audiobooks),
        LibrarySource.TvShows => LibraryViews.TvShows(entries),
        _ => [],
    };

    private IReadOnlyList<LibraryGroup> SearchGroups(LibrarySource source, IReadOnlyList<LibraryEntry> entries) => source switch
    {
        LibrarySource.Audiobooks => Listening().Audiobooks,
        LibrarySource.Podcasts => Listening().Podcasts,
        _ => Groups(source, entries),
    };

    /// <summary>Downloaded facts that make library search understand genres, cast, creators and book credits.</summary>
    private IEnumerable<string?> SearchMetadata(LibraryEntry entry)
    {
        if (entry.Kind == LibraryKind.Video)
        {
            if (LibraryViews.EpisodeOf(entry) is { } episode && ShowFacts(episode.Series) is { } show)
            {
                return [.. show.Genres, .. show.Cast.Select(member => member.Name), .. show.Creators];
            }

            if (MetadataMovie(entry) is { } movie)
            {
                return [.. movie.Genres, .. movie.Cast.Select(member => member.Name), .. movie.Directors, .. movie.Writers];
            }
        }

        var book = Listening().Audiobooks.FirstOrDefault(candidate => candidate.Entries.Any(part =>
            string.Equals(part.Path, entry.Path, StringComparison.OrdinalIgnoreCase)));
        var facts = book is null ? null : MetadataBook(book);
        return book is null ? [] : [book.Name, book.Detail, facts?.Author];
    }

    private bool GroupMatches(LibraryGroup group, string search) =>
        new[] { group.Name, group.Detail }.Concat(group.Entries.SelectMany(SearchMetadata))
            .Any(value => value?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true);
}
