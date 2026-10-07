using Rex.Media.AppCore.Player;
using Rex.Media.Settings;

namespace Rex.Media.Tests.AppCore;

public sealed class PlaylistTests
{
    private static Playlist With(int count, Random? random = null)
    {
        var playlist = new Playlist(random ?? new Random(7));
        playlist.Add(Enumerable.Range(1, count).Select(i => new PlaylistItem($"C:\\Music\\{i}.flac")));
        return playlist;
    }

    private static List<string> PlayAll(Playlist playlist, int steps) =>
        [.. Enumerable.Range(0, steps).Select(_ => playlist.Next(automatic: true)?.Title ?? "end")];

    [Fact]
    public void ItemsPlayInOrderAndTheEndIsReported()
    {
        var playlist = With(3);
        var changes = 0;
        playlist.Changed += (_, _) => changes++;

        Assert.Equal(["1", "2", "3", "end", "end"], PlayAll(playlist, 5));
        Assert.Null(playlist.Current);
        Assert.Equal(-1, playlist.CurrentIndex);
        Assert.Equal(5, changes);
    }

    [Fact]
    public void RepeatAllWrapsAndRepeatOneReplaysOnlyWhenAnItemEndsByItself()
    {
        var playlist = With(2);
        playlist.Repeat = RepeatMode.All;
        Assert.Equal(["1", "2", "1", "2"], PlayAll(playlist, 4));

        playlist.Repeat = RepeatMode.One;
        Assert.Equal("2", playlist.PeekNext(automatic: true)!.Title);
        Assert.Equal(["2", "2"], PlayAll(playlist, 2));
        Assert.Equal("1", playlist.Next(automatic: false)!.Title);
    }

    [Fact]
    public void RepeatOneBeforeAnythingPlaysStartsAtTheTop()
    {
        var playlist = With(2);
        playlist.Repeat = RepeatMode.One;

        Assert.Equal("1", playlist.Next(automatic: true)!.Title);
    }

    [Fact]
    public void APeekAlwaysAgreesWithTheMoveThatFollowsIt()
    {
        foreach (var shuffle in new[] { false, true })
        {
            var playlist = With(5, new Random(3));
            playlist.Repeat = RepeatMode.All;
            playlist.Shuffle = shuffle;
            for (var i = 0; i < 23; i++)
            {
                var peeked = playlist.PeekNext(automatic: true);
                Assert.Same(peeked, playlist.Next(automatic: true));
            }
        }

        Assert.Null(new Playlist().PeekNext(automatic: false));
    }

    [Fact]
    public void ShufflePlaysEverythingOnceBeforeAnythingAgainAndNeverTheSameTwiceInARow()
    {
        var playlist = With(6, new Random(11));
        playlist.Repeat = RepeatMode.All;
        playlist.Shuffle = true;

        var played = PlayAll(playlist, 60);

        foreach (var pass in played.Chunk(6))
        {
            Assert.Equal(6, pass.Distinct().Count());
        }

        Assert.All(played.Zip(played.Skip(1)), pair => Assert.NotEqual(pair.First, pair.Second));
    }

    [Fact]
    public void TurningShuffleOnKeepsTheCurrentItemAndOffResumesTheListOrderFromIt()
    {
        var playlist = With(5, new Random(5));
        playlist.Next(automatic: false);
        playlist.Next(automatic: false);

        playlist.Shuffle = true;
        playlist.Shuffle = true;
        Assert.Equal("2", playlist.Current!.Title);
        var rest = PlayAll(playlist, 3);
        Assert.Equal(3, rest.Distinct().Count());
        Assert.DoesNotContain("2", rest);

        playlist.Shuffle = false;
        var expected = playlist.CurrentIndex + 2 <= 5 ? $"{playlist.CurrentIndex + 2}" : "end";
        Assert.Equal(expected, playlist.Next(automatic: true)?.Title ?? "end");
    }

    [Fact]
    public void ShuffleTurnedOnBeforeAnythingPlaysCoversEveryItem()
    {
        var playlist = With(4);
        playlist.Shuffle = true;

        Assert.Equal(["1", "2", "3", "4"], PlayAll(playlist, 4).Order());
    }

    [Fact]
    public void ItemsAddedWhileShufflingJoinThePartStillToPlay()
    {
        var playlist = With(3, new Random(2));
        playlist.Shuffle = true;
        playlist.Next(automatic: true);
        playlist.Next(automatic: true);

        playlist.Add([new PlaylistItem("C:\\Music\\4.flac"), new PlaylistItem("C:\\Music\\5.flac")]);

        var rest = PlayAll(playlist, 4);
        Assert.Equal(["4", "5", "end"], rest.Where(t => t is "4" or "5" or "end").Distinct().Order());
        Assert.Equal("end", rest[^1]);
    }

    [Fact]
    public void InsertingMovingAndRemovingKeepTheCurrentItemPlaying()
    {
        var playlist = With(4);
        playlist.Next(automatic: false);
        playlist.Next(automatic: false);

        playlist.Insert(0, [new PlaylistItem("C:\\Music\\0.flac")]);
        Assert.Equal(2, playlist.CurrentIndex);
        playlist.Move(4, 0);
        Assert.Equal(["4", "0", "1", "2", "3"], playlist.Items.Select(i => i.Title));
        Assert.Equal("2", playlist.Current!.Title);
        Assert.Equal("3", playlist.Next(automatic: true)!.Title);

        playlist.RemoveAt(0);
        Assert.Equal("3", playlist.Current!.Title);
        Assert.Equal("2", playlist.Previous()!.Title);
    }

    [Fact]
    public void RemovingWhatIsPlayingCarriesOnWithWhatFollowedIt()
    {
        var playlist = With(4);
        playlist.Next(automatic: false);
        playlist.Next(automatic: false);

        playlist.RemoveAt(1);
        Assert.Null(playlist.Current);
        Assert.Equal("3", playlist.PeekNext(automatic: true)!.Title);
        Assert.Equal("1", playlist.Previous()!.Title);

        playlist.RemoveAt(0);
        playlist.Insert(0, [new PlaylistItem("C:\\Music\\x.flac")]);
        Assert.Equal("x", playlist.Next(automatic: true)!.Title);
    }

    [Fact]
    public void MovingWhileShufflingLeavesThePlayOrderAlone()
    {
        var playlist = With(4, new Random(9));
        playlist.Shuffle = true;
        var first = playlist.PeekNext(automatic: false);

        playlist.Move(0, 3);

        Assert.Same(first, playlist.Next(automatic: false));
    }

    [Fact]
    public void PreviousStopsAtTheFirstItemOrWrapsWithRepeatAll()
    {
        Assert.Null(new Playlist().Previous());

        var playlist = With(3);
        Assert.Equal("1", playlist.Previous()!.Title);
        Assert.Equal("1", playlist.Previous()!.Title);

        playlist.Repeat = RepeatMode.All;
        Assert.Equal("3", playlist.Previous()!.Title);

        var ended = With(3);
        PlayAll(ended, 4);
        Assert.Equal("3", ended.Previous()!.Title);
    }

    [Fact]
    public void JumpingPlaysThePickedItemThenCarriesOn()
    {
        var playlist = With(4);
        Assert.Equal("3", playlist.JumpTo(2).Title);
        Assert.Equal("4", playlist.Next(automatic: true)!.Title);

        // Shuffled: the pick plays next whether it had already played or was still to come.
        var shuffled = With(5, new Random(4));
        shuffled.Shuffle = true;
        var first = shuffled.Next(automatic: true)!;
        var second = shuffled.Next(automatic: true)!;
        var later = shuffled.Items.First(item => item != first && item != second);
        Assert.Same(later, shuffled.JumpTo(shuffled.Items.ToList().IndexOf(later)));
        Assert.Equal(["end"], PlayAll(shuffled, 3).Skip(2));

        // Picked again after the end: it plays, then the playlist ends once more.
        Assert.Same(first, shuffled.JumpTo(shuffled.Items.ToList().IndexOf(first)));
        Assert.Same(first, shuffled.Current);
        Assert.Equal(["end"], PlayAll(shuffled, 1));
    }

    [Fact]
    public void ClearingEmptiesEverything()
    {
        var playlist = With(3);
        playlist.Next(automatic: false);

        playlist.Clear();

        Assert.Empty(playlist.Items);
        Assert.Null(playlist.Current);
        Assert.Null(playlist.Next(automatic: true));
    }

    [Theory]
    [InlineData("C:\\Music\\01 Lonely At The Top.flac", "01 Lonely At The Top")]
    [InlineData("https://example.com/radio/Sungba%20Remix.mp3?x=1", "Sungba Remix")]
    [InlineData("https://example.com/", "https://example.com/")]
    [InlineData("C:\\Music\\.flac", "C:\\Music\\.flac")]
    public void TitlesComeFromTheFileName(string location, string title)
    {
        Assert.Equal(title, new PlaylistItem(location).Title);
    }
}
