using System.Globalization;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Metadata;
using Rex.Media.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// The album page (LIB-05): its kind from the catalogue or the songs, its line, discs, featured
/// artists, play shares, release facts and more by the same artist; and who else is like an artist.
/// </summary>
public sealed class AlbumPageTests
{
    private static LibraryEntry Song(string title, string album = "Lungu Boy", string? artist = "Asake", string? albumArtist = null, int? track = null, int? disc = null,
        int plays = 0, double minutes = 3, int? year = 2024, string? genre = "Afrobeats") => new()
        {
            Path = $@"D:\Music\{album}\{title}.flac",
            Kind = LibraryKind.Music,
            Title = title,
            Artist = artist,
            AlbumArtist = albumArtist,
            Album = album,
            Track = track,
            Disc = disc,
            Plays = plays,
            Duration = TimeSpan.FromMinutes(minutes),
            Year = year,
            Genre = genre,
        };

    private static LibraryGroup Album(params LibraryEntry[] songs) => LibraryViews.Albums(songs)[0];

    private static readonly EnrichedAlbum LunguBoy = new()
    {
        Album = "Lungu Boy",
        Artist = "Asake",
        ReleaseGroupId = "rg",
        Type = "Album",
        FirstReleased = "2024-08-08",
        Labels = ["EMPIRE", "YBNL Nation"],
        Genres = ["afro-fusion"],
    };

    private static LibraryGroup FullAlbum => Album([.. Enumerable.Range(1, 8).Select(n => Song($"Track {n}", track: n, minutes: 4))]);

    [Fact]
    [Capability("LIB-05")]
    public void TheCatalogueSaysWhatKindOfReleaseItIs()
    {
        var album = FullAlbum;
        Assert.Equal(ReleaseKind.Album, AlbumPage.Kind(album, LunguBoy));
        Assert.Equal(ReleaseKind.Single, AlbumPage.Kind(album, LunguBoy with { Type = "single" }));
        Assert.Equal(ReleaseKind.EP, AlbumPage.Kind(album, LunguBoy with { Type = "EP" }));
        Assert.Equal(ReleaseKind.Album, AlbumPage.Kind(album, LunguBoy with { Type = "Broadcast" }));
        Assert.Equal(ReleaseKind.Compilation, AlbumPage.Kind(album, LunguBoy with { SecondaryTypes = ["Live", "compilation"] }));
        Assert.Throws<ArgumentNullException>(() => AlbumPage.Kind(null!, null));
    }

    [Fact]
    [Capability("LIB-05")]
    public void WithoutTheCatalogueTheSongsSayWhatKindItIs()
    {
        Assert.Equal(ReleaseKind.Album, AlbumPage.Kind(FullAlbum, null));
        Assert.Equal(ReleaseKind.Single, AlbumPage.Kind(Album(Song("Wave"), Song("Wave (Instrumental)")), null));
        Assert.Equal(ReleaseKind.EP, AlbumPage.Kind(Album([.. Enumerable.Range(1, 5).Select(n => Song($"T{n}"))]), null));
        // Long enough to be an album even with few songs.
        Assert.Equal(ReleaseKind.Album, AlbumPage.Kind(Album(Song("Suite", minutes: 31)), null));
        Assert.Equal(ReleaseKind.Compilation, AlbumPage.Kind(Album(Song("A", albumArtist: "Various Artists"), Song("B", albumArtist: "Various Artists")), null));
        Assert.Equal(ReleaseKind.Compilation, AlbumPage.Kind(new LibraryGroup("Hits", null,
            [Song("A", "Hits", "Asake"), Song("B", "Hits", "Olamide"), Song("C", "Hits", "Wizkid"), Song("D", "Hits", "Rema")]), null));
        Assert.Equal("Compilation", AlbumPage.KindName(ReleaseKind.Compilation));
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheLineAndYearReadLikeAStreamingAlbumHeader()
    {
        var album = FullAlbum;
        Assert.Equal(2024, AlbumPage.Year(album, LunguBoy with { FirstReleased = "2024" }));
        Assert.Equal(2024, AlbumPage.Year(album, null));
        Assert.Equal("Album \u00B7 2024 \u00B7 8 songs, 32 min", AlbumPage.Line(album, ReleaseKind.Album, 2024));
        Assert.Equal("Single \u00B7 1 song", AlbumPage.Line(Album(Song("Wave", minutes: 0) with { Duration = null }), ReleaseKind.Single, null));
        Assert.Throws<ArgumentNullException>(() => AlbumPage.Year(null!, null));
        Assert.Throws<ArgumentNullException>(() => AlbumPage.Line(null!, ReleaseKind.Album, null));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AlbumDetailsRemainHonestWhenARescanRemovesTheLastLocalTrack()
    {
        var absent = new LibraryGroup("Disconnected album", "Artist", []);
        Assert.Equal(ReleaseKind.Album, AlbumPage.Kind(absent, null));
        Assert.Null(AlbumPage.Year(absent, null));
        Assert.Empty(AlbumPage.Discs(absent));
        Assert.Equal("Album · 0 songs", AlbumPage.Line(absent, AlbumPage.Kind(absent, null), null));
        Assert.Equal(0, AlbumPage.PlayShare(Song("Gone"), absent));

        // Restoring the original files returns to their actual album, disc,
        // track, year and credited artist, rather than retaining fake rows.
        var returned = new LibraryGroup("Disconnected album", "Artist",
            [Song("B", track: 2, disc: 2, year: 2024), Song("A", track: 1, disc: 1, year: 2023)]);
        Assert.Equal(2024, AlbumPage.Year(returned, null));
        Assert.Equal([1, 2], AlbumPage.Discs(returned).Select(d => d.Number));
        Assert.Equal(["A", "B"], AlbumPage.Discs(returned).SelectMany(d => d.Songs).Select(s => s.Title));
    }

    [Fact]
    [Capability("LIB-05")]
    public void DiscsAreHeadedOnlyForASetOfSeveral()
    {
        var single = AlbumPage.Discs(Album(Song("B", track: 2), Song("A", track: 1), Song("Bonus")));
        Assert.Equal([null], single.Select(disc => disc.Number));
        Assert.Equal(["A", "B", "Bonus"], single[0].Songs.Select(song => song.Title));

        var set = AlbumPage.Discs(Album(Song("Two-1", track: 1, disc: 2), Song("One-2", track: 2, disc: 1), Song("One-1", track: 1)));
        Assert.Equal([1, 2], set.Select(disc => disc.Number));
        Assert.Equal(["One-1", "One-2"], set[0].Songs.Select(song => song.Title));
        Assert.Equal("Disc 2", AlbumPage.DiscName(2));
        Assert.Throws<ArgumentNullException>(() => AlbumPage.Discs(null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void EachSongNamesOnlyTheArtistsBeyondTheAlbums()
    {
        var songs = new[] { Song("Active (feat. Travis Scott)"), Song("Mood", artist: "Asake/Wizkid"), Song("Wave") };
        var names = ArtistNames.Learn(songs);
        var album = Album(songs);
        var artists = AlbumPage.Artists(album, names);

        Assert.Equal(["Asake"], artists);
        Assert.Equal(["Travis Scott"], AlbumPage.Featured(songs[0], artists, names));
        Assert.Equal(["Wizkid"], AlbumPage.Featured(songs[1], artists, names));
        Assert.Empty(AlbumPage.Featured(songs[2], artists, names));
        // Filed under no artist: the first song's credit; nothing at all for an empty group.
        Assert.Equal(["Asake", "Wizkid"], AlbumPage.Artists(new LibraryGroup("X", null, [songs[1]]), names));
        Assert.Empty(AlbumPage.Artists(new LibraryGroup("X", null, []), names));
        Assert.Throws<ArgumentNullException>(() => AlbumPage.Artists(null!, names));
        Assert.Throws<ArgumentNullException>(() => AlbumPage.Artists(album, null!));
        Assert.Throws<ArgumentNullException>(() => AlbumPage.Featured(null!, artists, names));
        Assert.Throws<ArgumentNullException>(() => AlbumPage.Featured(songs[0], null!, names));
        Assert.Throws<ArgumentNullException>(() => AlbumPage.Featured(songs[0], artists, null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void APlayShareIsAgainstTheAlbumsMostPlayed()
    {
        var album = Album(Song("A", plays: 10), Song("B", plays: 5), Song("C"));
        Assert.Equal([1, 0.5, 0], album.Entries.Select(song => AlbumPage.PlayShare(song, album)));
        Assert.Equal(0, AlbumPage.PlayShare(album.Entries[0], Album(Song("Z"))));
        Assert.Throws<ArgumentNullException>(() => AlbumPage.PlayShare(null!, album));
        Assert.Throws<ArgumentNullException>(() => AlbumPage.PlayShare(album.Entries[0], null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheReleaseFactsShowOnlyWhatIsKnown()
    {
        var english = CultureInfo.GetCultureInfo("en-GB");
        Assert.Equal("Released 8 August 2024", AlbumPage.Released(LunguBoy, english));
        Assert.Equal("Released 2018", AlbumPage.Released(LunguBoy with { FirstReleased = "2018" }, english));
        Assert.Equal("Released 2018", AlbumPage.Released(LunguBoy with { FirstReleased = "2018-99-99" }, english));
        Assert.Null(AlbumPage.Released(LunguBoy with { FirstReleased = null }, english));
        Assert.Null(AlbumPage.Released(null, english));
        Assert.Throws<ArgumentNullException>(() => AlbumPage.Released(LunguBoy, null!));

        Assert.Equal("\u2117 2024 EMPIRE, YBNL Nation", AlbumPage.Phonographic(LunguBoy));
        Assert.Null(AlbumPage.Phonographic(LunguBoy with { Labels = [] }));
        Assert.Null(AlbumPage.Phonographic(LunguBoy with { FirstReleased = null }));
        Assert.Null(AlbumPage.Phonographic(null));

        Assert.Equal(["afro-fusion"], AlbumPage.Genres(FullAlbum, LunguBoy));
        Assert.Equal(["Afrobeats", "Amapiano"], AlbumPage.Genres(Album(Song("A", genre: "Afrobeats;Amapiano"), Song("B", genre: "afrobeats")), LunguBoy with { Genres = [] }));
        Assert.Throws<ArgumentNullException>(() => AlbumPage.Genres(null!, null));
    }

    [Fact]
    [Capability("LIB-05")]
    public void MoreByListsTheArtistsOtherAlbumsNewestFirst()
    {
        var songs = new[]
        {
            Song("Wave"), Song("Sungba", "Mr. Money with the Vibe", year: 2022), Song("Lonely At The Top", "Work of Art", year: 2023),
            Song("Loose", "Singles", year: null), Song("Duet", "Shared", artist: "Asake/Olamide", year: 2021),
            Song("Rough Ryder", "Unruly", "Olamide"), Song("Mix", "Hits", albumArtist: "Various Artists"), Song("Loose 2", "Unknown album", artist: "Asake") with { Album = null },
        };
        var names = ArtistNames.Learn(songs);
        var albums = LibraryViews.Albums(songs);
        var lunguBoy = albums.First(album => album.Name == "Lungu Boy");

        Assert.Equal(["Work of Art", "Mr. Money with the Vibe", "Shared", "Singles"], AlbumPage.MoreBy(lunguBoy, albums, names).Select(album => album.Name));
        Assert.Empty(AlbumPage.MoreBy(albums.First(album => album.Name == "Hits"), albums, names));
        Assert.Empty(AlbumPage.MoreBy(new LibraryGroup("X", null, []), albums, names));
        Assert.Throws<ArgumentNullException>(() => AlbumPage.MoreBy(null!, albums, names));
        Assert.Throws<ArgumentNullException>(() => AlbumPage.MoreBy(lunguBoy, null!, names));
        Assert.Throws<ArgumentNullException>(() => AlbumPage.MoreBy(lunguBoy, albums, null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void FansAlsoLikeRanksSharedSongsAboveSharedGenres()
    {
        var songs = new[]
        {
            Song("Wave"), Song("Sungba", genre: "Amapiano"), Song("Basquiat (feat. Wizkid)"),
            Song("Rough Ryder", "Unruly", "Olamide"), Song("Jinja", "Unruly", "Olamide", genre: "Amapiano"),
            Song("Calm Down", "Rave", "Rema", genre: "Afro-pop"),
            Song("Essence", "Made in Lagos", "Wizkid", genre: "R&B"),
            Song("Unknown", "X", artist: null, genre: "Afrobeats"),
        };
        var artists = LibraryViews.Artists(songs);
        var asake = artists.First(artist => artist.Name == "Asake");

        var like = ArtistKinship.Like(asake, artists);
        Assert.Equal(["Wizkid", "Olamide"], like.Select(kin => kin.Artist.Name));
        Assert.Equal((1, 1, 4), (like[0].SharedSongs, like[0].SharedGenres, like[0].Score));
        Assert.Equal((0, 2, 2), (like[1].SharedSongs, like[1].SharedGenres, like[1].Score));

        // A lookup's genres count too.
        var more = ArtistKinship.Like(asake, artists, name => name == "Rema" ? ["afrobeats", ""] : []);
        Assert.Equal(["Wizkid", "Olamide", "Rema"], more.Select(kin => kin.Artist.Name));
        Assert.Throws<ArgumentNullException>(() => ArtistKinship.Like(null!, artists));
        Assert.Throws<ArgumentNullException>(() => ArtistKinship.Like(asake, null!));
    }
}
