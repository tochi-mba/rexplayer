using Rex.Media.Audio;
using Rex.Media.Diagnostics;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.Subtitles;

namespace Rex.Media.AppCore.Player;

/// <summary>
/// The player as the window sees it: a <see cref="Playlist"/> played through engine sessions. Each
/// item the user starts gets its own <see cref="MediaSession"/>, so an event from media the user has
/// already moved away from can never be taken for the current one; within a session the next item
/// is queued shortly before the current one ends, which keeps albums gapless. Every event is handed
/// to <c>dispatch</c> first, so the controller's state is only ever touched on one thread (the
/// window's) and its own events are raised there.
/// </summary>
public sealed partial class PlayerController : IDisposable
{
    /// <summary>How long before the end the next item is queued for a gapless start.</summary>
    public static readonly TimeSpan QueueAhead = TimeSpan.FromSeconds(5);

    private readonly Func<Action<SessionEvent>, MediaSession> _newSession;
    private readonly Func<string, IByteSource> _openSource;
    private readonly Func<string, IReadOnlyList<string>> _expand;
    private readonly Func<string, IReadOnlyList<SubtitleSidecar>> _sidecars;
    private readonly Func<string, byte[]> _readFile;
    private readonly Action<string, byte[]> _writeFile;
    private readonly TimeProvider _time;
    private readonly RexLog _log;
    private readonly Func<string, IEnumerable<string>>? _listFolder;
    private readonly Func<string, bool> _exists;
    private readonly List<TrackFailedEvent> _trackFailures = [];
    private double _fade = 1;
    private DateTimeOffset? _lastLaunch;

    /// <summary>
    /// Launches started this close together hand over files that join one playlist: Explorer opens
    /// several selected files with one launch each, all at once.
    /// </summary>
    public static readonly TimeSpan HandOverBurst = TimeSpan.FromMilliseconds(600);
    private readonly Action<Action> _dispatch;
    private MediaSession? _session;
    private bool _openedInSession;
    private PlaylistItem? _queued;
    private bool _queueTried;
    private int _failuresInARow;

    /// <param name="newSession">Makes a session that reports to the given listener.</param>
    /// <param name="openSource">Opens a location for reading; it may throw when the location cannot be read.</param>
    /// <param name="dispatch">Runs an action on the window's thread (tests run it in place).</param>
    /// <param name="settings">The jumps, volume steps and starting volume and modes; the defaults when null.</param>
    /// <param name="expand">Turns a location into the media it names: a folder into its files. By default folders on disk are expanded.</param>
    /// <param name="random">Chooses the shuffle order (tests pass a seeded one).</param>
    /// <param name="sidecars">Finds the subtitle files beside a piece of media; by default on disk.</param>
    /// <param name="readFile">Reads a subtitle or playlist file; by default from disk.</param>
    /// <param name="writeFile">Writes a playlist file; by default to disk, replacing it in one step.</param>
    /// <param name="store">Where the player remembers things between runs; by default only in memory.</param>
    /// <param name="time">The clock the sleep timer keeps time by (tests pass their own).</param>
    /// <param name="log">Where what the player does is written down, for diagnosing problems; by default only in memory.</param>
    /// <param name="listFolder">Lists a folder's files, for its cover picture; by default the disk's.</param>
    /// <param name="exists">Whether a file is there, for missing playlist entries; by default the disk's.</param>
    public PlayerController(
        Func<Action<SessionEvent>, MediaSession> newSession,
        Func<string, IByteSource> openSource,
        Action<Action> dispatch,
        PlayerSettings? settings = null,
        Func<string, IReadOnlyList<string>>? expand = null,
        Random? random = null,
        Func<string, IReadOnlyList<SubtitleSidecar>>? sidecars = null,
        Func<string, byte[]>? readFile = null,
        Action<string, byte[]>? writeFile = null,
        Rex.Media.Library.RexStore? store = null,
        TimeProvider? time = null,
        RexLog? log = null,
        Func<string, IEnumerable<string>>? listFolder = null,
        Func<string, bool>? exists = null)
    {
        _listFolder = listFolder;
        _exists = exists ?? File.Exists;
        _time = time ?? TimeProvider.System;
        _log = log ?? RexLog.InMemory();
        Memory = new PlayerMemory(store ?? Rex.Media.Library.RexStore.InMemory());
        _writeFile = writeFile ?? ((path, bytes) => AtomicFile.Write(path, bytes));
        _sidecars = sidecars ?? (media => SubtitleSidecars.Find(media));
        _readFile = readFile ?? File.ReadAllBytes;
        _newSession = newSession ?? throw new ArgumentNullException(nameof(newSession));
        _openSource = openSource ?? throw new ArgumentNullException(nameof(openSource));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _expand = expand ?? (location => Directory.Exists(location) ? MediaFiles.ExpandFolder(location) : [location]);
        Settings = settings ?? new PlayerSettings();
        Playlist = new Playlist(random) { Repeat = Settings.Repeat, Shuffle = Settings.Shuffle };
        Volume = Settings.Volume;
        Muted = Settings.Muted;
    }

    /// <summary>Raised when the state, the item, its details, the volume or the modes change.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised as the position moves (about ten times a second while playing).</summary>
    public event EventHandler? PositionChanged;

    /// <summary>A short message for the screen: the volume, a new title, a file that would not play.</summary>
    public event EventHandler<string>? Message;

    public Playlist Playlist { get; }

    /// <summary>
    /// The settings the jumps, volume steps, limits and sound come from; the window replaces them
    /// when they change, and the sound settings reach what is playing at once.
    /// </summary>
    public PlayerSettings Settings
    {
        get;
        set
        {
            field = (value ?? throw new ArgumentNullException(nameof(value))).Normalize();
            Memory.KeepsHistory = field.KeepHistory;
            Sound = SoundFor(field);
            if (_session is not null)
            {
                _session.Sound = Sound;
            }
        }
    }

    /// <summary>The sound settings in the engine's terms, made once per change so the equaliser is not rebuilt needlessly.</summary>
    public SoundSettings Sound { get; private set; } = SoundSettings.Plain;

    private static SoundSettings SoundFor(PlayerSettings settings) => new(
        new EqualizerSettings(settings.EqualizerEnabled, settings.EqualizerPreamp, settings.EqualizerGains),
        (StereoMode)settings.Stereo,
        new ReplayGainSettings((ReplayGainMode)settings.Loudness, settings.LoudnessPreamp));

    /// <summary>Idle before anything plays and after Stop; otherwise the current session's state.</summary>
    public SessionState State { get; private set; } = SessionState.Idle;

    public bool IsPlaying => State is SessionState.Playing or SessionState.Buffering or SessionState.Seeking or SessionState.Opening;

    /// <summary>What is playing (or was, after it ended), with its details once opened.</summary>
    public PlaylistItem? Item { get; private set; }

    public MediaInfo? Info { get; private set; }

    /// <summary>The title to show: the media's own, else the file name.</summary>
    public string Title => Item is { IsPart: true } part ? part.Title
        : Info?.Metadata.GetValueOrDefault(MetadataKeys.Title) is { Length: > 0 } title ? title : Item?.Title ?? "";

    /// <summary>Who made what is playing: the media's own tag, or for a part of a file the playlist's word.</summary>
    public string? Artist => Item is { IsPart: true, Artist: { } artist } ? artist
        : Info?.Metadata.GetValueOrDefault(MetadataKeys.Artist) is { Length: > 0 } tagged ? tagged : Item?.Artist;

    public TimeSpan Position { get; private set; }

    /// <summary>The length, or zero when it is not known (live streams).</summary>
    public TimeSpan Duration { get; private set; }

    public bool CanSeek => Info is { IsSeekable: true } && Duration > TimeSpan.Zero;

    public double Volume { get; private set; }

    /// <summary>How much later the sound is heard than the pictures are shown; it holds for every item.</summary>
    public TimeSpan AudioDelay { get; private set; }

    /// <summary>The speed media plays at, 1 being normal.</summary>
    public double Speed { get; private set; } = 1;

    public bool Muted { get; private set; }

    /// <summary>The id of the audio track playing, once the engine has chosen one.</summary>
    public int? AudioTrack { get; private set; }

    /// <summary>The media's audio tracks, in the order they are cycled through.</summary>
    public IReadOnlyList<TrackInfo> AudioTracks => Info?.Tracks.Where(track => track.Kind == MediaKind.Audio && track.Audio is not null).ToList() ?? [];

    /// <summary>The sample rate of the sound <see cref="ReadSound"/> gives, or 0 before any has played.</summary>
    public int SoundSampleRate => _session?.Scope.SampleRate ?? 0;

    /// <summary>The sound being heard now, newest samples last, for visualisations; false when there is none.</summary>
    public bool ReadSound(Span<float> left, Span<float> right) => _session?.ReadSound(left, right) ?? false;

    /// <summary>The engine's counters for what is playing, or null when nothing is.</summary>
    public Task<SessionStats?> StatsAsync() =>
        _session is { } session ? session.GetStatsAsync().ContinueWith(stats => stats.IsCompletedSuccessfully ? stats.Result : null, TaskScheduler.Default) : Task.FromResult<SessionStats?>(null);

    /// <summary>Why the last item failed, until another starts.</summary>
    public string? Failure { get; private set; }

    /// <summary>
    /// Plays what <paramref name="locations"/> name (files, folders, URLs). They replace the playlist
    /// and the first plays, or with <paramref name="enqueue"/> they join the end and play only if
    /// nothing is playing.
    /// </summary>
    public void Open(IEnumerable<string> locations, bool enqueue = false)
    {
        ArgumentNullException.ThrowIfNull(locations);
        OpenItems([.. locations.SelectMany(_expand).SelectMany(location => ItemsFor(location, depth: 0))], enqueue);
    }

    /// <summary>Plays <paramref name="items"/> as <see cref="Open"/> plays what locations stand for.</summary>
    private void OpenItems(List<PlaylistItem> items, bool enqueue)
    {
        _log.Info(LogSource, $"Opening {items.Count} item(s){(enqueue ? " at the end of the playlist" : "")}: {string.Join(", ", items.Take(5).Select(item => item.Location))}{(items.Count > 5 ? ", ..." : "")}");
        if (items.Count == 0)
        {
            Message?.Invoke(this, "There is nothing there rexplayer can play.");
            return;
        }

        if (!enqueue)
        {
            Playlist.Clear();
        }

        Playlist.Add(items);
        if (!enqueue || _session is null || State is SessionState.Ended or SessionState.Faulted)
        {
            Start(enqueue ? Playlist.JumpTo(Playlist.Items.Count - items.Count) : Playlist.Next(automatic: false));
        }
    }

    /// <summary>
    /// Opens files a launch of rexplayer (this one, or another that handed them over) started at
    /// <paramref name="launchedAt"/> asked for: as <see cref="Open"/> does, except that files from
    /// launches started within <see cref="HandOverBurst"/> of each other join one playlist.
    /// </summary>
    public void OpenHandedOver(IReadOnlyList<string> locations, bool enqueue, DateTimeOffset launchedAt)
    {
        ArgumentNullException.ThrowIfNull(locations);
        var burst = _lastLaunch is { } last && (launchedAt - last).Duration() < HandOverBurst;
        _lastLaunch = launchedAt;
        Open(locations, enqueue || burst);
    }

    /// <summary>Plays <paramref name="location"/> from <paramref name="at"/> on its own, as when resuming after a crash.</summary>
    public void Resume(string location, TimeSpan at)
    {
        Playlist.Clear();
        Playlist.Add([new PlaylistItem(location)]);
        Start(Playlist.JumpTo(0), at);
    }

    /// <summary>Plays the playlist entry at <paramref name="index"/>, as when the user picks it.</summary>
    public void PlayAt(int index) => Start(Playlist.JumpTo(index));

    /// <summary>Moves the position, keeping it inside the media.</summary>
    public void Seek(TimeSpan target)
    {
        if (_session is null || !CanSeek)
        {
            return;
        }

        Position = target < TimeSpan.Zero ? TimeSpan.Zero : target > Duration ? Duration : target;
        _log.Debug(LogSource, $"Seeking to {TimeText.Format(Position, Duration)}.");
        _ = Observe(_session.SeekAsync(new MediaTime((Item!.Start + Position).Ticks)));
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sets the volume (1 is the media's own level), up to the maximum the settings allow.</summary>
    public void SetVolume(double volume)
    {
        Volume = Math.Clamp(double.IsFinite(volume) ? volume : 1, 0, Settings.MaxVolumePercent / 100.0);
        if (_session is not null)
        {
            _session.Volume = Volume * _fade;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetMuted(bool muted)
    {
        Muted = muted;
        if (_session is not null)
        {
            _session.Muted = muted;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops playing and releases the media and the audio device.</summary>
    public void Stop()
    {
        _log.Info(LogSource, "Stopped.");
        RememberPosition();
        ResumeOffer = null;
        EndSession();
        State = SessionState.Idle;
        Position = TimeSpan.Zero;
        Changed?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        RememberPosition();
        EndSession();
    }

    /// <summary>
    /// Starts <paramref name="item"/> in a session of its own, or stops when there is nothing to
    /// start; part-way and on a chosen audio track when asked (switching tracks restarts the item).
    /// </summary>
    private void Start(PlaylistItem? item, TimeSpan startAt = default, int? audioTrack = null, bool paused = false)
    {
        if (item is null)
        {
            Stop();
            return;
        }

        startAt = BeforeStart(item, startAt, audioTrack);
        _log.Info(LogSource, $"Starting \"{item.Title}\" ({item.Location})"
            + (item.IsPart ? $", the part from {TimeText.Format(item.Start, TimeSpan.Zero)}{(item.End is { } partEnd ? " to " + TimeText.Format(partEnd, TimeSpan.Zero) : "")}" : "")
            + (startAt > TimeSpan.Zero ? $", at {TimeText.Format(startAt, TimeSpan.Zero)}" : "")
            + (audioTrack is { } track ? $", audio track {track}" : "")
            + (paused ? ", paused" : "") + ".");
        _trackFailures.Clear();
        ForgetPresentation();
        EndSession();
        Item = item;
        Info = null;
        AudioTrack = null;
        FindSubtitles(item);
        Failure = null;
        Position = startAt;
        Duration = TimeSpan.Zero;
        State = SessionState.Opening;
        MediaSession? session = null;
        session = _newSession(sessionEvent => _dispatch(() => OnEvent(session!, sessionEvent)));
        session.Volume = Volume * _fade;
        session.Muted = Muted;
        session.StartPaused = paused;
        session.PreferredAudioTrack = audioTrack;
        session.AudioLanguages = Languages.ParseList(Settings.AudioLanguages);
        session.Sound = Sound;
        session.AudioDelay = AudioDelay;
        if (Speed != 1)
        {
            _ = Observe(session.SetSpeedAsync(Speed));
        }
        _session = session;
        Changed?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);

        IByteSource source;
        try
        {
            source = _openSource(item.Location);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            OnFailed(item, ex.Message);
            return;
        }

        // A failure to open arrives as the session's move to Faulted, in order with its other events.
        var from = item.Start + startAt;
        _ = Observe(session.OpenAsync(source, from > TimeSpan.Zero ? new MediaTime(from.Ticks) : null));
    }

    private void EndSession()
    {
        if (_session is { } old)
        {
            _session = null;
            _openedInSession = false;
            _queued = null;
            _queueTried = false;

            // Stopping joins the session's threads, which can take a moment: never on the window's thread.
            _ = Task.Run(old.Dispose);
        }
    }

    /// <summary>The current item failed, opening or part-way: say why and move on.</summary>
    private void OnFailed(PlaylistItem item, string reason)
    {
        _log.Error(LogSource, $"{item.Location} could not be played: {reason}");
        Failure = reason;
        State = SessionState.Faulted;
        Message?.Invoke(this, $"{item.Title} could not be played: {reason}");
        Changed?.Invoke(this, EventArgs.Empty);

        // Move on, unless every entry has now failed in a row: then there is nothing left worth trying.
        if (++_failuresInARow < Playlist.Items.Count && Playlist.Next(automatic: false) is { } next)
        {
            Start(next);
        }
    }

    /// <summary>Lets a command the session refuses (it may have just ended) fail quietly instead of as an unobserved task.</summary>
    internal static Task Observe(Task task) =>
        task.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
