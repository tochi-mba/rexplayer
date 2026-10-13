using System.Globalization;

namespace Rex.Media.Vision;

/// <summary>What the Preferences section shows and which of its buttons work, for a set of models.</summary>
public sealed record ModelDownloadView(
    string Status,
    string DownloadLabel,
    bool CanDownload,
    bool CanCancel,
    bool CanRemove,
    bool ShowProgress,
    double ProgressPercent,
    bool IsError);

/// <summary>
/// The words and button states of the "Enhanced Subject Lock (optional download)" section in
/// Preferences, decided here so the window only copies them onto controls.
/// </summary>
public static class ModelDownloadText
{
    public const string Heading = "Enhanced Subject Lock (optional download)";

    public const string Note =
        "Subject Lock works without this download. With it, a click selects the whole object and removal fills the space it leaves more naturally. "
        + "The models run on this computer through Windows ML, and nothing you watch is sent anywhere.";

    /// <summary>A size in decimal megabytes or kilobytes, as download sizes are usually written.</summary>
    public static string Size(long bytes) => bytes >= 1_000_000
        ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1_000_000.0:0.0} MB")
        : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, bytes) / 1000.0:0} KB");

    /// <summary>One line per model: what it is, its size, its licence and what it does.</summary>
    public static IReadOnlyList<string> Contents(IReadOnlyList<VisionModel> models) =>
        [.. models.Select(model => $"{model.Title}, {Size(model.Bytes)}, {model.Licence} licence: {Job(model.Purpose)} ({model.Authors})")];

    /// <summary>The total size line shown above the buttons.</summary>
    public static string Total(IReadOnlyList<VisionModel> models) =>
        $"Downloads {Size(models.Sum(model => model.Bytes))} from the model authors' own pages when you choose Download.";

    /// <summary>The section's state for these models, given each one's status in the same order.</summary>
    public static ModelDownloadView Describe(IReadOnlyList<VisionModel> models, IReadOnlyList<InstallStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(statuses);
        if (models.Count == 0 || models.Count != statuses.Count)
        {
            throw new ArgumentException("Every model has one status.", nameof(statuses));
        }

        var total = models.Sum(model => model.Bytes);
        if (statuses.Any(status => status.Busy))
        {
            // Finished models count in full; the running one counts what has arrived.
            var done = statuses.Select((status, i) => status.Phase switch
            {
                InstallPhase.Installed => models[i].Bytes,
                InstallPhase.Downloading or InstallPhase.Verifying => status.Done,
                _ => 0,
            }).Sum();
            var percent = Math.Clamp(100.0 * done / total, 0, 100);
            var text = statuses.Any(status => status.Phase == InstallPhase.Verifying)
                ? "Checking the downloaded files..."
                : $"Downloading {Size(done)} of {Size(total)}...";
            return new ModelDownloadView(text, "Download", false, true, false, true, percent, false);
        }

        var anyFiles = statuses.Any(status => status.Phase is InstallPhase.Installed or InstallPhase.UpdateAvailable or InstallPhase.Failed);
        if (statuses.FirstOrDefault(status => status.Phase == InstallPhase.Failed) is { Phase: InstallPhase.Failed } failed)
        {
            return new ModelDownloadView($"The download did not finish. {failed.Reason}", "Try again", true, false, anyFiles, false, 0, true);
        }

        if (statuses.All(status => status.Phase == InstallPhase.Installed))
        {
            return new ModelDownloadView("Installed. Subject Lock uses these models when you select and remove.", "Download", false, false, true, false, 100, false);
        }

        if (statuses.Any(status => status.Phase == InstallPhase.UpdateAvailable))
        {
            return new ModelDownloadView("A newer version of the models is available. Until it is downloaded, Subject Lock uses its built-in methods.", "Update", true, false, true, false, 0, false);
        }

        return new ModelDownloadView(
            anyFiles ? "Partly installed. Download fetches the rest." : "Not installed.",
            "Download", true, false, anyFiles, false, 0, false);
    }

    private static string Job(ModelPurpose purpose) => purpose switch
    {
        ModelPurpose.PromptSegmentation => "selects the object you click",
        _ => "fills the space a removed object leaves",
    };
}
