using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Updates;
using Rex.Media.Engine;
using Rex.Media.Settings;
using Windows.Networking.Connectivity;

namespace Rex.Media.App;

/// <summary>
/// The updater's wiring (TOOL-09): the clock, the connection, the check, the background download
/// on a low-priority thread, and the hand-off to the installer. Every decision is
/// <see cref="UpdateFlow"/>'s and every word <see cref="UpdateView"/>'s; this file does the
/// network and process work, and MainWindow.UpdateView.cs draws.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly string UpdateFolder = Path.Combine(App.DataRoot, "updates");
    private static readonly string UpdateLog = Path.Combine(App.DataRoot, "logs", "update.log");

    private DispatcherQueueTimer? _updateTimer;
    private bool _updatesStarted;
    private bool _updateChecking;
    private bool _updateAsked;
    private CancellationTokenSource? _updateDownload;
    private string? _updateDownloadVersion;
    private TransferProgress? _updateTransfer;
    private DateTimeOffset? _lastCheckFailure;
    private int _checkFailures;
    private string? _checkProblem;
    private (bool Online, bool Metered) _connection = (true, false);
    private long _lastInputTick = Environment.TickCount64;
    private volatile bool _updatePlaying;
    private bool _showUpdatedNote;
    private UpdateNotice? _updateNotice;

    private PendingUpdate? Pending => _settings.PendingUpdate;

    /// <summary>What the last run handed over: did it install? Clears what is no longer wanted.</summary>
    private StartupOutcome ReconcileUpdate()
    {
        var before = Pending;
        var end = UpdateFlow.InstallEndOf(ReadInstallRecord(), before);
        var outcome = UpdateFlow.AtStartup(before, Version, DateTimeOffset.UtcNow, end);
        _updateNotice = UpdateView.News(outcome.News, end, before?.Version, Version);
        if (outcome.Pending != before)
        {
            _settings = _settings with { PendingUpdate = outcome.Pending };
            SaveSettings();
        }

        if (outcome.News != StartupNews.None)
        {
            App.Log.Info(LogSource, $"The update handed over by the last run: {outcome.News} ({end}).");
        }

        var keep = UpdateFlow.KeptFiles(outcome.Pending);
        _ = Task.Run(() =>
        {
            UpdateDownloader.Clean(UpdateFolder, keep);
            CleanOldUpdateFolders();
        });
        return outcome;
    }

    /// <summary>How the hand-off helper said the installer ended, or null when it left no record.</summary>
    private static string? ReadInstallRecord()
    {
        try
        {
            return File.ReadAllText(Path.Combine(UpdateFolder, UpdateFlow.InstallRecordName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Earlier versions staged updates under the temporary folder; those copies are not needed.</summary>
    private static void CleanOldUpdateFolders()
    {
        var old = Path.Combine(Path.GetTempPath(), "rexplayer-update");
        try
        {
            if (Directory.Exists(old))
            {
                Directory.Delete(old, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file still in use: it goes another time.
        }
    }

    /// <summary>
    /// Starts the updater's clock: the first look waits <see cref="UpdateFlow.StartupDelay"/> so it
    /// never slows a launch or the first playback, then it looks every minute at whether a check,
    /// a download, an install or a card is due. A change of network (waking from sleep, coming
    /// back online) looks at once.
    /// </summary>
    private void StartUpdates()
    {
        Root.AddHandler(UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _lastInputTick = Environment.TickCount64), handledEventsToo: true);
        Root.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _lastInputTick = Environment.TickCount64), handledEventsToo: true);
        Root.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _lastInputTick = Environment.TickCount64), handledEventsToo: true);
        Notice.Closed += (_, _) => ShowUpdates();
        UpdateLaterButton.Flyout = LaterMenu("UpdateLater");
        UpdateCardLater.Flyout = LaterMenu("UpdateCardLater");
        _connection = Connection();
        _updateTimer = Timer(UpdateFlow.StartupDelay, () =>
        {
            if (!_updatesStarted)
            {
                _updatesStarted = true;
                _updateTimer!.Interval = TimeSpan.FromMinutes(1);
                _updateTimer.IsRepeating = true;
                _updateTimer.Start();
            }

            UpdateTick();
        });
        _updateTimer.Start();
        NetworkInformation.NetworkStatusChanged += OnNetworkChanged;
        ShowUpdates();
    }

    private void OnNetworkChanged(object sender) => OnWindowThread(() =>
    {
        // A new connection deserves a fresh try, not the wait a failure on the old one earned.
        _connection = Connection();
        _lastCheckFailure = null;
        _checkFailures = 0;
        if (Pending is { } pending && _connection.Online)
        {
            SetPending(UpdateFlow.Reconnected(pending));
        }

        if (_updatesStarted)
        {
            UpdateTick();
        }
        else
        {
            ShowUpdates();
        }
    });

    private void StopUpdates()
    {
        NetworkInformation.NetworkStatusChanged -= OnNetworkChanged;
        _updateTimer?.Stop();
        _updateDownload?.Cancel();
    }

    /// <summary>One look at what is due, as <see cref="UpdateFlow.Plan"/> says: a check, starting or stopping the download, an install while idle.</summary>
    private void UpdateTick()
    {
        if (_closing || !UpdateFlow.Applies(Version))
        {
            return;
        }

        _connection = Connection();
        var plan = UpdateFlow.Plan(
            new UpdateClock(Pending, _settings.UpdateMode, _settings.UpdateChecks, _settings.LastUpdateCheck, _lastCheckFailure, _checkFailures,
                _updateChecking, _updateDownload is not null, _connection.Online, _connection.Metered),
            UpdateMomentNow(), DateTimeOffset.UtcNow);
        SetPending(plan.Pending);
        if (plan.Check)
        {
            _ = CheckForUpdatesAsync(asked: false);
            return;
        }

        if (plan.StopDownload)
        {
            App.Log.Info(LogSource, "The update download stops: the connection or the update settings changed.");
            _updateDownload?.Cancel();
        }

        if (plan.StartDownload)
        {
            StartUpdateDownload();
        }

        if (plan.Install)
        {
            _ = InstallAsync(unattended: true);
        }

        ShowUpdates();
    }

    private UpdateMoment UpdateMomentNow() => new(
        _player.IsPlaying || _player.State == SessionState.Opening,
        HasVideo,
        IsFullScreen,
        _dialog is not null,
        _subjectOpen,
        Notice.IsOpen || ResumeBar.IsOpen,
        _updatePanelOpen,
        TimeSpan.FromMilliseconds(Environment.TickCount64 - _lastInputTick));

    /// <summary>Whether Windows has an internet connection, and whether it costs by the byte.</summary>
    private static (bool Online, bool Metered) Connection()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null || profile.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.InternetAccess)
            {
                return (false, false);
            }

            var cost = profile.GetConnectionCost();
            return (true, cost.NetworkCostType is NetworkCostType.Fixed or NetworkCostType.Variable
                || cost.Roaming || cost.OverDataLimit || cost.ApproachingDataLimit);
        }
        catch (COMException)
        {
            // No answer from Windows: try, and let the request say whether there is a connection.
            return (true, false);
        }
    }

    /// <summary>
    /// Looks for a newer release (TOOL-09): automatically on the schedule the user chose, or when
    /// they ask (Help, Check for updates), which opens the update panel with the live state.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool asked)
    {
        if (!UpdateFlow.Applies(Version))
        {
            if (asked)
            {
                Say($"This build ({Version}) is a developer's own and is not updated.");
            }

            return;
        }

        if (asked)
        {
            _updateAsked = true;
            OpenUpdatePanel();
        }

        if (_updateChecking)
        {
            return;
        }

        _connection = Connection();
        if (!_connection.Online)
        {
            CheckFailed(UpdateView.CheckProblem(online: false, null, false), "offline");
            return;
        }

        _updateChecking = true;
        ShowUpdates();
        string json;
        try
        {
            using var http = UpdateClient(TimeSpan.FromSeconds(20));
            json = await http.GetStringAsync(UpdateCheck.LatestRelease);
        }
        catch (HttpRequestException ex)
        {
            CheckFailed(UpdateView.CheckProblem(online: true, (int?)ex.StatusCode, false), ex.Message);
            return;
        }
        catch (TaskCanceledException ex)
        {
            CheckFailed(UpdateView.CheckProblem(online: true, null, timedOut: true), ex.Message);
            return;
        }
        finally
        {
            _updateChecking = false;
        }

        if (_closing)
        {
            return;
        }

        (_lastCheckFailure, _checkFailures, _checkProblem, _updateAsked) = (null, 0, null, false);
        var outcome = UpdateFlow.AfterCheck(Pending, UpdateCheck.Offer(json, Version), Version,
            asked ? null : _settings.SkippedUpdateVersion, _settings.UpdateMode, _connection.Metered, asked);
        if (outcome.Discard)
        {
            if (_updateDownloadVersion != outcome.Pending?.Version)
            {
                _updateDownload?.Cancel();
            }

            var keep = UpdateFlow.KeptFiles(outcome.Pending);
            _ = Task.Run(() => UpdateDownloader.Clean(UpdateFolder, keep));
        }

        _settings = _settings with
        {
            LastUpdateCheck = DateTimeOffset.UtcNow,
            PendingUpdate = outcome.Pending,
            SkippedUpdateVersion = asked ? null : _settings.SkippedUpdateVersion,
        };
        SaveSettings();
        App.Log.Info(LogSource, outcome.Message);
        UpdateTick();
    }

    private void CheckFailed(string problem, string detail)
    {
        (_lastCheckFailure, _checkProblem, _updateAsked) = (DateTimeOffset.UtcNow, problem, false);
        _checkFailures++;
        App.Log.Info(LogSource, "Could not look for updates: " + detail);
        ShowUpdates();
    }

    /// <summary>
    /// Starts the pending download on a lowest-priority background thread so it never competes
    /// with playback; while something plays it also keeps to a gentle speed.
    /// </summary>
    private void StartUpdateDownload()
    {
        if (_updateDownload is not null || _closing || Pending is not { Stage: UpdateStage.Downloading } pending)
        {
            return;
        }

        var cancel = new CancellationTokenSource();
        (_updateDownload, _updateDownloadVersion, _updateTransfer) = (cancel, pending.Version, null);
        App.Log.Info(LogSource, $"Downloading rexplayer {pending.Version} in the background.");
        var thread = new Thread(() =>
        {
            var meter = new TransferMeter();
            var clock = Stopwatch.StartNew();
            var (shownPercent, shownAt) = (-1L, TimeSpan.Zero);
            DownloadReport report;
            using (var http = UpdateClient(TimeSpan.FromSeconds(30)))
            {
                var downloader = new UpdateDownloader(http, UpdateFolder) { Busy = () => _updatePlaying };
                report = downloader.Download(pending, (done, total) =>
                {
                    // A whole percent, or a second, at a time: the window's thread is not flooded.
                    var percent = total > 0 ? done * 100 / total : -1;
                    if (percent == shownPercent && clock.Elapsed - shownAt < TransferMeter.SampleEvery && done < total)
                    {
                        return;
                    }

                    (shownPercent, shownAt) = (percent, clock.Elapsed);
                    var progress = meter.Measure(done, total, clock.Elapsed);
                    OnWindowThread(() => UpdateDownloadProgressed(cancel, progress));
                }, cancel.Token);
            }

            OnWindowThread(() => UpdateDownloadFinished(cancel, pending.Version, report));
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.Lowest,
            Name = "rexplayer update download",
        };
        thread.Start();
    }

    private void UpdateDownloadProgressed(CancellationTokenSource download, TransferProgress progress)
    {
        if (ReferenceEquals(download, _updateDownload))
        {
            _updateTransfer = progress;
            ShowUpdates();
            ShowTaskbarProgress();
        }
    }

    /// <summary>
    /// A download attempt ended: verified, failed or stopped. It does not start another at once; a
    /// failure waits its turn, a stop was asked for, and another window holding the file is left
    /// to finish it.
    /// </summary>
    private void UpdateDownloadFinished(CancellationTokenSource download, string version, DownloadReport report)
    {
        if (ReferenceEquals(download, _updateDownload))
        {
            (_updateDownload, _updateDownloadVersion, _updateTransfer) = (null, null, null);
        }

        download.Dispose();
        ShowTaskbarProgress();
        if (Pending is { Stage: UpdateStage.Downloading } pending && pending.Version == version)
        {
            var after = UpdateFlow.AfterDownload(pending, report, DateTimeOffset.UtcNow);
            if (after.Stage == UpdateStage.Ready)
            {
                App.Log.Info(LogSource, $"rexplayer {version} is downloaded and verified.");
            }
            else if (after != pending)
            {
                App.Log.Warning(LogSource, $"The download of rexplayer {version} failed: {report.Reason}.");
            }

            SetPending(after);
        }

        // Files this attempt held (a cancelled download, a version a check replaced) can go now.
        var keep = UpdateFlow.KeptFiles(Pending);
        _ = Task.Run(() => UpdateDownloader.Clean(UpdateFolder, keep));
        ShowUpdates();
    }

    /// <summary>Saves a changed pending update and draws it.</summary>
    private void SetPending(PendingUpdate? next)
    {
        if (next == Pending)
        {
            return;
        }

        _settings = _settings with { PendingUpdate = next };
        SaveSettings();
        ShowUpdates();
    }

    /// <summary>
    /// Installs the downloaded update now and reopens rexplayer where it was, paused. The file is
    /// checked against its checksum once more first; a damaged one is downloaded again. Unattended
    /// (automatic or scheduled while idle), it gives up if the user came back meanwhile.
    /// </summary>
    private async Task InstallAsync(bool unattended)
    {
        if (_installingUpdate || _closing || Pending is not { Stage: UpdateStage.Ready } pending)
        {
            return;
        }

        _installingUpdate = true;
        try
        {
            if (!unattended)
            {
                Say($"Preparing rexplayer {pending.Version}...");
            }

            var whole = await Task.Run(() => UpdateDownloader.IsReady(UpdateFolder, pending));
            if (_closing || Pending != pending)
            {
                return;
            }

            if (!whole)
            {
                App.Log.Warning(LogSource, $"The downloaded rexplayer {pending.Version} no longer matches its checksum; downloading it again.");
                Say("The downloaded update was damaged, so rexplayer is downloading it again.");
                SetPending(UpdateFlow.Damaged(pending, asked: !unattended));
                UpdateTick();
                return;
            }

            if (unattended && !UpdateFlow.InstallWhileOpen(Pending, _settings.UpdateMode, UpdateMomentNow(), DateTimeOffset.UtcNow))
            {
                return;
            }

            if (HandOff(pending, relaunch: true))
            {
                App.Log.Info(LogSource, $"Handed rexplayer {pending.Version} to the updater; closing cleanly.");
                Close();
            }
        }
        finally
        {
            _installingUpdate = false;
        }
    }

    /// <summary>
    /// Saves what is needed and starts the hand-off helper, which waits for this process to close
    /// before it starts the installer. Relaunching, the session is saved to come back paused; as
    /// rexplayer closes, it installs quietly and reopens nothing. False, with the update still on
    /// offer, when the helper could not start.
    /// </summary>
    private bool HandOff(PendingUpdate pending, bool relaunch)
    {
        var sessionSaved = false;
        try
        {
            if (relaunch)
            {
                // Store the one-time hand-off *before* starting the updater, which waits for us to close.
                _player.SaveForUpdate(_librarySource.ToString(), LibraryOpen, LibrarySearch.Text,
                    _libraryGroup?.Name, _libraryGroup?.Detail, _librarySeason?.Name,
                    PlaylistPane.Visibility == Visibility.Visible,
                    new UpdateWindowState(_aspect.Name, _crop.Name, _view.Zoom,
                        _view.CenterX, _view.CenterY, IsFullScreen, _showRemaining),
                    targetVersion: pending.Version, resumePaused: true);
                sessionSaved = true;
            }

            var updater = Path.Combine(UpdateFolder, "rexupdate.exe");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "rexupdate.exe"), updater, overwrite: true);
            File.Delete(Path.Combine(UpdateFolder, UpdateFlow.InstallRecordName));
            var start = new ProcessStartInfo(updater) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[]
            {
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                UpdateDownloader.InstallerPath(UpdateFolder, pending),
                UpdateLog,
                relaunch ? "relaunch" : "quiet",
                Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "rexplayer.exe"),
            })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows did not start the update hand-off.");
            _settings = _settings with { PendingUpdate = UpdateFlow.HandedOff(pending, DateTimeOffset.UtcNow) };
            SaveSettings();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            if (sessionSaved)
            {
                try
                {
                    _player.Memory.Updating = null;
                }
                catch (Exception clearError) when (clearError is IOException or UnauthorizedAccessException)
                {
                    App.Log.Warning(LogSource, "The abandoned update marker could not be cleared: " + clearError.Message);
                }
            }

            App.Log.Warning(LogSource, "The update hand-off could not start: " + ex.Message);
            if (relaunch)
            {
                Say("The update is ready, but it could not start: " + ex.Message);
            }

            return false;
        }
    }

    /// <summary>
    /// As the window closes: installs a ready update the user chose to install on closing, or
    /// automatic mode allows, quietly and without reopening rexplayer. Another window that handed
    /// the same update over moments ago wins, and its record is kept rather than overwritten.
    /// </summary>
    private void InstallAsWeClose()
    {
        StopUpdates();
        PendingUpdate? onDisk = null;
        try
        {
            onDisk = SettingsStore.Load(SettingsPath).PendingUpdate;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Log.Warning(LogSource, "The saved update state could not be read: " + ex.Message);
        }

        var now = DateTimeOffset.UtcNow;
        if (!UpdateFlow.InstallOnClose(Pending, _settings.UpdateMode, onDisk, now))
        {
            if (onDisk is { Stage: UpdateStage.Installing } handed && Pending?.Stage != UpdateStage.Installing)
            {
                _settings = _settings with { PendingUpdate = handed };
            }

            return;
        }

        var pending = Pending!;
        if (!UpdateDownloader.IsReady(UpdateFolder, pending))
        {
            App.Log.Warning(LogSource, $"rexplayer {pending.Version} was not installed on closing: the download no longer matches its checksum.");
            _settings = _settings with { PendingUpdate = UpdateFlow.Damaged(pending, asked: false) };
            return;
        }

        if (HandOff(pending, relaunch: false))
        {
            App.Log.Info(LogSource, $"Handed rexplayer {pending.Version} to the updater to install as rexplayer closes.");
        }
    }

    private static HttpClient UpdateClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"rexplayer/{Version}");
        return http;
    }
}
