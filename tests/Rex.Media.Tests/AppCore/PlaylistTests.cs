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
    public void PlayNextStaysImmediatelyNextAndInSelectionOrderEvenWithShuffle()
    {
        var playlist = With(4, new Random(19));
        playlist.Shuffle = true;
        var first = playlist.Next(automatic: false)!;
        var selected = new[]
        {
            new PlaylistItem("C:\\Music\\special-a.flac"),
            new PlaylistItem("C:\\Music\\special-b.flac"),
        };

        playlist.InsertNext(playlist.CurrentIndex + 1, selected);
        Assert.Same(first, playlist.Current);
        Assert.Same(selected[0], playlist.PeekNext(automatic: false));
        Assert.Same(selected[0], playlist.Next(automatic: false));
        Assert.Same(selected[1], playlist.Next(automatic: false));
        Assert.Throws<ArgumentNullException>(() => playlist.InsertNext(0, null!));
        playlist.InsertNext(0, []);
        var remaining = new List<PlaylistItem>();
        for (var item = playlist.Next(automatic: false); item is not null; item = playlist.Next(automatic: false))
        {
            remaining.Add(item);
        }

        Assert.Equal(3, remaining.Count);
        Assert.DoesNotContain(selected[0], remaining);
        Assert.DoesNotContain(selected[1], remaining);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueueNextMovesExistingEntriesWithoutDuplicatingOrRestartingPlayback(bool shuffle)
    {
        var playlist = With(5, new Random(4));
        playlist.Shuffle = shuffle;
        var playing = playlist.Next(automatic: false)!;
        var chosen = playlist.Items.Where(item => !ReferenceEquals(item, playing)).Take(2).ToArray();
        var original = playlist.Items.ToArray();

        playlist.QueueNext([chosen[1], chosen[0], chosen[1], playing, new PlaylistItem("absent.wav")]);

        Assert.Same(playing, playlist.Current);
        Assert.Equal(original.Length, playlist.Items.Count);
        Assert.True(original.ToHashSet(ReferenceEqualityComparer.Instance).SetEquals(playlist.Items));
        Assert.Same(chosen[1], playlist.PeekNext(automatic: false));
        Assert.Same(chosen[1], playlist.Next(automatic: false));
        Assert.Same(chosen[0], playlist.Next(automatic: false));
        Assert.Equal(original.Length, playlist.Items.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Throws<ArgumentNullException>(() => playlist.QueueNext(null!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UndoReorderRestoresOriginalEntryIdentitiesWithoutRestartingPlayback(bool shuffle)
    {
        var playlist = With(4, new Random(7));
        playlist.Shuffle = shuffle;
        playlist.Next(automatic: false);
        playlist.Next(automatic: false);
        var playing = playlist.Current;
        var original = playlist.Items.ToArray();
        var notifications = 0;
        playlist.Changed += (_, _) => notifications++;
        playlist.Move(3, 0);
        Assert.True(playlist.CanRestoreOrder(original));
        Assert.True(playlist.RestoreOrder(original));
        Assert.Equal(original, playlist.Items);
        Assert.Same(playing, playlist.Current);
        Assert.Equal(2, notifications);

        var another = new PlaylistItem(original[0].Location);
        Assert.False(playlist.CanRestoreOrder([another, original[1], original[2], original[3]]));
        Assert.Throws<ArgumentNullException>(() => playlist.RestoreOrder(null!));
        Assert.Throws<ArgumentNullException>(() => playlist.CanRestoreOrder(null!));
        Assert.False(playlist.CanRestoreOrder([]));
        playlist.Add([another]);
        Assert.False(playlist.RestoreOrder(original));
    }

    [Fact]
    public void QueueNextAfterEndStartsWithChosenEntryAndEmptySelectionDoesNotNotify()
    {
        var playlist = With(3);
        var changes = 0;
        playlist.Changed += (_, _) => changes++;
        playlist.QueueNext([]);
        playlist.QueueNext([new PlaylistItem("not-here.wav")]);
        Assert.Equal(0, changes);
        PlayAll(playlist, 4);
        var chosen = playlist.Items[2];
        playlist.QueueNext([chosen]);
        Assert.Equal(5, changes);
        Assert.Same(chosen, playlist.Next(automatic: false));
    }

    [Fact]
    public void QueueNextAfterRemovingCurrentPreservesTheFollowingPlace()
    {
        var playlist = With(5);
        playlist.Next(automatic: false);
        playlist.Next(automatic: false);
        playlist.RemoveAt(1);
        var firstNext = playlist.Items[2];
        playlist.QueueNext([firstNext]);
        Assert.Same(firstNext, playlist.PeekNext(automatic: false));
        Assert.Same(firstNext, playlist.Next(automatic: false));
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
    public void ItemsPutNextAfterRemovingWhatIsPlayingStillPlayNext()
    {
        var playlist = With(4);
        Assert.Equal(0, playlist.NextIndex);
        playlist.Next(automatic: false);
        playlist.Next(automatic: false);
        Assert.Equal(2, playlist.NextIndex);

        playlist.RemoveAt(1);
        Assert.Equal(1, playlist.NextIndex);
        playlist.InsertNext(playlist.NextIndex, [new PlaylistItem("C:\\Music\\x.flac")]);

        Assert.Equal(["1", "x", "3", "4"], playlist.Items.Select(i => i.Title));
        Assert.Equal(["x", "3", "4", "end"], PlayAll(playlist, 4));
        Assert.Equal(0, playlist.NextIndex);
    }

    [Fact]
    public void InsertingAboveWhereARemovedItemWasStillCarriesOnFromThere()
    {
        var playlist = With(4);
        playlist.Next(automatic: false);
        playlist.Next(automatic: false);
        playlist.Next(automatic: false);
        playlist.RemoveAt(2);

        playlist.Insert(0, [new PlaylistItem("C:\\Music\\y.flac")]);

        Assert.Equal(["y", "1", "2", "4"], playlist.Items.Select(i => i.Title));
        Assert.Equal(3, playlist.NextIndex);
        Assert.Equal(["4", "end"], PlayAll(playlist, 2));
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

    [Theory]
    [InlineData("C:\\Music\\Live\\Sungba.flac", "Live")]
    [InlineData("/music/studio/Sungba.flac", "studio")]
    [InlineData("https://radio.example/live/Sungba.mp3", "radio.example")]
    [InlineData("Sungba.flac", null)]
    public void SourcesDistinguishEquallyNamedItems(string location, string? source)
    {
        Assert.Equal(source, PlaylistItem.SourceOf(location));
    }
}
