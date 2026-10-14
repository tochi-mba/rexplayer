using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Shell;
using Rex.Media.AppCore.Transcription;
using Rex.Media.Interop.Imaging;
using Rex.Media.Interop.Shell;
using Rex.Media.Interop.Windowing;
using Rex.Media.Library;
using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.App;

/// <summary>
/// The window in the Windows shell (WIN-05, WIN-06, WIN-07): the buttons under its taskbar
/// thumbnail, the badge over its taskbar button and the progress on it, Windows' media controls
/// (media keys, the volume flyout and the lock screen) and the jump list. What they offer is
/// <see cref="TransportAvailability"/>'s, the same for all of them and for the window's own media
/// keys. This part reads the player as it changes, works out a new view only when what the view
/// depends on changed, and hands <see cref="ShellWorker"/> only what is different; every call into
/// Explorer or the media service is made on the worker's thread, never the window's. None of it
/// starts until the window is up and drawing, a moment after the launch's first frame.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>How long after the window is interactive the shell is told about it: past the first picture and sound.</summary>
    private static readonly TimeSpan ShellStartDelay = TimeSpan.FromSeconds(1);

    /// <summary>How long a new item plays before its artwork is looked for, so skipping through items looks for none.</summary>
    private static readonly TimeSpan ArtworkDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>How long after the last change the jump list is written again.</summary>
    private static readonly TimeSpan JumpListDelay = TimeSpan.FromSeconds(3);

    /// <summary>The longest side of the artwork Windows' media card is given.</summary>
    private const int ArtworkSide = 300;

    /// <summary>Artwork files bigger than this are not read for a thumbnail.</summary>
    private const long LargestArtworkFile = 64L * 1024 * 1024;

    private readonly MediaKeyEcho _shellKeyEcho = new();
    private readonly TimelineThrottle _shellTimeline = new();
    private readonly Dictionary<string, byte[]> _artworkMemory = new(StringComparer.Ordinal);
    private readonly Queue<string> _artworkOrder = new();
    private ShellWorker? _shell;
    private TaskbarMessages? _shellMessages;
    private DispatcherQueueTimer? _jumpListTimer;
    private TransportInput _shellInput;
    private bool _shellInputRead;
    private TransportView _shellView = TransportAvailability.Nothing;
    private PlaylistItem? _cardItem;
    private MediaInfo? _cardInfo;
    private MediaCard? _shellCard;
    private CancellationTokenSource? _artwork;
    private string? _jumpListShown;
    private double? _hearing;
    private TaskbarProgressView _shownProgress = TaskbarProgressView.Off;

    private static TimeSpan ShellNow() => TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>Once the window is interactive: the shell is started a moment later, off the launch's path.</summary>
    private void StartShellSoon()
    {
        var start = DispatcherQueue.CreateTimer();
        start.Interval = ShellStartDelay;
        start.IsRepeating = false;
        start.Tick += (_, _) => ShowTaskbarSettingsChanged();
        start.Start();
    }

    /// <summary>The setting changed, or the shell starts: on, everything is shown; off, it is all given back.</summary>
    private void ShowTaskbarSettingsChanged()
    {
        if (_closing || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        if (_shell is null)
        {
            // Started even with the setting off, so a jump list left from when it was on is emptied.
            StartShell();
        }

        _shell!.Use(_settings.ShellMediaControls);
        (_shellInputRead, _shellCard, _cardItem, _cardInfo) = (false, null, null, null);
        _shellTimeline.Reset();
        ShowTaskbarState();
        ShowTaskbarPosition();
        RefreshJumpListLater();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763")]
    private void StartShell()
    {
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _shell = new ShellWorker(window, App.Log);
        _shell.MediaButtonPressed += (_, button) => OnWindowThread(() => SystemMediaPressed(button));
        _shell.MediaPositionRequested += (_, at) => OnWindowThread(() =>
        {
            if (_player.CanSeek)
            {
                _player.Seek(at);
            }
        });
        _shellMessages = new TaskbarMessages(window);
        _shellMessages.ButtonClicked += (_, id) => ShellButtonClicked(id);
        _shellMessages.ButtonCreated += (_, _) => _shell?.TaskbarButtonCreated();
        _shellMessages.AppearanceChanged += (_, _) => _shell?.AppearanceChanged();
        _jumpListTimer = DispatcherQueue.CreateTimer();
        _jumpListTimer.Interval = JumpListDelay;
        _jumpListTimer.IsRepeating = false;
        _jumpListTimer.Tick += (_, _) => PublishJumpList();
        App.Milestones.Reach("shell");
    }

    /// <summary>
    /// After any change of the player or the playlist: the view is worked out again only when what it
    /// depends on changed, and the worker hears only of a view or a card that differs.
    /// </summary>
    private void ShowTaskbarState()
    {
        if (_shell is null || _closing)
        {
            return;
        }

        // Reading the player allocates nothing; on most calls it is all that happens.
        var input = TransportAvailability.Read(_player);
        if (!_shellInputRead || input != _shellInput)
        {
            (_shellInput, _shellInputRead) = (input, true);
            var view = TransportAvailability.Decide(input);
            if (view != _shellView)
            {
                _shellView = view;
                _shell.ShowView(view);
                ShowTransportTooltips();
            }
        }

        ShowMediaCard();
    }

    /// <summary>As the position moves: Previous's restart and Windows' timeline, which is told about once a second and on seeks.</summary>
    private void ShowTaskbarPosition()
    {
        if (_shell is null || _closing)
        {
            return;
        }

        ShowTaskbarState();
        if (_player.Item is not null && _shellTimeline.ShouldPublish(_player.Position, _player.Duration, _player.CanSeek, _player.IsPlaying, _player.Speed, ShellNow()))
        {
            _shell.ShowTimeline(_player.Position, _player.Duration, _player.CanSeek);
        }
    }

    /// <summary>The window's own Previous and Next say what they will do, as the taskbar's do.</summary>
    private void ShowTransportTooltips()
    {
        foreach (var (button, word, transport, command) in new (Microsoft.UI.Xaml.FrameworkElement Button, string Word, TransportButton Transport, string Command)[]
        {
            (PreviousButton, "Previous", _shellView.Previous, CommandCatalog.Previous),
            (NextButton, "Next", _shellView.Next, CommandCatalog.Next),
        })
        {
            var label = _keymap.Label(command);
            var text = transport.IsVisible ? transport.Tooltip : word;
            ToolTipService.SetToolTip(button, label.Length > 0 ? $"{text} ({label})" : text);
        }
    }

    /// <summary>The card for Windows' media controls, made again only when the item or its details change.</summary>
    private void ShowMediaCard()
    {
        if (ReferenceEquals(_player.Item, _cardItem) && ReferenceEquals(_player.Info, _cardInfo))
        {
            return;
        }

        var itemChanged = !ReferenceEquals(_player.Item, _cardItem);
        (_cardItem, _cardInfo) = (_player.Item, _player.Info);
        if (itemChanged)
        {
            _shellTimeline.Reset();
            _artwork?.Cancel();
            _artwork?.Dispose();
            _artwork = null;
            RefreshJumpListLater();
        }

        if (MediaCard.For(_player) is not { } card)
        {
            // Still opening: the last card stays until this one is known, rather than flashing empty.
            if (_player.Item is null && _shellCard is not null)
            {
                _shellCard = null;
                _shell!.ShowCard(null, null);
            }

            return;
        }

        if (card == _shellCard)
        {
            return;
        }

        _shellCard = card;
        var artwork = _player.Item is { } item ? KnownArtwork(item.Location) : null;
        _shell!.ShowCard(card, artwork);
        if (artwork is null && _player.Item is { } playing && Path.IsPathFullyQualified(playing.Location))
        {
            _artwork?.Cancel();
            _artwork?.Dispose();
            _artwork = new CancellationTokenSource();
            _ = ShowArtworkAsync(playing, card, TransportAvailability.MediaOf(_player), _player.Duration, _settings, _artwork.Token);
        }
    }

    /// <summary>Finds the item's picture away from the window, shrinks it once and keeps it, and gives it to the card.</summary>
    private async Task ShowArtworkAsync(PlaylistItem item, MediaCard card, TransportMedia media, TimeSpan duration, Rex.Media.Settings.PlayerSettings settings, CancellationToken token)
    {
        try
        {
            await Task.Delay(ArtworkDelay, token);
            var artwork = await Task.Run(() => ArtworkFor(item.Location, media, duration, settings, token), token);
            if (artwork is not null && !token.IsCancellationRequested && ReferenceEquals(_player.Item, item) && card == _shellCard)
            {
                _shell?.ShowCard(card, artwork);
            }
        }
        catch (OperationCanceledException)
        {
            // Another item came first.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or MediaFormatException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            App.Log.Debug(LogSource, "No artwork for Windows' media controls: " + ex.Message);
        }
    }

    /// <summary>The shrunk artwork already made for <paramref name="path"/>, by location; null when there is none yet.</summary>
    private byte[]? KnownArtwork(string path)
    {
        lock (_artworkMemory)
        {
            return _artworkMemory.GetValueOrDefault(path);
        }
    }

    /// <summary>
    /// The item's picture as a small PNG: the picture itself, artwork beside it, the library's own
    /// thumbnail, or one made the way the library makes them. The last sixteen are kept, so going
    /// back to an item costs nothing.
    /// </summary>
    private byte[]? ArtworkFor(string path, TransportMedia media, TimeSpan duration, Rex.Media.Settings.PlayerSettings settings, CancellationToken token)
    {
        var kind = media switch
        {
            TransportMedia.Picture => LibraryKind.Picture,
            TransportMedia.Video => LibraryKind.Video,
            _ => LibraryKind.Music,
        };
        var local = kind == LibraryKind.Video ? LocalVideoPoster(path) : kind == LibraryKind.Music ? FolderArt.Find(path) : null;
        if (PictureKey(path, kind, settings, local) is not { } key)
        {
            return null;
        }

        var source = kind == LibraryKind.Picture ? path : local;
        byte[] encoded;
        if (source is not null)
        {
            // A picture of tens of megabytes is not worth reading for a thumbnail.
            if (new FileInfo(source).Length > LargestArtworkFile)
            {
                return null;
            }

            encoded = File.ReadAllBytes(source);
        }
        else
        {
            var cached = PictureCachePath(key);
            encoded = File.Exists(cached) ? File.ReadAllBytes(cached) : MakePicture(path, kind, duration > TimeSpan.Zero ? duration : null, key, settings, token);
        }

        token.ThrowIfCancellationRequested();
        var shrunk = Shrunk(encoded);
        lock (_artworkMemory)
        {
            if (_artworkMemory.TryAdd(path, shrunk))
            {
                _artworkOrder.Enqueue(path);
                while (_artworkOrder.Count > 16)
                {
                    _artworkMemory.Remove(_artworkOrder.Dequeue());
                }
            }
        }

        return shrunk;
    }

    /// <summary>A picture at most <see cref="ArtworkSide"/> pixels a side, as PNG: Windows keeps it for the card.</summary>
    private static byte[] Shrunk(byte[] encoded)
    {
        var bitmap = WicPicture.Decode(encoded, 1, ArtworkSide);
        using var frame = VideoFrame.Rent(PixelFormat.Bgra32, bitmap.Width, bitmap.Height);
        for (var y = 0; y < bitmap.Height; y++)
        {
            bitmap.Pixels.AsSpan(y * bitmap.Width * 4, bitmap.Width * 4).CopyTo(frame.Row(0, y));
        }

        using var output = new MemoryStream();
        PngWriter.Write(output, frame);
        return output.ToArray();
    }

    /// <summary>A thumbnail button was clicked: it runs what it offers now, never what it offered a moment ago.</summary>
    private void ShellButtonClicked(int id)
    {
        var request = id switch
        {
            ShellWorker.PreviousId => TransportRequest.Previous,
            ShellWorker.PlayPauseId => TransportRequest.PlayPause,
            ShellWorker.NextId => TransportRequest.Next,
            _ => (TransportRequest?)null,
        };
        if (request is { } asked)
        {
            ShellRun(asked);
        }
    }

    /// <summary>A media key or Windows' media card: a key the window has just handled itself is not run twice.</summary>
    private void SystemMediaPressed(MediaControlButton button)
    {
        var request = button switch
        {
            MediaControlButton.Play => TransportRequest.Play,
            MediaControlButton.Pause => TransportRequest.Pause,
            MediaControlButton.Stop => TransportRequest.Stop,
            MediaControlButton.Next => TransportRequest.Next,
            MediaControlButton.Previous => TransportRequest.Previous,
            _ => (TransportRequest?)null,
        };
        if (request is { } asked && !_shellKeyEcho.IsEcho(asked, ShellNow()))
        {
            ShellRun(asked);
        }
    }

    /// <summary>The window's own commands do the work: the shell has no second way of playing.</summary>
    private void ShellRun(TransportRequest request)
    {
        if (TransportAvailability.CommandFor(request, TransportAvailability.Decide(TransportAvailability.Read(_player))) is { } command)
        {
            App.Log.Debug(LogSource, $"Shell {request}: {command}");
            Run(command);
        }
    }

    /// <summary>
    /// A media key while the window is in front: it does what the taskbar and Windows' card offer,
    /// and Windows' copy of it, which may follow, is not run again.
    /// </summary>
    private bool HandleShellMediaKey(int key)
    {
        if (!_settings.ShellMediaControls || MediaKeyEcho.RequestOfKey(key) is not { } request)
        {
            return false;
        }

        _shellKeyEcho.Handled(request, ShellNow());
        ShellRun(request);
        return true;
    }

    /// <summary>
    /// The taskbar button's progress: a new version downloading, or speech being heard that the user
    /// asked for (what plays, or files picked in the library); never the position in what plays, and
    /// never the library's own background sweep.
    /// </summary>
    private void ShowTaskbarProgress()
    {
        var update = _updateDownload is null ? null : _updateTransfer?.Fraction;
        var progress = TaskbarProgress.Choose(_hearing, update, _updateDownload is not null && _updatePlaying);
        if (_shell is not null && progress != _shownProgress)
        {
            _shownProgress = progress;
            _shell.ShowProgress(progress);
        }
    }

    /// <summary>What the transcription worker is hearing, for the taskbar's progress when the user asked for it.</summary>
    private void ShowHearingProgress(WorkerStatus status)
    {
        _hearing = status.Path is { } path && (status.PlayingItem || _askedTranscripts.Contains(path, StringComparer.OrdinalIgnoreCase)) ? status.Share : null;
        ShowTaskbarProgress();
    }

    private void RefreshJumpListLater()
    {
        if (_jumpListTimer is { } timer)
        {
            timer.Stop();
            timer.Start();
        }
    }

    /// <summary>
    /// Writes the jump list when what it would show changed. Not for a run with a data folder of
    /// its own (the tests and the startup measurements): the jump list belongs to the program, not
    /// to the data folder, and a test must not rewrite the user's.
    /// </summary>
    private void PublishJumpList()
    {
        if (_shell is null || _closing || Environment.GetEnvironmentVariable("REXPLAYER_ROOT") is { Length: > 0 } || Environment.ProcessPath is not { } program)
        {
            return;
        }

        var keep = _settings.KeepHistory;
        var recent = keep && _settings.ShellMediaControls ? _player.Recent : [];
        var shown = $"{_settings.ShellMediaControls}\n{keep}\n{string.Join("\n", recent)}";
        if (shown == _jumpListShown)
        {
            return;
        }

        _jumpListShown = shown;
        _shell.PublishJumpList(program, recent, keep);
    }

    /// <summary>Releases the shell's part before the window goes; the worker gives Windows back what it held.</summary>
    private void DisposeShell()
    {
        _artwork?.Cancel();
        _artwork?.Dispose();
        _artwork = null;
        _jumpListTimer?.Stop();
        _shellMessages?.Dispose();
        _shellMessages = null;
        _shell?.Dispose();
        _shell = null;
    }
}
