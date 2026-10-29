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
    private const string ZooHandSeg = "https://huggingface.co/opencv/human_segmentation_pphumanseg/resolve/e876e63603f6c65c1a22576bfd5fc7c07ecf9eb9/";
    private const string ZooPalm = "https://huggingface.co/opencv/palm_detection_mediapipe/resolve/233e619dcea1759bf6de707b9b904fe30881ea55/";
    private const string ZooHand = "https://huggingface.co/opencv/handpose_estimation_mediapipe/resolve/4b2a0b446e5cf2f11fb6b2c7251091c035d2c1f7/";
    private const string ZooPerson = "https://huggingface.co/opencv/person_detection_mediapipe/resolve/531b6da87204aa40aa5cc680b4d032dd09b5055e/";
    private const string ZooPose = "https://huggingface.co/opencv/pose_estimation_mediapipe/resolve/8cd55743af63a8578effaf144a68cb58f6450fe9/";

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

    /// <summary>
    /// PPHumanSeg (PaddleSeg, PaddlePaddle authors), as OpenCV's model zoo converted and published it
    /// (ADR-0022): a camera picture to a soft matte of the person in it, 16 ms on a laptop processor.
    /// </summary>
    public static VisionModel HumanMatte { get; } = new(
        "pphumanseg-2023mar",
        ModelPurpose.Matte,
        "PPHumanSeg",
        "PaddlePaddle authors, PaddleSeg; converted by the OpenCV model zoo",
        "Apache-2.0",
        new Uri("https://huggingface.co/opencv/human_segmentation_pphumanseg/blob/main/LICENSE"),
        "x float32 [1,3,192,192] RGB, (value / 255 - 0.5) / 0.5; save_infer_model/scale_0.tmp_1 float32 [1,2,192,192] class scores, room then person",
        [
            new("human_segmentation_pphumanseg_2023mar.onnx", new Uri(ZooHandSeg + "human_segmentation_pphumanseg_2023mar.onnx"), 6_163_938,
                "552d8a984054e59b5d773d24b9b12022b22046ceb2bbc4c9aaeaceb36a9ddf24"),
        ]);

    /// <summary>MediaPipe's palm detector (Google), as OpenCV's model zoo converted it: where the hands are, with seven palm points each.</summary>
    public static VisionModel PalmDetector { get; } = new(
        "mediapipe-palm-2023feb",
        ModelPurpose.PalmDetection,
        "MediaPipe palm detector",
        "Google, MediaPipe Hands; converted by the OpenCV model zoo",
        "Apache-2.0",
        new Uri("https://huggingface.co/opencv/palm_detection_mediapipe/blob/main/LICENSE"),
        "input_1 float32 [1,192,192,3] RGB 0 to 1; Identity float32 [1,2016,18] box and seven points as offsets from the anchors in pixels, Identity_1 float32 [1,2016,1] logits",
        [
            new("palm_detection_mediapipe_2023feb.onnx", new Uri(ZooPalm + "palm_detection_mediapipe_2023feb.onnx"), 3_905_734,
                "78ff51c38496b7fc8b8ebdb6cc8c1abb02fa6c38427c6848254cdaba57fcce7c"),
        ]);

    /// <summary>MediaPipe's hand landmark model (Google), as OpenCV's model zoo converted it: a window on a hand to its 21 points.</summary>
    public static VisionModel HandLandmarks { get; } = new(
        "mediapipe-hand-2023feb",
        ModelPurpose.HandLandmarks,
        "MediaPipe hand landmarks",
        "Google, MediaPipe Hands; converted by the OpenCV model zoo",
        "Apache-2.0",
        new Uri("https://huggingface.co/opencv/handpose_estimation_mediapipe/blob/main/LICENSE"),
        "input_1 float32 [1,224,224,3] RGB 0 to 1; Identity float32 [1,63] x, y, z per point in window pixels, Identity_1 [1,1] confidence, Identity_2 [1,1] handedness (1 right), Identity_3 [1,63] world points",
        [
            new("handpose_estimation_mediapipe_2023feb.onnx", new Uri(ZooHand + "handpose_estimation_mediapipe_2023feb.onnx"), 4_099_621,
                "db0898ae717b76b075d9bf563af315b29562e11f8df5027a1ef07b02bef6d81c"),
        ]);

    /// <summary>MediaPipe's person detector (Google), as OpenCV's model zoo converted it: where the body is, with the hips' middle and a point above the head.</summary>
    public static VisionModel PersonDetector { get; } = new(
        "mediapipe-person-2023mar",
        ModelPurpose.PersonDetection,
        "MediaPipe person detector",
        "Google, MediaPipe Pose; converted by the OpenCV model zoo",
        "Apache-2.0",
        new Uri("https://huggingface.co/opencv/person_detection_mediapipe/blob/main/LICENSE"),
        "input_1:0 float32 [1,3,224,224] RGB, (value / 255 - 0.5) * 2; Identity:0 float32 [1,2254,12] box and four points as offsets from the anchors in pixels, Identity_1:0 float32 [1,2254,1] logits",
        [
            new("person_detection_mediapipe_2023mar.onnx", new Uri(ZooPerson + "person_detection_mediapipe_2023mar.onnx"), 11_990_159,
                "47fd5599d6fa17608f03e0eb0ae230baa6e597d7e8a2c8199fe00abea55a701f"),
        ]);

    /// <summary>MediaPipe's pose model (Google), as OpenCV's model zoo converted it: a window on the body to 33 joints and a mask.</summary>
    public static VisionModel Pose { get; } = new(
        "mediapipe-pose-2023mar",
        ModelPurpose.Pose,
        "MediaPipe pose",
        "Google, MediaPipe Pose; converted by the OpenCV model zoo",
        "Apache-2.0",
        new Uri("https://huggingface.co/opencv/pose_estimation_mediapipe/blob/main/LICENSE"),
        "input_1 float32 [1,256,256,3] RGB 0 to 1; Identity float32 [1,195] x, y, z, visibility, presence per point in window pixels (33 joints, 6 helpers), Identity_1 [1,1] confidence, Identity_2 [1,256,256,1] mask, Identity_3 heatmaps, Identity_4 [1,117] world points",
        [
            new("pose_estimation_mediapipe_2023mar.onnx", new Uri(ZooPose + "pose_estimation_mediapipe_2023mar.onnx"), 5_557_238,
                "9d89c599319a18fb7d2e28451a883476164543182bafca5f09eb2cf767ed2f3f"),
        ]);

    /// <summary>Every model in the catalogue.</summary>
    public static IReadOnlyList<VisionModel> All { get; } = [EfficientSamTiny, MiGan, HumanMatte, PalmDetector, HandLandmarks, PersonDetector, Pose];

    /// <summary>What the "Enhanced Subject Lock" download installs: one model for selection, one for removal.</summary>
    public static IReadOnlyList<VisionModel> SubjectLock => [EfficientSamTiny, MiGan];

    /// <summary>What the "Camera effects pack" download installs (ADR-0022): the matte, the hands and the body.</summary>
    public static IReadOnlyList<VisionModel> CameraPack => [HumanMatte, PalmDetector, HandLandmarks, PersonDetector, Pose];

    /// <summary>The model with this identifier, or null.</summary>
    public static VisionModel? Find(string id) => All.FirstOrDefault(model => model.Id == id);

    /// <summary>Whether a licence identifier (SPDX) is one a downloadable model may carry.</summary>
    public static bool IsPermissive(string licence) => PermissiveLicences.Contains(licence, StringComparer.Ordinal);
}
