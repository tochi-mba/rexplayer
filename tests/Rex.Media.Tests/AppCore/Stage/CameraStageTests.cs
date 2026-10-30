using Rex.Media.AppCore.Visuals;
using Rex.Media.AppCore.Visuals.Stage;
using Rex.Media.Tests.Vision;
using Rex.Media.Video;
using Rex.Media.Vision;

namespace Rex.Media.Tests.AppCore.Stage;

public sealed class CameraStageTests
{
    private readonly RecordingStageGpu _gpu = new();
    private readonly SceneSense _scene = new() { Timestamp = 1 };
    private readonly MusicPulse _pulse = new();
    private readonly VisualPicture _camera = new(new byte[640 * 480 * 4], 640, 480);
    private static readonly Rgb[] Palette = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(1, 1, 1)];

    private void Draw(CameraStage stage, CameraEffect effect, long cameraStamp = 1, double dt = 1 / 30.0, bool mirror = true) =>
        stage.Draw(effect, _camera, cameraStamp, _scene, _pulse, Palette, 12.5, dt, mirror);

    private IEnumerable<string> Programs() => _gpu.Draws.Select(draw => draw.Program);

    [Fact]
    public void TheInkSimulatesThenTheLightIsBloomedAndToned()
    {
        using var stage = new CameraStage(_gpu);
        Draw(stage, new InkBodyEffect());

        string[] expected =
        [
            "ink-advect-velocity", "ink-divergence", "clear",
            .. Enumerable.Repeat("ink-jacobi", InkBodyEffect.Iterations),
            "ink-project", "ink-advect-ink", "ink-compose",
            "bright", "down", "down", "down", "up", "up", "up", "finish",
        ];
        Assert.Equal(expected, Programs());
        var finish = _gpu.Draws[^1];
        Assert.Equal(0, finish.Target);
        Assert.Equal([stage.Light.Id, _gpu.Draws[^8].Target], finish.Inputs);
        Assert.All(_gpu.Draws.Where(draw => draw.Program == "up"), draw => Assert.Equal(StageBlend.Add, draw.Blend));
        Assert.Equal((1.1f, 1f), (finish.Constants[64], finish.Constants[65]));
        // The bloom halves four times; the light is the stage's size.
        Assert.Equal((1920, 1080, StageFormat.Half4), (stage.Light.Width, stage.Light.Height, stage.Light.Format));
        Assert.Contains(_gpu.Made, texture => (texture.Width, texture.Height) == (120, 67));
        // The simulation is a quarter of the stage, the ink a half.
        var sim = _gpu.Draws[0].Constants;
        Assert.Equal((480f, 270f), (sim[66], sim[67]));
        Assert.Equal(1f / 480, sim[64], 6);
    }

    [Fact]
    public void ThePictureAndTheSceneAreUploadedOnlyWhenNew()
    {
        using var stage = new CameraStage(_gpu);
        var effect = new EchoClonesEffect();
        Draw(stage, effect, cameraStamp: 1);
        Draw(stage, effect, cameraStamp: 1);
        Assert.Single(_gpu.Uploads, upload => upload.Texture.Id == stage.Camera.Id);
        Assert.Single(_gpu.Uploads, upload => upload.Texture.Id == stage.Matte.Id);

        _scene.Timestamp = 2;
        Draw(stage, effect, cameraStamp: 2);
        Assert.Equal(2, _gpu.Uploads.Count(upload => upload.Texture.Id == stage.Camera.Id));
        Assert.Equal(2, _gpu.Uploads.Count(upload => upload.Texture.Id == stage.Points.Id));
        var flow = _gpu.Uploads.Last(upload => upload.Texture.Id == stage.Flow.Id);
        Assert.Equal((_scene.FlowX.Length * 2, _scene.FlowWidth * 2), (flow.Length, flow.Row));
        Assert.Equal((640 * 480 * 4, 640 * 4), (_gpu.Uploads.Last(upload => upload.Texture.Id == stage.Camera.Id).Length, 640 * 4));
    }

    [Fact]
    public void ANewPictureSizeMakesANewCameraTextureAndNoPictureKeepsTheLast()
    {
        using var stage = new CameraStage(_gpu);
        var effect = new EchoClonesEffect();
        Draw(stage, effect);
        var first = stage.Camera;
        stage.Draw(effect, new VisualPicture(new byte[320 * 240 * 4], 320, 240), 2, _scene, _pulse, Palette, 0, 0.03, true);
        Assert.NotEqual(first.Id, stage.Camera.Id);
        Assert.Contains(first, _gpu.Released);
        Assert.Equal((320, 240), (stage.Camera.Width, stage.Camera.Height));

        stage.Draw(effect, null, 3, _scene, _pulse, Palette, 0, 0.03, true);
        Assert.Equal((320, 240), (stage.Camera.Width, stage.Camera.Height));
    }

    [Fact]
    public void TheSharedConstantsCarryTimeMusicFitAndPalette()
    {
        using var stage = new CameraStage(_gpu);
        _scene.Matte[0] = 1;
        Draw(stage, new EchoClonesEffect(), dt: 1.0, mirror: true);
        var c = _gpu.Draws[^1].Constants;

        Assert.Equal((12.5f, 0.25f, 1920f, 1080f), (c[0], c[1], c[2], c[3]));
        // A 640 by 480 picture fills a 1920 by 1080 stage: all of its width, three quarters of its height, centred.
        Assert.Equal((1f, 0.75f, 0f, 0.125f), (c[12], c[13], c[14], c[15]));
        Assert.Equal(1, c[16]);
        Assert.Equal(_scene.Presence, c[17]);
        Assert.Equal((1f, 0f, 0f), (c[20], c[21], c[22]));
        Assert.Equal((1f, 1f, 1f), (c[32], c[33], c[34]));
        Assert.Equal(1920f / 1080, c[52], 5);
        // No hands, no body: their rows say so, the body's middle falls back to the stage's middle.
        Assert.Equal((0f, 0f), (c[44], c[46]));
        Assert.Equal((0.5f, (0.5f - 0.125f) / 0.75f, 0f), (c[48], c[49], c[51]));
    }

    [Fact]
    public void AMovingFingertipCarriesItsVelocity()
    {
        using var stage = new CameraStage(_gpu);
        var effect = new EchoClonesEffect();
        SceneSenseTests.PlaceHand(_scene.Hands[1], 0.5f, 0.5f, 0.1f, open: true, pinching: true);
        Draw(stage, effect, dt: 0.1);
        var first = _gpu.Draws[^1].Constants;
        Assert.Equal((0f, 0f), (first[42], first[43]));
        Assert.Equal(1, first[46]);
        Assert.True(first[47] > 0.9f, "a pinching hand's pinch is near 1");

        _scene.Hands[1].Landmarks[HandSense.IndexTip] = _scene.Hands[1].Landmarks[HandSense.IndexTip] with { X = _scene.Hands[1].Landmarks[HandSense.IndexTip].X + 0.1f };
        _scene.Timestamp = 2;
        Draw(stage, effect, dt: 0.1);
        var second = _gpu.Draws[^1].Constants;
        // A tenth of the picture's width in a tenth of a second is one stage width a second, half of it after smoothing.
        Assert.Equal(0.5f, second[42], 3);
        Assert.Equal(0, second[43], 3);

        _scene.Hands[1].Clear();
        _scene.Timestamp = 3;
        Draw(stage, effect, dt: 0.1);
        var gone = _gpu.Draws[^1].Constants;
        Assert.Equal((0f, 0f, 0f, 0f), (gone[40], gone[41], gone[42], gone[46]));
    }

    [Fact]
    public void ABodyIsPlacedOnTheStageByItsShoulders()
    {
        using var stage = new CameraStage(_gpu);
        _scene.Body.Present = true;
        _scene.Body.Landmarks[BodySense.LeftShoulder] = new Landmark(0.4f, 0.5f, 0, 1);
        _scene.Body.Landmarks[BodySense.RightShoulder] = new Landmark(0.6f, 0.5f, 0, 1);
        Draw(stage, new EchoClonesEffect());
        var c = _gpu.Draws[^1].Constants;
        Assert.Equal((0.5f, 0.5f, 1f), (c[48], c[49], c[51]));
        Assert.Equal(0.2f, c[50], 4);
    }

    [Fact]
    public void ThePointsArePackedHandsThenBodyWithNoConfidenceForWhatWasNotSeen()
    {
        var gpu = new CapturingGpu();
        using var stage = new CameraStage(gpu);
        SceneSenseTests.PlaceHand(_scene.Hands[0], 0.3f, 0.4f, 0.1f, open: true);
        stage.Draw(new EchoClonesEffect(), _camera, 1, _scene, _pulse, Palette, 0, 0.03, false);

        var points = gpu.Uploaded;
        Assert.Equal(CameraStage.PointCount * 4, points.Length);
        Assert.Equal((0.3f, 0.4f), (points[0], points[1]));
        Assert.True(points[3] > 0);
        Assert.Equal(0, points[(HandSense.Points * 4) + 3]);
        Assert.Equal(0, points[(HandSense.Points * 2 * 4) + 3]);
    }

    /// <summary>Keeps the last float upload to the points texture, which the recording fake only counts.</summary>
    private sealed class CapturingGpu : IStageGpu
    {
        private readonly RecordingStageGpu _inner = new();

        public float[] Uploaded { get; private set; } = [];

        public int Width => _inner.Width;

        public int Height => _inner.Height;

        public StageTexture Texture(int width, int height, StageFormat format) => _inner.Texture(width, height, format);

        public void Release(StageTexture texture) => _inner.Release(texture);

        public void Upload(StageTexture texture, ReadOnlySpan<byte> pixels, int rowBytes) => _inner.Upload(texture, pixels, rowBytes);

        public void Upload(StageTexture texture, ReadOnlySpan<float> values, int rowValues)
        {
            if (texture.Width == CameraStage.PointCount)
            {
                Uploaded = values.ToArray();
            }

            _inner.Upload(texture, values, rowValues);
        }

        public void Clear(StageTexture? target, float red, float green, float blue, float alpha) => _inner.Clear(target, red, green, blue, alpha);

        public void Pass(StageProgram program, StageTexture? target, ReadOnlySpan<StageTexture> inputs, ReadOnlySpan<float> constants, StageBlend blend = StageBlend.Replace) =>
            _inner.Pass(program, target, inputs, constants, blend);

        public void Points(StageProgram program, StageTexture? target, ReadOnlySpan<StageTexture> inputs, int count, ReadOnlySpan<float> constants, StageBlend blend = StageBlend.Add) =>
            _inner.Points(program, target, inputs, count, constants, blend);
    }

    [Fact]
    public void ResizingOrChangingEffectLetsTheOldTexturesGo()
    {
        var stage = new CameraStage(_gpu);
        var ink = new InkBodyEffect();
        Draw(stage, ink);
        var light = stage.Light;
        var made = _gpu.Made.Count;

        Draw(stage, ink);
        Assert.Equal(made, _gpu.Made.Count);

        var releasedBefore = _gpu.Released.Count;
        _gpu.Width = 1280;
        _gpu.Height = 720;
        Draw(stage, ink);
        Assert.Contains(light, _gpu.Released);
        Assert.Equal((1280, 720), (stage.Light.Width, stage.Light.Height));
        // The ink's own seven textures, the light and the four bloom levels were let go and made again at the new size.
        Assert.Equal(7 + 1 + 4, _gpu.Released.Count - releasedBefore);

        var neon = new NeonSkeletonEffect();
        Draw(stage, neon);
        var live = _gpu.Live.Count;
        Draw(stage, new EchoClonesEffect());
        Assert.True(_gpu.Live.Count > live - 2, "the neon's two trails went, the echoes' six came");

        stage.Dispose();
        Assert.Empty(_gpu.Live);
        stage.Dispose();
    }

    [Fact]
    public void TheSkeletonDrawsEveryBoneAndJointIntoItsTrails()
    {
        using var stage = new CameraStage(_gpu);
        Draw(stage, new NeonSkeletonEffect());

        Assert.Equal(["neon-fade", "neon-bones", "neon-joints", "neon-plasma", "neon-compose"], Programs().Take(5));
        var bones = _gpu.Draws[1];
        Assert.Equal(NeonSkeletonEffect.Bones.Count, bones.Count);
        Assert.Equal(66, bones.Count);
        Assert.Equal([stage.Points.Id], bones.Inputs);
        // The bones are listed two to a row from the effect's first row: the left hand's first bone, then its second.
        Assert.Equal((0f, 1f, 1f, 2f), (bones.Constants[64], bones.Constants[65], bones.Constants[66], bones.Constants[67]));
        var right = HandSense.Points;
        Assert.Equal((float)right, bones.Constants[64 + (NeonSkeletonEffect.HandBones.Count * 2)]);
        Assert.Equal(CameraStage.PointCount, _gpu.Draws[2].Count);
        Assert.Equal(StageBlend.Add, _gpu.Draws[3].Blend);
    }

    [Fact]
    public void TheSkeletonsTrailsAlternate()
    {
        using var stage = new CameraStage(_gpu);
        var neon = new NeonSkeletonEffect();
        Draw(stage, neon);
        var first = _gpu.Draws[1].Target;
        _gpu.Draws.Clear();
        Draw(stage, neon);
        Assert.Equal([first], _gpu.Draws[0].Inputs);
        Assert.NotEqual(first, _gpu.Draws[1].Target);
    }

    [Fact]
    public void TheEchoesStoreAnOutlineEveryTenthOfASecondNewestFirst()
    {
        using var stage = new CameraStage(_gpu);
        var echo = new EchoClonesEffect();
        Draw(stage, echo, dt: 0.04);
        // The first outline fills every echo.
        Assert.Equal(EchoClonesEffect.Echoes, Programs().Count(name => name == "echo-store"));
        var compose = _gpu.Draws.Single(draw => draw.Program == "echo-compose");
        Assert.Equal(8, compose.Inputs.Length);
        Assert.Equal((stage.Camera.Id, stage.Matte.Id), (compose.Inputs[6], compose.Inputs[7]));
        var newest = compose.Inputs[0];

        _gpu.Draws.Clear();
        Draw(stage, echo, dt: 0.04);
        Draw(stage, echo, dt: 0.04);
        Assert.DoesNotContain("echo-store", Programs());

        _gpu.Draws.Clear();
        Draw(stage, echo, dt: 0.04);
        var stored = _gpu.Draws.Single(draw => draw.Program == "echo-store");
        var after = _gpu.Draws.Single(draw => draw.Program == "echo-compose");
        Assert.Equal(stored.Target, after.Inputs[0]);
        Assert.Equal(newest, after.Inputs[1]);
        Assert.True(after.Constants[64] >= 0.035f);
    }

    [Fact]
    public void EveryProgramIsNamedOnceAndBuiltOnThePrelude()
    {
        CameraEffect[] effects = [new InkBodyEffect(), new NeonSkeletonEffect(), new EchoClonesEffect()];
        var programs = StageShaders.All.Concat(effects.SelectMany(effect => effect.Programs)).ToArray();
        Assert.Equal(programs.Length, programs.Select(program => program.Name).Distinct().Count());
        Assert.All(programs, program => Assert.StartsWith(StageShaders.Prelude, program.Source, StringComparison.Ordinal));
        Assert.All(programs, program => Assert.Contains("ps(", program.Source, StringComparison.Ordinal));
        Assert.All(programs.Where(program => program.Points), program => Assert.Contains("vsPoints(", program.Source, StringComparison.Ordinal));
        // HLSL keeps these words for itself; a variable named so fails to compile on the graphics card.
        Assert.All(programs, program => Assert.DoesNotMatch(@"\b(float\d?|Line|Spark)\s+(line|point|triangle|out|in)\b", program.Source));
        Assert.Equal(1f, new EchoClonesEffect().Exposure);
    }

    [Fact]
    public void BadInputsAreRefused()
    {
        using var stage = new CameraStage(_gpu);
        var echo = new EchoClonesEffect();
        Assert.Throws<ArgumentNullException>(() => new CameraStage(null!));
        Assert.Throws<ArgumentNullException>(() => stage.Draw(null!, _camera, 1, _scene, _pulse, Palette, 0, 0.03, true));
        Assert.Throws<ArgumentNullException>(() => stage.Draw(echo, _camera, 1, null!, _pulse, Palette, 0, 0.03, true));
        Assert.Throws<ArgumentNullException>(() => stage.Draw(echo, _camera, 1, _scene, null!, Palette, 0, 0.03, true));
        Assert.Throws<ArgumentException>(() => stage.Draw(echo, _camera, 1, _scene, _pulse, Palette.AsSpan(1), 0, 0.03, true));
        Draw(stage, echo);
        var smaller = new SceneSense(96, 72) { Timestamp = 9 };
        Assert.Throws<ArgumentException>(() => stage.Draw(echo, _camera, 1, smaller, _pulse, Palette, 0, 0.03, true));
        Assert.Throws<ArgumentNullException>(() => CameraStage.Drop(null!, ref Unsafe()));
        Assert.Throws<ArgumentNullException>(() => echo.Draw(null!));
        Assert.Throws<ArgumentNullException>(() => new InkBodyEffect().Draw(null!));
        Assert.Throws<ArgumentNullException>(() => new NeonSkeletonEffect().Draw(null!));
        // A bad frame time is taken as no time.
        stage.Draw(echo, _camera, 1, _scene, _pulse, Palette, 0, double.NaN, true);
        Assert.Equal(0, stage.Dt);
        new BareEffect().Release(_gpu);
        Assert.Equal(0.9f, new BareEffect().Bloom);
    }

    private static StageTexture _unused;

    private static ref StageTexture Unsafe() => ref _unused;

    private sealed class BareEffect : CameraEffect
    {
        public override IReadOnlyList<StageProgram> Programs => [];

        public override void Draw(CameraStage stage)
        {
        }
    }
}
