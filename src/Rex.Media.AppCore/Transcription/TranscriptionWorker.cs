using Rex.Media.AppCore.Captions;
using Rex.Media.Subtitles;

namespace Rex.Media.AppCore.Transcription;

/// <summary>A file's sound, read a stretch at a time at 16 kHz (<see cref="SpeechAudioReader"/> is the real one).</summary>
public interface ISpeechSource : IDisposable
{
    bool HasSound { get; }

    TimeSpan? Duration { get; }

    /// <summary>Whether the sound ended before the end of the last stretch read.</summary>
    bool Ended { get; }

    float[] Read(TimeRange range, CancellationToken cancellationToken);
}

/// <summary>A speech model, loaded once and kept while there is work.</summary>
public interface ISpeechHearer : IDisposable
{
    /// <summary>
    /// The words in <paramref name="samples"/> (16 kHz mono), timed from its first sample, and the
    /// language heard; on <paramref name="threads"/> threads.
    /// </summary>
    (IReadOnlyList<TranscriptWord> Words, string? Language) Hear(float[] samples, string? language, int threads, CancellationToken cancellationToken);
}

/// <summary>A file opened for hearing: its subtitle track, filled as pieces are heard.</summary>
public sealed class HearingEventArgs(string path, SubtitleTrack track, TimeRanges done, TimeSpan? duration, bool playingItem, bool complete) : EventArgs
{
    public string Path { get; } = path;

    /// <summary>The track the pieces go into; it may be shown at once and fills in by itself.</summary>
    public SubtitleTrack Track { get; } = track;

    public TimeRanges Done { get; } = done;

    public TimeSpan? Duration { get; } = duration;

    public bool PlayingItem { get; } = playingItem;

    public bool Complete { get; } = complete;
}

/// <summary>What the worker is doing, for the window's line and badges.</summary>
public sealed record WorkerStatus(string? Path, bool PlayingItem, double Share, string? Error)
{
    public static WorkerStatus Idle { get; } = new(null, false, 0, null);
}

/// <summary>
/// Hears speech in the background (SUB-20, SUB-21), a piece at a time, on a thread of its own below
/// normal priority, with a demuxer and decoder of its own: playback's are never touched. What plays
/// comes first, heard from the playhead; a seek drops the piece being heard when the playhead left
/// it behind. Between pieces the worker looks again at what should run, so what plays takes over
/// from the library within one piece, and a pause or energy saver stops it within one. Each piece's
/// cues go straight into the file's subtitle track (which is safe across threads); the window is
/// told only which stretches are done. Every decision is one of the pure classes beside it.
/// </summary>
public sealed class TranscriptionWorker : IDisposable
{
    /// <summary>The part heard is written down after this many pieces, and whenever hearing stops.</summary>
    private const int SaveEvery = 4;

    private readonly object _gate = new();
    private readonly TranscriptCache _cache;
    private readonly TranscriptionLedger _ledger;
    private readonly Func<string, ISpeechSource> _open;
    private readonly Func<ISpeechHearer> _load;
    private readonly TimeProvider _time;
    private readonly AutoResetEvent _wake = new(false);
    private readonly CancellationTokenSource _stop = new();
    private readonly HashSet<string> _settled = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<QueuedFile> _queue = [];
    private TranscriptionPlan _plan;
    private string? _playing;
    private bool _playingWanted;
    private TimeSpan _playhead;
    private Job? _job;
    private ISpeechHearer? _hearer;
    private SpeechChunk? _inFlight;
    private CancellationTokenSource? _piece;
    private Thread? _thread;
    private WorkerStatus _status = WorkerStatus.Idle;

    public TranscriptionWorker(TranscriptCache cache, TranscriptionLedger ledger, Func<string, ISpeechSource> open, Func<ISpeechHearer> load, string model, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        (_cache, _ledger, _open, _load, Model, _time) = (cache, ledger, open, load, model, time ?? TimeProvider.System);
    }

    /// <summary>A file was opened for hearing: its track, and what of it was heard before. On the worker's thread.</summary>
    public event EventHandler<HearingEventArgs>? Opened;

    /// <summary>A piece was heard (or the file finished): the stretches now done. On the worker's thread.</summary>
    public event EventHandler<HearingEventArgs>? Heard;

    /// <summary>What the worker does changed: another file, a file finished or failed, idle. On the worker's thread.</summary>
    public event EventHandler? Changed;

    public string Model { get; }

    /// <summary>The language to hear in (a two-letter code), or null to let the model tell.</summary>
    public string? Language { get; set; }

    /// <summary>How fast pieces are being heard.</summary>
    public TranscriptionSpeed Speed { get; } = new();

    public WorkerStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    /// <summary>What the computer allows now (from <see cref="TranscriptionConditions.Plan"/>).</summary>
    public void SetPlan(TranscriptionPlan plan)
    {
        lock (_gate)
        {
            _plan = plan;
            if (_job is { } job && !Allowed(job.PlayingItem, job.Asked))
            {
                _piece?.Cancel();
            }
        }

        Wake();
    }

    /// <summary>The library's queue, in order (from <see cref="TranscriptionOrder.Order"/>).</summary>
    public void SetQueue(IReadOnlyList<QueuedFile> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        lock (_gate)
        {
            _queue = queue;
        }

        Wake();
    }

    /// <summary>What plays (null for nothing), whether its speech is wanted, and where it is.</summary>
    public void SetPlaying(string? path, bool wanted, TimeSpan playhead)
    {
        lock (_gate)
        {
            (_playing, _playingWanted, _playhead) = (path, wanted, playhead);

            // Another file playing takes over from the piece being heard at once.
            if (_job is { } job && wanted && !string.Equals(job.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                _piece?.Cancel();
            }
        }

        Wake();
    }

    /// <summary>The playhead moved; a seek away from the piece being heard drops it.</summary>
    public void MovePlayhead(TimeSpan playhead)
    {
        lock (_gate)
        {
            _playhead = playhead;
            if (_job is { PlayingItem: true } job && _inFlight is { } chunk && ChunkPlanner.ShouldAbandon(chunk, playhead, job.Transcript.Done))
            {
                _piece?.Cancel();
            }
        }

        Wake();
    }

    /// <summary>Forgets that files were settled this session, so a retried or changed file is heard again.</summary>
    public void Unsettle(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        lock (_gate)
        {
            _settled.ExceptWith(paths);
        }

        Wake();
    }

    /// <summary>Everything heard so far of <paramref name="path"/>, when it is the file being heard.</summary>
    public Transcript? Snapshot(string path)
    {
        lock (_gate)
        {
            return _job is { } job && string.Equals(job.Path, path, StringComparison.OrdinalIgnoreCase) ? job.Transcript.ToTranscript() : null;
        }
    }

    /// <summary>Runs the worker on a thread of its own, below normal priority, until disposed.</summary>
    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "rexplayer transcription" };
        _thread.Start();
    }

    /// <summary>
    /// One step of work: chooses what should be heard, opens it if it is not open, and hears its
    /// next piece. False when there is nothing to do.
    /// </summary>
    public bool Step(CancellationToken cancellationToken)
    {
        if (Choose() is not { } target)
        {
            Close(save: true);
            Unload();
            Report(WorkerStatus.Idle);
            return false;
        }

        try
        {
            if (_job is not { } job || !string.Equals(job.Path, target.Path, StringComparison.OrdinalIgnoreCase))
            {
                Close(save: true);
                return Open(target, cancellationToken);
            }

            job.PlayingItem = target.PlayingItem;
            return Hear(job, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A seek, another file or a pause took over; the next step goes on from there.
            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Fail(target.Path, error);
            return true;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        // The model may be waiting inside Hear on the piece's own token. Cancel that
        // token explicitly as well, rather than relying on a linked cancellation
        // callback during shutdown, so closing never leaves a sleeping speech thread.
        lock (_gate)
        {
            _piece?.Cancel();
        }

        Wake();
        if (_thread is null)
        {
            Close(save: true);
            Unload();
            _wake.Dispose();
            _stop.Dispose();
        }
    }

    /// <summary>Wakes the thread to look again; nothing once the worker is closed.</summary>
    private void Wake()
    {
        try
        {
            _wake.Set();
        }
        catch (ObjectDisposedException)
        {
            // Closed: there is nothing left to wake.
        }
    }

    private void Run()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                if (!Step(_stop.Token))
                {
                    // Wait for either new work or shutdown. A wake signal alone can be
                    // consumed by another update before shutdown reaches this wait.
                    WaitHandle.WaitAny([_wake, _stop.Token.WaitHandle]);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Closing.
        }
        finally
        {
            // The stop source stays: the window may still be cancelling it.
            Close(save: true);
            Unload();
            _wake.Dispose();
        }
    }

    /// <summary>What should be heard now: what plays when wanted and allowed, else the queue's first allowed file.</summary>
    private Target? Choose()
    {
        lock (_gate)
        {
            if (_playing is { } playing && _playingWanted && _plan.PlayingItem && !_settled.Contains(playing))
            {
                return new Target(playing, true, true, null);
            }

            return _queue.Where(file => !_settled.Contains(file.Path) && Allowed(false, file.Asked)).Select(file => (Target?)new Target(file.Path, false, file.Asked, file)).FirstOrDefault();
        }
    }

    /// <summary>Whether hearing may go on: what plays and what was asked for need only the model; the rest, the library's leave. Called holding the gate.</summary>
    private bool Allowed(bool playingItem, bool asked) => playingItem || asked ? _plan.PlayingItem : _plan.Library;

    private bool Open(Target target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_cache.Load(target.Path, Model) is { } kept)
        {
            Settle(target.Path, Record(target.Path) with { State = TranscriptState.Done, Words = kept.Words.Count, Share = 1, Reason = null, NextTry = null, Attempts = 0 });
            return true;
        }

        var source = _open(target.Path);
        if (!source.HasSound)
        {
            source.Dispose();
            Settle(target.Path, Record(target.Path) with { State = TranscriptState.NoSound, Share = 0 });
            return true;
        }

        var transcript = _cache.LoadPartial(target.Path, Model) ?? new ProgressiveTranscript(Model, Language);
        transcript.Duration ??= source.Duration ?? target.File?.Duration;
        var track = new SubtitleTrack(TrackName(Model)) { Language = transcript.Language.Length > 0 ? transcript.Language : Language };
        track.AddRange(transcript.ToTranscript().ToCues());
        var job = new Job(target.Path, source, transcript, track, target.Asked) { PlayingItem = target.PlayingItem };
        lock (_gate)
        {
            _job = job;
            _status = new WorkerStatus(job.Path, job.PlayingItem, transcript.Share, null);
        }

        Opened?.Invoke(this, Args(job, complete: false));
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>The name a transcript's subtitle track goes by.</summary>
    public static string TrackName(string model) => "Transcript (" + model + ")";

    private bool Hear(Job job, CancellationToken cancellationToken)
    {
        TimeSpan from;
        lock (_gate)
        {
            from = job.PlayingItem ? _playhead : TimeSpan.Zero;
        }

        if (ChunkPlanner.Next(job.Transcript.Done, job.Transcript.Duration, from) is not { } chunk)
        {
            Finish(job);
            return true;
        }

        _hearer ??= _load();
        int threads;
        CancellationToken token;
        lock (_gate)
        {
            _piece = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _inFlight = chunk;
            token = _piece.Token;
            threads = job.PlayingItem ? _plan.PlayingThreads : _plan.LibraryThreads;
        }

        try
        {
            var started = _time.GetTimestamp();
            var samples = job.Source.Read(chunk.Heard, token);
            var heardLength = TimeSpan.FromSeconds((double)samples.Length / SpeechAudio.Rate);
            if (job.Source.Ended && chunk.Heard.Start + heardLength < chunk.Heard.End)
            {
                // The sound ends here, whatever the container said: this is the file's length.
                var end = chunk.Heard.Start + heardLength;
                job.Transcript.Duration = end;
                if (end <= chunk.Kept.Start)
                {
                    return true;
                }

                chunk = new SpeechChunk(chunk.Kept with { End = end < chunk.Kept.End ? end : chunk.Kept.End }, chunk.Heard with { End = end });
            }

            var (words, language) = _hearer.Hear(samples, Language, Math.Max(1, threads), token);
            token.ThrowIfCancellationRequested();
            var change = job.Transcript.Add(chunk, words, language);
            job.Track.Replace(change.Replaced.Start, change.Replaced.End, change.Cues);
            Speed.Heard(heardLength, _time.GetElapsedTime(started));
            if (++job.Pieces % SaveEvery == 0)
            {
                _cache.SavePartial(job.Path, job.Transcript);
            }

            Report(new WorkerStatus(job.Path, job.PlayingItem, job.Transcript.Share, null));
            Heard?.Invoke(this, Args(job, complete: false));
            return true;
        }
        finally
        {
            lock (_gate)
            {
                _inFlight = null;
                _piece?.Dispose();
                _piece = null;
            }
        }
    }

    /// <summary>Every stretch is heard: the whole transcript is kept and the part forgotten.</summary>
    private void Finish(Job job)
    {
        var transcript = job.Transcript.ToTranscript();
        _cache.Save(job.Path, transcript);
        _cache.ForgetPartial(job.Path, Model);
        Heard?.Invoke(this, Args(job, complete: true));
        Close(save: false);
        Settle(job.Path, Record(job.Path) with { State = TranscriptState.Done, Words = transcript.Words.Count, Share = 1, Reason = null, NextTry = null, Attempts = 0 });
    }

    private void Fail(string path, Exception error)
    {
        Close(save: true);
        Settle(path, TranscriptionRetry.Failed(Record(path), error, _time.GetUtcNow()), error.Message);
    }

    /// <summary>A file is done with for this session (heard, failed or without sound); its record is kept.</summary>
    private void Settle(string path, TranscriptRecord record, string? error = null)
    {
        _ledger.Set(record);
        lock (_gate)
        {
            _settled.Add(path);
        }

        Report(new WorkerStatus(null, false, 0, error));
    }

    /// <summary>The ledger's record for <paramref name="path"/> as the disk describes it now, or a new one.</summary>
    private TranscriptRecord Record(string path)
    {
        var info = new FileInfo(path);
        var signature = TranscriptSignature.Of(info.Exists ? info.Length : 0, info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue);
        return (_ledger.Find(signature) ?? new TranscriptRecord { Path = path, Signature = signature, Model = Model, State = TranscriptState.Partial }) with { Path = path, Model = Model };
    }

    /// <summary>Puts the file being heard aside, keeping what was heard of it when <paramref name="save"/>.</summary>
    private void Close(bool save)
    {
        Job? job;
        lock (_gate)
        {
            (job, _job) = (_job, null);
        }

        if (job is null)
        {
            return;
        }

        job.Source.Dispose();
        if (save && job.Transcript.Done.Ranges.Count > 0)
        {
            _cache.SavePartial(job.Path, job.Transcript);
            _ledger.Set(Record(job.Path) with { State = TranscriptState.Partial, Share = job.Transcript.Share, Words = job.Transcript.WordCount });
        }
    }

    /// <summary>The model is let go while there is nothing to hear: it holds hundreds of megabytes.</summary>
    private void Unload()
    {
        _hearer?.Dispose();
        _hearer = null;
    }

    private void Report(WorkerStatus status)
    {
        bool changed;
        lock (_gate)
        {
            changed = status.Path != _status.Path || status.Error != _status.Error || status.PlayingItem != _status.PlayingItem;
            _status = status;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static HearingEventArgs Args(Job job, bool complete) =>
        new(job.Path, job.Track, job.Transcript.Done, job.Transcript.Duration, job.PlayingItem, complete);

    private readonly record struct Target(string Path, bool PlayingItem, bool Asked, QueuedFile? File);

    private sealed class Job(string path, ISpeechSource source, ProgressiveTranscript transcript, SubtitleTrack track, bool asked)
    {
        public string Path { get; } = path;

        public ISpeechSource Source { get; } = source;

        public ProgressiveTranscript Transcript { get; } = transcript;

        public SubtitleTrack Track { get; } = track;

        public bool Asked { get; } = asked;

        public bool PlayingItem { get; set; }

        public int Pieces { get; set; }
    }
}
