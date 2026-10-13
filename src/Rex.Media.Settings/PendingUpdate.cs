using System.Globalization;

namespace Rex.Media.Settings;

/// <summary>How far a newer version has got on this PC.</summary>
public enum UpdateStage
{
    /// <summary>Found, but not downloaded: automatic downloads are off, or the connection is metered.</summary>
    Available,

    /// <summary>Being downloaded in the background, or waiting to try again.</summary>
    Downloading,

    /// <summary>Downloaded and verified; waiting for the user's choice of when to install.</summary>
    Ready,

    /// <summary>Handed to the installer; the next start says whether it worked.</summary>
    Installing,
}

/// <summary>When a downloaded version installs.</summary>
public enum UpdateTiming
{
    /// <summary>When the user says so; until then rexplayer offers it, at most once per reminder.</summary>
    Ask,

    /// <summary>As rexplayer closes.</summary>
    OnClose,

    /// <summary>At <see cref="PendingUpdate.InstallAt"/>, once rexplayer is idle, or as it closes if sooner.</summary>
    Scheduled,
}

/// <summary>
/// A newer version rexplayer has found and is downloading, holding or installing (TOOL-09). Kept in
/// the settings so a restart, a crash or a closed window carries on where it was. Every field has a
/// default, so a settings file from an older version reads as "nothing pending".
/// </summary>
public sealed record PendingUpdate
{
    /// <summary>The longest release notes kept; the rest is on the release page.</summary>
    public const int MaxNotesLength = 16_000;

    /// <summary>The version, as "major.minor.patch".</summary>
    public string Version { get; init; } = "";

    public string InstallerName { get; init; } = "";

    public string InstallerUrl { get; init; } = "";

    public string ChecksumUrl { get; init; } = "";

    public string ReleasePage { get; init; } = "";

    /// <summary>The installer's size in bytes as the release states it; 0 when it does not say.</summary>
    public long Size { get; init; }

    /// <summary>
    /// The release notes, tidied: one block per line ("### Heading", "- **Label.** text", "- text"
    /// or a paragraph). Notes saved by older versions as plain text read the same way.
    /// </summary>
    public string Notes { get; init; } = "";

    /// <summary>The release asks for the user's attention, so it is never installed without asking.</summary>
    public bool NeedsAttention { get; init; }

    public UpdateStage Stage { get; init; } = UpdateStage.Downloading;

    public UpdateTiming Timing { get; init; } = UpdateTiming.Ask;

    /// <summary>The user asked for this download, so it goes ahead even on a metered connection.</summary>
    public bool Approved { get; init; }

    /// <summary>The user paused the download; it waits, across restarts, until they resume it.</summary>
    public bool Paused { get; init; }

    /// <summary>
    /// The user cancelled the download: the version stays on offer, but rexplayer does not download
    /// it again by itself. A newer version clears this.
    /// </summary>
    public bool Declined { get; init; }

    /// <summary>The user cancelled the installer once, so automatic mode asks before trying again.</summary>
    public bool InstallCancelled { get; init; }

    /// <summary>Not offered again before this moment ("Remind me later").</summary>
    public DateTimeOffset? RemindAt { get; init; }

    /// <summary>For <see cref="UpdateTiming.Scheduled"/>: the earliest moment to install.</summary>
    public DateTimeOffset? InstallAt { get; init; }

    /// <summary>Downloads that failed in a row; each waits longer before the next attempt.</summary>
    public int DownloadFailures { get; init; }

    /// <summary>The next download attempt, after a failure.</summary>
    public DateTimeOffset? RetryAt { get; init; }

    /// <summary>Why the last download failed, in words for the user.</summary>
    public string? LastError { get; init; }

    /// <summary>Installs that were cancelled or failed.</summary>
    public int InstallFailures { get; init; }

    /// <summary>When the installer was handed this version.</summary>
    public DateTimeOffset? HandedOffAt { get; init; }

    /// <summary>
    /// A copy with every value in range, or null when this cannot be a version rexplayer published:
    /// an unreadable version, an installer name that is not the version's own, or a link that is not https.
    /// </summary>
    public PendingUpdate? Normalize()
    {
        var parts = (Version ?? "").Split('.');
        if (parts.Length != 3 || !parts.All(part => part.Length > 0 && int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            || InstallerName != $"rexplayer-Setup-{Version}.exe"
            || !IsHttps(InstallerUrl) || !IsHttps(ChecksumUrl))
        {
            return null;
        }

        return this with
        {
            ReleasePage = IsHttps(ReleasePage) ? ReleasePage : "https://github.com/tochi-mba/rexplayer/releases/latest",
            Size = Math.Max(0, Size),
            Notes = Notes is null ? "" : Notes.Length > MaxNotesLength ? Notes[..MaxNotesLength] : Notes,
            Stage = Enum.IsDefined(Stage) ? Stage : UpdateStage.Downloading,
            Timing = Enum.IsDefined(Timing) ? Timing : UpdateTiming.Ask,
            DownloadFailures = Math.Clamp(DownloadFailures, 0, 1000),
            InstallFailures = Math.Clamp(InstallFailures, 0, 1000),
        };
    }

    private static bool IsHttps(string? link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
