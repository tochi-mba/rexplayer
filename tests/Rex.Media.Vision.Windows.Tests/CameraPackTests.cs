using System.Diagnostics;
using System.Globalization;
using Rex.Media.Vision.WindowsML;

namespace Rex.Media.Vision.Windows.Tests;

/// <summary>
/// The camera pack (ADR-0022) run for real: skipped when it is not installed. A synthetic picture
/// proves every session opens and runs with the tensors the pure code builds; frames in
/// <c>REXPLAYER_VISION_FRAMES</c> named <c>person-*.png</c> must show a person, and their mattes and
/// points are written beside the other outputs to judge by eye. Timings go to timings.txt.
/// </summary>
public sealed class CameraPackTests
{
    private static void RequirePack()
    {
        foreach (var model in ModelCatalogue.CameraPack)
        {
            VisionFixtures.RequireInstalled(model);
        }
    }

    [Fact]
    public void NothingOpensWithoutThePack()
    {
        var empty = new ModelStore(Path.Combine(Path.GetTempPath(), "rexplayer-no-models-" + Guid.NewGuid().ToString("N")));
        Assert.Null(ScenePerceiver.Open(empty));
    }

    [Theory]
    [MemberData(nameof(VisionFixtures.Devices), MemberType = typeof(VisionFixtures))]
    public void EverySessionRunsOnACameraSizedPictureAndIsTimed(ExecutionDevice device)
    {
        RequirePack();
        VisionFixtures.RequireDevice(device);
        using var perceiver = ScenePerceiver.Open(VisionFixtures.Store, device)!;
        perceiver.Plan.Level = PerceptionLevel.Full;
        var picture = new byte[640 * 480 * 4];
        new Random(3).NextBytes(picture);
        var scene = new SceneSense();
        // The first pictures pay for loading and, on DirectML, compiling every model: left out of the timing.
        for (var i = 0; i < 4; i++)
        {
            perceiver.Perceive(new BgraPicture(picture, 640, 480, 640 * 4), true, scene, Stopwatch.GetTimestamp());
        }

        var times = new List<double>();
        for (var i = 0; i < 24; i++)
        {
            var started = Stopwatch.GetTimestamp();
            perceiver.Perceive(new BgraPicture(picture, 640, 480, 640 * 4), true, scene, Stopwatch.GetTimestamp());
            times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        times.Sort();
        var each = times[times.Count / 2];
        Assert.Equal(SenseSource.Model, scene.Source);
        Assert.All(scene.Matte, value => Assert.InRange(value, 0, 1));
        Assert.True(perceiver.Plan.MatteBudget.Samples > 0);
        VisionFixtures.Note(string.Create(CultureInfo.InvariantCulture,
            $"Camera pack on {perceiver.Device}: median {each:0.0} ms, p90 {times[(int)(times.Count * 0.9)]:0.0} ms a picture after warm-up (matte p90 {perceiver.Plan.MatteBudget.Slow:0.0}, palm {perceiver.Plan.PalmBudget.Slow:0.0}, person {perceiver.Plan.PersonBudget.Slow:0.0}, hands {perceiver.Plan.HandBudget.Slow:0.0}, pose {perceiver.Plan.PoseBudget.Slow:0.0} ms)"));
    }

    [Fact]
    public void APersonInARealFrameIsMattedAndPlaced()
    {
        RequirePack();
        var frames = VisionFixtures.Frames is { } folder ? Directory.GetFiles(folder, "person-*.png") : [];
        if (frames.Length == 0)
        {
            Assert.Skip("No person-*.png frames in REXPLAYER_VISION_FRAMES.");
        }

        using var perceiver = ScenePerceiver.Open(VisionFixtures.Store)!;
        perceiver.Plan.Level = PerceptionLevel.Full;
        foreach (var path in frames)
        {
            var picture = VisionFixtures.Load(path);
            var scene = new SceneSense();
            // Twice: the second picture runs the hand and pose models in the windows the detectors placed.
            perceiver.Perceive(picture.View, false, scene, 1);
            perceiver.Perceive(picture.View, false, scene, 2);

            Assert.True(scene.Presence > 0.05f, $"{Path.GetFileName(path)}: the matte holds {scene.Presence:0.00} of the picture.");
            var matte = new byte[scene.Width * scene.Height * 4];
            for (var i = 0; i < scene.Matte.Length; i++)
            {
                var value = (byte)(scene.Matte[i] * 255);
                (matte[i * 4], matte[(i * 4) + 1], matte[(i * 4) + 2], matte[(i * 4) + 3]) = (value, value, value, 255);
            }

            VisionFixtures.Save(new Picture(matte, scene.Width, scene.Height), Path.GetFileNameWithoutExtension(path) + "-matte.png");
            VisionFixtures.Note($"{Path.GetFileName(path)}: presence {scene.Presence:0.00}, body {scene.Body.Present}, hands {scene.Hands.Count(hand => hand.Present)}");
        }
    }
}
