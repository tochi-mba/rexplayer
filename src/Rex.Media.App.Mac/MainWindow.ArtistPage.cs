using System.Globalization;
using Rex.Media.AppCore.Design;
using Rex.Media.AppCore.Library;

namespace Rex.Media.App.Mac;

/// <summary>
/// An artist's page in the Mac library (LIB-05), laid out as the Windows artist page: a banner with
/// their name, their albums, songs and length, and Play, Shuffle and Add to queue; then the songs
/// of theirs played the most, their albums with the newest first and other artists' albums they
/// are featured on as rows of cards, and the artists in the library most like them, each album and
/// artist opening its own page. <see cref="ArtistPages"/> and <see cref="ArtistKinship"/> decide;
/// this file draws.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>How many related artists the page offers at most.</summary>
    private const int LibraryKinCount = 6;

    /// <summary>Opens the page of the artist of that name, if the library has them.</summary>
    private bool OpenLibraryArtistNamed(string name)
    {
        if (!LibraryShown || _mediaLibrary is null || _libraryPageHost is null)
        {
            return false;
        }

        var key = ArtistNames.Key(name);
        var artist = LibraryMemo.Artists(_mediaLibrary.Entries).FirstOrDefault(candidate =>
            candidate.Name != LibraryViews.UnknownArtist && ArtistNames.Key(candidate.Name) == key);
        if (artist is null)
        {
            return false;
        }

        ShowLibraryPage(BuildLibraryArtistPage(artist));
        return true;
    }

    /// <summary>The artist's page: the banner, top songs, albums, albums they are featured on, then artists like them.</summary>
    private LibraryDetailPage BuildLibraryArtistPage(LibraryGroup artist)
    {
        var albums = ArtistPages.Albums(artist);
        var featuredOn = ArtistPages.FeaturedOn(artist);
        var top = ArtistPages.TopSongs(artist);
        var songs = artist.Entries;
        var keyViews = new List<NSView>();
        var views = new List<NSView>();

        var hero = BuildLibraryHero("person", round: true);
        ((NSView)hero.Art).AccessibilityLabel = "Picture of " + artist.Name;
        hero.Kicker.Text = "ARTIST";
        hero.Title.StringValue = artist.Name;
        var counts = ViewFacts.Join(
            albums.Count == 1 ? "1 album" : albums.Count.ToString(CultureInfo.CurrentCulture) + " albums",
            songs.Count == 1 ? "1 song" : songs.Count.ToString(CultureInfo.CurrentCulture) + " songs",
            ViewFacts.Length(songs.Select(song => song.Duration)));
        hero.Summary.StringValue = counts;
        hero.Summary.AccessibilityLabel = "About this artist: " + counts;
        AddLibraryHeroActions(hero, songs, "every song by " + artist.Name, keyViews);

        if (top.Count > 0)
        {
            views.Add(MacLibraryViews.Heading("Top songs"));
            var rank = 0;
            foreach (var song in top)
            {
                rank++;
                var plays = song.Plays == 1 ? "1 play" : song.Plays.ToString("N0", CultureInfo.CurrentCulture) + " plays";
                views.Add(LibrarySongRow(song, top, rank.ToString(CultureInfo.CurrentCulture), song.Album ?? "", plays, keyViews));
            }
        }

        views.Add(MacLibraryViews.Heading("Albums"));
        views.Add(LibraryShelf(albums.Select(album => LibraryAlbumCard(album.Album, album.Year, keyViews))));

        if (featuredOn.Count > 0)
        {
            views.Add(MacLibraryViews.Heading("Featured on"));
            views.Add(LibraryShelf(featuredOn.Select(album => LibraryAlbumCard(album.Album, album.Year, keyViews))));
        }

        if (_mediaLibrary is not null &&
            ArtistKinship.Like(artist, LibraryMemo.Artists(_mediaLibrary.Entries)) is { Count: > 0 } kin)
        {
            views.Add(MacLibraryViews.Heading("Fans also like"));
            views.Add(LibraryShelf(kin.Take(LibraryKinCount).Select(other =>
            {
                var otherName = other.Artist.Name;
                void Open() => OpenLibraryArtistNamed(otherName);
                var art = new LibraryArtView(LibraryCardArtworkSize, (nfloat)(LibraryCardArtworkSize / 2), "person");
                ((NSView)art).AccessibilityLabel = "Picture of " + otherName;
                art.AddGestureRecognizer(new NSClickGestureRecognizer(Open));
                var link = new RexTextButton(otherName, "Open the artist " + otherName, Open, DesignRole.TextPrimary, MacTheme.Font(2, semibold: true));
                keyViews.Add(link);
                return LibraryCard(art, link, null);
            })));
        }

        var summary = $"artist={artist.Name};albums={albums.Count}";
        return LibraryDetailPageOf("Artist: " + artist.Name, summary, hero, views, keyViews);
    }
}
