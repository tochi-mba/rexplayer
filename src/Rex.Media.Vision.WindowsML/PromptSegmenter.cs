using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;

namespace Rex.Media.Vision.WindowsML;

/// <summary>
/// Click-to-select through EfficientSAM-Ti on Windows ML. The image encoder runs once per
/// <see cref="PictureKey"/> and its embedding is kept, so a second click on the same picture costs
/// only the prompt decoder. Tensors are built and read by <see cref="SegmentationTensors"/>.
/// </summary>
public sealed class PromptSegmenter : IPromptSegmenter
{
    private const int EmbeddingChannels = 256;
    private const int EmbeddingSide = 64;
    private static readonly string[] EncoderInputs = ["batched_images"];
    private static readonly string[] EncoderOutputs = ["image_embeddings"];
    private static readonly string[] DecoderInputs = ["image_embeddings", "batched_point_coords", "batched_point_labels", "orig_im_size"];
    private static readonly string[] DecoderOutputs = ["output_masks", "iou_predictions"];

    private readonly InferenceSession _encoder;
    private readonly InferenceSession _decoder;
    private readonly RunOptions _run = new();
    private readonly float[] _image = new float[3 * SegmentationTensors.EncoderSide * SegmentationTensors.EncoderSide];
    private readonly float[] _embedding = new float[EmbeddingChannels * EmbeddingSide * EmbeddingSide];
    private (PictureKey Key, int Width, int Height)? _encoded;

    private PromptSegmenter(InferenceSession encoder, InferenceSession decoder, ExecutionDevice device)
    {
        _encoder = encoder;
        _decoder = decoder;
        Device = device;
    }

    public ExecutionDevice Device { get; }

    public TimeSpan LastEncode { get; private set; }

    public TimeSpan LastDecode { get; private set; }

    /// <summary>
    /// Opens the installed model on <paramref name="device"/> (the policy's choice when null), or
    /// returns null when it is not installed at the catalogue's version: the caller then uses its
    /// hand-written selection.
    /// </summary>
    public static PromptSegmenter? Open(ModelStore store, ExecutionDevice? device = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        var model = ModelCatalogue.EfficientSamTiny;
        if (store.Inspect(model) != InstallPhase.Installed)
        {
            return null;
        }

        var wanted = device ?? OnnxSessions.Preferred();
        var (encoder, used) = OnnxSessions.Open(store.PathOf(model, model.Files[0]), wanted);
        try
        {
            // The decoder is small and runs where the encoder ended up.
            var (decoder, _) = OnnxSessions.Open(store.PathOf(model, model.Files[1]), used);
            return new PromptSegmenter(encoder, decoder, used);
        }
        catch
        {
            encoder.Dispose();
            throw;
        }
    }

    public PictureMask Segment(BgraPicture picture, PictureKey key, SegmentPrompt prompt)
    {
        var watch = Stopwatch.StartNew();
        if (_encoded != (key, picture.Width, picture.Height))
        {
            _encoded = null;
            SegmentationTensors.Image(picture, SegmentationTensors.EncoderSide, _image);
            using var image = OrtValue.CreateTensorValueFromMemory(_image, [1, 3, SegmentationTensors.EncoderSide, SegmentationTensors.EncoderSide]);
            using var encoded = _encoder.Run(_run, EncoderInputs, [image], EncoderOutputs);
            encoded[0].GetTensorDataAsSpan<float>().CopyTo(_embedding);
            _encoded = (key, picture.Width, picture.Height);
            LastEncode = watch.Elapsed;
        }
        else
        {
            LastEncode = TimeSpan.Zero;
        }

        watch.Restart();
        var (coordinates, labels) = SegmentationTensors.Prompt(prompt, picture.Width, picture.Height);
        using var embedding = OrtValue.CreateTensorValueFromMemory(_embedding, [1, EmbeddingChannels, EmbeddingSide, EmbeddingSide]);
        using var points = OrtValue.CreateTensorValueFromMemory(coordinates, [1, 1, labels.Length, 2]);
        using var kinds = OrtValue.CreateTensorValueFromMemory(labels, [1, 1, labels.Length]);
        using var size = OrtValue.CreateTensorValueFromMemory(new long[] { picture.Height, picture.Width }, [2]);
        using var decoded = _decoder.Run(_run, DecoderInputs, [embedding, points, kinds, size], DecoderOutputs);
        var mask = SegmentationTensors.Decode(decoded[0].GetTensorDataAsSpan<float>(), decoded[1].GetTensorDataAsSpan<float>(), picture.Width, picture.Height, prompt);
        LastDecode = watch.Elapsed;
        return mask;
    }

    public void Dispose()
    {
        _encoder.Dispose();
        _decoder.Dispose();
        _run.Dispose();
    }
}
