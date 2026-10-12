using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Updates;
using Rex.Media.Engine;
using Rex.Media.Settings;
using Windows.Networking.Connectivity;

namespace Rex.Media.App;

/// <summary>
/// The updater's wiring (TOOL-09): automatic checks, the background download on a low-priority
/// thread, the update bar, and the hand-off to the installer. Every decision is
/// <see cref="UpdateFlow"/>'s; this file keeps the clocks and does the network and process work.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly string UpdateFolder = Path.Combine(App.DataRoot, "updates");
    private static readonly string UpdateLog = Path.Combine(App.DataRoot, "logs", "update.log");

    private DispatcherQueueTimer? _updateTimer;
    private bool _updatesStarted;
    private bool _updateChecking;
    private bool _updateAsked;
    private bool _showingUpdateProgress;
    private UpdatePrompt _shownPrompt;
    private CancellationTokenSource? _updateDownload;
    private string? _updateDownloadVersion;
    private double? _updatePercent;
    private DateTimeOffset? _lastCheckFailure;
    private int _checkFailures;
    private long _lastInputTick = Environment.TickCount64;
    private volatile bool _updatePlaying;
    private TextBlock? _updateStatusText;
    private bool _showUpdatedNote;

    private PendingUpdate? Pending => _settings.PendingUpdate;

    /// <summary>What the last run handed over: did it install? Clears what is no longer wanted.</summary>
    private StartupOutcome ReconcileUpdate()
    {
        var outcome = UpdateFlow.AtStartup(Pending, Version, DateTimeOffset.UtcNow);
        if (outcome.Pending != Pending)
        {
            _settings = _settings with { PendingUpdate = outcome.Pending };
            SaveSettings();
        }

        if (outcome.News != StartupNews.None)
        {
            App.Log.Info(LogSource, $"The update handed over by the last run: {outcome.News}.");
        }

        var keep = outcome.Pending;
        _ = Task.Run(() =>
        {
            UpdateDownloader.Clean(UpdateFolder, keep);
            CleanOldUpdateFolders();
        });
        return outcome;
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

    /// <summary>Tells the user how the update the last run handed over went.</summary>
    private void ShowUpdateNews(StartupNews news)
    {
        var failed = _settings.PendingUpdate?.Version;
        switch (news)
        {
            case StartupNews.InstallFailed:
                var retry = new Button { Content = "Try again" };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(retry, "UpdateRetryButton");
                retry.Click += (_, _) =>
                {
                    Notice.IsOpen = false;
                    _ = InstallAsync(unattended: false);
                };
                ShowNotice($"rexplayer {failed} did not install",
                    $"The installer was cancelled or stopped, so rexplayer {Version} is still here and nothing was lost. The log (Help, Open the log folder) has the details.",
                    retry, InfoBarSeverity.Warning);
                break;
            case StartupNews.InstallAbandoned:
                ShowNotice("The update could not be installed",
                    $"It failed {UpdateFlow.MostInstallFailures} times, so rexplayer {Version} set it aside. A later check offers it again; the log has the details.",
                    null, InfoBarSeverity.Warning);
                break;
            default:
                if (_showUpdatedNote && !Notice.IsOpen)
                {
                    ShowUpdatedNote();
                }

                break;
        }
    }

    /// <summary>The small note the first start of a new version shows: "Updated to X", with What's new.</summary>
    private void ShowUpdatedNote()
    {
        var whatsNew = new Button { Content = "What's new" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(whatsNew, "UpdatedWhatsNewButton");
        whatsNew.Click += (_, _) =>
        {
            Notice.IsOpen = false;
            _ = ShowWhatsNewAsync();
        };
        ShowNotice($"Updated to rexplayer {Version}", "Everything you had open is as you left it.", whatsNew, InfoBarSeverity.Success);
    }

    /// <summary>
    /// Starts the updater's clock: the first look waits <see cref="UpdateFlow.StartupDelay"/> so it
    /// never slows a launch or the first playback, then it looks every minute at whether a check,
    /// a download, an install or a prompt is due. A change of network (waking from sleep, coming
    /// back online) looks at once.
    /// </summary>
    private void StartUpdates()
    {
        Root.AddHandler(UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _lastInputTick = Environment.TickCount64), handledEventsToo: true);
        Root.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _lastInputTick = Environment.TickCount64), handledEventsToo: true);
        Root.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _lastInputTick = Environment.TickCount64), handledEventsToo: true);
        UpdateBar.Closing += (_, _) => FocusAwayFromUpdateBar();
        Notice.Closed += (_, _) => ConsiderUpdatePrompt();
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
    }

    private void OnNetworkChanged(object sender) => OnWindowThread(() =>
    {
        if (!_updatesStarted)
        {
            return;
        }

        // A new connection deserves a fresh try, not the wait a failure on the old one earned.
        _lastCheckFailure = null;
        _checkFailures = 0;
        if (Pending is { Stage: UpdateStage.Downloading, RetryAt: not null } waiting && Connection().Online)
        {
            _settings = _settings with { PendingUpdate = waiting with { RetryAt = null } };
        }

        UpdateTick();
    });

    private void StopUpdates()
    {
        NetworkInformation.NetworkStatusChanged -= OnNetworkChanged;
        _updateTimer?.Stop();
        _updateDownload?.Cancel();
    }

    /// <summary>One look at what is due: a check, a download (or stopping one), an install while idle, a prompt.</summary>
    private void UpdateTick()
    {
        if (_closing || !UpdateFlow.Applies(Version))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!_updateChecking && UpdateFlow.CheckDue(_settings.UpdateChecks, _settings.LastUpdateCheck, _lastCheckFailure, _checkFailures, now))
        {
            _ = CheckForUpdatesAsync(asked: false);
            return;
        }

        var (online, metered) = Connection();
        if (_updateDownload is { } running && (!online || !UpdateFlow.MayDownload(Pending, _settings.UpdateMode, _settings.UpdateChecks, metered, now)))
        {
            App.Log.Info(LogSource, "The update download stops: the connection or the update settings changed.");
            running.Cancel();
        }

        StartUpdateDownload();
        if (UpdateFlow.InstallWhileOpen(Pending, _settings.UpdateMode, UpdateMomentNow(), now))
        {
            _ = InstallAsync(unattended: true);
            return;
        }

        ConsiderUpdatePrompt();
        ShowUpdateStatus();
    }

    private UpdateMoment UpdateMomentNow() => new(
        _player.IsPlaying || _player.State == SessionState.Opening,
        IsFullScreen,
        _dialog is not null,
        _subjectOpen,
        Notice.IsOpen || ResumeBar.IsOpen,
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
    /// they ask (Help, Check for updates), which always answers with what it found.
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

        if (_updateChecking)
        {
            if (asked)
            {
                Say("Already checking for updates...");
            }

            return;
        }

        var (online, metered) = Connection();
        if (!online)
        {
            _lastCheckFailure = DateTimeOffset.UtcNow;
            _checkFailures++;
            if (asked)
            {
                Say("You are offline. rexplayer looks again once you are connected.");
            }

            return;
        }

        if (asked)
        {
            Say("Checking for updates...");
        }

        _updateChecking = true;
        ShowUpdateStatus();
        string json;
        try
        {
            using var http = UpdateClient(TimeSpan.FromSeconds(20));
            json = await http.GetStringAsync(UpdateCheck.LatestRelease);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _lastCheckFailure = DateTimeOffset.UtcNow;
            _checkFailures++;
            App.Log.Info(LogSource, "Could not look for updates: " + ex.Message);
            if (asked)
            {
                Say("Could not reach the release page: " + ex.Message);
            }

            return;
        }
        finally
        {
            _updateChecking = false;
            ShowUpdateStatus();
        }

        if (_closing)
        {
            return;
        }

        _lastCheckFailure = null;
        _checkFailures = 0;
        var before = Pending;
        var outcome = UpdateFlow.AfterCheck(before, UpdateCheck.Offer(json, Version), Version,
            asked ? null : _settings.SkippedUpdateVersion, _settings.UpdateMode, metered, asked);
        if (outcome.Discard)
        {
            if (_updateDownloadVersion != outcome.Pending?.Version)
            {
                _updateDownload?.Cancel();
            }

            var keep = outcome.Pending;
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
        if (outcome.Pending is null && _shownPrompt != UpdatePrompt.None)
        {
            HideUpdateBar();
        }

        StartUpdateDownload();
        if (asked)
        {
            _updateAsked = outcome.Pending is not null;
            Say(_updateDownload is not null ? Status() : outcome.Message);
            if (_updateDownload is not null)
            {
                ShowUpdateProgress();
            }
            else
            {
                ConsiderUpdatePrompt(asked: true);
            }
        }
        else
        {
            ConsiderUpdatePrompt();
        }
    }

    /// <summary>
    /// Starts the download when one is due and allowed, on a lowest-priority background thread so
    /// it never competes with playback; while something plays it also keeps to a gentle speed.
    /// </summary>
    private void StartUpdateDownload()
    {
        var (online, metered) = Connection();
        if (_updateDownload is not null || _closing || !online || Pending is not { } pending
            || !UpdateFlow.MayDownload(pending, _settings.UpdateMode, _settings.UpdateChecks, metered, DateTimeOffset.UtcNow))
        {
            return;
        }

        var cancel = new CancellationTokenSource();
        _updateDownload = cancel;
        _updateDownloadVersion = pending.Version;
        _updatePercent = 0;
        App.Log.Info(LogSource, $"Downloading rexplayer {pending.Version} in the background.");
        var shown = -1;
        var thread = new Thread(() =>
        {
            DownloadReport report;
            using (var http = UpdateClient(TimeSpan.FromSeconds(30)))
            {
                var downloader = new UpdateDownloader(http, UpdateFolder) { Busy = () => _updatePlaying };
                report = downloader.Download(pending, fraction =>
                {
                    // One update per whole percent: the window's thread is not flooded.
                    var percent = (int)(fraction * 100);
                    if (percent != shown)
                    {
                        shown = percent;
                        OnWindowThread(() => UpdateDownloadProgressed(cancel, percent));
                    }
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
        ShowUpdateStatus();
    }

    private void UpdateDownloadProgressed(CancellationTokenSource download, int percent)
    {
        if (!ReferenceEquals(download, _updateDownload))
        {
            return;
        }

        _updatePercent = percent;
        if (_showingUpdateProgress && UpdateBar.IsOpen)
        {
            UpdateProgress.Value = percent;
            UpdateBar.Message = Status();
        }

        ShowUpdateStatus();
    }

    private void UpdateDownloadFinished(CancellationTokenSource download, string version, DownloadReport report)
    {
        if (ReferenceEquals(download, _updateDownload))
        {
            _updateDownload = null;
            _updateDownloadVersion = null;
            _updatePercent = null;
        }

        download.Dispose();
        if (Pending is not { Stage: UpdateStage.Downloading } pending || pending.Version != version)
        {
            ShowUpdateStatus();
            return;
        }

        switch (report.Outcome)
        {
            case DownloadOutcome.Ready:
                _settings = _settings with { PendingUpdate = UpdateFlow.Downloaded(pending) };
                App.Log.Info(LogSource, $"rexplayer {version} is downloaded and verified.");
                break;
            case DownloadOutcome.Cancelled or DownloadOutcome.Busy:
                // Stopped on purpose, or another window has it: the next look carries on.
                break;
            default:
                _settings = _settings with { PendingUpdate = UpdateFlow.DownloadFailed(pending, report.Reason ?? "unknown error", DateTimeOffset.UtcNow) };
                App.Log.Warning(LogSource, $"The download of rexplayer {version} failed: {report.Reason}.");
                break;
        }

        SaveSettings();
        if (_showingUpdateProgress && report.Outcome is not (DownloadOutcome.Ready or DownloadOutcome.Busy or DownloadOutcome.Cancelled))
        {
            UpdateProgress.Visibility = Visibility.Collapsed;
            UpdateBar.Severity = InfoBarSeverity.Warning;
            UpdateBar.Message = Status();
        }

        ConsiderUpdatePrompt(asked: _updateAsked);
        ShowUpdateStatus();
    }

    /// <summary>
    /// Shows, keeps or hides the update bar as <see cref="UpdateFlow.Prompt"/> says. It hides over
    /// full-screen playback and comes back when playing pauses or the window leaves full screen.
    /// </summary>
    private void ConsiderUpdatePrompt(bool asked = false)
    {
        if (!_updatesStarted || _closing)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var moment = UpdateMomentNow();
        var prompt = UpdateFlow.Prompt(Pending, _settings.UpdateMode, moment, now, asked);
        if (prompt == UpdatePrompt.None)
        {
            // A bar already showing stays while another banner or some editing comes and goes.
            if (_shownPrompt != UpdatePrompt.None && UpdateFlow.Prompt(Pending, _settings.UpdateMode, moment with { NoticeOpen = false, Editing = false }, now) == UpdatePrompt.None)
            {
                HideUpdateBar();
            }

            return;
        }

        _updateAsked = false;
        if (prompt != _shownPrompt || !UpdateBar.IsOpen)
        {
            ShowUpdateBar(prompt);
        }
    }

    private void ShowUpdateBar(UpdatePrompt prompt)
    {
        var pending = Pending!;
        var (title, message) = UpdateFlow.PromptText(prompt, pending, Connection().Metered);
        _shownPrompt = prompt;
        _showingUpdateProgress = false;
        UpdateBar.Title = title;
        UpdateBar.Message = message;
        UpdateBar.Severity = pending.NeedsAttention ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        UpdateProgress.Visibility = Visibility.Collapsed;
        UpdateActions.Visibility = Visibility.Visible;
        var ready = prompt == UpdatePrompt.Ready ? Visibility.Visible : Visibility.Collapsed;
        UpdateRestartButton.Visibility = ready;
        UpdateOnCloseButton.Visibility = ready;
        UpdateTonightButton.Visibility = ready;
        UpdateDownloadButton.Visibility = prompt == UpdatePrompt.Available ? Visibility.Visible : Visibility.Collapsed;
        ShowUpdateNotes(pending);
        UpdateBar.IsOpen = true;
        App.Log.Info(LogSource, $"Offered rexplayer {pending.Version} ({prompt}).");
    }

    /// <summary>The bar while a download the user asked for runs: progress, nothing to answer.</summary>
    private void ShowUpdateProgress()
    {
        if (Pending is not { } pending)
        {
            return;
        }

        _shownPrompt = UpdatePrompt.None;
        _showingUpdateProgress = true;
        UpdateBar.Title = $"Downloading rexplayer {pending.Version}";
        UpdateBar.Message = Status();
        UpdateBar.Severity = InfoBarSeverity.Informational;
        UpdateProgress.Value = _updatePercent ?? 0;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateActions.Visibility = Visibility.Collapsed;
        ShowUpdateNotes(pending);
        UpdateBar.IsOpen = true;
    }

    private void ShowUpdateNotes(PendingUpdate pending)
    {
        UpdateWhatsNew.IsExpanded = pending.NeedsAttention;
        UpdateNotesText.Text = pending.Notes.Length > 0 ? pending.Notes : "The release page lists what changed.";
    }

    private void HideUpdateBar()
    {
        FocusAwayFromUpdateBar();
        _shownPrompt = UpdatePrompt.None;
        _showingUpdateProgress = false;
        UpdateBar.IsOpen = false;
    }

    /// <summary>A bar that closes with the keyboard inside it hands the focus to the picture, not to nowhere.</summary>
    private void FocusAwayFromUpdateBar()
    {
        if (Content?.XamlRoot is { } root && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root) is DependencyObject focused && IsInside(focused, UpdateBar))
        {
            Stage.Focus(FocusState.Programmatic);
        }
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, container))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Escape with the keyboard in the update bar is "Remind me later"; true when it was.</summary>
    private bool EscapeFromUpdateBar()
    {
        if (!UpdateBar.IsOpen || Content?.XamlRoot is not { } root
            || Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root) is not DependencyObject focused || !IsInside(focused, UpdateBar))
        {
            return false;
        }

        DismissUpdateBar();
        return true;
    }

    /// <summary>Closing the bar answers "Remind me tomorrow"; a progress bar just goes.</summary>
    private void DismissUpdateBar()
    {
        if (_shownPrompt != UpdatePrompt.None)
        {
            RemindAboutUpdate(RemindChoice.Tomorrow);
        }
        else
        {
            HideUpdateBar();
        }
    }

    private void OnUpdateBarCloseClick(InfoBar sender, object args)
    {
        if (_shownPrompt != UpdatePrompt.None && Pending is { } pending)
        {
            _settings = _settings with { PendingUpdate = UpdateFlow.Remind(pending, RemindChoice.Tomorrow, DateTimeOffset.UtcNow) };
            SaveSettings();
            Say($"rexplayer reminds you about {pending.Version} tomorrow.");
        }

        _shownPrompt = UpdatePrompt.None;
        _showingUpdateProgress = false;
    }

    private void RemindAboutUpdate(RemindChoice choice)
    {
        HideUpdateBar();
        if (Pending is not { } pending)
        {
            return;
        }

        _settings = _settings with { PendingUpdate = UpdateFlow.Remind(pending, choice, DateTimeOffset.UtcNow) };
        SaveSettings();
        Say(choice switch
        {
            RemindChoice.InAnHour => $"rexplayer reminds you about {pending.Version} in an hour.",
            RemindChoice.NextWeek => $"rexplayer reminds you about {pending.Version} next week.",
            _ => $"rexplayer reminds you about {pending.Version} tomorrow.",
        });
    }

    private void OnUpdateRemindClick(SplitButton sender, SplitButtonClickEventArgs args) => RemindAboutUpdate(RemindChoice.Tomorrow);

    private void OnUpdateRemindChoiceClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string tag } && Enum.TryParse<RemindChoice>(tag, out var choice))
        {
            RemindAboutUpdate(choice);
        }
    }

    private void OnUpdateRestartClick(object sender, RoutedEventArgs e)
    {
        HideUpdateBar();
        _ = InstallAsync(unattended: false);
    }

    private void OnUpdateDownloadClick(object sender, RoutedEventArgs e)
    {
        if (Pending is not { } pending)
        {
            return;
        }

        _settings = _settings with { PendingUpdate = pending with { Stage = UpdateStage.Downloading, Approved = true, RemindAt = null, RetryAt = null } };
        SaveSettings();
        _updateAsked = true;
        StartUpdateDownload();
        if (_updateDownload is null)
        {
            HideUpdateBar();
            Say(Connection().Online ? Status() : "You are offline. The download starts once you are connected.");
            return;
        }

        ShowUpdateProgress();
    }

    private void OnUpdateOnCloseClick(object sender, RoutedEventArgs e)
    {
        HideUpdateBar();
        if (Pending is { } pending)
        {
            _settings = _settings with { PendingUpdate = UpdateFlow.WhenClosed(pending) };
            SaveSettings();
            Say(Status());
        }
    }

    private void OnUpdateTonightClick(object sender, RoutedEventArgs e)
    {
        HideUpdateBar();
        if (Pending is { } pending)
        {
            _settings = _settings with { PendingUpdate = UpdateFlow.ScheduledTonight(pending, DateTimeOffset.Now) };
            SaveSettings();
            Say(Status());
        }
    }

    private void OnUpdateSkipClick(object sender, RoutedEventArgs e)
    {
        HideUpdateBar();
        if (Pending is not { } pending)
        {
            return;
        }

        _updateDownload?.Cancel();
        _settings = _settings with { PendingUpdate = null, SkippedUpdateVersion = pending.Version };
        SaveSettings();
        _ = Task.Run(() => UpdateDownloader.Clean(UpdateFolder, null));
        Say($"rexplayer {pending.Version} will not be offered again; a newer version will be.");
        ShowUpdateStatus();
    }

    private void OnUpdateReleasePageClick(object sender, RoutedEventArgs e)
    {
        if (Pending is { } pending && Uri.TryCreate(pending.ReleasePage, UriKind.Absolute, out var page))
        {
            _ = Windows.System.Launcher.LaunchUriAsync(page);
        }
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
                _settings = _settings with { PendingUpdate = pending with { Stage = UpdateStage.Downloading, Approved = true } };
                SaveSettings();
                Say("The downloaded update was damaged, so rexplayer is downloading it again.");
                StartUpdateDownload();
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
            _settings = _settings with { PendingUpdate = pending with { Stage = UpdateStage.Installing, HandedOffAt = DateTimeOffset.UtcNow } };
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
            _settings = _settings with { PendingUpdate = pending with { Stage = UpdateStage.Downloading } };
            return;
        }

        if (HandOff(pending, relaunch: false))
        {
            App.Log.Info(LogSource, $"Handed rexplayer {pending.Version} to the updater to install as rexplayer closes.");
        }
    }

    /// <summary>The updater's state in words, for Preferences and Check for updates.</summary>
    private string Status()
    {
        var phase = UpdateFlow.Phase(Pending, _updateChecking, _updateDownload is not null);
        return UpdateFlow.Status(phase, Pending, _settings.UpdateMode, Version, _updatePercent, _settings.LastUpdateCheck, DateTimeOffset.Now);
    }

    private void ShowUpdateStatus()
    {
        if (_updateStatusText is { } text)
        {
            text.Text = Status();
        }
    }

    private static HttpClient UpdateClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"rexplayer/{Version}");
        return http;
    }
}
