using Rex.Media.AppCore.Transcription;
using Rex.Media.Library;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Transcription;

public sealed class TranscriptionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] Folders = [@"D:\Videos", @"E:\"];

    private static LibraryEntry Entry(string path, LibraryKind kind = LibraryKind.Video, double minutes = 40, bool probed = true, int added = 0, long size = 1000) => new()
    {
        Path = path,
        Kind = kind,
        Title = Path.GetFileNameWithoutExtension(path),
        Probed = probed,
        Duration = TimeSpan.FromMinutes(minutes),
        Added = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(added),
        Size = size,
        Modified = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc),
    };

    private static TranscriptRecord Record(LibraryEntry entry, TranscriptState state, double share = 0) =>
        new() { Path = entry.Path, Signature = TranscriptionOrder.Sign(entry), Model = "base.en", State = state, Share = share };

    [Fact]
    [Capability("SUB-20")]
    public void TheScopeIsTheWholeLibraryOrTheChosenFoldersLessTheOnesLeftOutNearestWinning()
    {
        var whole = new TranscriptionSettings { Library = true, Excluded = [@"D:\Videos\Home movies"] };
        Assert.True(TranscriptionEligibility.InScope(@"D:\Videos\Show\e1.mkv", whole, Folders));
        Assert.True(TranscriptionEligibility.InScope(@"E:\film.mkv", whole, Folders));
        Assert.False(TranscriptionEligibility.InScope(@"D:\Videos\Home movies\birthday.mp4", whole, Folders));
        Assert.False(TranscriptionEligibility.InScope(@"C:\elsewhere.mkv", whole, Folders));

        var chosen = whole with { Scope = TranscriptionScope.ChosenFolders, Folders = [@"D:\Videos\Show", @"D:\Videos\Home movies\Talks"] };
        Assert.True(TranscriptionEligibility.InScope(@"D:\Videos\Show\e1.mkv", chosen, Folders));
        Assert.False(TranscriptionEligibility.InScope(@"D:\Videos\Other\e1.mkv", chosen, Folders));

        // A chosen folder inside a left-out one is nearer, so it is in; the same folder both ways is out.
        Assert.True(TranscriptionEligibility.InScope(@"D:\Videos\Home movies\Talks\t.mp4", chosen, Folders));
        Assert.False(TranscriptionEligibility.InScope(@"D:\Videos\Show\e1.mkv", chosen with { Excluded = [@"D:\Videos\Show\"] }, Folders));
        Assert.Throws<ArgumentNullException>(() => TranscriptionEligibility.InScope(null!, whole, Folders));
        Assert.Throws<ArgumentNullException>(() => TranscriptionEligibility.InScope("x", null!, Folders));
        Assert.Throws<ArgumentNullException>(() => TranscriptionEligibility.InScope("x", whole, null!));
    }

    [Fact]
    [Capability("SUB-20")]
    public void PicturesSongsShortFilesAndFilesWithoutSoundAreNotHeard()
    {
        var settings = new TranscriptionSettings { Library = true };
        TranscriptionSkip Of(LibraryEntry entry, TranscriptRecord? record = null, TranscriptionSettings? with = null) => TranscriptionEligibility.Of(entry, with ?? settings, Folders, record);

        Assert.Equal(TranscriptionSkip.None, Of(Entry(@"D:\Videos\a.mkv")));
        Assert.Equal(TranscriptionSkip.NotLocal, Of(Entry("https://example.com/a.mp4")));
        Assert.Equal(TranscriptionSkip.OutOfScope, Of(Entry(@"C:\a.mkv")));
        Assert.Equal(TranscriptionSkip.Picture, Of(Entry(@"D:\Videos\a.jpg", LibraryKind.Picture)));
        Assert.Equal(TranscriptionSkip.Music, Of(Entry(@"D:\Videos\Asake - Lonely At The Top.mp3", LibraryKind.Music)));
        Assert.Equal(TranscriptionSkip.None, Of(Entry(@"D:\Videos\Asake - Sungba.mp3", LibraryKind.Music), with: settings with { IncludeMusic = true }));
        var silent = Entry(@"D:\Videos\b.mkv");
        Assert.Equal(TranscriptionSkip.NoSound, Of(silent, Record(silent, TranscriptState.NoSound)));
        Assert.Equal(TranscriptionSkip.NotReadYet, Of(Entry(@"D:\Videos\c.mkv", probed: false)));
        Assert.Equal(TranscriptionSkip.TooShort, Of(Entry(@"D:\Videos\d.mkv", minutes: 0.05)));
        Assert.Equal(TranscriptionSkip.None, Of(Entry(@"D:\Videos\e.mkv") with { Duration = null }));
        Assert.Throws<ArgumentNullException>(() => TranscriptionEligibility.Of(null!, settings, Folders, null));

        foreach (var skip in Enum.GetValues<TranscriptionSkip>())
        {
            Assert.Equal(skip == TranscriptionSkip.None, TranscriptionEligibility.Describe(skip).Length == 0);
        }
    }

    [Fact]
    [Capability("SUB-20")]
    public void WhatPlaysDoesNotAutomaticallyTranscribeMusic()
    {
        Assert.True(TranscriptionEligibility.Playing(asked: true, hasPictures: false, whatPlays: false, backgroundEligible: false));
        Assert.True(TranscriptionEligibility.Playing(asked: false, hasPictures: true, whatPlays: true, backgroundEligible: false));
        Assert.True(TranscriptionEligibility.Playing(asked: false, hasPictures: false, whatPlays: false, backgroundEligible: true));
        Assert.False(TranscriptionEligibility.Playing(asked: false, hasPictures: false, whatPlays: true, backgroundEligible: false));
        Assert.False(TranscriptionEligibility.Playing(asked: false, hasPictures: true, whatPlays: false, backgroundEligible: false));
    }

    [Fact]
    [Capability("SUB-20")]
    public void TheQueueTakesWhatWasAskedThenPartHeardThenLeftPartWayThenNewest()
    {
        var old = Entry(@"D:\Videos\old.mkv", added: 1, size: 1);
        var newest = Entry(@"D:\Videos\new.mkv", added: 9, size: 2);
        var middle = Entry(@"D:\Videos\middle.mkv", added: 5, size: 3);
        var partial = Entry(@"D:\Videos\partial.mkv", added: 0, size: 4);
        var watching = Entry(@"D:\Videos\watching.mkv", added: 0, size: 5);
        var done = Entry(@"D:\Videos\done.mkv", size: 6);
        var failed = Entry(@"D:\Videos\failed.mkv", size: 7);
        var outside = Entry(@"C:\outside.mkv", size: 8);
        var song = Entry(@"D:\Videos\Asake - Terminator.mp3", LibraryKind.Music, size: 9);
        var twin = Entry(@"D:\Videos\twin.mkv", added: 1, size: 10);
        var picture = Entry(@"D:\Videos\p.jpg", LibraryKind.Picture, size: 11);
        LibraryEntry[] entries = [old, newest, middle, partial, watching, done, failed, outside, song, twin, picture];
        var records = new Dictionary<TranscriptSignature, TranscriptRecord>
        {
            [TranscriptionOrder.Sign(partial)] = Record(partial, TranscriptState.Partial, 0.25),
            [TranscriptionOrder.Sign(done)] = Record(done, TranscriptState.Done),
            [TranscriptionOrder.Sign(failed)] = Record(failed, TranscriptState.Failed) with { NextTry = Now.AddHours(1) },
        };
        var settings = new TranscriptionSettings { Library = true };
        IReadOnlyList<QueuedFile> Order(TranscriptionSettings with, params string[] asked) =>
            TranscriptionOrder.Order(entries, asked, with, Folders, records.GetValueOrDefault, path => path == watching.Path, Now);

        var queue = Order(settings, outside.Path, done.Path, song.Path, picture.Path, @"D:\Videos\unknown.mkv", outside.Path);
        Assert.Equal([outside.Path, song.Path, partial.Path, watching.Path, newest.Path, middle.Path, old.Path, twin.Path], queue.Select(file => file.Path));
        Assert.Equal([true, true, false, false, false, false, false, false], queue.Select(file => file.Asked));
        Assert.Equal(0.25, queue[2].Share);
        Assert.Equal(TimeSpan.FromMinutes(40 * 7) + TimeSpan.FromMinutes(30), TranscriptionOrder.SoundLeft(queue));

        // Off: only what was asked.
        Assert.Equal([middle.Path], Order(settings with { Library = false }, middle.Path).Select(file => file.Path));
        Assert.Equal(TimeSpan.Zero, TranscriptionOrder.SoundLeft([new QueuedFile("x", default, null, 0)]));

        Assert.Throws<ArgumentNullException>(() => TranscriptionOrder.Order(null!, [], settings, Folders, records.GetValueOrDefault, _ => false, Now));
        Assert.Throws<ArgumentNullException>(() => TranscriptionOrder.Order(entries, null!, settings, Folders, records.GetValueOrDefault, _ => false, Now));
        Assert.Throws<ArgumentNullException>(() => TranscriptionOrder.Order(entries, [], null!, Folders, records.GetValueOrDefault, _ => false, Now));
        Assert.Throws<ArgumentNullException>(() => TranscriptionOrder.Order(entries, [], settings, Folders, null!, _ => false, Now));
        Assert.Throws<ArgumentNullException>(() => TranscriptionOrder.Order(entries, [], settings, Folders, records.GetValueOrDefault, null!, Now));
        Assert.Throws<ArgumentNullException>(() => TranscriptionOrder.Sign(null!));
        Assert.Throws<ArgumentNullException>(() => TranscriptionOrder.SoundLeft(null!));
    }

    [Fact]
    [Capability("SUB-20")]
    public void AFailedFileIsTriedAgainLaterAndOneThatCannotBeReadIsLeftUntilAsked()
    {
        var record = new TranscriptRecord { Path = "f.mkv", Signature = new(1, 2), Model = "m", State = TranscriptState.Partial };
        var busy = new IOException("in use");
        var first = TranscriptionRetry.Failed(record, busy, Now);
        Assert.Equal((TranscriptState.Failed, 1, Now.AddMinutes(1)), (first.State, first.Attempts, first.NextTry));
        Assert.Equal("The file was busy or out of reach.", first.Reason);
        var waits = new List<DateTimeOffset?>();
        var again = first;
        for (var i = 0; i < 5; i++)
        {
            again = TranscriptionRetry.Failed(again, busy, Now);
            waits.Add(again.NextTry);
        }

        Assert.Equal([Now.AddMinutes(5), Now.AddMinutes(30), Now.AddHours(2), Now.AddHours(12), null], waits);

        var unreadable = TranscriptionRetry.Failed(record, new MediaFormatException("bad"), Now);
        Assert.Equal(Now.AddMinutes(30), unreadable.NextTry);
        Assert.Null(TranscriptionRetry.Failed(unreadable, new MediaFormatException("bad"), Now).NextTry);

        Assert.False(TranscriptionRetry.MayTry(first, Now));
        Assert.True(TranscriptionRetry.MayTry(first, Now.AddMinutes(1)));
        Assert.True(TranscriptionRetry.MayTry(null, Now));
        Assert.True(TranscriptionRetry.MayTry(record, Now));
        Assert.False(TranscriptionRetry.MayTry(again, Now.AddYears(1)));
        Assert.False(TranscriptionRetry.MayTry(record with { State = TranscriptState.Done }, Now));

        var retried = TranscriptionRetry.Retry(again);
        Assert.True(TranscriptionRetry.MayTry(retried, Now));
        Assert.Equal(1, TranscriptionRetry.Failed(retried, busy, Now).Attempts);
        Assert.Same(record, TranscriptionRetry.Retry(record));
        Assert.Throws<ArgumentNullException>(() => TranscriptionRetry.Retry(null!));
        Assert.Throws<ArgumentNullException>(() => TranscriptionRetry.Failed(null!, busy, Now));

        Assert.Equal(("The file is no longer there.", true), TranscriptionRetry.Classify(new FileNotFoundException()));
        Assert.True(TranscriptionRetry.Classify(new DirectoryNotFoundException()).Passing);
        Assert.False(TranscriptionRetry.Classify(new UnauthorizedAccessException()).Passing);
        Assert.Equal("This file's audio is in a format rexplayer cannot decode.", TranscriptionRetry.Classify(new NotSupportedException()).Reason);
        Assert.Equal("This file's audio could not be read.", TranscriptionRetry.Classify(new InvalidDataException("x")).Reason);
        Assert.Equal("This file's audio could not be read.", TranscriptionRetry.Classify(new MediaFormatException("x")).Reason);
        Assert.Equal(("Not enough memory to transcribe now.", true), TranscriptionRetry.Classify(new InsufficientMemoryException()));
        Assert.Equal(("The speech model could not be loaded.", true), TranscriptionRetry.Classify(new DllNotFoundException("whisper")));
        Assert.Equal(("The speech model could not be loaded.", true), TranscriptionRetry.Classify(new BadImageFormatException()));

        // The exception's own words never reach the user.
        Assert.Equal(("Transcription stopped unexpectedly.", false), TranscriptionRetry.Classify(new InvalidOperationException("Cannot dispose while processing")));
        Assert.Throws<ArgumentNullException>(() => TranscriptionRetry.Classify(null!));
    }

    [Fact]
    [Capability("SUB-20")]
    public void PlaybackComesFirstAndTheLibraryWaitsForTheChargerTheEndOfPlaybackAndTheModel()
    {
        var on = new TranscriptionSettings { Library = true };
        var idle = new TranscriptionMoment(Playing: false, OnBattery: false, EnergySaver: false, ModelInstalled: true);
        Assert.Equal(new TranscriptionPlan(true, true, 6, 4, null), TranscriptionConditions.Plan(on, idle, 8));
        Assert.Equal(new TranscriptionPlan(true, false, 2, 1, "Waiting until playback stops"), TranscriptionConditions.Plan(on, idle with { Playing = true }, 8));
        Assert.Equal(new TranscriptionPlan(true, true, 2, 1, null), TranscriptionConditions.Plan(on with { WhilePlaying = true }, idle with { Playing = true }, 8));
        Assert.Equal("Waiting for the charger", TranscriptionConditions.Plan(on, idle with { OnBattery = true }, 8).Waiting);
        Assert.True(TranscriptionConditions.Plan(on with { OnBattery = true }, idle with { OnBattery = true }, 8).Library);
        Assert.Equal("Waiting while energy saver is on", TranscriptionConditions.Plan(on with { OnBattery = true }, idle with { EnergySaver = true }, 8).Waiting);
        Assert.Equal("Paused", TranscriptionConditions.Plan(on with { Paused = true }, idle, 8).Waiting);
        var noModel = TranscriptionConditions.Plan(on, idle with { ModelInstalled = false }, 8);
        Assert.Equal((false, false, "Waiting for the speech model to be downloaded"), (noModel.PlayingItem, noModel.Library, noModel.Waiting));

        // Off: nothing waits; the playing item may still be heard when asked for.
        var off = TranscriptionConditions.Plan(new TranscriptionSettings(), idle, 8);
        Assert.Equal((true, false, null), (off.PlayingItem, off.Library, off.Waiting));

        // A small processor still gets a thread.
        Assert.Equal(new TranscriptionPlan(true, true, 1, 1, null), TranscriptionConditions.Plan(on, idle, 1));
        Assert.Equal((1, 1), (TranscriptionConditions.Plan(on, idle with { Playing = true }, 0).PlayingThreads, TranscriptionConditions.Plan(on, idle, 0).LibraryThreads));
        Assert.Equal(2, TranscriptionConditions.Plan(on, idle with { Playing = true }, 32).PlayingThreads);
        Assert.Equal(1, TranscriptionConditions.Plan(on, idle with { Playing = true }, 4).PlayingThreads);
        Assert.Throws<ArgumentNullException>(() => TranscriptionConditions.Plan(null!, idle, 8));

        // The model is fetched on a metered connection only when asked there and then.
        Assert.True(TranscriptionConditions.MayDownloadModel(online: true, metered: false, askedNow: false));
        Assert.False(TranscriptionConditions.MayDownloadModel(online: true, metered: true, askedNow: false));
        Assert.True(TranscriptionConditions.MayDownloadModel(online: true, metered: true, askedNow: true));
        Assert.False(TranscriptionConditions.MayDownloadModel(online: false, metered: false, askedNow: true));
    }

    [Fact]
    [Capability("SUB-20")]
    public void BadgesAndTheQueueLineSayWhereEachFileStands()
    {
        var record = new TranscriptRecord { Path = "f", Signature = new(1, 2), Model = "m", State = TranscriptState.Done, Words = 120 };
        BadgeView Badge(TranscriptRecord? r, TranscriptionSkip skip = TranscriptionSkip.None, bool working = false, bool queued = false) =>
            TranscriptionStatus.Badge(r, skip, working, queued, 0.4, Now);

        Assert.Equal((TranscriptBadge.Working, "Transcribing, 40% done"), (Badge(null, working: true).Kind, Badge(null, working: true).Text.Replace(" %", "%", StringComparison.Ordinal)));
        Assert.Equal((TranscriptBadge.Ready, "Subtitles from its speech are ready (120 words)"), (Badge(record).Kind, Badge(record).Text));
        Assert.Equal(((char)0xE7F0).ToString(), Badge(record).Glyph);
        Assert.Equal(TranscriptBadge.NoSpeech, Badge(record with { Words = 0 }).Kind);
        Assert.Equal((TranscriptBadge.NotEligible, "It has no sound to transcribe"), (Badge(record with { State = TranscriptState.NoSound }).Kind, Badge(record with { State = TranscriptState.NoSound }).Text));

        var failed = record with { State = TranscriptState.Failed, Reason = "The file could not be read.", NextTry = Now.AddMinutes(30) };
        Assert.Equal("Not transcribed. The file could not be read. Will try again in about 30 minutes. Choose Retry transcription to try now.", Badge(failed).Text);
        Assert.Equal("Not transcribed. The file could not be read. Choose Retry transcription to try now.", Badge(failed with { NextTry = null }).Text);
        Assert.Equal("Not transcribed. Will try again in a moment. Choose Retry transcription to try now.", Badge(failed with { NextTry = Now, Reason = null }).Text);
        Assert.Equal("Will try again in about 30 minutes.", TranscriptionStatus.RetryIn(TimeSpan.FromMinutes(29)));
        Assert.Equal("Will try again in about 30 minutes.", TranscriptionStatus.RetryIn(TimeSpan.FromMinutes(28.6)));
        Assert.Equal("Will try again in about 4 minutes.", TranscriptionStatus.RetryIn(TimeSpan.FromMinutes(3.2)));
        Assert.Equal("Will try again in about 1 minute.", TranscriptionStatus.RetryIn(TimeSpan.FromSeconds(60)));
        Assert.Equal("Will try again in about 2 hours.", TranscriptionStatus.RetryIn(TimeSpan.FromMinutes(119)));
        Assert.Equal("Will try again in a moment.", TranscriptionStatus.RetryIn(TimeSpan.FromSeconds(-5)));
        Assert.Equal(TranscriptBadge.Failed, Badge(failed, queued: true).Kind);

        var partial = record with { State = TranscriptState.Partial, Share = 0.5 };
        Assert.Equal((TranscriptBadge.Queued, "Waiting to be transcribed"), (Badge(null, queued: true).Kind, Badge(null, queued: true).Text));
        Assert.StartsWith("Waiting to be transcribed, 50", Badge(partial, queued: true).Text, StringComparison.Ordinal);
        Assert.Equal(TranscriptBadge.Partial, Badge(partial).Kind);
        Assert.StartsWith("Partly transcribed, 50", Badge(partial).Text, StringComparison.Ordinal);
        Assert.Equal(TranscriptBadge.NotEligible, Badge(null, TranscriptionSkip.TooShort).Kind);
        Assert.Equal(TranscriptBadge.NotEligible, Badge(null, TranscriptionSkip.NoSound).Kind);
        Assert.Same(BadgeView.Nothing, Badge(null, TranscriptionSkip.Music));
        Assert.Same(BadgeView.Nothing, Badge(null));

        Assert.Equal("Transcription: Paused, 3 waiting", TranscriptionStatus.Line(1, 3, "Episode 4", 0.4, null, "Paused"));
        Assert.Equal("Transcription: Paused", TranscriptionStatus.Line(0, 0, null, 0, null, "Paused"));
        Assert.Equal("Transcription: everything chosen is transcribed", TranscriptionStatus.Line(0, 0, null, 0, null, null));
        Assert.Equal("Transcription: everything chosen is transcribed", TranscriptionStatus.Line(0, 2, null, 0, null, null));
        Assert.Equal("Transcribing 3 of 120: Episode 4 (40%), about 2 hours left", TranscriptionStatus.Line(3, 120, "Episode 4", 0.4, TimeSpan.FromMinutes(130), null).Replace(" %", "%", StringComparison.Ordinal));
        Assert.Equal("Transcribing 1 of 2: E (0%)", TranscriptionStatus.Line(0, 2, "E", 0, null, null).Replace(" %", "%", StringComparison.Ordinal));

        Assert.Equal("under a minute", TranscriptionStatus.About(TimeSpan.FromSeconds(20)));
        Assert.Equal("1 minute", TranscriptionStatus.About(TimeSpan.FromSeconds(70)));
        Assert.Equal("89 minutes", TranscriptionStatus.About(TimeSpan.FromMinutes(89)));
        Assert.Equal("2 hours", TranscriptionStatus.About(TimeSpan.FromMinutes(90)));
        Assert.Equal("40%", TranscriptionStatus.Percent(0.404));
        Assert.Equal("100%", TranscriptionStatus.Percent(3));

        Assert.Equal("Transcription: Paused", TranscriptionStatus.State(true, "Paused", 4));
        Assert.Equal("Transcribing the library", TranscriptionStatus.State(true, null, 4));
        Assert.Equal("Transcription finished", TranscriptionStatus.State(false, null, 0));
        Assert.Equal("Transcription idle", TranscriptionStatus.State(false, null, 3));
        Assert.Null(TranscriptionStatus.Announcement("a", "a"));
        Assert.Equal("b", TranscriptionStatus.Announcement("a", "b"));
    }

    [Fact]
    [Capability("SUB-20")]
    public void TheSpeedIsSmoothedOverThePiecesHeard()
    {
        var speed = new TranscriptionSpeed();
        Assert.Null(speed.Factor);
        Assert.Null(speed.TimeFor(TimeSpan.FromMinutes(1)));
        speed.Heard(TimeSpan.FromSeconds(30), TimeSpan.Zero);
        speed.Heard(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Assert.Null(speed.Factor);
        speed.Heard(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(3));
        Assert.Equal(10, speed.Factor);
        speed.Heard(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
        Assert.Equal(7.6, speed.Factor!.Value, 6);
        Assert.Equal(76, speed.TimeFor(TimeSpan.FromSeconds(577.6))!.Value.TotalSeconds, 3);
        Assert.InRange(speed.SafeFactor!.Value, 1.1, 2.1);
        speed.Heard(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(3));
        Assert.True(speed.SafeFactor < speed.Factor);
        speed.Heard(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        Assert.True(speed.SafeFactor < 1.5);

        // Garbage or zero-length measurements never poison the prediction.
        var before = speed.SafeFactor;
        speed.Heard(TimeSpan.MaxValue, TimeSpan.FromTicks(1));
        Assert.NotNull(speed.SafeFactor);
        Assert.True(speed.SafeFactor > 0);
        Assert.True(before > 0);
    }

    [Fact]
    [Capability("SUB-20")]
    public void TheSettingsAreCleanedOfBlanksAndRepeats()
    {
        var settings = new TranscriptionSettings { Scope = (TranscriptionScope)9, Folders = [@"D:\a", " ", @"d:\A", @" D:\b "], Excluded = null! }.Normalize();
        Assert.Equal(TranscriptionScope.WholeLibrary, settings.Scope);
        Assert.Equal([@"D:\a", @"D:\b"], settings.Folders);
        Assert.Empty(settings.Excluded);
        Assert.False(new TranscriptionSettings().Library);
        Assert.Equal(TranscriptionScope.ChosenFolders, new TranscriptionSettings { Scope = TranscriptionScope.ChosenFolders }.Normalize().Scope);
        Assert.Equal(settings.Folders, new PlayerSettings { Transcription = settings }.Normalize().Transcription.Folders);
        Assert.NotNull(new PlayerSettings { Transcription = null! }.Normalize().Transcription);
    }
}
