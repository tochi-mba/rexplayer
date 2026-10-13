using Rex.Media.AppCore;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.Video;
using Rex.Media.Vision.WindowsML;

namespace Rex.Media.Vision.Windows.Tests;

/// <summary>A picture in memory, BGRA, rows packed.</summary>
internal sealed record Picture(byte[] Pixels, int Width, int Height)
{
    public int Stride => Width * 4;

    public BgraPicture View => new(Pixels, Width, Height, Stride);
}

/// <summary>
/// Where the models, the movie frames and the outputs are. Models come from the store the app uses
/// (or <c>REXPLAYER_MODELS</c>); frames from <c>REXPLAYER_VISION_FRAMES</c>, a folder of PNG snapshots
/// (<c>rexplay snapshot</c>) with a <c>clicks.txt</c> of "file x y" lines and a <c>sequence.txt</c>
/// whose first line is "file x y" and whose other lines name the following pictures. Nothing here is
/// committed: no weights, no frames, no outputs.
/// </summary>
internal static class VisionFixtures
{
    public static ModelStore Store { get; } = new(Environment.GetEnvironmentVariable("REXPLAYER_MODELS") is { Length: > 0 } root
        ? root
        : ModelStore.DefaultRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));

    public static string? Frames => Environment.GetEnvironmentVariable("REXPLAYER_VISION_FRAMES") is { Length: > 0 } folder && Directory.Exists(folder) ? folder : null;

    public static string Output
    {
        get
        {
            var folder = Environment.GetEnvironmentVariable("REXPLAYER_VISION_OUT") is { Length: > 0 } chosen
                ? chosen
                : Path.Combine(Path.GetTempPath(), "rexplayer-vision");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>The devices worth measuring here: the processor, and the graphics processor when Windows ML offers one.</summary>
    public static TheoryData<ExecutionDevice> Devices() => new(ExecutionDevice.Cpu, ExecutionDevice.Gpu);

    public static void RequireInstalled(VisionModel model)
    {
        if (Store.Inspect(model) != InstallPhase.Installed)
        {
            Assert.Skip($"{model.Title} is not installed in {Store.Root}; download it from Preferences or run the download test.");
        }
    }

    public static void RequireDevice(ExecutionDevice device)
    {
        if (device == ExecutionDevice.Gpu && OnnxSessions.Preferred() != ExecutionDevice.Gpu)
        {
            Assert.Skip("Windows ML offers no graphics processor here.");
        }
    }

    /// <summary>Decodes a picture file the way the app does: through the demuxers and Windows' imaging decoder.</summary>
    public static Picture Load(string path)
    {
        var source = new MemoryByteSource(File.ReadAllBytes(path), Path.GetFileName(path));
        using var demuxer = MediaRegistries.Demuxers().Open(source, CancellationToken.None);
        var factory = new WicDecoderFactory();
        using var decoder = factory.CreateVideo(demuxer.Info.Tracks[0]);
        using var packet = demuxer.ReadPacket(CancellationToken.None)!;
        var frames = new List<VideoFrame>();
        decoder.Decode(packet, frames);
        using var frame = Assert.Single(frames);
        var pixels = new byte[frame.Width * frame.Height * 4];
        for (var y = 0; y < frame.Height; y++)
        {
            frame.Row(0, y)[..(frame.Width * 4)].CopyTo(pixels.AsSpan(y * frame.Width * 4));
        }

        return new Picture(pixels, frame.Width, frame.Height);
    }

    public static void Save(Picture picture, string name)
    {
        using var frame = VideoFrame.Rent(PixelFormat.Bgra32, picture.Width, picture.Height);
        for (var y = 0; y < picture.Height; y++)
        {
            picture.Pixels.AsSpan(y * picture.Stride, picture.Stride).CopyTo(frame.Row(0, y));
        }

        using var file = File.Create(Path.Combine(Output, name));
        PngWriter.Write(file, frame);
    }

    /// <summary>The picture with the mask tinted green and its bounds drawn in red, for judging an outline by eye.</summary>
    public static Picture Overlay(Picture picture, PictureMask mask)
    {
        var pixels = (byte[])picture.Pixels.Clone();
        for (var y = 0; y < picture.Height; y++)
        {
            for (var x = 0; x < picture.Width; x++)
            {
                var at = (y * picture.Stride) + (x * 4);
                if (mask.Contains(x, y))
                {
                    pixels[at] = (byte)(pixels[at] / 2);
                    pixels[at + 1] = (byte)((pixels[at + 1] / 2) + 120);
                    pixels[at + 2] = (byte)(pixels[at + 2] / 2);
                }

                var box = mask.Bounds;
                var onEdge = (x == box.Left || x == box.Right - 1) && y >= box.Top && y < box.Bottom
                    || (y == box.Top || y == box.Bottom - 1) && x >= box.Left && x < box.Right;
                if (onEdge)
                {
                    (pixels[at], pixels[at + 1], pixels[at + 2]) = ((byte)0, (byte)0, (byte)255);
                }
            }
        }

        return picture with { Pixels = pixels };
    }

    /// <summary>"file x y" lines of a manifest in the frames folder.</summary>
    public static IReadOnlyList<(string File, int X, int Y)> Clicks(string folder, string manifest) =>
        [.. File.ReadAllLines(Path.Combine(folder, manifest))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 3)
            .Select(parts => (Path.Combine(folder, parts[0]), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)))];

    public static void Note(string line)
    {
        File.AppendAllLines(Path.Combine(Output, "timings.txt"), [line]);
        TestContext.Current.SendDiagnosticMessage(line);
    }
}
