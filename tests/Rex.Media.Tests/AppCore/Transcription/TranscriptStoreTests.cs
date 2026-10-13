using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Transcription;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Transcription;

public sealed class TranscriptStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("rexplayer-transcripts-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static readonly Transcript Heard = new("en", "base.en", [new TranscriptWord("Hello.", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5))]);

    private string Media(string name, byte[] bytes)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc));
        return path;
    }

    [Fact]
    [Capability("SUB-20")]
    public void AMovedOrRenamedFileKeepsItsTranscriptAndAChangedOneLosesIt()
    {
        var cache = new TranscriptCache(Path.Combine(_directory, "cache"));
        var film = Media("film.mkv", [1, 2, 3]);
        cache.Save(film, Heard);

        var moved = Path.Combine(_directory, "moved.mkv");
        File.Move(film, moved);
        Assert.Equal("Hello.", cache.Load(moved, "base.en")!.Words[0].Text);
        Assert.Equal(cache.PathFor(moved, "base.en"), cache.PathFor(TranscriptSignature.Of(3, new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc)), "base.en"));

        File.WriteAllBytes(moved, [1, 2, 3, 4]);
        Assert.Null(cache.Load(moved, "base.en"));
        Assert.Equal("3-" + new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc).Ticks.ToString(CultureInfo.InvariantCulture), TranscriptSignature.Of(3, new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc)).ToString());
        Assert.Throws<ArgumentNullException>(() => cache.PathFor(default(TranscriptSignature), null!));
    }

    [Fact]
    [Capability("SUB-20")]
    public void ATranscriptKeptByNameBeforeIsMovedToItsSignatureWhenRead()
    {
        var cache = new TranscriptCache(Path.Combine(_directory, "cache"));
        var film = Media("film.mkv", [1, 2, 3]);
        var info = new FileInfo(film);
        var identity = string.Create(CultureInfo.InvariantCulture, $"v1|base.en|{Path.GetFullPath(film)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
        var legacy = Path.Combine(cache.Root, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32] + ".json");
        Directory.CreateDirectory(cache.Root);
        File.WriteAllText(legacy, Heard.ToJson());

        Assert.Equal("Hello.", cache.Load(film, "base.en")!.Words[0].Text);
        Assert.False(File.Exists(legacy));
        Assert.True(File.Exists(cache.PathFor(film, "base.en")));
        Assert.Null(cache.Load(Path.Combine(_directory, "missing.mkv"), "base.en"));
    }

    [Fact]
    [Capability("SUB-21")]
    public void ThePartHeardIsKeptBesideTheTranscriptAndEverythingCanBeMeasuredAndCleared()
    {
        var cache = new TranscriptCache(Path.Combine(_directory, "cache"));
        Assert.Equal((0, 0L), cache.Usage());
        Assert.Equal(0, cache.Clear());
        var film = Media("film.mkv", [1, 2, 3]);
        Assert.Null(cache.LoadPartial(film, "base.en"));

        var partial = new ProgressiveTranscript("base.en", "en", TimeSpan.FromMinutes(1));
        partial.Add(new SpeechChunk(new(TimeSpan.Zero, TimeSpan.FromSeconds(26)), new(TimeSpan.Zero, TimeSpan.FromSeconds(28))), Heard.Words, "en");
        cache.SavePartial(film, partial);
        Assert.Equal(partial.Done.Ranges, cache.LoadPartial(film, "base.en")!.Done.Ranges);
        cache.ForgetPartial(film, "base.en");
        Assert.Null(cache.LoadPartial(film, "base.en"));

        cache.SavePartial(film, partial);
        cache.Save(film, Heard);
        File.WriteAllText(Path.Combine(cache.Root, "left.json.part"), "x");
        var (files, bytes) = cache.Usage();
        Assert.Equal(2, files);
        Assert.True(bytes > 0);
        cache.Forget(film, "base.en");
        Assert.Equal((0, 0L), cache.Usage());

        cache.Save(film, Heard);
        cache.SavePartial(film, partial);
        cache.Forget(TranscriptSignature.Of(3, new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc)), "base.en");
        Assert.Equal(0, cache.Usage().Files);

        cache.Save(film, Heard);
        Assert.True(cache.Clear() > 0);
        Assert.Empty(Directory.GetFiles(cache.Root));
        Assert.Throws<ArgumentNullException>(() => cache.SavePartial(film, null!));

        // A part that cannot be read is no part.
        cache.SavePartial(film, partial);
        using (new FileStream(Path.Combine(cache.Root, Path.GetFileNameWithoutExtension(cache.PathFor(film, "base.en")) + ".partial.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Null(cache.LoadPartial(film, "base.en"));
        }
    }

    [Fact]
    [Capability("SUB-20")]
    public void TheLedgerIsReadWhenFirstAskedAndWrittenWholeOnEveryChange()
    {
        var path = Path.Combine(_directory, "cache", "transcription.json");
        Assert.Equal(Path.Combine("data", "cache", "transcription.json"), TranscriptionLedger.DefaultPath("data"));
        var ledger = new TranscriptionLedger(path);
        Assert.False(ledger.Loaded);
        Assert.Empty(ledger.All);
        Assert.True(ledger.Loaded);
        Assert.False(File.Exists(path));

        var record = new TranscriptRecord { Path = @"D:\a.mkv", Signature = new(10, 20), Model = "base.en", State = TranscriptState.Done, Words = 4 };
        ledger.Set(record);
        ledger.Set(record with { Path = @"D:\b.mkv", Signature = new(11, 21), State = TranscriptState.Failed, Reason = "busy", NextTry = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) });
        Assert.True(File.Exists(path));

        var again = new TranscriptionLedger(path);
        Assert.Equal(2, again.All.Count);
        Assert.Equal(record, again.Find(new(10, 20)));
        Assert.Equal("busy", again.Find(new(11, 21))!.Reason);
        Assert.Null(again.Find(new(1, 1)));

        Assert.Single(again.Remove(found => found.State == TranscriptState.Failed));
        Assert.Empty(again.Remove(_ => false));
        Assert.Single(new TranscriptionLedger(path).All);

        // A damaged ledger is an empty one.
        File.WriteAllText(path, "{broken");
        Assert.Empty(new TranscriptionLedger(path).All);
        File.WriteAllText(path, "null");
        Assert.Empty(new TranscriptionLedger(path).All);

        Assert.Throws<ArgumentException>(() => new TranscriptionLedger(" "));
        Assert.Throws<ArgumentNullException>(() => TranscriptionLedger.DefaultPath(null!));
        Assert.Throws<ArgumentNullException>(() => again.Set(null!));
        Assert.Throws<ArgumentNullException>(() => again.Remove(null!));
    }

    [Fact]
    [Capability("SUB-20")]
    public void ChangesFromManyThreadsAllLand()
    {
        var path = Path.Combine(_directory, "cache", "transcription.json");
        var ledger = new TranscriptionLedger(path);
        Parallel.For(0, 64, i => ledger.Set(new TranscriptRecord { Path = "f" + i, Signature = new(i, i), Model = "m", State = TranscriptState.Done }));
        Assert.Equal(64, new TranscriptionLedger(path).All.Count);
    }
}
