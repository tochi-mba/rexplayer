namespace Rex.Media.Vision;

/// <summary>What a downloadable model does for Subject Lock.</summary>
public enum ModelPurpose
{
    /// <summary>A click or a box in, the outline of the object under it out.</summary>
    PromptSegmentation,

    /// <summary>A picture and a hole in, the picture with the hole filled out.</summary>
    Inpainting,
}

/// <summary>
/// One file of a model, pinned by its SHA-256 (lower-case hexadecimal) so a changed or damaged
/// download is refused rather than run.
/// </summary>
public sealed record ModelFile(string Name, Uri Url, long Bytes, string Sha256);

/// <summary>
/// A set of model weights rexplayer can run through Windows ML once the user has chosen to
/// download it (ADR-0019). <see cref="Input"/> records the tensors the files expect, so a reader can
/// check the adapter against the model without opening it.
/// </summary>
public sealed record VisionModel(
    string Id,
    ModelPurpose Purpose,
    string Title,
    string Authors,
    string Licence,
    Uri LicenceUrl,
    string Input,
    IReadOnlyList<ModelFile> Files)
{
    /// <summary>The download size of every file together.</summary>
    public long Bytes => Files.Sum(file => file.Bytes);
}
