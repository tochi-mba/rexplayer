using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Metadata;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>
/// Movies against Other videos (<see cref="MovieRules"/>): what the movie lookups said about each
/// video, read from memory only, and the person's own "This is a movie" and "Not a movie", kept with
/// their other detail choices in the library's store so rescans and cleared metadata never undo them.
/// Nothing here is stored or worked out ahead: each view asks as it is built.
/// <para>
/// Other videos sits in the sidebar between TV Shows and All videos, a plain card grid with no
/// billboard. All videos stays the whole set (Movies, TV shows and Other videos together), because
/// it is where a file is found whatever it turned out to be; Other videos only takes the clutter
/// out of Movies.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>What the lookups said about a video's title, from memory only: safe on the window's thread.</summary>
    private MovieLookup MovieLookupOf(LibraryEntry entry)
    {
        var metadata = _settings.Metadata;
        if (!metadata.Movies || entry.Kind != LibraryKind.Video || MetadataPolicy.Excludes(metadata.Excluded, entry.Path))
        {
            return MovieLookup.NotSearched;
        }

        var key = MetadataMatch.MovieKey(LibraryViews.MovieTitleOf(entry), LibraryViews.MovieYearOf(entry));
        lock (_metadataGate)
        {
            return !_metadataMovies.TryGetValue(key, out var movie) ? MovieLookup.NotSearched
                : movie.Found ? MovieLookup.Matched : MovieLookup.Unmatched;
        }
    }

    /// <summary>The section a standalone video is in now: Movies or Other videos.</summary>
    private LibrarySource SectionOf(LibraryEntry video) =>
        MovieRules.IsMovie(video, MovieLookupOf) ? LibrarySource.Movies : LibrarySource.OtherVideos;

    /// <summary>"This is a movie" and "Not a movie" for the chosen videos that are not TV episodes, whichever would move something.</summary>
    private void AddMovieChoiceMenuItems(MenuFlyout menu, IReadOnlyList<LibraryEntry> entries)
    {
        // Only videos outside a show: a special or an extra that joined one stays with it.
        var standalone = LibraryViews.Standalone(_library!.Entries).Select(entry => entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var videos = entries.Where(entry => standalone.Contains(entry.Path)).ToList();
        if (videos.Count == 0)
        {
            return;
        }

        AddFindFilmMenuItem(menu, videos);
        var movies = videos.Count(entry => SectionOf(entry) == LibrarySource.Movies);
        if (movies < videos.Count)
        {
            menu.Items.Add(MenuItem("This is a movie", "LibraryMenuIsMovie", () => ChooseMovie(videos, movie: true)));
        }

        if (movies > 0)
        {
            menu.Items.Add(MenuItem("Not a movie", "LibraryMenuNotMovie", () => ChooseMovie(videos, movie: false)));
        }
    }

    /// <summary>Keeps the person's choice over the rules; the view moves the videos at once and keeps its place.</summary>
    private void ChooseMovie(IReadOnlyList<LibraryEntry> videos, bool movie)
    {
        _library!.SetOverride([.. videos.Select(video => video.Path)], new DetailOverride { Movie = movie });
        var name = videos.Count == 1 ? LibraryViews.MovieTitleOf(videos[0]) : $"{videos.Count} videos";
        Say(movie ? $"{name} moved to Movies" : $"{name} moved to Other videos");
    }
}
