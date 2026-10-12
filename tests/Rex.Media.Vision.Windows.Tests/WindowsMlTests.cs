using System.Diagnostics;
using System.Globalization;
using Rex.Media.Vision.WindowsML;

namespace Rex.Media.Vision.Windows.Tests;

/// <summary>
/// The Windows ML adapters run for real: the runtime the Windows App SDK ships, the downloaded
/// models (skipped cleanly when absent), on the processor and on the graphics processor. Masks and
/// fills are written as PNGs to judge by eye, and every timing is noted in timings.txt.
/// </summary>
public sealed class WindowsMlTests
{
    [Fact]
    public void TheRuntimeOffersAtLeastTheProcessor()
    {
        var accelerators = OnnxSessions.Accelerators();

        Assert.Contains(accelerators, device => !device.IsGpu);
        VisionFixtures.Note("Accelerators: " + string.Join("; ", accelerators.Select(device => $"{device.Provider} gpu={device.IsGpu} vendor=0x{device.VendorId:x4} device=0x{device.DeviceId:x4}")));
    }

    [Fact]
    public void NothingOpensWithoutTheDownload()
    {
        var empty = new ModelStore(Path.Combine(Path.GetTempPath(), "rexplayer-no-models-" + Guid.NewGuid().ToString("N")));

        Assert.Null(PromptSegmenter.Open(empty));
        Assert.Null(Inpainter.Open(empty));
    }

    [Theory]
    [MemberData(nameof(VisionFixtures.Devices), MemberType = typeof(VisionFixtures))]
    public void AClickSelectsTheWholeShapeAndASecondClickReusesTheEncoding(ExecutionDevice device)
    {
        VisionFixtures.RequireInstalled(ModelCatalogue.EfficientSamTiny);
        VisionFixtures.RequireDevice(device);
        var picture = Disc(640, 480, centreX: 400, centreY: 220, radius: 90);
        using var segmenter = PromptSegmenter.Open(VisionFixtures.Store, device)!;

        var mask = segmenter.Segment(picture.View, new PictureKey(1, 0), SegmentPrompt.At(400, 220));
        var encode = segmenter.LastEncode;
        var again = segmenter.Segment(picture.View, new PictureKey(1, 0), SegmentPrompt.At(420, 250));

        Assert.Equal(device, segmenter.Device);
        Assert.True(Overlap(mask, picture, 400, 220, 90) > 0.85, $"The outline overlaps the disc by {Overlap(mask, picture, 400, 220, 90):0.00}.");
        Assert.True(encode > TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, segmenter.LastEncode);
        Assert.True(again.Contains(420, 250));
        VisionFixtures.Save(VisionFixtures.Overlay(picture, mask), $"disc-{device}.png");
    }

    [Theory]
    [MemberData(nameof(VisionFixtures.Devices), MemberType = typeof(VisionFixtures))]
    public void AHoleIsFilledFromItsSurroundingsAndNothingElseChanges(ExecutionDevice device)
    {
        VisionFixtures.RequireInstalled(ModelCatalogue.MiGan);
        VisionFixtures.RequireDevice(device);
        var picture = Gradient(640, 360);
        var hole = new byte[640 * 360];
        for (var y = 140; y < 220; y++)
        {
            for (var x = 280; x < 360; x++)
            {
                hole[(y * 640) + x] = 255;
                picture.Pixels[(y * picture.Stride) + (x * 4)] = 0;
                picture.Pixels[(y * picture.Stride) + (x * 4) + 1] = 0;
                picture.Pixels[(y * picture.Stride) + (x * 4) + 2] = 0;
            }
        }

        using var inpainter = Inpainter.Open(VisionFixtures.Store, device)!;
        var output = new byte[picture.Pixels.Length];
        inpainter.Fill(picture.View, new PictureMask(hole, 640, 360, 1), output);

        var expected = Gradient(640, 360);
        double error = 0;
        for (var y = 140; y < 220; y++)
        {
            for (var x = 280; x < 360; x++)
            {
                error += Math.Abs(output[(y * picture.Stride) + (x * 4) + 2] - expected.Pixels[(y * picture.Stride) + (x * 4) + 2]);
            }
        }

        var mean = error / (80 * 80);
        Assert.True(mean < 30, $"The fill is {mean:0.0} levels from the gradient on average.");
        Assert.Equal(picture.Pixels.AsSpan(0, 100 * picture.Stride), output.AsSpan(0, 100 * picture.Stride));
        VisionFixtures.Save(new Picture(output, 640, 360), $"gradient-fill-{device}.png");
    }

    [Theory]
    [MemberData(nameof(VisionFixtures.Devices), MemberType = typeof(VisionFixtures))]
    public void MovieFramesAreSelectedAndFilled(ExecutionDevice device)
    {
        VisionFixtures.RequireInstalled(ModelCatalogue.EfficientSamTiny);
        VisionFixtures.RequireInstalled(ModelCatalogue.MiGan);
        VisionFixtures.RequireDevice(device);
        if (VisionFixtures.Frames is not { } folder)
        {
            Assert.Skip("Set REXPLAYER_VISION_FRAMES to a folder of snapshots with a clicks.txt.");
            return;
        }

        using var segmenter = PromptSegmenter.Open(VisionFixtures.Store, device)!;
        using var inpainter = Inpainter.Open(VisionFixtures.Store, device)!;
        var encodes = new FrameBudget();
        var decodes = new FrameBudget();
        var fills = new FrameBudget();
        var index = 0;
        foreach (var (file, x, y) in VisionFixtures.Clicks(folder, "clicks.txt"))
        {
            index++;
            var picture = VisionFixtures.Load(file);
            var key = new PictureKey(0, StringComparer.Ordinal.GetHashCode(file));
            var mask = segmenter.Segment(picture.View, key, SegmentPrompt.At(x, y));
            if (segmenter.LastEncode > TimeSpan.Zero)
            {
                encodes.Record(segmenter.LastEncode);
            }

            decodes.Record(segmenter.LastDecode);
            var output = new byte[picture.Pixels.Length];
            inpainter.Fill(picture.View, mask, output);
            fills.Record(inpainter.LastFill);

            var name = $"{Path.GetFileNameWithoutExtension(file)}-{index}-{device}";
            VisionFixtures.Save(VisionFixtures.Overlay(picture, mask), name + "-mask.png");
            VisionFixtures.Save(new Picture(output, picture.Width, picture.Height), name + "-fill.png");
            VisionFixtures.Note(string.Create(CultureInfo.InvariantCulture,
                $"{device} {name}: {picture.Width}x{picture.Height} encode {segmenter.LastEncode.TotalMilliseconds:0} ms, decode {segmenter.LastDecode.TotalMilliseconds:0} ms, fill {inpainter.LastFill.TotalMilliseconds:0} ms, score {mask.Score:0.00}, area {mask.Area}, box {mask.Bounds}"));
            Assert.True(mask.Area > 0, $"Nothing was selected at {x},{y} in {name}.");
        }

        VisionFixtures.Note(string.Create(CultureInfo.InvariantCulture,
            $"{device} budget at 24 fps: encoder {encodes.Decide(24)} ({encodes.Slow:0} ms), decoder {decodes.Decide(24)} ({decodes.Slow:0} ms), fill {fills.Decide(24)} ({fills.Slow:0} ms)"));
    }

    [Theory]
    [MemberData(nameof(VisionFixtures.Devices), MemberType = typeof(VisionFixtures))]
    public void TheTrackerAnswersEveryPictureAndExplainsEveryLoss(ExecutionDevice device)
    {
        VisionFixtures.RequireInstalled(ModelCatalogue.EfficientSamTiny);
        VisionFixtures.RequireDevice(device);
        if (VisionFixtures.Frames is not { } folder || !File.Exists(Path.Combine(folder, "sequence.txt")))
        {
            Assert.Skip("Set REXPLAYER_VISION_FRAMES to a folder of snapshots with a sequence.txt.");
            return;
        }

        var lines = File.ReadAllLines(Path.Combine(folder, "sequence.txt")).Where(line => line.Length > 0).ToArray();
        var (first, x, y) = VisionFixtures.Clicks(folder, "sequence.txt")[0];
        using var segmenter = PromptSegmenter.Open(VisionFixtures.Store, device)!;
        var tracker = new PromptedMaskTracker(segmenter);
        var start = VisionFixtures.Load(first);
        tracker.Start(segmenter.Segment(start.View, new PictureKey(0, 0), SegmentPrompt.At(x, y)));

        var answers = 0;
        for (var i = 1; i < lines.Length; i++)
        {
            var picture = VisionFixtures.Load(Path.Combine(folder, lines[i].Trim()));
            var watch = Stopwatch.StartNew();
            var result = tracker.Track(picture.View, new PictureKey(0, i));
            var elapsed = watch.Elapsed;
            answers++;
            Assert.True(result.Outcome == TrackOutcome.Tracked ? result.Mask is { Area: > 0 } : result.Reason.Length > 0);
            if (result.Mask is { } mask)
            {
                VisionFixtures.Save(VisionFixtures.Overlay(picture, mask), $"track-{i}-{device}.png");
            }

            VisionFixtures.Note(string.Create(CultureInfo.InvariantCulture,
                $"{device} track {lines[i].Trim()}: {result.Outcome} {result.Reason} in {elapsed.TotalMilliseconds:0} ms, box {result.Mask?.Bounds}"));
        }

        // How often the subject is kept is measured in timings.txt, not asserted: the on-demand
        // tracker is allowed to give up, never to follow something else silently.
        Assert.Equal(lines.Length - 1, answers);
    }

    [Fact]
    public async Task TheModelsDownloadFromTheAuthorsPagesAndVerify()
    {
        if (Environment.GetEnvironmentVariable("REXPLAYER_VISION_DOWNLOAD") != "1")
        {
            Assert.Skip("Set REXPLAYER_VISION_DOWNLOAD=1 to download the models (about 70 MB) into the store.");
            return;
        }

        using var http = new HttpClient();
        var downloader = new ModelDownloader(http, VisionFixtures.Store, root => new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace);
        var set = new ModelSet(VisionFixtures.Store, ModelCatalogue.SubjectLock);

        await set.DownloadAsync(downloader, TestContext.Current.CancellationToken);

        Assert.True(set.Ready, set.View.Status);
    }

    private static Picture Disc(int width, int height, int centreX, int centreY, int radius)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = (y * width * 4) + (x * 4);
                var inside = ((x - centreX) * (x - centreX)) + ((y - centreY) * (y - centreY)) <= radius * radius;
                (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]) = inside ? ((byte)40, (byte)40, (byte)220, (byte)255) : ((byte)170, (byte)180, (byte)175, (byte)255);
            }
        }

        return new Picture(pixels, width, height);
    }

    private static Picture Gradient(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = (y * width * 4) + (x * 4);
                (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]) = ((byte)(255 - (x * 255 / width)), (byte)(y * 255 / height), (byte)(x * 255 / width), (byte)255);
            }
        }

        return new Picture(pixels, width, height);
    }

    /// <summary>Intersection over union of a mask and the disc it should outline.</summary>
    private static double Overlap(PictureMask mask, Picture picture, int centreX, int centreY, int radius)
    {
        int both = 0, either = 0;
        for (var y = 0; y < picture.Height; y++)
        {
            for (var x = 0; x < picture.Width; x++)
            {
                var inside = ((x - centreX) * (x - centreX)) + ((y - centreY) * (y - centreY)) <= radius * radius;
                var masked = mask.Contains(x, y);
                both += inside && masked ? 1 : 0;
                either += inside || masked ? 1 : 0;
            }
        }

        return (double)both / either;
    }
}
