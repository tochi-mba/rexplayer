using System.Globalization;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Updates;

/// <summary>What the update card offers, if anything.</summary>
public enum UpdatePrompt
{
    None,

    /// <summary>A newer version that has not been downloaded: download it, later, or skip it.</summary>
    Available,

    /// <summary>A downloaded, verified version: restart now, or later.</summary>
    Ready,
}

/// <summary>How long "Later" waits before reminding.</summary>
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

    /// <summary>The installer failed; the update is still on offer.</summary>
    InstallFailed,

    /// <summary>The user cancelled the installer; the update is still on offer.</summary>
    InstallCancelled,

    /// <summary>The installer failed too often; the download is given up until a later check.</summary>
    InstallAbandoned,
}

/// <summary>How the installer ended, as the hand-off helper recorded it.</summary>
public enum InstallEnd
{
    /// <summary>Nothing was recorded (the helper or the PC stopped first).</summary>
    Unknown,

    /// <summary>The user cancelled it.</summary>
    Cancelled,

    /// <summary>It could not close a running rexplayer.</summary>
    AppOpen,

    Failed,
}

/// <summary>Why a download that is due is not running.</summary>
public enum DownloadHold
{
    None,

    /// <summary>The user paused it.</summary>
    Paused,

    /// <summary>The last attempt failed; the next waits a while.</summary>
    Failed,

    Offline,
}

/// <summary>The window at this moment, as far as the updater cares.</summary>
/// <param name="Playing">Something is playing or opening.</param>
/// <param name="Video">What is open has pictures.</param>
/// <param name="FullScreen">The window fills the screen.</param>
/// <param name="DialogOpen">A dialog is open.</param>
/// <param name="Editing">The user is part-way through something, such as Subject Lock.</param>
/// <param name="NoticeOpen">Another banner (crash recovery, a message) is showing.</param>
/// <param name="PanelOpen">The update panel is open, so it says everything the card would.</param>
/// <param name="IdleFor">How long since the user last touched the window.</param>
public sealed record UpdateMoment(bool Playing, bool Video, bool FullScreen, bool DialogOpen, bool Editing, bool NoticeOpen, bool PanelOpen, TimeSpan IdleFor);

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

/// <summary>What the updater's clock knows at a tick.</summary>
/// <param name="Pending">The pending update.</param>
/// <param name="Mode">What the user chose to happen with a new version.</param>
/// <param name="Cadence">How often to look.</param>
/// <param name="LastCheck">The last successful check.</param>
/// <param name="LastCheckFailure">The last failed check since then.</param>
/// <param name="CheckFailures">Failed checks in a row.</param>
/// <param name="Checking">A check is running.</param>
/// <param name="Downloading">A download is running.</param>
/// <param name="Online">Windows has an internet connection.</param>
/// <param name="Metered">The connection costs by the byte.</param>
public sealed record UpdateClock(
    PendingUpdate? Pending, UpdateMode Mode, UpdateCadence Cadence, DateTimeOffset? LastCheck, DateTimeOffset? LastCheckFailure,
    int CheckFailures, bool Checking, bool Downloading, bool Online, bool Metered);

/// <summary>What a tick does: the pending update as the settings now allow it, and the work to start or stop.</summary>
public sealed record UpdatePlan(PendingUpdate? Pending, bool Check, bool StopDownload, bool StartDownload, bool Install);

/// <summary>
/// Every decision of the update flow (TOOL-09): what a check changes, when to download, when the
/// update card may show, when installing may happen without asking, and what a restart learns.
/// The window keeps the clocks and does the network and process work; <see cref="UpdateView"/>
/// words it.
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

    /// <summary>The hand-off helper's record of how the installer ended, beside the installer.</summary>
    public const string InstallRecordName = "install-result.txt";

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
    /// A version the user skipped is not offered again unless they ask, and a download they
    /// cancelled does not start again by itself.
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

        var promote = pending is { Stage: UpdateStage.Available, Declined: false } && (asked || (mode != UpdateMode.NotifyOnly && !metered));
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
    /// version was cancelled or failed. A cancelled one is offered again, asking first; a failed one
    /// too, until it has failed <see cref="MostInstallFailures"/> times. A pending version this
    /// build already has or passes (installed some other way) goes.
    /// </summary>
    /// <param name="pending">What the last run left pending.</param>
    /// <param name="current">The running version.</param>
    /// <param name="now">Now.</param>
    /// <param name="end">How the installer ended, from <see cref="InstallEndOf"/>.</param>
    public static StartupOutcome AtStartup(PendingUpdate? pending, string current, DateTimeOffset now, InstallEnd end = InstallEnd.Unknown)
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
        var again = pending with
        {
            Stage = UpdateStage.Ready,
            Timing = UpdateTiming.Ask,
            HandedOffAt = null,
            InstallAt = null,
            RemindAt = null,
        };
        if (end == InstallEnd.Cancelled)
        {
            return new StartupOutcome(again with { InstallCancelled = true }, StartupNews.InstallCancelled, false, recent);
        }

        var failures = pending.InstallFailures + 1;
        return failures >= MostInstallFailures
            ? new StartupOutcome(null, StartupNews.InstallAbandoned, true, recent)
            : new StartupOutcome(again with { InstallFailures = failures }, StartupNews.InstallFailed, false, recent);
    }

    /// <summary>
    /// How the installer for <paramref name="pending"/> ended, from the hand-off helper's record
    /// ("installer-name exit-code"). A record for another installer, or none, says nothing.
    /// </summary>
    public static InstallEnd InstallEndOf(string? record, PendingUpdate? pending)
    {
        var parts = record?.Trim().Split(' ', 2) ?? [];
        if (pending is null || parts.Length != 2 || parts[0] != pending.InstallerName
            || !int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var code))
        {
            return InstallEnd.Unknown;
        }

        // The installer's own exit codes: 2 and 5 are a cancel before or during installing, 7 and 8
        // mean it could not prepare (rexplayer would not close).
        return code switch
        {
            2 or 5 => InstallEnd.Cancelled,
            7 or 8 => InstallEnd.AppOpen,
            _ => InstallEnd.Failed,
        };
    }

    /// <summary>
    /// Whether automatic mode installs <paramref name="pending"/> without asking: never a release
    /// that asks for attention, and never again after an install failed or was cancelled.
    /// </summary>
    public static bool InstallsUnasked(PendingUpdate pending, UpdateMode mode)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return mode == UpdateMode.Automatic && pending.Timing == UpdateTiming.Ask && !pending.NeedsAttention
            && pending.InstallFailures == 0 && !pending.InstallCancelled;
    }

    /// <summary>
    /// What the update card may offer now. Never over a dialog or the open update panel, never while
    /// a video plays or anything plays full screen, and not before a reminder is due (a reminder
    /// further away than the longest one means the clock went back, so it is due). Another banner or
    /// unfinished editing keeps a card from appearing but not one already <paramref name="showing"/>.
    /// A version that will install by itself, on close or on schedule, is not offered, nor is one
    /// whose download the user cancelled.
    /// </summary>
    public static UpdatePrompt Prompt(PendingUpdate? pending, UpdateMode mode, UpdateMoment moment, DateTimeOffset now, bool showing = false)
    {
        ArgumentNullException.ThrowIfNull(moment);
        if (pending is null || moment.DialogOpen || moment.PanelOpen || (moment.Playing && (moment.Video || moment.FullScreen))
            || (!showing && (moment.Editing || moment.NoticeOpen)) || Waiting(pending.RemindAt, now, LongestReminder))
        {
            return UpdatePrompt.None;
        }

        return pending switch
        {
            { Stage: UpdateStage.Available, Declined: false } => UpdatePrompt.Available,
            { Stage: UpdateStage.Ready, Timing: UpdateTiming.Ask } when !InstallsUnasked(pending, mode) => UpdatePrompt.Ready,
            _ => UpdatePrompt.None,
        };
    }

    /// <summary>
    /// Whether to install while rexplayer is open: a scheduled time has come, or automatic mode
    /// allows it, and rexplayer is idle (nothing playing, not full screen, no dialog, editing or
    /// update panel, untouched for <see cref="IdleBeforeInstall"/>). It then reopens where it was, paused.
    /// </summary>
    public static bool InstallWhileOpen(PendingUpdate? pending, UpdateMode mode, UpdateMoment moment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(moment);
        if (pending is not { Stage: UpdateStage.Ready }
            || moment.Playing || moment.FullScreen || moment.DialogOpen || moment.Editing || moment.PanelOpen || moment.IdleFor < IdleBeforeInstall)
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

    /// <summary>
    /// The pending update after a download attempt: verified, it is ready; stopped on purpose or
    /// left to another window, it is as it was; otherwise the attempt failed and waits to try again.
    /// </summary>
    public static PendingUpdate AfterDownload(PendingUpdate pending, DownloadReport report, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(report);
        return report.Outcome switch
        {
            DownloadOutcome.Ready => Downloaded(pending),
            DownloadOutcome.Cancelled or DownloadOutcome.Busy => pending,
            _ => DownloadFailed(pending, report.Reason ?? "unknown error", now),
        };
    }

    /// <summary>The user chose Download: it goes ahead now, whatever the connection, and any reminder, pause or wait is dropped.</summary>
    public static PendingUpdate Approve(PendingUpdate pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return pending with { Stage = UpdateStage.Downloading, Approved = true, Paused = false, Declined = false, RemindAt = null, RetryAt = null };
    }

    /// <summary>The user paused the download: it waits until they resume it, even across restarts.</summary>
    public static PendingUpdate Pause(PendingUpdate pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return pending with { Paused = true };
    }

    /// <summary>The user resumed the download, or asked to try a failed one again: it goes now, whatever the connection.</summary>
    public static PendingUpdate Resume(PendingUpdate pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return pending with { Paused = false, Approved = true, RetryAt = null };
    }

    /// <summary>
    /// The user cancelled the download: the version is still on offer, its partial file goes, and
    /// rexplayer does not download it again by itself.
    /// </summary>
    public static PendingUpdate CancelDownload(PendingUpdate pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return pending with
        {
            Stage = UpdateStage.Available,
            Declined = true,
            Approved = false,
            Paused = false,
            RetryAt = null,
            DownloadFailures = 0,
            LastError = null,
        };
    }

    /// <summary>The update whose files stay in the updates folder: none once the user cancelled its download.</summary>
    public static PendingUpdate? KeptFiles(PendingUpdate? pending) => pending is { Declined: true } ? null : pending;

    /// <summary>
    /// A downloaded installer that no longer matches its checksum is downloaded again. It is
    /// approved (so it goes ahead on a metered connection) only when it was, or the user
    /// <paramref name="asked"/> to install it; an install rexplayer started by itself does not.
    /// </summary>
    public static PendingUpdate Damaged(PendingUpdate pending, bool asked)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return pending with { Stage = UpdateStage.Downloading, Approved = pending.Approved || asked };
    }

    /// <summary>A new connection: a download waiting to try again after failing on the old one tries now.</summary>
    public static PendingUpdate Reconnected(PendingUpdate pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return pending is { Stage: UpdateStage.Downloading, RetryAt: not null } ? pending with { RetryAt = null } : pending;
    }

    /// <summary>Handed to the installer at <paramref name="now"/>; the next start says whether it installed.</summary>
    public static PendingUpdate HandedOff(PendingUpdate pending, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return pending with { Stage = UpdateStage.Installing, HandedOffAt = now };
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
    public static PendingUpdate ScheduledTonight(PendingUpdate pending, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return pending with { Timing = UpdateTiming.Scheduled, InstallAt = Tonight(now, zone), RemindAt = null };
    }

    /// <summary>
    /// The next 3 o'clock in the morning in <paramref name="zone"/>, at least half an hour away,
    /// with that night's own offset: a night when the clocks change installs at 3 by the clock,
    /// not an hour early or late. A 3 o'clock the change skips becomes the hour after.
    /// </summary>
    public static DateTimeOffset Tonight(DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        var at = AtThree(today, zone);
        return at > now.AddMinutes(30) ? at : AtThree(today.AddDays(1), zone);
    }

    private static DateTimeOffset AtThree(DateTime day, TimeZoneInfo zone)
    {
        var wall = DateTime.SpecifyKind(day.AddHours(3), DateTimeKind.Unspecified);
        wall = zone.IsInvalidTime(wall) ? wall.AddHours(1) : wall;
        return new DateTimeOffset(wall, zone.GetUtcOffset(wall));
    }

    /// <summary>
    /// The pending update as the settings and connection now have it: one on offer starts
    /// downloading once the mode, the cadence and an unmetered connection allow it (unless the
    /// user cancelled its download), and a download nobody asked for goes back to being on offer
    /// when the user chose only to be told, turned checks off, or the connection became metered.
    /// </summary>
    public static PendingUpdate Settle(PendingUpdate pending, UpdateMode mode, UpdateCadence cadence, bool metered)
    {
        ArgumentNullException.ThrowIfNull(pending);
        var allowed = mode != UpdateMode.NotifyOnly && cadence != UpdateCadence.Off && !metered;
        return pending switch
        {
            { Stage: UpdateStage.Available, Declined: false } when allowed => pending with { Stage = UpdateStage.Downloading },
            { Stage: UpdateStage.Downloading, Approved: false } when !allowed => pending with { Stage = UpdateStage.Available },
            _ => pending,
        };
    }

    /// <summary>Why a download of <paramref name="pending"/> is not running now, or <see cref="DownloadHold.None"/>.</summary>
    public static DownloadHold Hold(PendingUpdate pending, bool online, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return pending.Paused ? DownloadHold.Paused
            : pending.DownloadFailures > 0 && Waiting(pending.RetryAt, now, DownloadWaits[^1]) ? DownloadHold.Failed
            : online ? DownloadHold.None
            : DownloadHold.Offline;
    }

    /// <summary>Whether a download may run now: approved, or allowed by the mode, the cadence and the connection, and neither paused nor waiting to retry.</summary>
    public static bool MayDownload(PendingUpdate? pending, UpdateMode mode, UpdateCadence cadence, bool metered, DateTimeOffset now) =>
        pending is { Stage: UpdateStage.Downloading, Paused: false }
        && (pending.Approved || (mode != UpdateMode.NotifyOnly && cadence != UpdateCadence.Off && !metered))
        && !Waiting(pending.RetryAt, now, DownloadWaits[^1]);

    /// <summary>
    /// One tick of the updater's clock: a check when one is due (and nothing else that tick, since
    /// it may change what is pending); otherwise the download started or stopped as allowed, and an
    /// install while rexplayer is idle.
    /// </summary>
    public static UpdatePlan Plan(UpdateClock clock, UpdateMoment moment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(moment);
        var pending = clock.Pending is { } found ? Settle(found, clock.Mode, clock.Cadence, clock.Metered) : null;
        if (!clock.Checking && CheckDue(clock.Cadence, clock.LastCheck, clock.LastCheckFailure, clock.CheckFailures, now))
        {
            return new UpdatePlan(pending, true, false, false, false);
        }

        var may = clock.Online && MayDownload(pending, clock.Mode, clock.Cadence, clock.Metered, now);
        return new UpdatePlan(pending, false, clock.Downloading && !may, !clock.Downloading && may,
            !clock.Downloading && InstallWhileOpen(pending, clock.Mode, moment, now));
    }

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
        return pending with { Stage = UpdateStage.Ready, DownloadFailures = 0, RetryAt = null, LastError = null, Paused = false };
    }

    /// <summary>
    /// Whether an automatic check should run: the cadence says one is due, and after a failed
    /// attempt (offline, say) enough time has passed: 5 minutes, then 15, then an hour, then 4.
    /// </summary>
    public static bool CheckDue(UpdateCadence cadence, DateTimeOffset? lastCheck, DateTimeOffset? lastFailure, int failures, DateTimeOffset now) =>
        UpdateCheck.IsDue(cadence, lastCheck, now)
        && (lastFailure is not { } failed || failures <= 0 || failed > now
            || now - failed >= CheckWaits[Math.Min(failures, CheckWaits.Length) - 1]);

    /// <summary>"Updated to 0.16.0 yesterday.", or null when no update is known.</summary>
    public static string? LastUpdated(string? version, DateTimeOffset? at, DateTimeOffset now) =>
        version is { Length: > 0 } && at is { } when ? $"Updated to {version} {When(when, now)}." : null;

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

    /// <summary>"15:05", in the offset of <paramref name="now"/>.</summary>
    public static string Clock(DateTimeOffset at, DateTimeOffset now) =>
        at.ToOffset(now.Offset).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Whether <paramref name="until"/> is still ahead, believing no wait longer than <paramref name="longest"/>.</summary>
    private static bool Waiting(DateTimeOffset? until, DateTimeOffset now, TimeSpan longest) =>
        until is { } at && at > now && at - now <= longest + TimeSpan.FromHours(1);
}
