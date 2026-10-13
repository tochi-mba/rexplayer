using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Shell;
using Rex.Media.Interop.Shell;
using Rex.Media.Interop.Windowing;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>Windows shell surfaces: a view of the same transport and the same media information.</summary>
public sealed partial class MainWindow
{
    private readonly TimelineThrottle _shellTimeline = new();
    private readonly MediaKeyEcho _shellKeyEcho = new();
    private readonly Dictionary<TaskbarGlyph, TaskbarIcon> _shellIcons = [];

    private TaskbarMessages? _shellMessages;
    private Taskbar? _shellTaskbar;
    private MediaTransportControls? _shellControls;
    private PlaylistItem? _shellItem;
    private TransportView? _shellView;
    private MediaCard? _shellCard;
    private bool _shellTaskbarReady;
    private bool _shellButtonsAdded;
    private string? _shellRecentKey;
    private nint _shellHandle;
    private CancellationTokenSource? _shellPictureCancel;
    private byte[] _shellPicture = [];
    private bool _shellPictureRequested;

    /// <summary>Register before the window is activated, so even its first taskbar creation is heard.</summary>
    private void WireTaskbar()
    {
        if (!_settings.ShellMediaControls || _shellMessages is not null)
        {
            return;
        }

        _shellHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        try
        {
            _shellMessages = new TaskbarMessages(_shellHandle);
            _shellMessages.ButtonCreated += (_, _) => TaskbarButtonCreated();
            _shellMessages.ButtonClicked += (_, id) => ShellButtonClicked(id);
            _shellMessages.AppearanceChanged += (_, _) => TaskbarAppearanceChanged();
        }
        catch (Exception ex) when (ShellUnavailable(ex))
        {
            App.Log.Info(LogSource, "Taskbar messages unavailable: " + ex.Message);
        }

        try
        {
            _shellControls = MediaTransportControls.ForWindow(_shellHandle);
            _shellControls.ButtonPressed += (_, button) => OnWindowThread(() => SystemMediaPressed(button));
            _shellControls.PositionRequested += (_, at) => OnWindowThread(() =>
            {
                if (_player.CanSeek)
                {
                    _player.Seek(at);
                }
            });
            _shellControls.IsEnabled = false;
        }
        catch (Exception ex) when (ShellUnavailable(ex))
        {
            _shellControls?.Dispose();
            _shellControls = null;
            App.Log.Info(LogSource, "System media controls unavailable: " + ex.Message);
        }

        ShowTaskbarState();
    }

    /// <summary>A missing shell, a stopped media service or a failed COM activation is optional.</summary>
    private static bool ShellUnavailable(Exception ex) =>
        ex is COMException or InvalidOperationException or PlatformNotSupportedException or NotSupportedException;

    /// <summary>The taskbar button can be remade after Explorer restarts. Add again, never update first.</summary>
    private void TaskbarButtonCreated()
    {
        if (_closing || !_settings.ShellMediaControls)
        {
            return;
        }

        _shellTaskbarReady = true;
        _shellButtonsAdded = false;
        _shellTaskbar?.Dispose();
        _shellTaskbar = null;
        try
        {
            _shellTaskbar = Taskbar.Create();
            RepaintShellIcons();
            ShowTaskbarState();
        }
        catch (Exception ex) when (ShellUnavailable(ex))
        {
            App.Log.Info(LogSource, "Taskbar unavailable: " + ex.Message);
        }
    }

    private void TaskbarAppearanceChanged()
    {
        if (_shellTaskbar is null)
        {
            return;
        }

        RepaintShellIcons();
        _shellView = null;
        ShowTaskbarState();
    }

    /// <summary>Create the same shapes at the new DPI and with the taskbar's ink.</summary>
    private void RepaintShellIcons()
    {
        foreach (var icon in _shellIcons.Values)
        {
            icon.Dispose();
        }

        _shellIcons.Clear();
        var size = TaskbarMessages.IconSize(_shellHandle);
        var ink = TaskbarGlyphs.InkFor(TaskbarMessages.TaskbarIsLight());
        foreach (var glyph in Enum.GetValues<TaskbarGlyph>())
        {
            _shellIcons[glyph] = TaskbarIcon.FromPixels(TaskbarGlyphs.Render(glyph, size, ink), size);
        }
    }

    private void ShellButtonClicked(int id)
    {
        var request = id switch
        {
            1 => TransportRequest.Previous,
            2 => TransportRequest.PlayPause,
            3 => TransportRequest.Next,
            _ => (TransportRequest?)null,
        };
        if (request is { } asked)
        {
            ShellRun(asked);
        }
    }

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

        if (request is not { } asked || _shellKeyEcho.IsEcho(asked, ShellNow()))
        {
            return;
        }

        ShellRun(asked);
    }

    /// <summary>Use the window's command path, not a second implementation of playback controls.</summary>
    private void ShellRun(TransportRequest request)
    {
        var view = TransportAvailability.Decide(TransportAvailability.Read(_player));
        if (TransportAvailability.CommandFor(request, view) is { } command)
        {
            Run(command);
        }
    }

    /// <summary>Used by the foreground key handler before Windows sends its matching media event.</summary>
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

    private static TimeSpan ShellNow() => TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>Synchronise button visibility, the card and Windows' playback status when they change.</summary>
    private void ShowTaskbarState()
    {
        if (_closing || !_settings.ShellMediaControls)
        {
            return;
        }

        var input = TransportAvailability.Read(_player);
        var view = TransportAvailability.Decide(input);
        if (!ReferenceEquals(_shellItem, _player.Item))
        {
            _shellItem = _player.Item;
            _shellCard = null;
            _shellTimeline.Reset();
            _shellPictureCancel?.Cancel();
            _shellPictureCancel?.Dispose();
            _shellPictureCancel = null;
            _shellPicture = [];
            _shellPictureRequested = false;
        }

        if (_shellTaskbarReady && _shellTaskbar is not null && (!_shellButtonsAdded || _shellView != view))
        {
            try
            {
                var buttons = new[]
                {
                    ShellButton(1, view.Previous, TaskbarGlyph.Previous),
                    ShellButton(2, view.PlayPause, view.ShowsPause ? TaskbarGlyph.Pause : TaskbarGlyph.Play),
                    ShellButton(3, view.Next, TaskbarGlyph.Next),
                };
                if (_shellButtonsAdded)
                {
                    _shellTaskbar.UpdateButtons(_shellHandle, buttons);
                }
                else
                {
                    _shellTaskbar.AddButtons(_shellHandle, buttons);
                    _shellButtonsAdded = true;
                }

                var badge = view.Badge switch
                {
                    TransportBadge.Paused => TaskbarGlyph.PausedBadge,
                    TransportBadge.Failed => TaskbarGlyph.FailedBadge,
                    _ => (TaskbarGlyph?)null,
                };
                _shellTaskbar.SetOverlay(_shellHandle, badge is { } glyph ? _shellIcons[glyph] : null,
                    view.Badge == TransportBadge.Paused ? "Paused" : view.Badge == TransportBadge.Failed ? "Could not play" : "");

            }
            catch (Exception ex) when (ShellUnavailable(ex))
            {
                App.Log.Info(LogSource, "Taskbar did not accept the transport: " + ex.Message);
                _shellButtonsAdded = false;
            }
        }

        if (_shellControls is not null && _shellView != view)
        {
            try
            {
                _shellControls.SetButtons(view.PlayPause.IsEnabled && !view.ShowsPause,
                    view.PlayPause.IsEnabled && view.ShowsPause, view.Stop.IsEnabled,
                    view.Previous.IsEnabled, view.Next.IsEnabled);
                _shellControls.Status = (int)view.Status;
                _shellControls.IsEnabled = view.Status != TransportStatus.Closed;
            }
            catch (Exception ex) when (ShellUnavailable(ex))
            {
                App.Log.Info(LogSource, "Media controls could not change state: " + ex.Message);
            }
        }

        if (_shellControls is not null)
        {
            try
            {
                var card = MediaCard.For(_player);
                if (_shellCard != card)
                {
                    if (card is null)
                    {
                        _shellControls.ClearDisplay();
                    }
                    else
                    {
                        _shellControls.SetDisplay((int)card.Kind, card.Title, card.Subtitle,
                            card.AlbumArtist, card.AlbumTitle, card.TrackNumber, _shellPicture);
                    }

                    _shellCard = card;
                }

                if (card is not null && !_shellPictureRequested && _player.Item is { } item)
                {
                    StartShellPicture(item, card.Kind);
                }
            }
            catch (Exception ex) when (ShellUnavailable(ex))
            {
                App.Log.Info(LogSource, "Media card unavailable: " + ex.Message);
            }
        }

        _shellView = view;
        ShowTaskbarPosition();
        ShowTaskbarProgress();
        RefreshJumpList();
    }

    /// <summary>Show background download progress, never media playback position.</summary>
    private void ShowTaskbarProgress()
    {
        if (_closing || !_settings.ShellMediaControls || !_shellTaskbarReady || _shellTaskbar is null)
        {
            return;
        }

        var progress = TaskbarProgress.Choose(null,
            _updateDownload is null ? null : _updateTransfer?.Fraction,
            _updateDownload is not null && _updatePlaying);
        var state = progress.State switch
        {
            TaskbarProgressState.Normal => TaskbarProgressKind.Normal,
            TaskbarProgressState.Paused => TaskbarProgressKind.Paused,
            _ => TaskbarProgressKind.None,
        };
        try
        {
            _shellTaskbar.SetProgress(_shellHandle, state, progress.Percent);
        }
        catch (Exception ex) when (ShellUnavailable(ex))
        {
            App.Log.Info(LogSource, "Taskbar could not show download progress: " + ex.Message);
        }
    }

    private ThumbnailButton ShellButton(int id, TransportButton button, TaskbarGlyph glyph) =>
        new(id, _shellIcons[glyph], TransportAvailability.Fit(button.Tooltip), button.IsVisible, button.IsEnabled);

    /// <summary>Tell Windows a changed seek, duration or speed, not each position notification.</summary>
    private void ShowTaskbarPosition()
    {
        if (_closing || !_settings.ShellMediaControls || _shellControls is null || _player.Item is null)
        {
            return;
        }

        var position = _player.Position;
        var duration = _player.Duration;
        if (_shellTimeline.ShouldPublish(position, duration, _player.CanSeek, _player.IsPlaying, _player.Speed, ShellNow()))
        {
            try
            {
                _shellControls.SetTimeline(position, duration, _player.CanSeek);
            }
            catch (Exception ex) when (ShellUnavailable(ex))
            {
                App.Log.Info(LogSource, "System media timeline unavailable: " + ex.Message);
            }
        }
    }

    /// <summary>Rebuild only when the recent list or privacy choice changed, off the first paint path.</summary>
    private void RefreshJumpList()
    {
        if (!_settings.ShellMediaControls || _closing)
        {
            return;
        }

        var recent = _player.Recent;
        var key = (_settings.KeepHistory ? "on:" : "off:") + string.Join("\n", recent);
        if (key == _shellRecentKey)
        {
            return;
        }

        _shellRecentKey = key;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_closing || !_settings.ShellMediaControls)
            {
                return;
            }

            try
            {
                var executable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "rexplayer.exe");
                JumpList.Publish(null, executable, "Recently played", (slots, removed) =>
                {
                    var plan = JumpListPlan.Plan(_player.Recent, _settings.KeepHistory, removed, slots, File.Exists);
                    return (plan.Recent.Select(item => new JumpListLine(item.Title, item.Arguments)).ToArray(),
                        plan.Tasks.Select(item => new JumpListLine(item.Title, item.Arguments)).ToArray());
                });
            }
            catch (Exception ex) when (ShellUnavailable(ex))
            {
                App.Log.Info(LogSource, "Jump list unavailable: " + ex.Message);
            }
        });
    }

    /// <summary>Load the current item's artwork off the window thread, without delaying its first frame.</summary>
    private void StartShellPicture(PlaylistItem item, MediaCardKind kind)
    {
        _shellPictureRequested = true;
        if (!Path.IsPathFullyQualified(item.Location))
        {
            return;
        }

        _shellPictureCancel = new CancellationTokenSource();
        _ = LoadShellPictureAsync(item, kind, _player.Duration, _settings, _shellPictureCancel.Token);
    }

    private async Task LoadShellPictureAsync(PlaylistItem item, MediaCardKind kind, TimeSpan duration, Rex.Media.Settings.PlayerSettings settings, CancellationToken token)
    {
        var entered = false;
        try
        {
            await _pictureSlots.WaitAsync(token);
            entered = true;
            var picture = await Task.Run(() =>
            {
                var mediaKind = kind switch
                {
                    MediaCardKind.Image => LibraryKind.Picture,
                    MediaCardKind.Video => LibraryKind.Video,
                    _ => LibraryKind.Music,
                };
                var path = item.Location;
                var local = mediaKind == LibraryKind.Video ? LocalVideoPoster(path)
                    : mediaKind == LibraryKind.Music ? FolderArt.Find(path) : null;
                var key = PictureKey(path, mediaKind, settings, local);
                if (key is null)
                {
                    return Array.Empty<byte>();
                }

                // Windows' card accepts JPEG or PNG bytes. Use an existing local cover when it
                // is modest in size; otherwise use the same bounded artwork cache as the library.
                if (local is { } poster && Path.GetExtension(poster).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png"
                    && new FileInfo(poster).Length is > 0 and <= 2_000_000)
                {
                    return File.ReadAllBytes(poster);
                }

                var cached = PictureCachePath(key);
                return File.Exists(cached) ? File.ReadAllBytes(cached)
                    : MakePicture(path, mediaKind, duration, key, settings, token);
            }, token);
            token.ThrowIfCancellationRequested();
            OnWindowThread(() =>
            {
                if (!_settings.ShellMediaControls || !ReferenceEquals(_player.Item, item) || _shellControls is null)
                {
                    return;
                }

                _shellPicture = picture;
                _shellCard = null;
                ShowTaskbarState();
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or NotSupportedException or Rex.Media.Primitives.MediaFormatException)
        {
            App.Log.Debug(LogSource, "No system media artwork: " + ex.Message);
        }
        finally
        {
            if (entered)
            {
                _pictureSlots.Release();
            }
        }
    }

    private void ShowTaskbarSettingsChanged()
    {
        if (_settings.ShellMediaControls)
        {
            WireTaskbar();
            _shellView = null;
            _shellRecentKey = null;
            ShowTaskbarState();
        }
        else
        {
            DisposeShell();
        }
    }

    /// <summary>Releases native icons, subscriptions and COM objects before the WinUI window goes.</summary>
    private void DisposeShell()
    {
        _shellPictureCancel?.Cancel();
        _shellPictureCancel?.Dispose();
        _shellPictureCancel = null;
        _shellPicture = [];
        _shellPictureRequested = false;
        _shellControls?.Dispose();
        _shellControls = null;
        _shellMessages?.Dispose();
        _shellMessages = null;
        _shellTaskbar?.Dispose();
        _shellTaskbar = null;
        foreach (var icon in _shellIcons.Values)
        {
            icon.Dispose();
        }

        _shellIcons.Clear();
        _shellTaskbarReady = false;
        _shellButtonsAdded = false;
        _shellView = null;
        _shellCard = null;
        _shellRecentKey = null;
    }
}
