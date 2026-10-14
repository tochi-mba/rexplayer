using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Transcription;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Transcription;

public sealed class TranscriptionWorkerTests : IDisposable
{
    private static readonly TranscriptionPlan Everything = new(true, true, 4, 2, null);

    private readonly string _directory = Directory.CreateTempSubdirectory("rexplayer-worker-").FullName;
    private readonly List<TranscriptWord> _speech = ProgressiveTranscriptTests.Speech(200);
    private readonly Dictionary<string, FakeSource> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly ManualTimeProvider _time = new();
    private readonly TranscriptCache _cache;
    private readonly TranscriptionLedger _ledger;
    private FakeHearer? _hearer;
    private int _loads;
    private int _made;

    public TranscriptionWorkerTests()
    {
        _cache = new TranscriptCache(Path.Combine(_directory, "cache"));
        _ledger = new TranscriptionLedger(Path.Combine(_directory, "ledger.json"));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Media(string name, double seconds = 200, double? declared = 200, bool sound = true)
    {
        var path = Path.Combine(_directory, name);
        // Every file its own signature: its own size and time.
        File.WriteAllBytes(path, new byte[4 + ++_made]);
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(_made));
        _sources[path] = new FakeSource(S(seconds), declared is { } d ? S(d) : null, sound);
        return path;
    }

    private readonly List<string> _log = [];

    /// <summary>Thrown by the next model load, then cleared.</summary>
    private Exception? _loadFails;

    private TranscriptionWorker Worker()
    {
        var worker = new TranscriptionWorker(_cache, _ledger, path => _sources.TryGetValue(path, out var source) ? source.Open() : throw new FileNotFoundException("gone", path), () =>
        {
            if (_loadFails is { } fails)
            {
                _loadFails = null;
                throw fails;
            }

            _loads++;
            return _hearer = new FakeHearer(_speech, _time);
        }, "base.en", _time)
        {
            Log = _log.Add,
        };
        worker.SetPlan(Everything);
        return worker;
    }

    /// <summary>The fake media is short: an unproductive worker must fail the test, not hang CI.</summary>
    private static void Complete(TranscriptionWorker worker)
    {
        for (var step = 0; step < 64; step++)
        {
            if (!worker.Step(Token))
            {
                return;
            }
        }

        Assert.Fail("Transcription did not finish within 64 bounded pieces: " + worker.Status);
    }

    private TranscriptRecord? RecordOf(string path) => _ledger.Find(TranscriptSignature.Of(new FileInfo(path).Length, new FileInfo(path).LastWriteTimeUtc));

    [Fact]
    [Capability("SUB-21")]
    public void WhatPlaysIsHeardFromThePlayheadToTheEndThenFromTheTopIntoItsTrack()
    {
        var film = Media("film.mkv");
        using var worker = Worker();
        HearingEventArgs? opened = null;
        var heard = new List<HearingEventArgs>();
        var changes = 0;
        worker.Opened += (_, e) => opened = e;
        worker.Heard += (_, e) => heard.Add(e);
        worker.Changed += (_, _) => changes++;
        worker.Language = "en";
        worker.SetPlaying(film, wanted: true, S(120.4));

        Assert.True(worker.Step(Token));
        Assert.NotNull(opened);
        Assert.Equal((film, true, false, "Transcript (base.en)", "en"), (opened.Path, opened.PlayingItem, opened.Complete, opened.Track.Name, opened.Track.Language));
        Assert.Equal(S(200), opened.Duration);
        Assert.Equal(film, worker.Status.Path);

        Assert.True(worker.Step(Token));
        Assert.Equal([new TimeRange(S(120), S(146))], heard[^1].Done.Ranges);
        Assert.All(opened.Track.All(), cue => Assert.InRange(cue.Start, S(120), S(146)));
        Assert.NotEmpty(opened.Track.All());
        Assert.Equal(4, _hearer!.Threads[^1]);
        // The 30-second model input includes 4 seconds of overlap; only the
        // 26 kept seconds add new captions, and those determine playback's buffer.
        Assert.Equal(26d / 3, worker.Speed.Factor!.Value, 3);
        Assert.Null(worker.Snapshot("other.mkv"));
        Assert.NotEmpty(worker.Snapshot(film)!.Words);

        Complete(worker);

        // Ahead to the end first, then the part before.
        Assert.Equal([S(118), S(144), S(170), S(0), S(24), S(50), S(76), S(102)], _sources[film].Reads.Select(range => range.Start));
        Assert.True(heard[^1].Complete);
        Assert.Equal(new Transcript("en", "base.en", _speech).ToCues().Select(ProgressiveTranscriptTests.Describe), opened.Track.All().Select(ProgressiveTranscriptTests.Describe));
        Assert.Equal(_speech.Count, _cache.Load(film, "base.en")!.Words.Count);
        Assert.Null(_cache.LoadPartial(film, "base.en"));
        Assert.Equal((TranscriptState.Done, _speech.Count, 1.0), (RecordOf(film)!.State, RecordOf(film)!.Words, RecordOf(film)!.Share));

        // The model is kept a while with nothing to hear, so the next piece needs no load; then let go.
        Assert.False(_hearer.Disposed);
        _time.Advance(TranscriptionWorker.KeepModel - TimeSpan.FromSeconds(1));
        Assert.False(worker.Step(Token));
        Assert.False(_hearer.Disposed);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(worker.Step(Token));
        Assert.True(_hearer.Disposed);
        Assert.Same(WorkerStatus.Idle, worker.Status);
        Assert.True(changes >= 2);
        Assert.Equal(1, _loads);
    }

    [Fact]
    [Capability("SUB-21")]
    public void ASeekAwayDropsThePieceBeingHeardAndHearingMovesThere()
    {
        var film = Media("film.mkv");
        using var worker = Worker();
        worker.SetPlaying(film, wanted: true, S(100));
        Assert.True(worker.Step(Token));
        Assert.True(worker.Step(Token));
        _hearer!.During = () =>
        {
            // A seek within the piece keeps it; one far before it drops it.
            worker.MovePlayhead(S(130));
            worker.MovePlayhead(S(10));
        };
        var before = worker.Snapshot(film)!.Words.Count;
        Assert.True(worker.Step(Token));
        Assert.Equal(before, worker.Snapshot(film)!.Words.Count);
        _hearer.During = null;
        Assert.True(worker.Step(Token));
        Assert.Contains(worker.Snapshot(film)!.Words, word => word.Start >= S(10) && word.Start < S(36));
    }

    [Fact]
    [Capability("SUB-20")]
    public void WhatPlaysTakesOverFromTheLibraryAndAPauseStopsItWithinAPiece()
    {
        var episode = Media("episode.mkv");
        var film = Media("film-two.mkv");
        using var worker = Worker();
        HearingEventArgs? last = null;
        worker.Heard += (_, e) => last = e;
        worker.SetQueue([new QueuedFile(episode, default, S(200), 0)]);
        Assert.True(worker.Step(Token));
        Assert.True(worker.Step(Token));
        Assert.Equal(2, _hearer!.Threads[^1]);

        // Playing another file mid-piece: the piece is dropped and the episode put aside, part kept.
        _hearer.During = () => worker.SetPlaying(film, wanted: true, S(50));
        Assert.True(worker.Step(Token));
        _hearer.During = null;
        Assert.True(worker.Step(Token));
        Assert.Equal(film, worker.Status.Path);
        Assert.True(worker.Status.PlayingItem);
        Assert.Equal(TranscriptState.Partial, RecordOf(episode)!.State);
        Assert.Equal(26.0 / 200, RecordOf(episode)!.Share, 6);
        Assert.Equal([new TimeRange(S(0), S(26))], _cache.LoadPartial(episode, "base.en")!.Done.Ranges);

        // Not wanted any more: back to the episode, from where it stopped.
        worker.SetPlaying(film, wanted: false, S(50));
        Assert.True(worker.Step(Token));
        Assert.True(worker.Step(Token));
        Assert.Equal((episode, false), (last!.Path, last.PlayingItem));
        Assert.Equal([new TimeRange(S(0), S(52))], last.Done.Ranges);

        // The library paused mid-piece: the piece is dropped, the episode put aside, then idle.
        _hearer.During = () => worker.SetPlan(Everything with { Library = false });
        Assert.True(worker.Step(Token));
        Assert.False(worker.Step(Token));
        Assert.Null(worker.Status.Path);
        Assert.False(_hearer.Disposed);
        _time.Advance(TranscriptionWorker.KeepModel);
        Assert.False(worker.Step(Token));
        Assert.True(_hearer.Disposed);

        // What was asked for is heard even with the library paused.
        worker.SetQueue([new QueuedFile(episode, default, S(200), 0, Asked: true)]);
        Assert.True(worker.Step(Token));
        Assert.Equal(episode, worker.Status.Path);
        worker.SetPlan(Everything with { PlayingItem = false });
        Assert.False(worker.Step(Token));
    }

    [Fact]
    [Capability("SUB-20")]
    public void FilesWithoutSoundOrAlreadyHeardAreSettledAndAFailureWaitsItsTurn()
    {
        var silent = Media("silent.mkv", sound: false);
        var kept = Media("kept.mkv");
        _cache.Save(kept, new Transcript("en", "base.en", _speech));
        var gone = Path.Combine(_directory, "gone.mkv");
        var broken = Media("broken.mkv");
        _sources[broken].Fail = new IOException("busy");
        using var worker = Worker();
        string? error = null;
        worker.Changed += (_, _) => error ??= worker.Status.Error;
        worker.SetQueue([new QueuedFile(silent, default, null, 0), new QueuedFile(kept, default, null, 0), new QueuedFile(gone, default, null, 0), new QueuedFile(broken, default, null, 0)]);

        Assert.True(worker.Step(Token));
        Assert.Equal(TranscriptState.NoSound, RecordOf(silent)!.State);
        Assert.True(_sources[silent].Disposed);
        Assert.True(worker.Step(Token));
        Assert.Equal((TranscriptState.Done, _speech.Count), (RecordOf(kept)!.State, RecordOf(kept)!.Words));

        // A file gone from the disk fails with a record of where it was.
        Assert.True(worker.Step(Token));
        var goneRecord = _ledger.All.Single(record => record.Path == gone);
        Assert.Equal((TranscriptState.Failed, "The file is no longer there."), (goneRecord.State, goneRecord.Reason));
        Assert.Equal("The file is no longer there.", error);

        // The details are the log's, never the user's.
        Assert.Contains(_log, line => line.StartsWith("Transcribing " + gone + " failed: System.IO.FileNotFoundException: gone", StringComparison.Ordinal));

        // A read that fails mid-file keeps the part heard.
        Assert.True(worker.Step(Token));
        Assert.True(worker.Step(Token));
        _sources[broken].Failing = true;
        Assert.True(worker.Step(Token));
        Assert.Equal(TranscriptState.Failed, RecordOf(broken)!.State);
        Assert.Equal(_time.GetUtcNow().AddMinutes(1), RecordOf(broken)!.NextTry);
        Assert.NotNull(_cache.LoadPartial(broken, "base.en"));
        Assert.False(worker.Step(Token));

        // Retry: unsettled, it is heard again from where it stopped.
        _sources[broken].Failing = false;
        worker.Unsettle([broken]);
        Assert.True(worker.Step(Token));
        Assert.True(worker.Step(Token));
        Assert.Equal(new TimeRange(S(24), S(54)), _sources[broken].Reads[^1]);
        Assert.Throws<ArgumentNullException>(() => worker.Unsettle(null!));
        Assert.Throws<ArgumentNullException>(() => worker.SetQueue(null!));
    }

    [Fact]
    [Capability("SUB-21")]
    public void ASoundShorterThanItsContainerSaysEndsTheFileWhereItEnds()
    {
        var unknown = Media("short.mkv", seconds: 40, declared: null);
        var shorter = Media("shorter.mkv", seconds: 99, declared: 300);
        using var worker = Worker();
        worker.SetQueue([new QueuedFile(unknown, default, null, 0)]);
        Complete(worker);

        Assert.Equal((TranscriptState.Done, 1.0), (RecordOf(unknown)!.State, RecordOf(unknown)!.Share));
        Assert.Equal(_speech.Count(word => word.Start < S(40)), _cache.Load(unknown, "base.en")!.Words.Count);

        // Played from past where the sound really ends: the piece there is empty, and the rest is heard from the top.
        worker.SetPlaying(shorter, wanted: true, S(100));
        Complete(worker);

        Assert.Equal(TranscriptState.Done, RecordOf(shorter)!.State);
        Assert.Equal(S(98), _sources[shorter].Reads[0].Start);
        Assert.Equal(_speech.Count(word => word.Start < S(99)), _cache.Load(shorter, "base.en")!.Words.Count);
    }

    [Fact]
    [Capability("SUB-21")]
    public void ThePartHeardIsWrittenDownEveryFewPiecesAndAfterAPieceHeardWhileCancelled()
    {
        var film = Media("film.mkv");
        using var worker = Worker();
        worker.SetQueue([new QueuedFile(film, default, null, 0)]);
        for (var i = 0; i < 5; i++)
        {
            Assert.True(worker.Step(Token));
        }

        Assert.Equal(TimeSpan.FromSeconds(104), _cache.LoadPartial(film, "base.en")!.Done.Total);

        // A hearer that finishes its piece though it was told to stop: the piece is not kept.
        _hearer!.During = () => worker.SetPlan(Everything with { Library = false });
        _hearer.IgnoresCancellation = true;
        var words = worker.Snapshot(film)!.Words.Count;
        Assert.True(worker.Step(Token));
        Assert.Equal(words, _cache.LoadPartial(film, "base.en")!.WordCount);
    }

    [Fact]
    [Capability("SUB-20")]
    public void StoppingWhileAPieceIsHeardEndsTheStepAndTheWorkerKeepsTheRest()
    {
        var film = Media("film.mkv");
        var worker = Worker();
        worker.SetQueue([new QueuedFile(film, default, null, 0)]);
        Assert.True(worker.Step(Token));
        Assert.True(worker.Step(Token));
        using var stop = new CancellationTokenSource();
        _hearer!.During = stop.Cancel;
        Assert.Throws<OperationCanceledException>(() => worker.Step(stop.Token));
        worker.Dispose();
        Assert.True(_hearer.Disposed);
        Assert.Equal(TranscriptState.Partial, RecordOf(film)!.State);

        // Closed: whatever the window still says is ignored.
        worker.SetQueue([]);
        worker.SetPlaying(null, wanted: false, TimeSpan.Zero);
        worker.MovePlayhead(TimeSpan.Zero);
        worker.Unsettle([]);
        worker.SetPlan(Everything);
        Assert.Throws<ArgumentException>(() => new TranscriptionWorker(_cache, _ledger, _ => null!, () => null!, " "));
        Assert.Throws<ArgumentNullException>(() => new TranscriptionWorker(null!, _ledger, _ => null!, () => null!, "m"));
        Assert.Throws<ArgumentNullException>(() => new TranscriptionWorker(_cache, null!, _ => null!, () => null!, "m"));
        Assert.Throws<ArgumentNullException>(() => new TranscriptionWorker(_cache, _ledger, null!, () => null!, "m"));
        Assert.Throws<ArgumentNullException>(() => new TranscriptionWorker(_cache, _ledger, _ => null!, null!, "m"));
        Assert.Equal("base.en", worker.Model);
        Assert.Equal("Transcript (tiny.en)", TranscriptionWorker.TrackName("tiny.en"));
    }

    [Fact]
    [Capability("SUB-21")]
    public void AModelThatComplainsWhenItIsStoppedPartWayIsNotAFailure()
    {
        var film = Media("film.mkv");
        using var worker = Worker();
        worker.SetPlaying(film, wanted: true, S(100));
        Assert.True(worker.Step(Token));
        Assert.True(worker.Step(Token));

        // A seek far back drops the piece; the model reports it was stopped with an error of its own.
        _hearer!.During = () => worker.MovePlayhead(S(5));
        _hearer.ThrowsWhenCancelled = new InvalidOperationException("Cannot dispose while processing, please use DisposeAsync instead.");
        Assert.True(worker.Step(Token));
        Assert.Null(worker.Status.Error);
        Assert.NotEqual(TranscriptState.Failed, RecordOf(film)?.State);
        Assert.Empty(_log);

        // Hearing goes on from the new place.
        (_hearer.During, _hearer.ThrowsWhenCancelled) = (null, null);
        Assert.True(worker.Step(Token));
        Assert.Contains(worker.Snapshot(film)!.Words, word => word.Start < S(31));
    }

    [Fact]
    [Capability("SUB-20")]
    public void AModelThatCannotBeLoadedFailsNoFileAndIsTriedAgainLater()
    {
        var film = Media("film.mkv");
        using var worker = Worker();
        worker.SetQueue([new QueuedFile(film, default, null, 0)]);
        Assert.True(worker.Step(Token));
        _loadFails = new InvalidDataException("bad model file");
        Assert.True(worker.Step(Token));
        Assert.Equal("The speech model could not be loaded.", worker.Status.Error);
        Assert.Null(RecordOf(film));
        Assert.Contains(_log, line => line.Contains("bad model file", StringComparison.Ordinal));

        // Nothing is heard until it is due to be tried again; meanwhile the line says why.
        Assert.False(worker.Step(Token));
        Assert.Equal("The speech model could not be loaded.", worker.Status.Error);
        _time.Advance(TranscriptionWorker.ModelRetry);
        Assert.True(worker.Step(Token));
        Assert.True(worker.Step(Token));
        Assert.Null(worker.Status.Error);
        Assert.Equal(1, _loads);
        Assert.Equal(("The speech model could not be loaded.", true), TranscriptionRetry.Classify(new SpeechModelException(new IOException())));
    }

    [Fact]
    [Capability("SUB-20")]
    public void AModelThatCannotBeReleasedCleanlyIsLoggedNotThrown()
    {
        var film = Media("film.mkv");
        var worker = Worker();
        worker.SetQueue([new QueuedFile(film, default, null, 0)]);
        Assert.True(worker.Step(Token));
        Assert.True(worker.Step(Token));
        _hearer!.DisposeThrows = true;
        worker.Dispose();
        Assert.Contains(_log, line => line.StartsWith("The speech model could not be released cleanly", StringComparison.Ordinal));
    }

    [Fact]
    [Capability("SUB-20")]
    public void AnIdleWorkerExitsItsLoopWhenClosed()
    {
        var worker = Worker();
        worker.Start();
        worker.Dispose();
        var field = typeof(TranscriptionWorker).GetField("_thread", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var thread = (Thread)field.GetValue(worker)!;
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The idle transcription thread did not stop.");
    }

    [Fact]
    [Capability("SUB-20")]
    public async Task OnItsOwnThreadItHearsWhatItIsGivenAndLetsTheModelGoWhenClosed()
    {
        var film = Media("film.mkv", seconds: 60, declared: 60);
        var worker = Worker();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        worker.Heard += (_, e) =>
        {
            if (e.Complete)
            {
                finished.TrySetResult();
            }
        };
        worker.Start();
        worker.SetQueue([new QueuedFile(film, default, null, 0)]);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        // Closed while hearing: the hearer is told to stop and let go.
        var second = Media("second.mkv", seconds: 60, declared: 60);
        var hearing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeHearer.Created = hearer =>
        {
            hearer.During = () => hearing.TrySetResult();
            hearer.Blocks = true;
            hearer.OnDispose = () => released.TrySetResult();
        };
        worker.SetQueue([new QueuedFile(second, default, null, 0)]);
        await hearing.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        worker.Dispose();
        await released.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        // Disposal signals the worker but intentionally does not join on the UI thread.
        // The test must wait until its background cleanup has actually completed.
        var field = typeof(TranscriptionWorker).GetField("_thread", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var thread = (Thread)field.GetValue(worker)!;
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The transcription thread did not stop.");
        FakeHearer.Created = null;
    }

    /// <summary>A file's sound whose every sample is the time it is at, so the hearer knows where it is.</summary>
    private sealed class FakeSource(TimeSpan sound, TimeSpan? declared, bool hasSound)
    {
        public TimeSpan Sound { get; } = sound;

        public TimeSpan? Declared { get; } = declared;

        public bool HasSound { get; } = hasSound;

        public List<TimeRange> Reads { get; } = [];

        public bool Disposed { get; private set; }

        public Exception? Fail { get; set; }

        public bool Failing { get; set; }

        public ISpeechSource Open() => new Opened(this);

        private sealed class Opened(FakeSource file) : ISpeechSource
        {
            public bool HasSound => file.HasSound;

            public TimeSpan? Duration => file.Declared;

            public bool Ended { get; private set; }

            public float[] Read(TimeRange range, CancellationToken cancellationToken)
            {
                if (file.Failing)
                {
                    throw file.Fail!;
                }

                file.Reads.Add(range);
                var end = range.End < file.Sound ? range.End : file.Sound;
                Ended = range.End > file.Sound;
                var count = Math.Max(0, (int)Math.Round((end - range.Start).TotalSeconds * SpeechAudio.Rate));
                var samples = new float[count];
                if (count > 0)
                {
                    samples[0] = (float)range.Start.TotalSeconds;
                }

                return samples;
            }

            public void Dispose() => file.Disposed = true;
        }
    }

    /// <summary>Hears the test's speech: what starts within the piece, timed from its start; each piece takes three seconds.</summary>
    private sealed class FakeHearer(IReadOnlyList<TranscriptWord> speech, ManualTimeProvider time) : ISpeechHearer
    {
        /// <summary>Applied to whichever hearer hears next, the one kept or a new one.</summary>
        public static Action<FakeHearer>? Created { get; set; }

        public List<int> Threads { get; } = [];

        public Action? During { get; set; }

        public Action? OnDispose { get; set; }

        public bool Blocks { get; set; }

        public bool IgnoresCancellation { get; set; }

        /// <summary>Thrown instead of a cancellation when the piece is stopped, as a native model may.</summary>
        public Exception? ThrowsWhenCancelled { get; set; }

        public bool DisposeThrows { get; set; }

        public bool Disposed { get; private set; }

        public ValueTask<(IReadOnlyList<TranscriptWord> Words, string? Language)> HearAsync(float[] samples, string? language, int threads, CancellationToken cancellationToken)
        {
            Created?.Invoke(this);

            Threads.Add(threads);
            During?.Invoke();
            if (Blocks && !cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
            {
                // An unresponsive cancellation must fail the fixture instead of leaving the
                // entire portable or Windows test runner blocked until CI cancels its job.
                throw new TimeoutException("The blocked fake speech model was never cancelled.");
            }

            if (ThrowsWhenCancelled is { } complaint && cancellationToken.IsCancellationRequested)
            {
                throw complaint;
            }

            if (!IgnoresCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            time.Advance(TimeSpan.FromSeconds(samples.Length / (double)SpeechAudio.Rate / 10));
            var start = TimeSpan.FromSeconds(samples.Length > 0 ? samples[0] : 0);
            var end = start + TimeSpan.FromSeconds(samples.Length / (double)SpeechAudio.Rate);
            return ValueTask.FromResult<(IReadOnlyList<TranscriptWord>, string?)>(([.. speech.Where(word => word.Start >= start && word.Start < end).Select(word => word with { Start = word.Start - start, End = word.End - start })], "en"));
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            OnDispose?.Invoke();
            return DisposeThrows ? ValueTask.FromException(new InvalidOperationException("still processing")) : ValueTask.CompletedTask;
        }
    }
}
