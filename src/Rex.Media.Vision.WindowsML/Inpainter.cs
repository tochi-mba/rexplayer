using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;

namespace Rex.Media.Vision.WindowsML;

/// <summary>
/// Removal through the MI-GAN pipeline on Windows ML: the pipeline crops around the hole, fills it
/// at 512 pixels and blends it back, for a picture of any size. Tensors are built and read by
/// <see cref="InpaintTensors"/>, which also keeps every pixel outside the hole exactly as it was.
/// </summary>
public sealed class Inpainter : IInpainter
{
    private static readonly string[] Inputs = ["image", "mask"];
    private static readonly string[] Outputs = ["result"];

    private readonly InferenceSession _session;
    private readonly RunOptions _run = new();
    private byte[] _image = [];
    private byte[] _mask = [];

    private Inpainter(InferenceSession session, ExecutionDevice device)
    {
        _session = session;
        Device = device;
    }

    public ExecutionDevice Device { get; }

    public TimeSpan LastFill { get; private set; }

    /// <summary>Opens the installed model, or returns null when it is not installed at the catalogue's version.</summary>
    public static Inpainter? Open(ModelStore store, ExecutionDevice? device = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        var model = ModelCatalogue.MiGan;
        if (store.Inspect(model) != InstallPhase.Installed)
        {
            return null;
        }

        var (session, used) = OnnxSessions.Open(store.PathOf(model, model.Files[0]), device ?? OnnxSessions.Preferred());
        return new Inpainter(session, used);
    }

    public void Fill(BgraPicture picture, PictureMask hole, Span<byte> output)
    {
        ArgumentNullException.ThrowIfNull(hole);
        if (hole.Width != picture.Width || hole.Height != picture.Height)
        {
            throw new ArgumentException("The hole must have the picture's size.", nameof(hole));
        }

        var watch = Stopwatch.StartNew();
        var plane = picture.Width * picture.Height;
        if (_mask.Length != plane)
        {
            _image = new byte[3 * plane];
            _mask = new byte[plane];
        }

        InpaintTensors.Image(picture, _image);
        InpaintTensors.Hole(hole, InpaintTensors.DefaultGrowth, _mask);
        using var image = OrtValue.CreateTensorValueFromMemory(_image, [1, 3, picture.Height, picture.Width]);
        using var mask = OrtValue.CreateTensorValueFromMemory(_mask, [1, 1, picture.Height, picture.Width]);
        using var filled = _session.Run(_run, Inputs, [image, mask], Outputs);
        InpaintTensors.Compose(picture, filled[0].GetTensorDataAsSpan<byte>(), _mask, output);
        LastFill = watch.Elapsed;
    }

    public void Dispose()
    {
        _session.Dispose();
        _run.Dispose();
    }
}
