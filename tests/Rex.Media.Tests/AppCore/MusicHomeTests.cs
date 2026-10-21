using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// The music Home's rows, worked out from the library alone: the hero, Jump back in, On repeat,
/// Recently added, the artist, genre and decade mixes, albums not played in a while, and artists
/// new to the library. Long mixes and sets never rank.
/// </summary>
public sealed class MusicHomeTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    private static LibraryEntry Song(string title, string album, string artist = "Asake", int plays = 0, int? daysAgo = null, int addedDaysAgo = 100,
        string genre = "Afrobeats", int year = 2023, double minutes = 3) => new()
        {
            Path = $@"D:\Music\{artist}\{album}\{title}.flac",
            Kind = LibraryKind.Music,
            Title = title,
            Artist = artist,
            Album = album,
            Genre = genre,
            Year = year,
            Plays = plays,
            LastPlayed = daysAgo is { } ago ? Now.AddDays(-ago) : null,
            Added = Now.AddDays(-addedDaysAgo),
            Duration = TimeSpan.FromMinutes(minutes),
        };

    private static readonly LibraryEntry[] Library =
    [
        Song("Lonely At The Top", "Work of Art", plays: 30, daysAgo: 1),
        Song("Terminator", "Work of Art", plays: 12, daysAgo: 2),
        Song("Basquiat", "Work of Art"),
        Song("Sungba", "Mr. Money with the Vibe", plays: 50, daysAgo: 90, genre: "Amapiano", year: 2022),
        Song("Joha", "Mr. Money with the Vibe", plays: 20, daysAgo: 100, genre: "Amapiano", year: 2022),
        Song("Peace Be Unto You", "Mr. Money with the Vibe", genre: "Amapiano", year: 2022),
        Song("Wave", "Lungu Boy", addedDaysAgo: 2, year: 2024),
        Song("Mood", "Lungu Boy", addedDaysAgo: 2, year: 2024),
        Song("Active (feat. Travis Scott)", "Lungu Boy", addedDaysAgo: 2, year: 2024),
        Song("Rough Ryder", "Unruly", "Olamide", plays: 3, daysAgo: 3, addedDaysAgo: 5),
        Song("Jinja", "Unruly", "Olamide", addedDaysAgo: 5),
        Song("Live at the O2 (full set)", "Live", plays: 99, daysAgo: 0, minutes: 75),
        new() { Path = @"D:\Video\x.mp4", Kind = LibraryKind.Video, Title = "x", Plays = 500, LastPlayed = Now },
    ];

    [Fact]
    [Capability("LIB-05")]
    public void OnRepeatRanksByPlaysLatelyAndLeavesSetsOut()
    {
        var rows = MusicHome.Rows(Library, [], Now);

        // Sungba has the most plays, but not lately; the 75-minute set never ranks; videos are not music.
        Assert.Equal(["Lonely At The Top", "Terminator", "Rough Ryder", "Sungba", "Joha"], rows.OnRepeat.Select(song => song.Title));
        Assert.Equal(0, MusicHome.Score(Library[11], Now));
        Assert.True(MusicHome.IsLong(Library[11]));
        Assert.Equal(30 * Math.Pow(0.5, 1 / 14.0), MusicHome.Score(Library[0], Now), 6);
        // Played with no time remembered counts as long ago; a play from the future counts as now.
        Assert.Equal(4 * Math.Pow(0.5, 365 / 14.0), MusicHome.Score(Library[0] with { Plays = 4, LastPlayed = null }, Now), 9);
        Assert.Equal(4, MusicHome.Score(Library[0] with { Plays = 4, LastPlayed = Now.AddDays(1) }, Now));
        Assert.Throws<ArgumentNullException>(() => MusicHome.Score(null!, Now));
        Assert.Throws<ArgumentNullException>(() => MusicHome.IsLong(null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheHeroIsTheAlbumOnRepeatOrTheArtistAcrossAlbumsOrTheNewest()
    {
        var hero = MusicHome.Rows(Library, [], Now).Hero!;
        Assert.Equal(("Work of Art", false, "On repeat lately"), (hero.Group.Name, hero.IsArtist, hero.Reason));

        // Played evenly across two albums, the artist outweighs either album.
        var spread = new[]
        {
            Song("A", "One", plays: 10, daysAgo: 0), Song("B", "Two", plays: 10, daysAgo: 0), Song("C", "Three", plays: 10, daysAgo: 0),
            Song("D", "Other", "Olamide", plays: 9, daysAgo: 0),
        };
        var artist = MusicHome.Rows(spread, [], Now).Hero!;
        Assert.Equal(("Asake", true, "Your top artist lately"), (artist.Group.Name, artist.IsArtist, artist.Reason));

        // Nothing played: the album added last.
        var fresh = MusicHome.Rows(Library.Select(song => song with { Plays = 0 }), [], Now).Hero!;
        Assert.Equal(("Lungu Boy", "New in your library"), (fresh.Group.Name, fresh.Reason));
        Assert.Null(MusicHome.Rows([], [], Now).Hero);
    }

    [Fact]
    [Capability("LIB-05")]
    public void JumpBackInMixesAlbumsAndPlaylistsByWhenTheyWerePlayed()
    {
        var playlist = new NamedPlaylist("p1", "Road trip", [new QueuedItem(Library[9].Path, null, null, TimeSpan.Zero, null)]);
        var unplayed = new NamedPlaylist("p2", "Later", [new QueuedItem(Library[2].Path, null, null, TimeSpan.Zero, null), new QueuedItem(@"D:\gone.mp3", null, null, TimeSpan.Zero, null)]);
        var rows = MusicHome.Rows(Library, [playlist, unplayed], Now);

        Assert.Equal(["Live", "Work of Art", "Unruly", "Road trip", "Mr. Money with the Vibe"],
            rows.JumpBackIn.Select(place => place.Album?.Name ?? place.Playlist!.Name));
        Assert.Throws<ArgumentNullException>(() => MusicHome.Rows(null!, [], Now));
        Assert.Throws<ArgumentNullException>(() => MusicHome.Rows([], null!, Now));
    }

    [Fact]
    [Capability("LIB-05")]
    public void RecentlyAddedNotLatelyAndNewArtistsReadTheirOwnDates()
    {
        var rows = MusicHome.Rows(Library, [], Now);

        Assert.Equal(["Lungu Boy", "Unruly"], rows.RecentlyAdded.Take(2).Select(album => album.Name));
        Assert.Equal(["Mr. Money with the Vibe"], rows.NotLately.Select(album => album.Name));
        Assert.Equal(["Travis Scott", "Olamide"], rows.NewArtists.Take(2).Select(artist => artist.Name));
        Assert.DoesNotContain(rows.NewArtists, artist => artist.Name == LibraryViews.UnknownArtist);
    }

    [Fact]
    [Capability("LIB-05")]
    public void MixesAreMadeAroundTopArtistsGenresAndADecade()
    {
        var rows = MusicHome.Rows(Library, [], Now);
        var mixes = rows.Mixes.Select(mix => (mix.Kind, mix.Title)).ToList();

        // Olamide has two songs, too few for a mix of his own, Travis Scott one, and Amapiano three.
        Assert.Equal([(MusicMixKind.Artist, "Asake Mix"), (MusicMixKind.Genre, "Afrobeats Mix"), (MusicMixKind.Decade, "2020s Mix")], mixes);
        var asake = rows.Mixes[0];
        Assert.Equal("Asake", asake.Around);
        // His most played lately first, two at a time, with an artist he shares a genre or a song with between.
        Assert.Equal(["Lonely At The Top", "Terminator"], asake.Songs.Take(2).Select(song => song.Title));
        Assert.Equal("Olamide", asake.Songs[2].Artist);
        Assert.Equal("Asake, Travis Scott and more", asake.Line);
        Assert.DoesNotContain(rows.Mixes.SelectMany(mix => mix.Songs), song => MusicHome.IsLong(song));
        Assert.Equal(asake.Songs.Count, asake.Songs.Select(song => song.Path).Distinct().Count());

        // A genre mix takes its artists in turn.
        var afrobeats = rows.Mixes[1];
        Assert.Equal(["Asake", "Olamide"], afrobeats.Songs.Take(2).Select(song => song.FiledArtist!.Split(' ')[0]));
        Assert.Equal("Asake and Olamide", afrobeats.Line);
        Assert.Equal(("2020s", "Asake and Olamide"), (rows.Mixes[2].Around, rows.Mixes[2].Line));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AMixStopsAtItsSizeAndNeedsEnoughSongs()
    {
        var many = Enumerable.Range(1, 60).Select(n => Song($"Song {n:00}", "Big", plays: n, daysAgo: 1)).ToList();
        var rows = MusicHome.Rows(many, [], Now);
        Assert.All(rows.Mixes, mix => Assert.Equal(MusicHome.MixSize, mix.Songs.Count));
        Assert.Equal("Asake", rows.Mixes[0].Line);
        Assert.Equal(MusicHome.RowSize, rows.OnRepeat.Count);

        var few = MusicHome.Rows([Song("A", "X", year: 1999), Song("B", "X", year: 1999), Song("C", "X", year: 2005)], [], Now);
        Assert.Empty(few.Mixes);
        // Untagged genres and years make no mixes; an artist with no other artist like them still gets one.
        var bare = MusicHome.Rows(Enumerable.Range(1, 5).Select(n => Song($"S{n}", "X", genre: "", year: 0) with { Year = null, Genre = null }), [], Now);
        Assert.Equal(["Asake Mix"], bare.Mixes.Select(mix => mix.Title));
        // Songs with no artist make a genre mix with no line under it, and no artist mix.
        var nameless = MusicHome.Rows(Enumerable.Range(1, 4).Select(n => Song($"N{n}", "Y", genre: "Jazz") with { Artist = null }), [], Now);
        Assert.Equal(("Jazz Mix", ""), (nameless.Mixes[0].Title, nameless.Mixes[0].Line));
    }
}
