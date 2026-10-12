using System.Globalization;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Updates;

/// <summary>Where the updater is, as the user sees it.</summary>
public enum UpdatePhase
{
    Idle,
    Checking,
    Available,
    Downloading,
    Ready,
    Scheduled,
    Installing,
    Failed,
}

/// <summary>What the update bar offers, if anything.</summary>
public enum UpdatePrompt
{
    None,

    /// <summary>A newer version that has not been downloaded: Download, remind or skip.</summary>
    Available,

    /// <summary>A downloaded, verified version: when to install it.</summary>
    Ready,
}

/// <summary>How long "Remind me later" waits.</summary>
public enum RemindChoice
{
    InAnHour,
    Tomorrow,
    NextWeek,
}

/// <summary>What the start of a run learned about the update the last run handed over.</summary>
public enum StartupNews
{
    None,

    /// <summary>The handed-over version is the one now running.</summary>
    Installed,

    /// <summary>The installer was cancelled or failed; the update is still on offer.</summary>
    InstallFailed,

    /// <summary>The installer failed too often; the download is given up until a later check.</summary>
    InstallAbandoned,
}

/// <summary>The window at this moment, as far as the updater cares.</summary>
/// <param name="Playing">Something is playing or opening.</param>
/// <param name="FullScreen">The window fills the screen.</param>
/// <param name="DialogOpen">A dialog is open.</param>
/// <param name="Editing">The user is part-way through something, such as Subject Lock.</param>
/// <param name="NoticeOpen">Another banner (crash recovery, a message) is showing.</param>
/// <param name="IdleFor">How long since the user last touched the window.</param>
public sealed record UpdateMoment(bool Playing, bool FullScreen, bool DialogOpen, bool Editing, bool NoticeOpen, TimeSpan IdleFor);

/// <summary>The pending update after a check, and whether the files of the one before go.</summary>
public sealed record CheckOutcome(PendingUpdate? Pending, bool Discard, string Message);

/// <summary>The pending update after a start, what to tell the user, and whether to put the last session back.</summary>
/// <param name="Pending">What is still pending.</param>
/// <param name="News">What happened to the version the last run handed over.</param>
/// <param name="Discard">The downloaded files are no longer wanted.</param>
/// <param name="RestoreSession">
/// The installer gave up moments ago and reopened this version: the session saved for the update
/// is put back even though the version did not change.
/// </param>
public sealed record StartupOutcome(PendingUpdate? Pending, StartupNews News, bool Discard, bool RestoreSession);

/// <summary>
/// Every decision of the update flow (TOOL-09): what a check changes, when to download, when the
/// update bar may show, when installing may happen without asking, what a restart learns, and the
/// words for each state. The window keeps the clocks and does the network and process work.
/// </summary>
public static class UpdateFlow
{
    /// <summary>How long after starting the first check waits, so it never slows a launch or the first playback.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(45);

    /// <summary>How long a hand-off to the installer counts as happening now.</summary>
    public static readonly TimeSpan HandOffGrace = TimeSpan.FromMinutes(10);

    /// <summary>How long nobody must touch rexplayer before it installs while open.</summary>
    public static readonly TimeSpan IdleBeforeInstall = TimeSpan.FromMinutes(10);

    /// <summary>The longest reminder; one further away means the clock was put back.</summary>
    public static readonly TimeSpan LongestReminder = TimeSpan.FromDays(7);

    /// <summary>Install attempts after which the download is given up until a later check.</summary>
    public const int MostInstallFailures = 3;

    private static readonly TimeSpan[] DownloadWaits =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1), TimeSpan.FromHours(4), TimeSpan.FromHours(12),
    ];

    private static readonly TimeSpan[] CheckWaits =
    [
        TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(4),
    ];

    /// <summary>Whether updates apply to this build: a build whose version is not "major.minor.patch" is a developer's own.</summary>
    public static bool Applies(string current) => UpdateCheck.TryVersion(current, out _);

    /// <summary>The pending update for a newly found <paramref name="offer"/>, downloading when the mode and connection allow.</summary>
    public static PendingUpdate Found(UpdateOffer offer, UpdateMode mode, bool metered, bool asked)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return new PendingUpdate
        {
            Version = offer.Version.ToString(),
            InstallerName = offer.InstallerName,
            InstallerUrl = offer.Installer.ToString(),
            ChecksumUrl = offer.Checksum.ToString(),
            ReleasePage = offer.ReleasePage.ToString(),
            Size = offer.Size,
            Notes = offer.Notes,
            NeedsAttention = offer.NeedsAttention,
            Approved = asked,
            Stage = asked || (mode != UpdateMode.NotifyOnly && !metered) ? UpdateStage.Downloading : UpdateStage.Available,
        };
    }

    /// <summary>
    /// What a successful check changes. The latest release is the truth: a pending version it no
    /// longer offers (withdrawn, superseded, or already running) goes, and a newer one replaces it.
    /// A version the user skipped is not offered again unless they ask.
    /// </summary>
    public static CheckOutcome AfterCheck(PendingUpdate? pending, UpdateOffer? offer, string current, string? skipped, UpdateMode mode, bool metered, bool asked)
    {
        if (pending is { Stage: UpdateStage.Installing })
        {
            return new CheckOutcome(pending, false, $"Installing rexplayer {pending.Version}...");
        }

        if (offer is null)
        {
            return new CheckOutcome(null, pending is not null, $"rexplayer {current} is the newest version.");
        }

        var version = offer.Version.ToString();
        if (!asked && version == skipped)
        {
            return new CheckOutcome(null, pending is not null, $"rexplayer {version} is available; you chose to skip it.");
        }

        if (pending?.Version != version)
        {
            var found = Found(offer, mode, metered, asked);
            return new CheckOutcome(found, pending is not null, Announce(found));
        }

        var promote = pending.Stage == UpdateStage.Available && (asked || (mode != UpdateMode.NotifyOnly && !metered));
        var kept = pending with
        {
            InstallerUrl = offer.Installer.ToString(),
            ChecksumUrl = offer.Checksum.ToString(),
            ReleasePage = offer.ReleasePage.ToString(),
            Size = offer.Size,
            Notes = offer.Notes,
            NeedsAttention = offer.NeedsAttention,
            Approved = pending.Approved || asked,
            Stage = promote ? UpdateStage.Downloading : pending.Stage,
            RemindAt = asked ? null : pending.RemindAt,
            RetryAt = asked ? null : pending.RetryAt,
        };
        return new CheckOutcome(kept, false, Announce(kept));
    }

    private static string Announce(PendingUpdate pending) => pending.Stage switch
    {
        UpdateStage.Available => $"rexplayer {pending.Version} is available.",
        UpdateStage.Ready => $"rexplayer {pending.Version} is ready to install.",
        _ => $"Downloading rexplayer {pending.Version}...",
    };

    /// <summary>
    /// What a start learns. A handed-over version now running installed; one still newer than this
    /// version was cancelled or failed, and is offered again (asking, even in automatic mode) until
    /// it has failed <see cref="MostInstallFailures"/> times. A pending version this build already
    /// has or passes (installed some other way) goes.
    /// </summary>
    public static StartupOutcome AtStartup(PendingUpdate? pending, string current, DateTimeOffset now)
    {
        if (pending is null || !UpdateCheck.TryVersion(current, out var running) || !UpdateCheck.TryVersion(pending.Version, out var target))
        {
            return new StartupOutcome(pending, StartupNews.None, false, false);
        }

        if (target <= running)
        {
            return new StartupOutcome(null, pending.Stage == UpdateStage.Installing ? StartupNews.Installed : StartupNews.None, true, false);
        }

        if (pending.Stage != UpdateStage.Installing)
        {
            return new StartupOutcome(pending, StartupNews.None, false, false);
        }

        var recent = pending.HandedOffAt is { } handed && handed <= now.AddMinutes(5) && now - handed <= HandOffGrace;
        var failures = pending.InstallFailures + 1;
        if (failures >= MostInstallFailures)
        {
            return new StartupOutcome(null, StartupNews.InstallAbandoned, true, recent);
        }

        var again = pending with
        {
            Stage = UpdateStage.Ready,
            Timing = UpdateTiming.Ask,
            InstallFailures = failures,
            HandedOffAt = null,
            InstallAt = null,
            RemindAt = null,
        };
        return new StartupOutcome(again, StartupNews.InstallFailed, false, recent);
    }

    /// <summary>
    /// Whether automatic mode installs <paramref name="pending"/> without asking: never a release
    /// that asks for attention, and never again after an install failed.
    /// </summary>
    public static bool InstallsUnasked(PendingUpdate pending, UpdateMode mode)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return mode == UpdateMode.Automatic && pending.Timing == UpdateTiming.Ask && !pending.NeedsAttention && pending.InstallFailures == 0;
    }

    /// <summary>
    /// What the update bar may offer now. Never over full-screen playback, a dialog, unfinished
    /// editing or another banner, and not before a reminder is due (a reminder further away than
    /// the longest one means the clock went back, so it is due). A version that will install by
    /// itself, on close or on schedule, is not offered. <paramref name="asked"/> (Check for updates)
    /// shows it regardless, except over a dialog.
    /// </summary>
    public static UpdatePrompt Prompt(PendingUpdate? pending, UpdateMode mode, UpdateMoment moment, DateTimeOffset now, bool asked = false)
    {
        ArgumentNullException.ThrowIfNull(moment);
        if (pending is null || moment.DialogOpen)
        {
            return UpdatePrompt.None;
        }

        if (!asked && ((moment.FullScreen && moment.Playing) || moment.Editing || moment.NoticeOpen || Waiting(pending.RemindAt, now, LongestReminder)))
        {
            return UpdatePrompt.None;
        }

        return pending.Stage switch
        {
            UpdateStage.Available => UpdatePrompt.Available,
            UpdateStage.Ready when asked || (pending.Timing == UpdateTiming.Ask && !InstallsUnasked(pending, mode)) => UpdatePrompt.Ready,
            _ => UpdatePrompt.None,
        };
    }

    /// <summary>
    /// Whether to install while rexplayer is open: a scheduled time has come, or automatic mode
    /// allows it, and rexplayer is idle (nothing playing, not full screen, no dialog or editing,
    /// untouched for <see cref="IdleBeforeInstall"/>). It then reopens where it was, paused.
    /// </summary>
    public static bool InstallWhileOpen(PendingUpdate? pending, UpdateMode mode, UpdateMoment moment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(moment);
        if (pending is not { Stage: UpdateStage.Ready }
            || moment.Playing || moment.FullScreen || moment.DialogOpen || moment.Editing || moment.IdleFor < IdleBeforeInstall)
        {
            return false;
        }

        return pending.Timing == UpdateTiming.Scheduled
            ? !Waiting(pending.InstallAt, now, TimeSpan.FromDays(2))
            : InstallsUnasked(pending, mode);
    }

    /// <summary>
    /// Whether closing rexplayer installs: chosen ("when I close", a schedule) or automatic. Not
    /// when another rexplayer window handed the same update over moments ago (<paramref name="onDisk"/>
    /// is what the settings file says now).
    /// </summary>
    public static bool InstallOnClose(PendingUpdate? pending, UpdateMode mode, PendingUpdate? onDisk, DateTimeOffset now)
    {
        if (pending is not { Stage: UpdateStage.Ready }
            || (onDisk is { Stage: UpdateStage.Installing, HandedOffAt: { } handed } && now - handed <= HandOffGrace && handed <= now.AddMinutes(5)))
        {
            return false;
        }

        return pending.Timing is UpdateTiming.OnClose or UpdateTiming.Scheduled || InstallsUnasked(pending, mode);
    }

    /// <summary>Not offered again until <paramref name="choice"/>'s time.</summary>
    public static PendingUpdate Remind(PendingUpdate pending, RemindChoice choice, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return pending with
        {
            Timing = UpdateTiming.Ask,
            InstallAt = null,
            RemindAt = now + choice switch
            {
                RemindChoice.InAnHour => TimeSpan.FromHours(1),
                RemindChoice.NextWeek => TimeSpan.FromDays(7),
                _ => TimeSpan.FromDays(1),
            },
        };
    }

    /// <summary>Installs as rexplayer closes.</summary>
    public static PendingUpdate WhenClosed(PendingUpdate pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return pending with { Timing = UpdateTiming.OnClose, InstallAt = null, RemindAt = null };
    }

    /// <summary>Installs tonight (<see cref="Tonight"/>) once rexplayer is idle, or as it closes if that is sooner.</summary>
    public static PendingUpdate ScheduledTonight(PendingUpdate pending, DateTimeOffset localNow)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return pending with { Timing = UpdateTiming.Scheduled, InstallAt = Tonight(localNow), RemindAt = null };
    }

    /// <summary>The next 3 o'clock in the morning, local time, at least half an hour away.</summary>
    public static DateTimeOffset Tonight(DateTimeOffset localNow)
    {
        var at = new DateTimeOffset(localNow.Date.AddHours(3), localNow.Offset);
        return at <= localNow.AddMinutes(30) ? at.AddDays(1) : at;
    }

    /// <summary>Whether a download may run now: approved, or allowed by the mode, the cadence and the connection, and not waiting to retry.</summary>
    public static bool MayDownload(PendingUpdate? pending, UpdateMode mode, UpdateCadence cadence, bool metered, DateTimeOffset now) =>
        pending is { Stage: UpdateStage.Downloading }
        && (pending.Approved || (mode != UpdateMode.NotifyOnly && cadence != UpdateCadence.Off && !metered))
        && !Waiting(pending.RetryAt, now, DownloadWaits[^1]);

    /// <summary>A failed download: the next attempt waits longer each time, up to 12 hours.</summary>
    public static PendingUpdate DownloadFailed(PendingUpdate pending, string reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pending);
        var failures = pending.DownloadFailures + 1;
        return pending with
        {
            DownloadFailures = failures,
            RetryAt = now + DownloadWaits[Math.Min(failures, DownloadWaits.Length) - 1],
            LastError = reason,
        };
    }

    /// <summary>A finished, verified download.</summary>
    public static PendingUpdate Downloaded(PendingUpdate pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return pending with { Stage = UpdateStage.Ready, DownloadFailures = 0, RetryAt = null, LastError = null };
    }

    /// <summary>
    /// Whether an automatic check should run: the cadence says one is due, and after a failed
    /// attempt (offline, say) enough time has passed: 5 minutes, then 15, then an hour, then 4.
    /// </summary>
    public static bool CheckDue(UpdateCadence cadence, DateTimeOffset? lastCheck, DateTimeOffset? lastFailure, int failures, DateTimeOffset now) =>
        UpdateCheck.IsDue(cadence, lastCheck, now)
        && (lastFailure is not { } failed || failures <= 0 || failed > now
            || now - failed >= CheckWaits[Math.Min(failures, CheckWaits.Length) - 1]);

    /// <summary>The phase the user sees.</summary>
    public static UpdatePhase Phase(PendingUpdate? pending, bool checking, bool downloading) =>
        downloading ? UpdatePhase.Downloading
        : checking ? UpdatePhase.Checking
        : pending?.Stage switch
        {
            UpdateStage.Available => UpdatePhase.Available,
            UpdateStage.Downloading => pending.DownloadFailures > 0 ? UpdatePhase.Failed : UpdatePhase.Downloading,
            UpdateStage.Ready => pending.Timing == UpdateTiming.Scheduled ? UpdatePhase.Scheduled : UpdatePhase.Ready,
            UpdateStage.Installing => UpdatePhase.Installing,
            _ => UpdatePhase.Idle,
        };

    /// <summary>One line for Preferences and for "Check for updates".</summary>
    /// <param name="phase">From <see cref="Phase"/>.</param>
    /// <param name="pending">The pending update, if any.</param>
    /// <param name="mode">The update mode.</param>
    /// <param name="current">The running version.</param>
    /// <param name="percent">How much of an active download is done.</param>
    /// <param name="lastCheck">The last successful check.</param>
    /// <param name="now">Now, with the local offset, for times and days.</param>
    public static string Status(UpdatePhase phase, PendingUpdate? pending, UpdateMode mode, string current, double? percent, DateTimeOffset? lastCheck, DateTimeOffset now)
    {
        var version = pending?.Version ?? current;
        return phase switch
        {
            UpdatePhase.Checking => "Checking for updates...",
            UpdatePhase.Available => $"rexplayer {version} is available.",
            UpdatePhase.Downloading => percent is { } done
                ? string.Create(CultureInfo.InvariantCulture, $"Downloading rexplayer {version}: {Math.Clamp(done, 0, 100):0}%")
                : $"rexplayer {version} will download in the background.",
            UpdatePhase.Failed => $"rexplayer {version} could not be downloaded: {pending?.LastError ?? "unknown error"}. "
                + (pending?.RetryAt is { } retry ? $"Trying again {When(retry, now)} at {Clock(retry, now)}." : "Trying again later."),
            UpdatePhase.Ready => pending?.Timing == UpdateTiming.OnClose ? $"rexplayer {version} will install when you close rexplayer."
                : pending is not null && InstallsUnasked(pending, mode) ? $"rexplayer {version} will install when rexplayer is closed or not in use."
                : $"rexplayer {version} is ready to install.",
            UpdatePhase.Scheduled => $"rexplayer {version} will install "
                + (pending?.InstallAt is { } at ? $"{When(at, now)} at {Clock(at, now)}" : "tonight")
                + " when rexplayer is not in use, or when you close it.",
            UpdatePhase.Installing => $"Installing rexplayer {version}...",
            _ => lastCheck is { } last
                ? $"rexplayer {current} is up to date. Last checked {When(last, now)}."
                : $"This is rexplayer {current}.",
        };
    }

    /// <summary>"Updated to 0.16.0 yesterday.", or null when no update is known.</summary>
    public static string? LastUpdated(string? version, DateTimeOffset? at, DateTimeOffset now) =>
        version is { Length: > 0 } && at is { } when ? $"Updated to {version} {When(when, now)}." : null;

    /// <summary>The words for a prompt: its title and message.</summary>
    public static (string Title, string Message) PromptText(UpdatePrompt prompt, PendingUpdate pending, bool metered)
    {
        ArgumentNullException.ThrowIfNull(pending);
        var attention = pending.NeedsAttention ? " This release asks you to read what's new before installing." : "";
        return prompt == UpdatePrompt.Available
            ? ($"rexplayer {pending.Version} is available",
                (metered ? "You are on a metered connection, so it has not been downloaded." : "Download it in the background and choose when to install it.") + attention)
            : ($"rexplayer {pending.Version} is ready to install",
                "Restart now and rexplayer reopens with your queue, place and view, paused." + attention);
    }

    /// <summary>"today", "yesterday", "tomorrow", or "on 12 Oct 2026", in the offset of <paramref name="now"/>.</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var days = (at.ToOffset(now.Offset).Date - now.Date).Days;
        return days switch
        {
            0 => "today",
            -1 => "yesterday",
            1 => "tomorrow",
            _ => "on " + at.ToOffset(now.Offset).ToString("d MMM yyyy", CultureInfo.InvariantCulture),
        };
    }

    private static string Clock(DateTimeOffset at, DateTimeOffset now) =>
        at.ToOffset(now.Offset).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Whether <paramref name="until"/> is still ahead, believing no wait longer than <paramref name="longest"/>.</summary>
    private static bool Waiting(DateTimeOffset? until, DateTimeOffset now, TimeSpan longest) =>
        until is { } at && at > now && at - now <= longest + TimeSpan.FromHours(1);
}
