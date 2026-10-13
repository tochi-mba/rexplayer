using Rex.Media.Settings;

namespace Rex.Media.AppCore.Updates;

/// <summary>Where the updater is, as the user sees it.</summary>
public enum UpdateState
{
    UpToDate,
    Checking,
    CheckFailed,
    Available,
    Downloading,

    /// <summary>Every byte is in and the file is being checked against its checksum.</summary>
    Verifying,

    Paused,
    Offline,

    /// <summary>The last download attempt failed; the next waits a while.</summary>
    Failed,

    Ready,
    Installing,
}

/// <summary>The styling a state asks for: error styling only for failures.</summary>
public enum UpdateTone
{
    Neutral,
    Accent,
    Success,
    Error,
}

/// <summary>The indicator's icon.</summary>
public enum UpdateGlyph
{
    Download,
    Paused,
    Ready,
    Checking,
    UpToDate,
}

/// <summary>The small mark on the indicator's corner.</summary>
public enum UpdateBadge
{
    None,

    /// <summary>A dot: something to look at when it suits you.</summary>
    Attention,

    /// <summary>A tick: ready.</summary>
    Success,

    /// <summary>A cross: something failed.</summary>
    Critical,
}

/// <summary>The buttons the update panel shows.</summary>
[Flags]
public enum UpdateCommands
{
    None = 0,
    Download = 1,
    Resume = 2,
    Retry = 4,
    Restart = 8,
    CheckAgain = 16,
    Pause = 32,
    Later = 64,
    Cancel = 128,
}

/// <summary>Everything the update panel, the indicator and the card are drawn from.</summary>
/// <param name="Current">The running version.</param>
/// <param name="Pending">The pending update.</param>
/// <param name="Mode">What the user chose to happen with a new version.</param>
/// <param name="Checking">A check is running.</param>
/// <param name="Downloading">A download is running.</param>
/// <param name="Transfer">How far the running download has got, once it has said.</param>
/// <param name="Online">Windows has an internet connection.</param>
/// <param name="Metered">The connection costs by the byte.</param>
/// <param name="LastCheck">The last successful check.</param>
/// <param name="CheckProblem">Why the last check failed, in plain words; null after a successful one.</param>
/// <param name="Open">The user opened the update panel or asked for a check, so even a passing state is shown.</param>
/// <param name="Now">Now, with the local offset, for times and days.</param>
public sealed record UpdateSituation(
    string Current, PendingUpdate? Pending, UpdateMode Mode, bool Checking, bool Downloading, TransferProgress? Transfer,
    bool Online, bool Metered, DateTimeOffset? LastCheck, string? CheckProblem, bool Open, DateTimeOffset Now);

/// <summary>The update panel: what the indicator opens, and what Preferences and screen readers are told.</summary>
/// <param name="State">The state shown.</param>
/// <param name="Version">The version it is about.</param>
/// <param name="Title">A short heading.</param>
/// <param name="Status">One or two plain sentences, or the download's progress.</param>
/// <param name="Note">A further line, or null.</param>
/// <param name="Tone">Its styling.</param>
/// <param name="Progress">The share downloaded, from 0 to 1, when known.</param>
/// <param name="Busy">Work is going on whose end is not known (checking, verifying, installing).</param>
/// <param name="Commands">The buttons shown.</param>
/// <param name="Primary">The one button drawn as the main action, or none.</param>
/// <param name="OffersTiming">Later also offers installing on close or tonight.</param>
/// <param name="Shown">The indicator stays in the controls even when the panel is closed.</param>
/// <param name="Line">The whole state in one line: the indicator's tooltip and the Preferences status.</param>
/// <param name="Spoken">What a screen reader is told when the panel reaches this state.</param>
public sealed record UpdatePanel(
    UpdateState State, string Version, string Title, string Status, string? Note, UpdateTone Tone, double? Progress, bool Busy,
    UpdateCommands Commands, UpdateCommands Primary, bool OffersTiming, bool Shown, string Line, string Spoken);

/// <summary>The small indicator in the controls.</summary>
public sealed record UpdateIndicatorView(bool Visible, UpdateGlyph Glyph, UpdateBadge Badge, double? Progress, bool Busy, string ToolTip);

/// <summary>The compact card offering a decision: its one line and its main action.</summary>
public sealed record UpdateCardView(UpdatePrompt Kind, string Version, string Message, string Action);

/// <summary>A startup notice: title, message, and whether it reports a failure.</summary>
public sealed record UpdateNotice(string Title, string Message, bool Failure);

/// <summary>
/// The words and styling of the update experience (TOOL-09): a quiet indicator while work goes on
/// in the background, a panel with the live state, and a compact card only when a decision is
/// needed. Pure: the window draws what this returns.
/// </summary>
public static class UpdateView
{
    /// <summary>The line the panel adds while a download runs.</summary>
    public const string KeepsGoing = "Keeps downloading in the background. You can keep watching.";

    /// <summary>The panel for <paramref name="situation"/>.</summary>
    public static UpdatePanel Panel(UpdateSituation situation)
    {
        ArgumentNullException.ThrowIfNull(situation);
        var (pending, now) = (situation.Pending, situation.Now);
        if (situation.Checking && !situation.Downloading && (pending is null || situation.Open))
        {
            return Make(UpdateState.Checking, situation.Current, "Checking for updates", "Looking for a newer version of rexplayer...", tone: UpdateTone.Neutral, busy: true);
        }

        if (pending is null)
        {
            return situation.CheckProblem is { } problem
                ? Make(UpdateState.CheckFailed, situation.Current, "Could not check for updates", problem, tone: UpdateTone.Error,
                    commands: UpdateCommands.CheckAgain, primary: UpdateCommands.CheckAgain)
                : Make(UpdateState.UpToDate, situation.Current, "rexplayer is up to date",
                    $"You have rexplayer {situation.Current}." + (situation.LastCheck is { } last ? $" Last checked {UpdateFlow.When(last, now)} at {UpdateFlow.Clock(last, now)}." : ""),
                    tone: UpdateTone.Success);
        }

        var version = pending.Version;
        var attention = pending.NeedsAttention ? "This release asks you to read what's new before installing." : null;
        switch (pending.Stage)
        {
            case UpdateStage.Installing:
                return Make(UpdateState.Installing, version, $"Installing rexplayer {version}", "rexplayer closes, installs the update and opens again where you were.",
                    busy: true, shown: true);
            case UpdateStage.Ready:
                return Make(UpdateState.Ready, version, $"rexplayer {version} is ready", ReadyStatus(pending, situation.Mode, now), attention,
                    UpdateTone.Success, commands: UpdateCommands.Restart | UpdateCommands.Later, primary: UpdateCommands.Restart, timing: true, shown: true);
            case UpdateStage.Available:
                var status = pending.Declined ? "You cancelled its download. Download it whenever it suits you."
                    : situation.Metered && situation.Mode != UpdateMode.NotifyOnly ? "You are on a metered connection, so it has not been downloaded."
                    : "Download it in the background, then choose when to install it.";
                var size = pending.Size > 0 ? $"Download size: {TransferProgress.Size(pending.Size)}." : null;
                return Make(UpdateState.Available, version, $"rexplayer {version} is available", status, Join(size, attention),
                    commands: UpdateCommands.Download | UpdateCommands.Later, primary: UpdateCommands.Download, shown: !pending.Declined);
        }

        if (situation.Downloading && situation.Transfer is { Complete: true })
        {
            return Make(UpdateState.Verifying, version, $"Downloading rexplayer {version}", "Checking the download...", KeepsGoing,
                busy: true, commands: UpdateCommands.Cancel, shown: true);
        }

        var hold = situation.Downloading ? DownloadHold.None : UpdateFlow.Hold(pending, situation.Online, now);
        return hold switch
        {
            DownloadHold.Paused => Make(UpdateState.Paused, version, "Download paused", $"rexplayer {version} waits until you resume the download.",
                tone: UpdateTone.Neutral, commands: UpdateCommands.Resume | UpdateCommands.Cancel, primary: UpdateCommands.Resume, shown: true),
            DownloadHold.Failed => Make(UpdateState.Failed, version, "The download stopped",
                $"rexplayer {version} could not be downloaded: {pending.LastError ?? "unknown error"}. It tries again {UpdateFlow.When(pending.RetryAt!.Value, now)} at {UpdateFlow.Clock(pending.RetryAt.Value, now)}.",
                tone: UpdateTone.Error, commands: UpdateCommands.Retry | UpdateCommands.Cancel, primary: UpdateCommands.Retry, shown: true),
            DownloadHold.Offline => Make(UpdateState.Offline, version, "Waiting for a connection", $"rexplayer {version} downloads once you are online.",
                tone: UpdateTone.Neutral, commands: UpdateCommands.Cancel, shown: true),
            _ => Downloading(version, situation.Downloading ? situation.Transfer : null),
        };
    }

    /// <summary>The indicator for <paramref name="panel"/>: shown while there is something to see, or while the panel is <paramref name="open"/>.</summary>
    public static UpdateIndicatorView Indicator(UpdatePanel panel, bool open)
    {
        ArgumentNullException.ThrowIfNull(panel);
        var glyph = panel.State switch
        {
            UpdateState.UpToDate => UpdateGlyph.UpToDate,
            UpdateState.Checking or UpdateState.CheckFailed => UpdateGlyph.Checking,
            UpdateState.Paused => UpdateGlyph.Paused,
            UpdateState.Ready or UpdateState.Installing => UpdateGlyph.Ready,
            _ => UpdateGlyph.Download,
        };
        var badge = panel.State switch
        {
            UpdateState.CheckFailed or UpdateState.Failed => UpdateBadge.Critical,
            UpdateState.Ready => UpdateBadge.Success,
            UpdateState.Available => UpdateBadge.Attention,
            _ => UpdateBadge.None,
        };
        return new UpdateIndicatorView(open || panel.Shown, glyph, badge, panel.Progress, panel.Busy, panel.Line);
    }

    /// <summary>The card for <paramref name="prompt"/> (from <see cref="UpdateFlow.Prompt"/>), or null when there is none.</summary>
    public static UpdateCardView? Card(UpdatePrompt prompt, PendingUpdate? pending, bool metered)
    {
        if (prompt == UpdatePrompt.None || pending is null)
        {
            return null;
        }

        var attention = pending.NeedsAttention ? " This release asks you to read what's new first." : "";
        if (prompt == UpdatePrompt.Ready)
        {
            return new UpdateCardView(prompt, pending.Version, $"rexplayer {pending.Version} is ready. Restart to update; you will come back to the same moment.{attention}", "Restart now");
        }

        var size = pending.Size > 0 ? $" ({TransferProgress.Size(pending.Size)})" : "";
        var why = metered ? "You are on a metered connection, so it has not been downloaded." : "Download it in the background, then choose when to install it.";
        return new UpdateCardView(prompt, pending.Version, $"rexplayer {pending.Version}{size} is available. {why}{attention}", "Download");
    }

    /// <summary>
    /// What to tell a screen reader as the panel goes from <paramref name="before"/> to
    /// <paramref name="after"/>: only a change of state or version, never each step of a download,
    /// and the result of a check only when the user asked for it (<paramref name="open"/>).
    /// </summary>
    public static string? Announcement(UpdatePanel? before, UpdatePanel after, bool open)
    {
        ArgumentNullException.ThrowIfNull(after);
        if (before is not null && before.State == after.State && before.Version == after.Version)
        {
            return null;
        }

        return open || after.State is not (UpdateState.UpToDate or UpdateState.Checking or UpdateState.CheckFailed) ? after.Spoken : null;
    }

    /// <summary>What "Later" says once chosen.</summary>
    public static string Reminded(RemindChoice choice, string version) => choice switch
    {
        RemindChoice.InAnHour => $"rexplayer reminds you about {version} in an hour.",
        RemindChoice.NextWeek => $"rexplayer reminds you about {version} next week.",
        _ => $"rexplayer reminds you about {version} tomorrow.",
    };

    /// <summary>The notice for what a start learned, or null when there is nothing to say.</summary>
    /// <param name="news">From <see cref="UpdateFlow.AtStartup"/>.</param>
    /// <param name="end">How the installer ended.</param>
    /// <param name="version">The version that did not install.</param>
    /// <param name="current">The running version.</param>
    public static UpdateNotice? News(StartupNews news, InstallEnd end, string? version, string current) => news switch
    {
        StartupNews.InstallCancelled => new UpdateNotice($"rexplayer {version} was not installed",
            $"The installer was cancelled, so rexplayer {current} is still here and nothing was lost.", false),
        StartupNews.InstallFailed => new UpdateNotice($"rexplayer {version} did not install",
            (end == InstallEnd.AppOpen ? "The installer could not close rexplayer." : "The installer stopped before it finished.")
            + $" rexplayer {current} is still here and nothing was lost. The log (Help, Open the log folder) has the details.", true),
        StartupNews.InstallAbandoned => new UpdateNotice("The update could not be installed",
            $"It failed {UpdateFlow.MostInstallFailures} times, so rexplayer {current} set it aside. A later check offers it again; the log has the details.", true),
        _ => null,
    };

    /// <summary>Why a check failed, in plain words.</summary>
    /// <param name="online">Windows had a connection.</param>
    /// <param name="status">The server's answer, when it gave one.</param>
    /// <param name="timedOut">The server did not answer in time.</param>
    public static string CheckProblem(bool online, int? status, bool timedOut) => (online, status, timedOut) switch
    {
        (false, _, _) => "You are offline. rexplayer looks again once you are connected.",
        (_, _, true) => "The update server did not answer in time. rexplayer tries again later.",
        (_, 403 or 429, _) => "The update server is busy. rexplayer tries again later.",
        (_, { } code, _) => $"The update server answered with an error ({code}). rexplayer tries again later.",
        _ => "rexplayer could not reach the update server. rexplayer tries again later.",
    };

    private static UpdatePanel Downloading(string version, TransferProgress? transfer)
    {
        var progress = transfer?.Text() ?? "Starting the download...";
        return Make(UpdateState.Downloading, version, $"Downloading rexplayer {version}", progress, KeepsGoing,
            progress: transfer?.Fraction, busy: transfer?.Fraction is null, commands: UpdateCommands.Pause | UpdateCommands.Cancel, shown: true,
            line: $"Downloading rexplayer {version} in the background" + (transfer is null ? "." : $": {progress}"), spoken: $"Downloading rexplayer {version} in the background.");
    }

    private static string ReadyStatus(PendingUpdate pending, UpdateMode mode, DateTimeOffset now) => pending.Timing switch
    {
        UpdateTiming.Scheduled => "It installs "
            + (pending.InstallAt is { } at ? $"{UpdateFlow.When(at, now)} at {UpdateFlow.Clock(at, now)}" : "tonight")
            + " if rexplayer is not in use, or when you close it.",
        UpdateTiming.OnClose => "It installs when you close rexplayer.",
        _ when UpdateFlow.InstallsUnasked(pending, mode) => "It installs by itself when rexplayer is closed or not in use.",
        _ => "Restart to update; you will come back to the same moment.",
    };

    private static string? Join(string? first, string? second) =>
        first is null ? second : second is null ? first : first + " " + second;

    private static UpdatePanel Make(
        UpdateState state, string version, string title, string status, string? note = null, UpdateTone tone = UpdateTone.Accent,
        double? progress = null, bool busy = false, UpdateCommands commands = UpdateCommands.None, UpdateCommands primary = UpdateCommands.None,
        bool timing = false, bool shown = false, string? line = null, string? spoken = null)
    {
        line ??= $"{title}. {status}";
        return new UpdatePanel(state, version, title, status, note, tone, progress, busy, commands, primary, timing, shown, line, spoken ?? line);
    }
}
