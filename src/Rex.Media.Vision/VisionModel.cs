namespace Rex.Media.Vision;

/// <summary>What a downloadable model does: for Subject Lock, or for the camera (ADR-0022).</summary>
public enum ModelPurpose
{
    /// <summary>A click or a box in, the outline of the object under it out.</summary>
    PromptSegmentation,

    /// <summary>A picture and a hole in, the picture with the hole filled out.</summary>
    Inpainting,

    /// <summary>A camera picture in, where the person is (a soft matte) out.</summary>
    Matte,

    /// <summary>A camera picture in, boxes around the palms and seven palm points out.</summary>
    PalmDetection,

    /// <summary>A window on one hand in, its 21 points and which hand it is out.</summary>
    HandLandmarks,

    /// <summary>A camera picture in, a box around the person and four body points out.</summary>
    PersonDetection,

    /// <summary>A window on the body in, 33 joints and a mask out.</summary>
    Pose,
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
