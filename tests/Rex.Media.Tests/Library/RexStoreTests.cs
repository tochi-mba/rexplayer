using System.Globalization;
using System.Text;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Library;

/// <summary>The store of what the player remembers, and what it keeps there.</summary>
public sealed class RexStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rexplayer-store-" + Guid.NewGuid().ToString("N"));

    private string StorePath => Path.Combine(_folder, "nested", "store.log");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    [Capability("LIB-11")]
    public void ChangesSurviveReopening()
    {
        var store = RexStore.Open(StorePath);
        Assert.Equal(0, store.Count);
        Assert.False(File.Exists(StorePath));

        store.Set("a", "1");
        store.Set("b/1", "two");
        store.Set("b/2", "three");
        store.Set("a", "1");
        store.Set("a", "one");
        Assert.True(store.Remove("b/1"));
        Assert.False(store.Remove("missing"));

        var again = RexStore.Open(StorePath);
        Assert.Equal(StorePath, again.Path);
        Assert.Equal("one", again.Get("a"));
        Assert.Null(again.Get("b/1"));
        Assert.Equal([new KeyValuePair<string, string>("b/2", "three")], again.WithPrefix("b/"));
        Assert.Equal(2, again.Count);
        Assert.Equal(0, again.Skipped);

        // Five changes and a removal: five lines, the unchanged value written once.
        Assert.Equal(5, File.ReadAllLines(StorePath).Length);
    }

    [Fact]
    [Capability("LIB-11")]
    public void ATornLastWriteLosesOnlyThatChange()
    {
        var store = RexStore.Open(StorePath);
        store.Set("kept", "yes");
        store.Set("torn", "partly");
        var bytes = File.ReadAllBytes(StorePath);
        File.WriteAllBytes(StorePath, bytes[..^7]);
        File.AppendAllText(StorePath, "\nnot a record\n12345678 {}\n\n");

        var again = RexStore.Open(StorePath);

        Assert.Equal("yes", again.Get("kept"));
        Assert.Null(again.Get("torn"));
        Assert.Equal(3, again.Skipped);
    }

    [Fact]
    public void ALineThatChecksOutButIsNoRecordIsSkipped()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        string Line(string json) => Crc.Crc32.Compute(Encoding.UTF8.GetBytes(json)).ToString("x8", CultureInfo.InvariantCulture) + " " + json;
        File.WriteAllLines(StorePath, [Line("not json"), Line("[1]"), Line("{\"k\":1}"), Line("{\"k\":\"a\",\"v\":2}"), "zzzzzzzz {}", "1234567"]);

        var store = RexStore.Open(StorePath);

        Assert.Equal(5, store.Skipped);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    [Capability("LIB-11")]
    public void ALogOfMostlyOldChangesIsRewrittenAsJustTheValues()
    {
        var store = RexStore.Open(StorePath);
        for (var i = 0; i < 70; i++)
        {
            store.Set("counter", i.ToString(CultureInfo.InvariantCulture));
        }

        Assert.True(File.ReadAllLines(StorePath).Length < 20);
        Assert.Equal("69", RexStore.Open(StorePath).Get("counter"));

        store.Set("x/1", "a");
        store.Set("x/2", "b");
        Assert.Equal(2, store.RemovePrefix("x/"));
        Assert.Equal(0, store.RemovePrefix("x/"));
        Assert.Single(File.ReadAllLines(StorePath));
    }

    [Fact]
    public void AStoreInMemoryKeepsNothingOnDisk()
    {
        var store = RexStore.InMemory();
        store.Set("a", "1");
        store.Remove("a");
        store.Set("b", "2");
        store.Compact();

        Assert.Null(store.Path);
        Assert.Equal("2", store.Get("b"));
        Assert.Throws<ArgumentNullException>(() => RexStore.Open(null!));
        Assert.Throws<ArgumentNullException>(() => store.Get(null!));
        Assert.Throws<ArgumentNullException>(() => store.Set(null!, ""));
        Assert.Throws<ArgumentNullException>(() => store.Set("", null!));
        Assert.Throws<ArgumentNullException>(() => store.Remove(null!));
        Assert.Throws<ArgumentNullException>(() => store.RemovePrefix(null!));
        Assert.Throws<ArgumentNullException>(() => store.WithPrefix(null!));
    }

    [Fact]
    [Capability("PB-11")]
    public void ResumePointsAreKeptOnlyAwayFromTheEnds()
    {
        var memory = new PlayerMemory(RexStore.InMemory());
        var hour = TimeSpan.FromHours(1);

        memory.Left(@"C:\Films\a.mkv", TimeSpan.FromMinutes(20), hour);
        Assert.Equal(TimeSpan.FromMinutes(20), memory.ResumePoint(@"c:\films\A.MKV"));

        memory.Left(@"C:\Films\a.mkv", TimeSpan.FromSeconds(5), hour);
        Assert.Null(memory.ResumePoint(@"C:\Films\a.mkv"));
        memory.Left(@"C:\Films\a.mkv", hour - TimeSpan.FromSeconds(5), hour);
        Assert.Null(memory.ResumePoint(@"C:\Films\a.mkv"));

        // A stream of unknown length is worth resuming anywhere past the start; URLs keep their case.
        memory.Left("https://example.com/A", TimeSpan.FromMinutes(1), TimeSpan.Zero);
        Assert.NotNull(memory.ResumePoint("https://example.com/A"));
        Assert.Null(memory.ResumePoint("https://example.com/a"));

        // The store holds hashes, never the locations.
        Assert.DoesNotContain(memory.Store.WithPrefix(""), pair => pair.Key.Contains("Films", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(32, PlayerMemory.KeyFor("x").Length);
        Assert.Throws<ArgumentNullException>(() => PlayerMemory.KeyFor(null!));
        Assert.Throws<ArgumentNullException>(() => new PlayerMemory(null!));
    }

    [Fact]
    [Capability("PRIV-03")]
    public void HistoryCanBeTurnedOffAndCleared()
    {
        var memory = new PlayerMemory(RexStore.InMemory());
        memory.Left("a.mkv", TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));
        memory.Played("a.mkv");
        memory.SetBookmarks("a.mkv", [new Bookmark("Start", TimeSpan.Zero)]);

        memory.KeepsHistory = false;
        memory.Left("b.mkv", TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));
        memory.Played("b.mkv");
        Assert.Null(memory.ResumePoint("b.mkv"));
        Assert.Equal(["a.mkv"], memory.Recent);

        // Even with history off, a point near the end is forgotten.
        memory.Left("a.mkv", TimeSpan.FromSeconds(1), TimeSpan.FromHours(1));
        Assert.Null(memory.ResumePoint("a.mkv"));

        memory.Left("c.mkv", TimeSpan.FromSeconds(1), TimeSpan.FromHours(1));
        memory.KeepsHistory = true;
        memory.Left("c.mkv", TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));
        memory.ClearHistory();
        Assert.Null(memory.ResumePoint("c.mkv"));
        Assert.Empty(memory.Recent);
        Assert.Single(memory.Bookmarks("a.mkv"));
    }

    [Fact]
    [Capability("LIB-07")]
    public void RecentMediaIsNewestFirstWithoutRepeats()
    {
        var memory = new PlayerMemory(RexStore.InMemory());
        for (var i = 0; i < PlayerMemory.RecentLimit + 5; i++)
        {
            memory.Played($"{i}.mp3");
        }

        memory.Played("21.mp3");

        Assert.Equal(PlayerMemory.RecentLimit, memory.Recent.Count);
        Assert.Equal(["21.mp3", "24.mp3", "23.mp3"], memory.Recent.Take(3));
        Assert.Throws<ArgumentNullException>(() => memory.Played(null!));
    }

    [Fact]
    [Capability("PB-10")]
    public void BookmarksAreKeptInOrderPerFile()
    {
        var memory = new PlayerMemory(RexStore.InMemory());
        memory.SetBookmarks("a.mkv", [new Bookmark("Late", TimeSpan.FromMinutes(9)), new Bookmark("Early", TimeSpan.FromMinutes(1))]);

        Assert.Equal(["Early", "Late"], memory.Bookmarks("A.MKV").Select(b => b.Name));
        Assert.Empty(memory.Bookmarks("b.mkv"));

        memory.SetBookmarks("a.mkv", []);
        Assert.Empty(memory.Bookmarks("a.mkv"));
        Assert.Throws<ArgumentNullException>(() => memory.SetBookmarks("a.mkv", null!));
    }

    [Fact]
    [Capability("LIB-08")]
    public void QuickSlotsAndTheQueueAreRemembered()
    {
        var store = RexStore.InMemory();
        var memory = new PlayerMemory(store);
        memory.SetSlot(3, new QuickSlot("a.mp3", TimeSpan.FromSeconds(42)));

        Assert.Equal(new QuickSlot("a.mp3", TimeSpan.FromSeconds(42)), memory.Slot(3));
        Assert.Null(memory.Slot(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => memory.Slot(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => memory.Slot(PlayerMemory.Slots + 1));
        Assert.Throws<ArgumentNullException>(() => memory.SetSlot(1, null!));

        var queue = new QueueSnapshot([new QueuedItem("a.flac", "Olorun", "Asake", TimeSpan.Zero, TimeSpan.FromMinutes(3))], 0, TimeSpan.FromSeconds(30));
        memory.Queue = queue;
        Assert.Equal(queue.Items.Single(), memory.Queue!.Items.Single());
        Assert.Equal((0, TimeSpan.FromSeconds(30)), (memory.Queue.Current, memory.Queue.At));
        memory.Queue = queue with { Items = [] };
        Assert.Null(memory.Queue);
        memory.Queue = queue;
        memory.Queue = null;
        Assert.Null(memory.Queue);

        // A value from some other version that no longer reads is as good as none.
        store.Set("slot/5", "{not json");
        Assert.Null(memory.Slot(5));
    }
}
