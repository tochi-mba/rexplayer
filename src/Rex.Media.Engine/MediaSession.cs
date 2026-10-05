using System.Collections.Concurrent;
using Rex.Media.Diagnostics;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Engine;

/// <summary>
/// Plays one piece of media. Every public call posts a command to a single mailbox thread that owns
/// the state machine (ADR-004), so the session's state only ever changes in one place and in one
/// order; the calls return tasks that complete when the command has been carried out. Media data
/// flows on separate threads: a demux thread feeds packet queues and an audio thread decodes,
/// processes and writes to the sink, whose back-pressure paces the whole pipeline.
/// </summary>
public sealed partial class MediaSession : IDisposable
{
    private const string LogSource = "engine";

    private readonly EngineOptions _options;
    private readonly BlockingCollection<Command> _mailbox = new();
    private readonly Thread _mailboxThread;
    private readonly EventPump _events;
    private readonly RexLog _log;
    private TaskCompletionSource _finished = NewCompletion();
    private volatile SessionState _state = SessionState.Idle;
    private Playback? _playback;
    private bool _disposed;

    public MediaSession(EngineOptions options, Action<SessionEvent>? listener = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _log = options.Log;
        _events = new EventPump(listener ?? (_ => { }));
        _mailboxThread = new Thread(RunMailbox) { IsBackground = true, Name = "rexplayer session" };
        _mailboxThread.Start();
    }

    public SessionState State => _state;

    /// <summary>What the open media contains, or null when nothing is open.</summary>
    public MediaInfo? Info => _playback?.Info;

    public MediaTime Duration => _playback?.Info.Duration ?? MediaTime.Unknown;

    /// <summary>The position being heard right now; the full duration once the media has ended.</summary>
    public MediaTime Position => _state == SessionState.Ended && Duration.IsKnown ? Duration : _playback?.Position ?? MediaTime.Zero;

    /// <summary>The volume slider: 0 silent, 1 unity, 2 maximum boost. Applies immediately.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = double.IsFinite(value) ? Math.Clamp(value, 0, Audio.VolumeProcessor.Maximum) : 1.0;
            _playback?.ApplyVolume(_volume, _muted);
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            _playback?.ApplyVolume(_volume, _muted);
        }
    }

    private double _volume = 1.0;
    private bool _muted;

    /// <summary>Opens media and, unless <see cref="EngineOptions.AutoPlay"/> is off, starts playing it.</summary>
    public Task OpenAsync(IByteSource source, MediaTime? startAt = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Post(() => Open(source, startAt ?? MediaTime.Zero));
    }

    public Task PlayAsync() => Post(Play);

    public Task PauseAsync() => Post(Pause);

    public Task TogglePauseAsync() => Post(() =>
    {
        if (_state is SessionState.Playing or SessionState.Buffering)
        {
            Pause();
        }
        else
        {
            Play();
        }
    });

    public Task SeekAsync(MediaTime target, SeekMode mode = SeekMode.Precise) => Post(() => Seek(target, mode));

    /// <summary>Stops playback and releases the media; the session can open something else afterwards.</summary>
    public Task StopAsync() => Post(Stop);

    public Task<SessionStats> GetStatsAsync()
    {
        var completion = new TaskCompletionSource<SessionStats>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new Command(() => completion.TrySetResult(_playback?.Stats() ?? new SessionStats()), null));
        return completion.Task;
    }

    /// <summary>Completes when the current media ends, fails or is stopped.</summary>
    public Task WaitForFinishAsync() => Volatile.Read(ref _finished).Task;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
        _mailbox.CompleteAdding();
        if (Thread.CurrentThread != _mailboxThread)
        {
            _mailboxThread.Join();
        }

        _events.Dispose();
        _mailbox.Dispose();
    }

    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task Post(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new Command(action, completion));
        return completion.Task;
    }

    private void Enqueue(Command command)
    {
        ObjectDisposedException.ThrowIf(_mailbox.IsAddingCompleted, this);
        _mailbox.Add(command);
    }

    private void RunMailbox()
    {
        foreach (var command in _mailbox.GetConsumingEnumerable())
        {
            try
            {
                command.Action();
                command.Completion?.TrySetResult();
            }
            catch (Exception ex) when (ex is InvalidOperationException or MediaFormatException or IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
            {
                _log.Error(LogSource, ExceptionDiagnostics.Summary(ex));
                command.Completion?.TrySetException(ex);
            }
        }
    }

    private void MoveTo(SessionState next)
    {
        var previous = _state;
        if (previous == next)
        {
            return;
        }

        SessionStateMachine.Ensure(previous, next);
        _state = next;
        _events.Post(new StateChangedEvent(previous, next));
        if (next is SessionState.Ended or SessionState.Faulted or SessionState.Idle)
        {
            _finished.TrySetResult();
        }
    }

    private void Fail(string message)
    {
        _log.Error(LogSource, message);
        _events.Post(new ErrorEvent(message));
        if (SessionStateMachine.CanMove(_state, SessionState.Faulted))
        {
            MoveTo(SessionState.Faulted);
        }
    }

    private sealed record Command(Action Action, TaskCompletionSource? Completion);
}
