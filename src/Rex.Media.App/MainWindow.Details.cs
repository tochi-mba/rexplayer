using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Metadata;
using Rex.Media.Library;
using static Rex.Media.AppCore.Library.LibraryActions;

namespace Rex.Media.App;

/// <summary>
/// Pages opened over a library view: a movie's details (from Browse's More info, a card's info
/// button or its menu's Details) and everything one cast member appears in. They stack, so Back
/// leaves them one at a time and then returns to the view underneath exactly where it was left
/// (LIB-05). Their places last the session, kept under the view's route like a search's. What each
/// page shows is decided by <see cref="TitleFacts"/> and <see cref="TitleSections"/>; with metadata
/// turned off a movie's page reads from its file alone and still looks whole.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Marks a details page's place key apart from the view's own and its searches.</summary>
    private const string DetailsMark = "@details";

    private readonly List<DetailsStop> _details = [];

    /// <summary>The route the pages were opened over; when the view underneath changes, they close.</summary>
    private string _detailsBase = "";

    /// <summary>The movie whose page is on screen, which Play and Watch trailer act on; null elsewhere.</summary>
    private LibraryEntry? _detailsMovie;

    private Uri? _shownTrailer;
    private string _shownDetails = "";

    /// <summary>One page on the stack: a movie's, or a cast member's (with the photo their card had).</summary>
    private sealed record DetailsStop(LibraryEntry? Movie, CastMember? Person)
    {
        public string Key => Movie is { } movie ? "movie:" + movie.Path : "person:" + Person!.Name;
    }

    /// <summary>Opens a movie's page over the view shown, which keeps its place with the movie chosen.</summary>
    private void OpenMovieDetails(LibraryEntry movie)
    {
        if (_details.Count == 0)
        {
            KeepPlace("file:" + movie.Path);
            _detailsBase = _placeKey;
        }
        else
        {
            KeepPlace();
        }

        _details.Add(new DetailsStop(movie, null));
        ShowLibraryAtPlace();
    }

    /// <summary>Opens everything <paramref name="person"/> appears in, as a view titled with their name.</summary>
    private void OpenPerson(CastMember person)
    {
        if (_details.Count == 0)
        {
            KeepPlace();
            _detailsBase = _placeKey;
        }
        else
        {
            KeepPlace();
        }

        _details.Add(new DetailsStop(null, person));
        ShowLibraryAtPlace();
    }

    /// <summary>Back on a details page: the page underneath, or the view the pages were opened over.</summary>
    private bool LeaveDetailsPage()
    {
        if (_details.Count == 0)
        {
            return false;
        }

        KeepPlace();
        _details.RemoveAt(_details.Count - 1);
        ShowLibraryAtPlace();
        return true;
    }

    /// <summary>Whether a movie is one a details page is offered for.</summary>
    private bool IsMovie(LibraryEntry entry) => LibraryViews.Movies([entry], MovieLookupOf).Count == 1;

    /// <summary>
    /// Draws the page on top of the stack instead of the view, when there is one; true when it did.
    /// Called once the view's own route is known, so pages opened over another route close.
    /// </summary>
    private bool ShowDetailsPage(string search, IReadOnlyList<LibraryEntry> entries)
    {
        _detailsMovie = null;
        if (_details.Count > 0 && _placeKey != _detailsBase)
        {
            // The view underneath changed: the pages close, and the view sets the banner again.
            _details.Clear();
            ResolveShowPage(search);
        }

        if (_details.Count == 0)
        {
            _shownDetails = "";
            return false;
        }

        var stop = _details[^1];
        LibraryEntry? movie = null;
        if (stop.Movie is { } opened)
        {
            movie = _library?.Entry(opened.Path);
            if (movie is null)
            {
                // The movie left the library while its page was open.
                _details.RemoveAt(_details.Count - 1);
                return ShowDetailsPage(search, entries);
            }
        }

        _placeKey = LibraryPlaces.Key(_librarySource.ToString(), _libraryGroup?.Name, _librarySource == LibrarySource.Albums ? _libraryGroup?.Detail : null,
            _librarySeason?.Name, DetailsMark + _details.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + stop.Key);
        _selectionView = _placeKey;
        LibraryHome.Visibility = Visibility.Visible;
        ShowLibraryTitleOnly(false);
        LibraryList.Visibility = LibraryCollage.Visibility = Visibility.Collapsed;
        foreach (var control in new FrameworkElement[] { LibraryLookButton, LibrarySortButton, LibraryGroupButton, LibraryFilterButton, LibrarySizePanel, LibraryFolderActions, LibrarySeasonPicker })
        {
            control.Visibility = Visibility.Collapsed;
        }

        LibraryShuffle.Visibility = LibraryEnqueue.Visibility = Visibility.Collapsed;
        var backLabel = _details.Count > 1
            ? _details[^2].Movie?.Title ?? _details[^2].Person?.Name
            : _libraryParents.TryPeek(out var parent) ? parent.Label
            : _libraryGroup?.Name ?? LibrarySourceItems.First(item => item.Source == _librarySource).Item.Name;
        ShowLibraryBack(true, backLabel);
        ShowLibraryEmpty(false, false, null);
        if (movie is not null)
        {
            ShowMoviePage(movie);
        }
        else
        {
            ShowPersonPage(stop.Person!, entries);
        }

        return true;
    }

    /// <summary>
    /// A movie's page: its backdrop behind its poster, title, facts, synopsis with its credit, its
    /// makers, Play or Resume and Watch trailer; under them the cast, the rest of its series in
    /// the library and More like this.
    /// </summary>
    private void ShowMoviePage(LibraryEntry movie)
    {
        _detailsMovie = movie;
        var facts = MetadataMovie(movie);
        var known = KnownMovies();
        var alike = facts is null ? [] : MoreLikeThis.Rank(facts, known);
        var mates = facts is null ? [] : TitleSections.SeriesMates(facts, known);
        var sections = TitleSections.Movie(facts, alike.Count, mates.Count);
        var left = _player.LeftAt(movie.Path);
        LibraryPlay.Visibility = Visibility.Visible;
        SetLibraryActionsPlayable(null);
        LibraryPlayText.Text = left is null ? "Play" : "Resume";
        ShowHero("MOVIE", LibraryViews.MovieTitleOf(movie), TitleFacts.Movie(movie, facts), "", movie);
        ShowTitleExtras(sections.Crew, sections.Synopsis, credit: null, sections.Trailer, poster: true, sections.Backdrop);

        var shown = "movie\n" + movie.Path + "\n" + (facts?.ToJson() ?? "") + "\n" + string.Join("|", alike.Concat(mates).Select(entry => entry.Path)) + "\n" + left;
        if (shown == _shownDetails)
        {
            return;
        }

        CancelLibraryPictures();
        _shownDetails = shown;
        _shownLibrary = "";
        _libraryRows = [];
        LibraryShelves.Children.Clear();
        if (sections.Cast.Count > 0)
        {
            LibraryShelves.Children.Add(CastRow(sections.Cast));
        }

        if (sections.SeriesHeading is { } series)
        {
            LibraryShelves.Children.Add(Shelf(series, LibrarySource.Movies, [.. mates.Select(EntryRow)], wide: false, seeAll: false, id: "Details-Series"));
        }

        if (sections.MoreLikeThis)
        {
            LibraryShelves.Children.Add(Shelf("More like this", LibrarySource.Movies, [.. alike.Select(EntryRow)], wide: false, seeAll: false, id: "Details-MoreLikeThis"));
        }

        _homeShelfRows = [.. LibraryShelves.Children.OfType<StackPanel>().SelectMany(ShelfRows)];
        LibraryHome.ChangeView(null, 0, null, disableAnimation: true);
    }

    /// <summary>A cast member's own view: every movie and show in the library they appear in.</summary>
    private void ShowPersonPage(CastMember person, IReadOnlyList<LibraryEntry> entries)
    {
        var movies = TitleSections.AppearsIn(person.Name, LibraryViews.Movies(entries, MovieLookupOf)
            .Select(entry => (Entry: entry, Facts: MetadataMovie(entry)))
            .Where(pair => pair.Facts is not null)
            .DistinctBy(pair => pair.Facts!.CatalogueId)
            .Select(pair => (pair.Entry, pair.Facts!.Cast)));
        var shows = TitleSections.AppearsIn(person.Name, LibraryViews.TvShows(entries)
            .Select(group => (Group: group, Facts: ShowFacts(group.Name)))
            .Where(pair => pair.Facts is not null)
            .Select(pair => (pair.Group, pair.Facts!.Cast)));
        LibraryPlay.Visibility = Visibility.Collapsed;
        ShowHero("CAST", person.Name.Trim(), Count(movies.Count + shows.Count, "title"), "", null);
        ShowTitleExtras("", null, credit: null, trailer: null, poster: false, backdrop: null);
        ShowHeroPhoto(person.PhotoUrl);

        var shown = "person\n" + person.Name + "\n" + string.Join("|", movies.Select(entry => entry.Path)) + "\n" + string.Join("|", shows.Select(show => show.Name));
        if (shown == _shownDetails)
        {
            return;
        }

        CancelLibraryPictures();
        _shownDetails = shown;
        _shownLibrary = "";
        _libraryRows = [];
        LibraryShelves.Children.Clear();
        if (movies.Count > 0)
        {
            LibraryShelves.Children.Add(Shelf("Movies", LibrarySource.Movies, [.. movies.Select(EntryRow)], wide: false, seeAll: false, id: "Person-Movies"));
        }

        if (shows.Count > 0)
        {
            LibraryShelves.Children.Add(Shelf("TV shows", LibrarySource.TvShows, [.. shows.Select(GroupRow)], wide: false, seeAll: false, id: "Person-TvShows"));
        }

        _homeShelfRows = [.. LibraryShelves.Children.OfType<StackPanel>().SelectMany(ShelfRows)];
        LibraryHome.ChangeView(null, 0, null, disableAnimation: true);
    }

    private static IEnumerable<LibraryRow> ShelfRows(StackPanel shelf) =>
        shelf.Children.OfType<ListView>().SelectMany(cards => cards.ItemsSource as IEnumerable<LibraryRow> ?? []);

    /// <summary>The library's movies with their cached facts, for More like this and a film's series; none with metadata off.</summary>
    private List<(LibraryEntry Item, EnrichedMovie Facts)> KnownMovies()
    {
        var known = new List<(LibraryEntry, EnrichedMovie)>();
        foreach (var entry in LibraryViews.Movies(_library?.Entries ?? [], MovieLookupOf))
        {
            if (MetadataMovie(entry) is { } facts)
            {
                known.Add((entry, facts));
            }
        }

        return known;
    }

    /// <summary>A show's cached record when TV metadata is on and the lookup found it; null otherwise.</summary>
    private EnrichedShow? ShowFacts(string series) =>
        _settings.Metadata.TvShows && MetadataShow(series) is { Found: true } show ? show : null;

    /// <summary>Play on a movie's page plays it, resuming where it was left; false elsewhere.</summary>
    private bool PlayDetailsMovie()
    {
        if (_detailsMovie is not { } movie || _details.Count == 0)
        {
            return false;
        }

        Play([movie], 0, resume: true);
        return true;
    }

    private void OnLibraryTrailer(object sender, RoutedEventArgs e)
    {
        if (_shownTrailer is { } trailer)
        {
            _ = Windows.System.Launcher.LaunchUriAsync(trailer);
        }
    }

    /// <summary>A movie card's info button opens its page.</summary>
    internal void OnCardInfoClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryRow { Item: LibraryEntry movie } } && IsMovie(movie))
        {
            OpenMovieDetails(movie);
        }
    }
}
