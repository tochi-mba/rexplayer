using System.Globalization;
using Rex.Media.AppCore.Library;

namespace Rex.Media.App.Mac;

/// <summary>
/// An artist's page in the Mac library (LIB-05): their name and counts, the songs of theirs
/// played the most, their albums with the newest first, other artists' albums they are featured
/// on, and the artists in the library most like them, each album and artist opening its own page.
/// <see cref="ArtistPages"/> and <see cref="ArtistKinship"/> decide; this file draws.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>How many related artists the page offers at most.</summary>
    private const int LibraryKinCount = 6;

    /// <summary>Opens the page of the artist of that name, if the library has them.</summary>
    private bool OpenLibraryArtistNamed(string name)
    {
        if (_libraryWindow?.IsVisible != true || _mediaLibrary is null || _libraryPageHost is null)
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

    /// <summary>The artist's page: top songs, albums, albums they are featured on, then artists like them.</summary>
    private LibraryDetailPage BuildLibraryArtistPage(LibraryGroup artist)
    {
        var albums = ArtistPages.Albums(artist);
        var featuredOn = ArtistPages.FeaturedOn(artist);
        var top = ArtistPages.TopSongs(artist);
        var keyViews = new List<NSView>();
        var views = new List<NSView>();

        var kicker = NSTextField.CreateLabel("ARTIST");
        kicker.TextColor = NSColor.SecondaryLabel;
        kicker.Font = NSFont.SystemFontOfSize(11, NSFontWeight.Semibold)!;
        var name = NSTextField.CreateLabel(artist.Name);
        name.Font = NSFont.SystemFontOfSize(26, NSFontWeight.Semibold)!;
        name.LineBreakMode = NSLineBreakMode.TruncatingTail;
        name.WidthAnchor.ConstraintLessThanOrEqualTo(560).Active = true;
        var counts = ViewFacts.Join(
            albums.Count == 1 ? "1 album" : albums.Count.ToString(CultureInfo.CurrentCulture) + " albums",
            artist.Entries.Count == 1 ? "1 song" : artist.Entries.Count.ToString(CultureInfo.CurrentCulture) + " songs");
        var line = NSTextField.CreateLabel(counts);
        line.TextColor = NSColor.SecondaryLabel;
        line.AccessibilityLabel = "About this artist: " + counts;
        var songs = artist.Entries;
        var playAll = NSButton.CreateButton("Play all", () =>
        {
            if (songs.Count > 0)
            {
                PlayLibraryEntry(songs[0], songs);
            }
        });
        playAll.Enabled = songs.Count > 0;
        ((NSView)playAll).AccessibilityLabel = "Play every song by " + artist.Name;
        keyViews.Add(playAll);
        views.Add(kicker);
        views.Add(name);
        views.Add(line);
        views.Add(playAll);

        if (top.Count > 0)
        {
            views.Add(LibraryHeading("Top songs"));
            var rank = 0;
            foreach (var song in top)
            {
                rank++;
                var detail = ViewFacts.Join(song.Album, song.Plays == 1 ? "1 play"
                    : song.Plays.ToString("N0", CultureInfo.CurrentCulture) + " plays");
                views.Add(LibrarySongRow(song, top, rank.ToString(CultureInfo.CurrentCulture), detail, keyViews));
            }
        }

        views.Add(LibraryHeading("Albums"));
        foreach (var album in albums)
        {
            views.Add(LibraryAlbumRow(album.Album, album.Year, keyViews));
        }

        if (featuredOn.Count > 0)
        {
            views.Add(LibraryHeading("Featured on"));
            foreach (var album in featuredOn)
            {
                views.Add(LibraryAlbumRow(album.Album, album.Year, keyViews));
            }
        }

        if (_mediaLibrary is not null &&
            ArtistKinship.Like(artist, LibraryMemo.Artists(_mediaLibrary.Entries)) is { Count: > 0 } kin)
        {
            views.Add(LibraryHeading("Fans also like"));
            var links = new List<NSView>();
            foreach (var other in kin.Take(LibraryKinCount))
            {
                var otherName = other.Artist.Name;
                var link = LibraryLink(otherName, "Open the artist " + otherName, () => OpenLibraryArtistNamed(otherName));
                keyViews.Add(link);
                links.Add(link);
            }

            var shelf = NSStackView.FromViews([.. links]);
            shelf.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            shelf.Spacing = 12;
            views.Add(shelf);
        }

        var summary = $"artist={artist.Name};albums={albums.Count}";
        return LibraryDetailPageOf("Artist: " + artist.Name, summary, views, keyViews);
    }
}
