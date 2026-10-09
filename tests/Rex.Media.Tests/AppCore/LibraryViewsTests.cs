using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The library's views (LIB-05): songs, albums, artists, genres, videos, lately, part-way and search.</summary>
public sealed class LibraryViewsTests
{
    private static readonly DateTime Monday = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    private static LibraryEntry Song(string title, string? artist, string? album, int? track = null, int? disc = null, string? genre = null, string? albumArtist = null) =>
        new() { Path = $@"D:\Music\{title}.mp3", Kind = LibraryKind.Music, Title = title, Artist = artist, Album = album, Track = track, Disc = disc, Genre = genre, AlbumArtist = albumArtist, Added = Monday };

    private static LibraryEntry Video(string title, DateTime? lastPlayed = null) =>
        new() { Path = $@"D:\Videos\{title}.mp4", Kind = LibraryKind.Video, Title = title, Added = Monday, LastPlayed = lastPlayed };

    private static LibraryEntry VideoAt(string folder, string title) =>
        new() { Path = Path.Combine(folder, title + ".mkv"), Kind = LibraryKind.Video, Title = title, Added = Monday };

    private static LibraryEntry Picture(string folder, string title) =>
        new() { Path = Path.Combine(folder, title), Kind = LibraryKind.Picture, Title = Path.GetFileNameWithoutExtension(title), Added = Monday };

    private static readonly LibraryEntry[] Entries =
    [
        Song("Terminator", "Asake", "Work of Art", track: 2, genre: "Afrobeats"),
        Song("Sungba", "Asake", "Mr. Money with the Vibe", track: 9, genre: "afrobeats"),
        Song("Lonely At The Top", "Asake", "Work of Art", track: 1, genre: "Afrobeats"),
        Song("Bonus", "Asake", "Work of Art", track: 1, disc: 2),
        Song("Demo", null, null),
        Song("Joha", "asake", null),
        Song("Feature", "Olamide", "Ọ̀rẹ́", albumArtist: "Various"),
        Song("Other", "Wizkid", "Ọ̀rẹ́", albumArtist: "Various"),
        Song("Single", "Burna Boy", "Mixed"),
        Song("Single 2", "Asake", "Mixed"),
        Video("Episode 10"),
        Video("Episode 2", lastPlayed: Monday),
    ];

    private static string[] Titles(IEnumerable<LibraryEntry> entries) => [.. entries.Select(entry => entry.Title)];

    [Fact]
    [Capability("LIB-05")]
    public void SongsGoByArtistAlbumDiscAndTrack()
    {
        Assert.Equal(
            ["Single 2", "Sungba", "Lonely At The Top", "Terminator", "Bonus", "Joha", "Single", "Feature", "Other", "Demo"],
            Titles(LibraryViews.Songs(Entries)));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AlbumsArtistsAndGenresGatherTheirSongs()
    {
        var albums = LibraryViews.Albums(Entries);
        // Two artists' "Mixed" stay apart; the compilation tagged "Various" stays together.
        Assert.Equal(
            [("Mixed", "Asake"), ("Mixed", "Burna Boy"), ("Mr. Money with the Vibe", "Asake"), ("Ọ̀rẹ́", "Various"), ("Work of Art", "Asake"), (LibraryViews.UnknownAlbum, "asake"), (LibraryViews.UnknownAlbum, null)],
            albums.Select(album => (album.Name, album.Detail)));
        Assert.Equal(["Feature", "Other"], Titles(albums[3].Entries));
        Assert.Equal(["Lonely At The Top", "Terminator", "Bonus"], Titles(albums[4].Entries));

        // Named as most of their songs spell them.
        var artists = LibraryViews.Artists(Entries);
        Assert.Equal(["Asake", "Burna Boy", "Various", LibraryViews.UnknownArtist], artists.Select(artist => artist.Name));
        Assert.Equal(6, artists[0].Entries.Count);
        var genres = LibraryViews.Genres(Entries);
        Assert.Equal(["Afrobeats", LibraryViews.UnknownGenre], genres.Select(genre => genre.Name));
        Assert.Equal(["Sungba", "Lonely At The Top", "Terminator"], Titles(genres[0].Entries));
    }

    [Fact]
    [Capability("LIB-05")]
    public void EveryCreditedArtistGetsTheirOwnGroup()
    {
        var together = Song("Peace Be Unto You", "Asake; DJ Snake; asake", "Singles");
        var another = Song("Amapiano", "Asake; Kabza De Small", "Singles");
        var artists = LibraryViews.Artists([together, another]);

        Assert.Equal(["Asake", "DJ Snake", "Kabza De Small"], artists.Select(artist => artist.Name));
        Assert.Equal(["Peace Be Unto You", "Amapiano"], Titles(artists[0].Entries));
        Assert.Equal("Asake; DJ Snake; asake", together.Artist);
    }

    [Fact]
    [Capability("LIB-05")]
    public void VideosLatelyAndPartWay()
    {
        Assert.Equal(["Episode 2", "Episode 10"], Titles(LibraryViews.Videos(Entries)));

        var played = Entries.Select((entry, i) => i < 3 ? entry with { LastPlayed = Monday.AddMinutes(i) } : entry with { LastPlayed = null }).ToList();
        Assert.Equal(["Lonely At The Top", "Sungba", "Terminator"], Titles(LibraryViews.RecentlyPlayed(played)));

        var added = Entries.Select((entry, i) => entry with { Added = Monday.AddDays(-i) }).ToList();
        Assert.Equal(LibraryViews.Lately, LibraryViews.RecentlyAdded(Enumerable.Repeat(added, 10).SelectMany(x => x)).Count);
        Assert.Equal("Terminator", LibraryViews.RecentlyAdded(added)[0].Title);

        var left = LibraryViews.ContinueWatching(Entries, path => path.Contains("Episode", StringComparison.Ordinal) || path.Contains("Sungba", StringComparison.Ordinal) ? TimeSpan.FromMinutes(5) : null);
        Assert.Equal(["Episode 2", "Episode 10"], Titles(left));
        Assert.Throws<ArgumentNullException>(() => LibraryViews.ContinueWatching(Entries, null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void PicturesHaveTheirOwnNaturallyOrderedViewAndSearch()
    {
        var pictures = new[]
        {
            Picture(@"D:\Photos\Trip", "Photo 10.jpg"),
            Picture(@"D:\Photos\Home", "Cat.png"),
            Picture(@"D:\Photos\Trip", "Photo 2.jpg"),
        };

        Assert.Equal(["Cat", "Photo 2", "Photo 10"], Titles(LibraryViews.Pictures(pictures)));
        Assert.Equal(["Photo 2"], Titles(LibraryViews.Search([.. Entries, .. pictures], "photo 2")));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AnEpisodeAutoplaysOnlyItsSeasonInEpisodeOrder()
    {
        var folder = Path.Combine("D:\\Videos", "The Rehearsal Season 1");
        var episodes = new[] { 10, 2, 1 }.Select(number => VideoAt(folder, $"The.Rehearsal.S01E{number:00}.1080p.WEBRip.x265-RARBG")).ToList();
        var anotherShow = VideoAt(folder, "Elsewhere.S01E03.1080p");
        var anotherFolder = VideoAt(Path.Combine("D:\\Videos", "Copy"), "The.Rehearsal.S01E04.1080p");
        var movie = VideoAt(folder, "A Movie");

        var detected = LibraryViews.EpisodeOf(episodes[1]);
        Assert.Equal(new VideoEpisode("The Rehearsal", 1, 2), detected);
        Assert.Equal("The Rehearsal · S01E02", detected!.DisplayName);
        Assert.Equal([1, 2, 10], LibraryViews.AutoplayVideos([.. episodes, anotherShow, anotherFolder, movie], episodes[1]).Select(entry => LibraryViews.EpisodeOf(entry)!.Episode));
        Assert.Equal([movie], LibraryViews.AutoplayVideos(episodes, movie));
        Assert.Equal([Song("Not video", null, null)], LibraryViews.AutoplayVideos([], Song("Not video", null, null)));
        Assert.Null(LibraryViews.EpisodeOf(VideoAt(folder, "S01E01")));
        Assert.Throws<ArgumentNullException>(() => LibraryViews.EpisodeOf(null!));
        Assert.Throws<ArgumentNullException>(() => LibraryViews.AutoplayVideos(null!, episodes[0]));
        Assert.Throws<ArgumentNullException>(() => LibraryViews.AutoplayVideos(episodes, null!));
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData("", new string[0])]
    [InlineData("  ", new string[0])]
    [InlineData(null, new string[0])]
    [InlineData("work", new[] { "Lonely At The Top", "Terminator", "Bonus" })]
    [InlineData("ASAKE  sungba", new[] { "Sungba" })]
    [InlineData("ore", new[] { "Feature", "Other" })]
    [InlineData("episode", new[] { "Episode 2", "Episode 10" })]
    [InlineData("demo.mp3", new[] { "Demo" })]
    [InlineData("afro single", new string[0])]
    public void SearchFindsEveryWordAnywhere(string? text, string[] found) => Assert.Equal(found, Titles(LibraryViews.Search(Entries, text)));
}
