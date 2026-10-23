using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.Tests.AppCore;

public sealed class SavedPlaylistFactsTests
{
    private static QueuedItem Item(string location, TimeSpan? start = null, TimeSpan? end = null) =>
        new(location, null, null, start ?? TimeSpan.Zero, end);

    [Fact]
    public void EmptyAndUnclassifiedMediaDoNotInventDurationOrCategory()
    {
        Assert.Equal("Empty playlist", SavedPlaylistFacts.For(new NamedPlaylist("1", "Empty", []), _ => null).Label);
        var mix = new NamedPlaylist("2", "Streams", [Item("https://example.test/stream"), Item("unusual-file.abc")]);
        var facts = SavedPlaylistFacts.For(mix, _ => null);
        Assert.Equal((2, 0, 0, 0, 2, 0, TimeSpan.Zero),
            (facts.Count, facts.Audio, facts.Video, facts.Pictures, facts.Other, facts.Timed, facts.KnownDuration));
        Assert.Equal("2 unclassified items", facts.Label);
        Assert.Throws<ArgumentNullException>(() => SavedPlaylistFacts.For(null!, _ => null));
        Assert.Throws<ArgumentNullException>(() => SavedPlaylistFacts.For(mix, null!));
    }

    [Fact]
    public void MixedMediaUseProbedClassificationAndOnlyProvenTimings()
    {
        var playlist = new NamedPlaylist("1", "Mixed",
        [
            Item("music.mp3"),
            Item("song.mp4"),
            Item("movie.mp4", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)),
            Item("picture.png"),
            Item("missing.mp3"),
            Item("https://example.test/live"),
        ]);
        var library = new Dictionary<string, LibraryEntry>
        {
            ["music.mp3"] = Entry("music.mp3", LibraryKind.Music, TimeSpan.FromMinutes(4)),
            ["song.mp4"] = Entry("song.mp4", LibraryKind.Music, TimeSpan.FromMinutes(3)),
            ["movie.mp4"] = Entry("movie.mp4", LibraryKind.Video, TimeSpan.FromMinutes(10)),
        };
        var facts = SavedPlaylistFacts.For(playlist, path => library.GetValueOrDefault(path));
        Assert.Equal((6, 3, 1, 1, 1, 3),
            (facts.Count, facts.Audio, facts.Video, facts.Pictures, facts.Other, facts.Timed));
        Assert.Equal(TimeSpan.FromMinutes(8), facts.KnownDuration);
        Assert.Contains("8:00 known duration", facts.Label, StringComparison.Ordinal);
        Assert.Contains("3 audio items", facts.Label, StringComparison.Ordinal);
        Assert.Contains("1 video", facts.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void KnownCueBoundsAreUsedAndClampedToKnownFileLength()
    {
        var playlist = new NamedPlaylist("x", "Cue",
        [
            Item("a.wav", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120)),
            Item("b.wav", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(90)),
            Item("c.wav", TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(30)),
            Item("d.wav", TimeSpan.FromSeconds(20)),
        ]);
        var facts = SavedPlaylistFacts.For(playlist, path => path == "b.wav"
            ? Entry("b.wav", LibraryKind.Music, TimeSpan.FromSeconds(60))
            : null);
        Assert.Equal(2, facts.Timed);
        Assert.Equal(TimeSpan.FromSeconds(140), facts.KnownDuration);
        Assert.EndsWith("known duration", facts.Label, StringComparison.Ordinal);
    }

    private static LibraryEntry Entry(string path, LibraryKind kind, TimeSpan duration) =>
        new() { Path = path, Title = path, Kind = kind, Duration = duration };
}
