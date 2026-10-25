using System.Diagnostics;
using Rex.Media.AppCore.Visuals;
using Rex.Media.Interop.Graphics;
using Rex.Media.Library;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.TestKit;
using Rex.Media.Video;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests.Perf;

/// <summary>
/// What people feel, measured part by part at full HD: each software visualisation's frame, each
/// picture effect's time on the graphics card (timestamp queries), the conversion of a decoded
/// picture to BGRA for Subject Lock, Subject Lock's tracking, and a large library. Each item is
/// reported on its own; the totals are gated against the lane's baselines (lower is better for a
/// cost, higher for a rate). Every figure is the fastest of several passes after a warm-up, because
/// noise only ever slows a pass down.
/// </summary>
[Trait("Category", "Perf")]
[Collection("Performance measurements")]
[System.Runtime.Versioning.SupportedOSPlatform("windows8.0")]
public sealed class ExperiencePerfTests
{
    private const int Width = 1920;
    private const int Height = 1080;

    /// <summary>The fastest of <paramref name="passes"/> runs of <paramref name="pass"/>, in milliseconds, after <paramref name="warmUp"/> unmeasured ones.</summary>
    private static double Fastest(Action pass, int warmUp = 3, int passes = 8)
    {
        for (var i = 0; i < warmUp; i++)
        {
            pass();
        }

        var fastest = double.MaxValue;
        for (var i = 0; i < passes; i++)
        {
            var started = Stopwatch.GetTimestamp();
            pass();
            fastest = Math.Min(fastest, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        return fastest;
    }

    /// <summary>A sound that has a beat and a melody, so every scene has something to move to.</summary>
    private static (float[] Left, float[] Right) Music(int frame)
    {
        var left = new float[VisualStage.SoundSize];
        var right = new float[VisualStage.SoundSize];
        for (var i = 0; i < left.Length; i++)
        {
            var t = ((frame * 1600) + i) / 48_000.0;
            var kick = (t % 0.5) < 0.05 ? Math.Sin(2 * Math.PI * 60 * t) : 0;
            var tone = 0.3 * Math.Sin(2 * Math.PI * (440 + (110 * Math.Sin(t))) * t);
            left[i] = (float)(kick + tone);
            right[i] = (float)((kick * 0.8) + tone);
        }

        return (left, right);
    }

    [Fact]
    public void EachVisualisationDrawsAFullHdFrame()
    {
        var options = new Dictionary<string, string>();
        var sound = Enumerable.Range(0, 8).Select(Music).ToArray();
        double total = 0;
        foreach (var choice in Enum.GetValues<VisualizerChoice>().Where(VisualScene.Draws))
        {
            var stage = new VisualStage(Width, Height, new Random(7));
            var frame = 0;
            var ms = Fastest(() =>
            {
                var (left, right) = sound[frame++ % sound.Length];
                stage.Draw(choice, options, left, right, 48_000, TimeSpan.FromMilliseconds(33));
            }, warmUp: 5, passes: 10);
            PerfBaselines.Report($"visual-1080p-ms-{choice}", ms);
            total += ms;
        }

        PerfBaselines.AtMost("visuals-1080p-total-ms", total);
    }

    [Fact]
    public void EachPictureEffectOnTheGraphicsCardAtFullHd()
    {
        D3D11VideoRenderer.CompiledShaders = new CompiledShaderCache(RepoPaths.Combine("artifacts", "perf", "shaders")).GetOrCompile;
        var effects = Enum.GetValues<VideoEffect>().Where(effect => effect != VideoEffect.Off).ToList();
        D3D11VideoRenderer.PrepareEffects(effects.Select(effect => (int)effect).Prepend(0));
        // The weak-computer imitation (scripts/measure-startup.ps1 -LowEnd) draws in software, as the player then does.
        using var renderer = D3D11VideoRenderer.Create(software: string.Equals(Environment.GetEnvironmentVariable("REXPLAYER_GRAPHICS"), "software", StringComparison.OrdinalIgnoreCase));
        renderer.UseOffscreen(Width, Height);
        var luma = new byte[Width * Height];
        var chroma = new byte[Width * Height / 2];
        for (var i = 0; i < luma.Length; i++)
        {
            luma[i] = (byte)((i % Width) ^ (i / Width));
        }

        Array.Fill(chroma, (byte)128);
        var matrix = YuvTransform.For(new ColorInfo(ColorMatrix.Bt709, ColorTransfer.Bt709, ColorPrimaries.Bt709, false), 8).ForShader(255);
        double? Time(int effect, float seconds)
        {
            renderer.Upload(VideoPlaneFormat.Nv12, Width, Height, luma, Width, chroma, Width);
            return renderer.TimeOnGpu(() => renderer.Draw(matrix, 0, 0, Width, Height, (0, 0, 1, 1), true, effect: effect, seconds: seconds));
        }

        var plain = Enumerable.Range(0, 8).Select(i => Time(0, i / 30f)).Where(ms => ms is not null).Min() ?? 0;
        PerfBaselines.Report("effect-1080p-gpu-ms-Off", plain);
        double total = 0;
        foreach (var effect in effects)
        {
            renderer.EnableMotionHistory(VideoEffects.UsesHistory(effect, 65, 50, 50));
            var ms = Enumerable.Range(0, 8).Select(i => Time((int)effect, i / 30f)).Where(ms => ms is not null).Min() ?? 0;
            PerfBaselines.Report($"effect-1080p-gpu-ms-{effect}", ms);
            total += ms;
        }

        PerfBaselines.AtMost("effects-1080p-gpu-total-ms", total);
    }

    /// <summary>A full-HD NV12 picture of soft gradients and texture.</summary>
    private static VideoFrame Nv12Picture()
    {
        var frame = VideoFrame.Rent(PixelFormat.Nv12, Width, Height);
        for (var y = 0; y < Height; y++)
        {
            var row = frame.Row(0, y);
            for (var x = 0; x < Width; x++)
            {
                row[x] = (byte)(((x + y) / 8) + ((x * y) % 17));
            }
        }

        for (var y = 0; y < Height / 2; y++)
        {
            var row = frame.Row(1, y);
            for (var x = 0; x < Width; x++)
            {
                row[x] = (byte)(96 + ((x + y) % 64));
            }
        }

        frame.Color = new ColorInfo(ColorMatrix.Bt709, ColorTransfer.Bt709, ColorPrimaries.Bt709, false);
        return frame;
    }

    [Fact]
    public void ADecodedPictureBecomesBgraQuickly()
    {
        using var picture = Nv12Picture();

        PerfBaselines.AtMost("nv12-to-bgra-1080p-ms", Fastest(() => ColorConverter.ToBgra(picture).Dispose()));
    }

    /// <summary>A textured square moving across a textured background, as BGRA pictures one frame apart.</summary>
    private static VideoFrame Scene(int frame)
    {
        var picture = VideoFrame.Rent(PixelFormat.Bgra32, Width, Height);
        var (left, top) = (400 + (frame * 6), 300 + (frame * 2));
        for (var y = 0; y < Height; y++)
        {
            var row = picture.Row(0, y);
            for (var x = 0; x < Width; x++)
            {
                var inside = x >= left && x < left + 240 && y >= top && y < top + 240;
                var value = inside ? (byte)(((x - left) * 7) ^ ((y - top) * 13)) : (byte)(60 + (((x / 32) + (y / 32)) % 2 * 40));
                row[(x * 4) + 0] = value;
                row[(x * 4) + 1] = inside ? (byte)200 : value;
                row[(x * 4) + 2] = inside ? (byte)(255 - value) : value;
                row[(x * 4) + 3] = 255;
            }
        }

        picture.Pts = MediaTime.FromMilliseconds(frame * 40L);
        picture.Generation = 1;
        return picture;
    }

    [Fact]
    public void SubjectLockFollowsAFullHdPicture()
    {
        var frames = Enumerable.Range(0, 40).Select(Scene).ToArray();
        try
        {
            using var session = new SubjectEditSession();
            session.Select(400f / Width, 300f / Height, 240f / Width, 240f / Height);
            var next = 0;
            var ms = Fastest(() => session.Process(frames[next++])?.Dispose(), warmUp: 8, passes: 30);

            Assert.True(session.Locked, session.Status);
            PerfBaselines.Report("subject-lock-1080p-track-ms", ms);
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    [Fact]
    public void ALargeLibraryIsScannedAndReadBackQuickly()
    {
        const int Files = 20_000;
        var folder = Path.Combine(Path.GetTempPath(), "rexplayer-perf-library-" + Guid.NewGuid().ToString("N"));
        var storePath = Path.Combine(folder, "library.rex");
        Directory.CreateDirectory(folder);
        try
        {
            var files = Enumerable.Range(0, Files)
                .Select(i => new LibraryFile(Path.Combine(folder, $"Artist {i / 200}", $"Album {i / 20}", $"{i % 20:00} Song {i}.mp3"), 4_000_000 + i, new DateTime(2026, 1, 1).AddMinutes(i)))
                .ToList();
            var library = new MediaLibrary(RexStore.Open(storePath));
            library.AddFolder(folder);
            var started = Stopwatch.GetTimestamp();
            library.Scan(folder, files, _ => LibraryKind.Music);
            var scanned = Stopwatch.GetElapsedTime(started).TotalSeconds;

            started = Stopwatch.GetTimestamp();
            var reopened = new MediaLibrary(RexStore.Open(storePath));
            var read = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            Assert.Equal(Files, reopened.Entries.Count);
            PerfBaselines.AtLeast("library-scan-files-per-second", Files / scanned);
            PerfBaselines.AtMost("library-20k-open-ms", read);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
