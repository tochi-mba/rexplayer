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
/// One optional download as Preferences presents it: its heading, what it adds, where it comes
/// from, what the section says once it is installed or out of date, and its models.
/// </summary>
public sealed record ModelPack(string Heading, string Note, string Source, string Installed, string Update, IReadOnlyList<VisionModel> Models)
{
    /// <summary>Enhanced Subject Lock (ADR-0019).</summary>
    public static ModelPack SubjectLock { get; } = new(
        ModelDownloadText.Heading,
        ModelDownloadText.Note,
        "the model authors' own pages",
        "Installed. Subject Lock uses these models when you select and remove.",
        "A newer version of the models is available. Until it is downloaded, Subject Lock uses its built-in methods.",
        ModelCatalogue.SubjectLock);

    /// <summary>The camera effects pack (ADR-0022).</summary>
    public static ModelPack Camera { get; } = new(
        "Camera effects pack (optional download)",
        "The camera effects work without this download, with rexplayer's own outline and motion. With it, they see your outline, "
        + "every finger of your hands and the joints of your body, so the effects follow you closely. The models run on this computer, "
        + "on two processor threads at most, only while a camera effect shows; nothing the camera sees is sent anywhere or kept.",
        "OpenCV's model zoo, where the authors' models are published ready to run",
        "Installed. The camera effects use these models while one of them shows.",
        "A newer version of the models is available. Until it is downloaded, the camera effects use rexplayer's own outline and motion.",
        ModelCatalogue.CameraPack);
}

/// <summary>
/// The words and button states of an optional download's section in Preferences (Enhanced Subject
/// Lock, the camera effects pack), decided here so the window only copies them onto controls.
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
    public static string Total(IReadOnlyList<VisionModel> models) => Total(ModelPack.SubjectLock with { Models = models });

    /// <summary>The total size line for <paramref name="pack"/>: how much, and from where.</summary>
    public static string Total(ModelPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        return $"Downloads {Size(pack.Models.Sum(model => model.Bytes))} from {pack.Source} when you choose Download.";
    }

    /// <summary>The section's state for these models, given each one's status in the same order.</summary>
    public static ModelDownloadView Describe(IReadOnlyList<VisionModel> models, IReadOnlyList<InstallStatus> statuses) =>
        Describe(ModelPack.SubjectLock with { Models = models }, statuses);

    /// <summary>The section's state for <paramref name="pack"/>, given each model's status in the same order.</summary>
    public static ModelDownloadView Describe(ModelPack pack, IReadOnlyList<InstallStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var models = pack.Models;
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
            return new ModelDownloadView(pack.Installed, "Download", false, false, true, false, 100, false);
        }

        if (statuses.Any(status => status.Phase == InstallPhase.UpdateAvailable))
        {
            return new ModelDownloadView(pack.Update, "Update", true, false, true, false, 0, false);
        }

        return new ModelDownloadView(
            anyFiles ? "Partly installed. Download fetches the rest." : "Not installed.",
            "Download", true, false, anyFiles, false, 0, false);
    }

    private static string Job(ModelPurpose purpose) => purpose switch
    {
        ModelPurpose.PromptSegmentation => "selects the object you click",
        ModelPurpose.Matte => "finds where you are in the camera's picture",
        ModelPurpose.PalmDetection => "finds your hands",
        ModelPurpose.HandLandmarks => "follows each finger",
        ModelPurpose.PersonDetection => "finds your body",
        ModelPurpose.Pose => "follows your joints",
        _ => "fills the space a removed object leaves",
    };
}
