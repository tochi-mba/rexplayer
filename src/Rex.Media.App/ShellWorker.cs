using System.Runtime.InteropServices;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Shell;
using Rex.Media.Diagnostics;
using Rex.Media.Interop.Shell;
using Rex.Media.Interop.Windowing;

namespace Rex.Media.App;

/// <summary>
/// Tells the Windows shell what the window decided, on a thread of its own so no call into Explorer
/// or the media service ever holds up the window: the taskbar's thumbnail buttons, its badge and
/// progress, Windows' media controls and the jump list. The window hands over only what changed;
/// what arrives while the thread is busy replaces what was waiting, so a burst of changes (skipping
/// through ten songs) ends in one call per kind, for the last. Every decision is the window's; this
/// class only applies them, and a call Windows refuses is logged, never thrown.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763")]
internal sealed class ShellWorker : IDisposable
{
    private const string LogSource = "shell";

    /// <summary>The thumbnail buttons' ids, which the window's clicks are matched by.</summary>
    public const int PreviousId = 1;
    public const int PlayPauseId = 2;
    public const int NextId = 3;

    private readonly nint _window;
    private readonly RexLog _log;
    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _gate = new();
    private readonly Dictionary<(TaskbarGlyph Glyph, int Size, bool Light), TaskbarIcon> _icons = [];

    // Waiting for the thread, under the gate.
    private TransportView? _pendingView;
    private (MediaCard? Card, byte[]? Artwork)? _pendingCard;
    private (TimeSpan Position, TimeSpan Duration, bool Seekable)? _pendingTimeline;
    private TaskbarProgressView? _pendingProgress;
    private JumpListRequest? _pendingJumpList;
    private bool? _pendingMediaControls;
    private bool _pendingTaskbarRebuild;
    private bool _pendingAppearance;
    private bool _stopping;

    // The thread's own.
    private Taskbar? _taskbar;
    private bool _buttonsAdded;
    private int _iconSize;
    private bool _light;
    private TransportView _view = TransportAvailability.Nothing;
    private TransportBadge? _shownBadge;
    private TaskbarProgressView _progress = TaskbarProgressView.Off;
    private MediaTransportControls? _controls;
    private bool _controlsWanted;
    private (MediaCard? Card, byte[]? Artwork) _card;
    private (TimeSpan Position, TimeSpan Duration, bool Seekable)? _timeline;

    public ShellWorker(nint window, RexLog log)
    {
        _window = window;
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = "rexplayer shell", Priority = ThreadPriority.BelowNormal };

        // The taskbar's objects belong to a single-threaded apartment; this thread is theirs.
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Raised on a Windows thread when a media key or Windows' media card asks for something.</summary>
    public event EventHandler<MediaControlButton>? MediaButtonPressed;

    /// <summary>Raised on a Windows thread when Windows' media card asks to move to a position.</summary>
    public event EventHandler<TimeSpan>? MediaPositionRequested;

    /// <summary>What the transport offers now.</summary>
    public void ShowView(TransportView view) => Post(() => _pendingView = view);

    /// <summary>The media card to show (null for none), with its artwork encoded as PNG when known.</summary>
    public void ShowCard(MediaCard? card, byte[]? artwork) => Post(() => _pendingCard = (card, artwork));

    public void ShowTimeline(TimeSpan position, TimeSpan duration, bool seekable) => Post(() => _pendingTimeline = (position, duration, seekable));

    public void ShowProgress(TaskbarProgressView progress) => Post(() => _pendingProgress = progress);

    /// <summary>
    /// Turns the shell's part on or off (the setting). Off, the thumbnail buttons hide (Windows cannot
    /// take them away), the badge and progress go, Windows' media controls are given back and the
    /// jump list is emptied.
    /// </summary>
    public void Use(bool use) => Post(() => _pendingMediaControls = use);

    /// <summary>The taskbar button was made again (Explorer restarted, the window was shown again): start over with it.</summary>
    public void TaskbarButtonCreated() => Post(() => _pendingTaskbarRebuild = true);

    /// <summary>The scale or the theme may have changed: draw the icons again if so.</summary>
    public void AppearanceChanged() => Post(() => _pendingAppearance = true);

    /// <summary>Publishes the jump list for <paramref name="program"/> from what played recently.</summary>
    public void PublishJumpList(string program, IReadOnlyList<string> recent, bool keepHistory) =>
        Post(() => _pendingJumpList = new JumpListRequest(program, recent, keepHistory));

    public void Dispose()
    {
        Post(() => _stopping = true);
        _thread.Join(TimeSpan.FromSeconds(2));
        _wake.Dispose();
    }

    private void Post(Action change)
    {
        lock (_gate)
        {
            change();
        }

        _wake.Set();
    }

    private void Run()
    {
        _pendingTaskbarRebuild = true;
        while (true)
        {
            // On a single-threaded apartment this wait still lets COM deliver calls to the thread.
            _wake.WaitOne();
            TransportView? view;
            (MediaCard?, byte[]?)? card;
            (TimeSpan, TimeSpan, bool)? timeline;
            TaskbarProgressView? progress;
            JumpListRequest? jumpList;
            bool? mediaControls;
            bool rebuild, appearance, stopping;
            lock (_gate)
            {
                (view, card, timeline, progress, jumpList, mediaControls, rebuild, appearance, stopping) =
                    (_pendingView, _pendingCard, _pendingTimeline, _pendingProgress, _pendingJumpList, _pendingMediaControls, _pendingTaskbarRebuild, _pendingAppearance, _stopping);
                (_pendingView, _pendingCard, _pendingTimeline, _pendingProgress, _pendingJumpList, _pendingMediaControls, _pendingTaskbarRebuild, _pendingAppearance) =
                    (null, null, null, null, null, null, false, false);
            }

            if (stopping)
            {
                Stop();
                return;
            }

            if (view is { } newView)
            {
                _view = newView;
            }

            if (mediaControls is { } wanted)
            {
                _controlsWanted = wanted;
                view ??= _view;
                progress ??= _progress;
            }

            if (card is { } newCard)
            {
                _card = newCard;
            }

            if (progress is { } newProgress)
            {
                _progress = newProgress;
            }

            Attempt("the taskbar", () => ApplyTaskbar(rebuild, appearance, view is not null, progress is not null));
            Attempt("Windows' media controls", () => ApplyMediaControls(view is not null || mediaControls is not null, card is not null, timeline));
            if (jumpList is not null)
            {
                Attempt("the jump list", () => PublishJumpList(jumpList));
            }
        }
    }

    private void ApplyTaskbar(bool rebuild, bool appearance, bool viewChanged, bool progressChanged)
    {
        if (rebuild || _taskbar is null)
        {
            _taskbar?.Dispose();
            _taskbar = null;
            _buttonsAdded = false;
            _shownBadge = null;
            _taskbar = Taskbar.Create();
            ReadAppearance();
            progressChanged = true;
        }
        else if (appearance)
        {
            var (size, light) = (_iconSize, _light);
            ReadAppearance();
            if (size == _iconSize && light == _light)
            {
                return;
            }

            // New icons: the badge is drawn again with them.
            _shownBadge = null;
            viewChanged = true;
        }

        if (!_buttonsAdded)
        {
            // The taskbar takes buttons only once its button for the window exists; until then this
            // fails, and the taskbar's message that the button is made brings the window back here.
            try
            {
                _taskbar.AddButtons(_window, Buttons());
                _buttonsAdded = true;
            }
            catch (COMException ex)
            {
                _log.Debug(LogSource, "The thumbnail buttons wait for the taskbar button: " + ex.Message);
                return;
            }
        }
        else if (viewChanged)
        {
            _taskbar.UpdateButtons(_window, Buttons());
        }

        var badgeShown = Shown.Badge;
        if (_shownBadge != badgeShown)
        {
            var (glyph, description) = badgeShown switch
            {
                TransportBadge.Paused => ((TaskbarGlyph?)TaskbarGlyph.PausedBadge, "Paused"),
                TransportBadge.Failed => (TaskbarGlyph.FailedBadge, "Could not play"),
                _ => (null, ""),
            };
            _taskbar.SetOverlay(_window, glyph is { } badge ? Icon(badge) : null, description);
            _shownBadge = badgeShown;
        }

        if (progressChanged)
        {
            _taskbar.SetProgress(_window, (_controlsWanted ? _progress.State : TaskbarProgressState.None) switch
            {
                TaskbarProgressState.Normal => TaskbarProgressKind.Normal,
                TaskbarProgressState.Paused => TaskbarProgressKind.Paused,
                _ => TaskbarProgressKind.None,
            }, _progress.Percent);
        }
    }

    /// <summary>What the taskbar shows: the view, or nothing while the setting is off.</summary>
    private TransportView Shown => _controlsWanted ? _view : TransportAvailability.Nothing;

    private ThumbnailButton[] Buttons()
    {
        var view = Shown;
        return
        [
            new(PreviousId, Icon(TaskbarGlyph.Previous), view.Previous.Tooltip, view.Previous.IsVisible, view.Previous.IsEnabled),
            new(PlayPauseId, Icon(view.ShowsPause ? TaskbarGlyph.Pause : TaskbarGlyph.Play), view.PlayPause.Tooltip, view.PlayPause.IsVisible, view.PlayPause.IsEnabled),
            new(NextId, Icon(TaskbarGlyph.Next), view.Next.Tooltip, view.Next.IsVisible, view.Next.IsEnabled),
        ];
    }

    /// <summary>Each icon is drawn once per size and theme and kept: switching back costs nothing.</summary>
    private TaskbarIcon Icon(TaskbarGlyph glyph)
    {
        if (!_icons.TryGetValue((glyph, _iconSize, _light), out var icon))
        {
            icon = TaskbarIcon.FromPixels(TaskbarGlyphs.Render(glyph, _iconSize, TaskbarGlyphs.InkFor(_light)), _iconSize);
            _icons[(glyph, _iconSize, _light)] = icon;
        }

        return icon;
    }

    private void ReadAppearance()
    {
        _iconSize = TaskbarMessages.IconSize(_window);
        _light = TaskbarMessages.TaskbarIsLight();
    }

    private void ApplyMediaControls(bool stateChanged, bool cardChanged, (TimeSpan Position, TimeSpan Duration, bool Seekable)? timeline)
    {
        if (!_controlsWanted)
        {
            if (_controls is not null)
            {
                _controls.Dispose();
                _controls = null;
                _log.Info(LogSource, "Windows' media controls are off.");
            }

            return;
        }

        if (_controls is null)
        {
            _controls = MediaTransportControls.ForWindow(_window);
            _controls.ButtonPressed += (_, button) => MediaButtonPressed?.Invoke(this, button);
            _controls.PositionRequested += (_, position) => MediaPositionRequested?.Invoke(this, position);
            (stateChanged, cardChanged) = (true, true);
            timeline ??= _timeline;
            _log.Info(LogSource, "Windows' media controls are on.");
        }

        if (stateChanged)
        {
            var open = _view.Status != TransportStatus.Closed;
            _controls.IsEnabled = open;
            _controls.SetButtons(_view.PlayPause.IsEnabled, _view.PlayPause.IsEnabled, _view.Stop.IsEnabled, _view.Previous.IsEnabled, _view.Next.IsEnabled);
            _controls.Status = (int)_view.Status;
            if (!open)
            {
                _controls.ClearDisplay();
                _card = default;
            }
        }

        if (cardChanged && _view.Status != TransportStatus.Closed)
        {
            if (_card.Card is { } shown)
            {
                _controls.SetDisplay((int)shown.Kind, shown.Title, shown.Subtitle, shown.AlbumArtist, shown.AlbumTitle, shown.TrackNumber, _card.Artwork);
            }
            else
            {
                _controls.ClearDisplay();
            }
        }

        if (timeline is { } told)
        {
            _timeline = told;
            _controls.SetTimeline(told.Position, told.Duration, told.Seekable);
        }
    }

    private void PublishJumpList(JumpListRequest request) =>
        JumpList.Publish(null, request.Program, "Recently played", (slots, removed) =>
        {
            if (!_controlsWanted)
            {
                return ([], []);
            }

            var plan = JumpListPlan.Plan(request.Recent, request.KeepHistory, removed, slots, File.Exists);
            return ([.. plan.Recent.Select(entry => new JumpListLine(entry.Title, entry.Arguments))], [.. plan.Tasks.Select(entry => new JumpListLine(entry.Title, entry.Arguments))]);
        });

    /// <summary>Runs one part; a refusal from Windows is logged and the rest still goes ahead.</summary>
    private void Attempt(string what, Action part)
    {
        try
        {
            part();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or IOException)
        {
            _log.Warning(LogSource, $"Windows did not take the change to {what}: {ex.Message}");
            if (what == "the taskbar")
            {
                // Most often Explorer restarting: start over with the next change or its message.
                _taskbar?.Dispose();
                _taskbar = null;
            }
        }
    }

    private void Stop()
    {
        try
        {
            _controls?.Dispose();
            _taskbar?.Dispose();
        }
        catch (COMException)
        {
            // Closing: what Windows refuses now no longer matters.
        }

        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }
    }

    private sealed record JumpListRequest(string Program, IReadOnlyList<string> Recent, bool KeepHistory);
}
