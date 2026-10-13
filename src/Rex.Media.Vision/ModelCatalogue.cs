namespace Rex.Media.Vision;

/// <summary>
/// Every model rexplayer may download, pinned to one revision of the authors' own release and to
/// the SHA-256 of each file (ADR-0019). Weights are never bundled: a model reaches a computer only
/// after its user chooses to download it, and every feature still works without it.
/// </summary>
public static class ModelCatalogue
{
    private const string EfficientSamRevision = "https://raw.githubusercontent.com/yformer/EfficientSAM/f13035d925a0de803e8f733f3239fa05b1fcf36f/weights/";
    private const string MiGanRevision = "https://huggingface.co/andraniksargsyan/migan/resolve/406830d0fa60666da0071c342ad2fbc8f30c5c64/";

    /// <summary>The only licences a downloadable model may carry: permissive, commercial use allowed.</summary>
    public static IReadOnlyList<string> PermissiveLicences { get; } = ["Apache-2.0", "MIT", "BSD-2-Clause", "BSD-3-Clause"];

    /// <summary>
    /// EfficientSAM-Ti (Xiong et al., CVPR 2024): a click or a box to the outline of the object under
    /// it. The image encoder runs once per picture; the prompt decoder is cheap enough to run per click.
    /// </summary>
    public static VisionModel EfficientSamTiny { get; } = new(
        "efficientsam-ti",
        ModelPurpose.PromptSegmentation,
        "EfficientSAM-Ti",
        "Yunyang Xiong et al., EfficientSAM, CVPR 2024",
        "Apache-2.0",
        new Uri("https://github.com/yformer/EfficientSAM/blob/main/LICENSE"),
        "encoder: batched_images float32 [1,3,H,W] RGB 0 to 1, resized to 1024 square inside; "
            + "decoder: image_embeddings float32 [1,256,64,64], batched_point_coords float32 [1,1,N,2] in pixels, "
            + "batched_point_labels float32 [1,1,N] (1 point, 2 box top left, 3 box bottom right), "
            + "orig_im_size int64 [2] (height, width); output_masks float32 [1,1,3,height,width] logits, iou_predictions float32 [1,1,3]",
        [
            new("efficient_sam_vitt_encoder.onnx", new Uri(EfficientSamRevision + "efficient_sam_vitt_encoder.onnx"), 24_799_761,
                "84ed466ffcc5c1f8d08409bc34a23bb364ab2c15e402cb12d4335a42be0e0951"),
            new("efficient_sam_vitt_decoder.onnx", new Uri(EfficientSamRevision + "efficient_sam_vitt_decoder.onnx"), 16_565_728,
                "a62f8fa5ea080447c0689418d69e58f1e83e0b7adf9c142e2bd9bcc8045c0b11"),
        ]);

    /// <summary>
    /// MI-GAN at 512 pixels, trained on Places2 (Sargsyan et al., ICCV 2023): fills a hole in a
    /// picture of any size. The published pipeline crops around the hole, fills it and blends it back.
    /// </summary>
    public static VisionModel MiGan { get; } = new(
        "migan-512-places2",
        ModelPurpose.Inpainting,
        "MI-GAN 512 (Places2)",
        "Andranik Sargsyan et al., MI-GAN, ICCV 2023",
        "MIT",
        new Uri("https://huggingface.co/andraniksargsyan/migan/blob/main/LICENSE"),
        "image uint8 [1,3,H,W] RGB; mask uint8 [1,1,H,W], 255 kept and 0 to fill; result uint8 [1,3,H,W] RGB",
        [
            new("migan_pipeline_v2.onnx", new Uri(MiGanRevision + "migan_pipeline_v2.onnx"), 28_079_181,
                "6f1f3530a1a2324b19752018ce756088b07973cda8d7d890034ace5c8a48c40b"),
        ]);

    /// <summary>Every model in the catalogue.</summary>
    public static IReadOnlyList<VisionModel> All { get; } = [EfficientSamTiny, MiGan];

    /// <summary>What the "Enhanced Subject Lock" download installs: one model for selection, one for removal.</summary>
    public static IReadOnlyList<VisionModel> SubjectLock => All;

    /// <summary>The model with this identifier, or null.</summary>
    public static VisionModel? Find(string id) => All.FirstOrDefault(model => model.Id == id);

    /// <summary>Whether a licence identifier (SPDX) is one a downloadable model may carry.</summary>
    public static bool IsPermissive(string licence) => PermissiveLicences.Contains(licence, StringComparer.Ordinal);
}
