using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The artist page (LIB-05): top songs by plays and albums newest first.</summary>
public sealed class ArtistPagesTests
{
    private static LibraryEntry Song(string title, string? album, int? year = null, int plays = 0, int? track = null) =>
        new() { Path = $@"D:\Music\{title}.mp3", Kind = LibraryKind.Music, Title = title, Artist = "Asake", Album = album, Year = year, Plays = plays, Track = track };

    private static LibraryGroup Artist(params LibraryEntry[] songs) => new("Asake", null, songs);

    [Fact]
    [Capability("LIB-05")]
    public void TopSongsGoByPlaysAndOnlyPlayedSongsCount()
    {
        var artist = Artist(
            Song("Lonely At The Top", "Work of Art", 2023, plays: 12),
            Song("Terminator", "Work of Art", 2023, plays: 40),
            Song("Sungba", "Mr. Money with the Vibe", 2022, plays: 25),
            Song("Never played", "Work of Art", 2023));

        Assert.Equal(["Terminator", "Sungba", "Lonely At The Top"],
            ArtistPages.TopSongs(artist).Select(song => song.Title));
    }

    [Fact]
    [Capability("LIB-05")]
    public void TopSongsStopAtTenAndKeepViewOrderOnTies()
    {
        var many = Enumerable.Range(1, 12).Select(n => Song($"Track {n:00}", "Work of Art", 2023, plays: n == 3 ? 9 : 5)).ToArray();
        var top = ArtistPages.TopSongs(Artist(many));

        Assert.Equal(ArtistPages.TopSongCount, top.Count);
        Assert.Equal("Track 03", top[0].Title);
        // The tied rest keep the order the artist's songs already have.
        Assert.Equal(["Track 01", "Track 02", "Track 04"], top.Skip(1).Take(3).Select(song => song.Title));
    }

    [Fact]
    [Capability("LIB-05")]
    public void TopSongsOfAFreshLibraryAreEmpty()
    {
        Assert.Empty(ArtistPages.TopSongs(Artist(Song("Sungba", "Mr. Money with the Vibe", 2022))));
        Assert.Throws<ArgumentNullException>(() => ArtistPages.TopSongs(null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AlbumsComeNewestFirstThenYearlessThenTheUnknownAlbum()
    {
        var artist = Artist(
            Song("Sungba", "Mr. Money with the Vibe", 2022, track: 9),
            Song("Terminator", "Work of Art", 2023, track: 2),
            Song("Lonely At The Top", "Work of Art", 2023, track: 1),
            Song("Mystery", "Undated"),
            Song("Loose single", null));

        var albums = ArtistPages.Albums(artist);
        Assert.Equal(
            [("Work of Art", 2023), ("Mr. Money with the Vibe", 2022), ("Undated", (int?)null), (LibraryViews.UnknownAlbum, (int?)null)],
            albums.Select(album => (album.Album.Name, album.Year)));

        // The album keeps its track order.
        Assert.Equal(["Lonely At The Top", "Terminator"], albums[0].Album.Entries.Select(song => song.Title));
        Assert.Throws<ArgumentNullException>(() => ArtistPages.Albums(null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AnAlbumsYearIsTheLatestItsSongsCarry()
    {
        var artist = Artist(
            Song("Terminator", "Work of Art", 2023),
            Song("Bonus", "Work of Art", 2024),
            Song("Sungba", "Mr. Money with the Vibe", 2022));

        Assert.Equal([2024, 2022], ArtistPages.Albums(artist).Select(album => album.Year));
    }
}
