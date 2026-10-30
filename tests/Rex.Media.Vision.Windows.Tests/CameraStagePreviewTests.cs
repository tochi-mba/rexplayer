using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Visuals;
using Rex.Media.AppCore.Visuals.Stage;
using Rex.Media.Video.D3D11;
using Rex.Media.Vision.WindowsML;

namespace Rex.Media.Vision.Windows.Tests;

/// <summary>
/// Renders each camera effect for a few seconds on a real camera frame (<c>REXPLAYER_VISION_FRAMES</c>,
/// <c>person-*.png</c>) with the camera pack's real matte, two hands circling and a body, to music
/// with a kick on every beat, on this computer's graphics card, and saves frames to judge by eye
/// (ADR-0022: every effect is rendered and tuned before it ships). Skipped without the pack or frames.
/// </summary>
public sealed class CameraStagePreviewTests
{
    private const int Width = 960;
    private const int Height = 540;
    private const int Rate = 48_000;
    private const int Fps = 30;

    public static TheoryData<string> Effects() => new("ink", "neon", "echo");

    private static CameraEffect Make(string name) => name switch
    {
        "ink" => new InkBodyEffect(),
        "neon" => new NeonSkeletonEffect(),
        _ => new EchoClonesEffect(),
    };

    [Theory]
    [MemberData(nameof(Effects))]
    public void EachEffectIsRenderedOnARealFrameToJudgeByEye(string name)
    {
        foreach (var model in ModelCatalogue.CameraPack)
        {
            VisionFixtures.RequireInstalled(model);
        }

        var frames = VisionFixtures.Frames is { } folder ? Directory.GetFiles(folder, "person-*.png") : [];
        if (frames.Length == 0)
        {
            Assert.Skip("No person-*.png frames in REXPLAYER_VISION_FRAMES.");
        }

        var picture = VisionFixtures.Load(frames[0]);
        using var perceiver = ScenePerceiver.Open(VisionFixtures.Store)!;
        perceiver.Plan.Level = PerceptionLevel.Full;
        var scene = new SceneSense();
        perceiver.Perceive(picture.View, true, scene, 1);
        perceiver.Perceive(picture.View, true, scene, 2);

        using var gpu = D3D11StageGpu.Offscreen(Width, Height, software: false);
        using var stage = new CameraStage(gpu);
        var effect = Make(name);
        var camera = new VisualPicture(picture.Pixels, picture.Width, picture.Height);
        var pulse = new MusicPulse();
        var music = Kicks(seconds: 4);
        Span<Rgb> palette = stackalloc Rgb[4];
        for (var i = 0; i < 4; i++)
        {
            palette[i] = Rgb.From(Visualizers.PaletteColor(VisualPalette.Neon, i / 3.0, 0, new Argb(255, 96, 205, 255), 0));
        }

        var mono = new float[MusicPulse.AnalysisSize];
        var pixels = new byte[Width * Height * 4];
        for (var frame = 0; frame < Fps * 3; frame++)
        {
            var seconds = (double)frame / Fps;
            Circle(scene, seconds, frame + 3);
            var end = (int)(seconds * Rate) + MusicPulse.AnalysisSize;
            music.AsSpan(end - MusicPulse.AnalysisSize, MusicPulse.AnalysisSize).CopyTo(mono);
            pulse.Update(mono, Rate, TimeSpan.FromSeconds(1.0 / Fps));
            stage.Draw(effect, camera, 1, scene, pulse, palette, seconds, 1.0 / Fps, mirror: true);
            if (frame is 30 or 60 or 89)
            {
                gpu.ReadBack(pixels, Width * 4);
                VisionFixtures.Save(new Picture((byte[])pixels.Clone(), Width, Height), $"stage-{name}-{frame}.png");
            }
        }

        // Something was drawn: not black, not blown out.
        var average = pixels.Where((_, i) => i % 4 != 3).Average(value => value);
        Assert.InRange(average, 4, 200);
        stage.Dispose();
    }

    /// <summary>Two open hands circling either side of the listener, and a body under them, so every part of an effect has something to follow.</summary>
    private static void Circle(SceneSense scene, double seconds, long timestamp)
    {
        for (var h = 0; h < 2; h++)
        {
            var angle = (seconds * 2.4) + (h * Math.PI);
            var (x, y) = ((float)(0.5 + ((h == 0 ? -0.25 : 0.25) + (0.08 * Math.Cos(angle)))), (float)(0.45 + (0.12 * Math.Sin(angle))));
            var hand = scene.Hands[h];
            hand.Present = true;
            hand.Confidence = 0.9f;
            for (var i = 0; i < HandSense.Points; i++)
            {
                // A fan of five fingers above the palm.
                var finger = i == 0 ? -1 : (i - 1) / 4;
                var joint = i == 0 ? 0 : ((i - 1) % 4) + 1;
                var spread = (finger - 2) * 0.35f;
                var reach = joint * 0.025f;
                hand.Landmarks[i] = new Landmark(x + (MathF.Sin(spread) * reach), y + 0.06f - (MathF.Cos(spread) * reach), 0, 0.9f);
            }
        }

        var body = scene.Body;
        body.Present = true;
        void Put(int point, float px, float py) => body.Landmarks[point] = new Landmark(px, py, 0, 0.9f);
        Put(BodySense.Nose, 0.5f, 0.3f);
        Put(BodySense.LeftShoulder, 0.38f, 0.55f);
        Put(BodySense.RightShoulder, 0.62f, 0.55f);
        Put(BodySense.LeftElbow, 0.3f, 0.6f);
        Put(BodySense.RightElbow, 0.7f, 0.6f);
        Put(BodySense.LeftWrist, scene.Hands[0].Landmarks[0].X, scene.Hands[0].Landmarks[0].Y);
        Put(BodySense.RightWrist, scene.Hands[1].Landmarks[0].X, scene.Hands[1].Landmarks[0].Y);
        Put(BodySense.LeftHip, 0.42f, 0.95f);
        Put(BodySense.RightHip, 0.58f, 0.95f);
        scene.Timestamp = timestamp;
    }

    /// <summary>A kick every half second (120 beats a minute) over a held bass, mono.</summary>
    private static float[] Kicks(double seconds)
    {
        var samples = new float[(int)(seconds * Rate) + MusicPulse.AnalysisSize];
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (double)i / Rate;
            var sinceBeat = t % 0.5;
            var kick = Math.Exp(-sinceBeat * 18) * Math.Sin(Math.Tau * (50 + (90 * Math.Exp(-sinceBeat * 30))) * sinceBeat);
            var hats = Math.Exp(-((t + 0.25) % 0.5) * 60) * (Random.Shared.NextDouble() - 0.5) * 0.3;
            samples[i] = (float)((0.8 * kick) + hats + (0.15 * Math.Sin(Math.Tau * 55 * t)));
        }

        return samples;
    }
}
