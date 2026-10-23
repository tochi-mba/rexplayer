using System.Globalization;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Visuals;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// Every visualisation drawn in software, played real music made for the test (AU-18): each must
/// light the stage without burning it out, move more with music than in silence, follow its colour
/// settings, and change when any one of its own settings changes.
/// </summary>
public sealed class VisualStageTests
{
    public static readonly TheoryData<VisualizerChoice> Scenes = new(
        VisualizerChoice.Vinyl,
        VisualizerChoice.Halo,
        VisualizerChoice.Mirror,
        VisualizerChoice.Aurora,
        VisualizerChoice.Embers,
        VisualizerChoice.Ripples,
        VisualizerChoice.Strobe,
        VisualizerChoice.Silhouette,
        VisualizerChoice.BeatEdit,
        VisualizerChoice.Resonance,
        VisualizerChoice.Groove,
        VisualizerChoice.Flight,
        VisualizerChoice.Cymatics,
        VisualizerChoice.Ridgelines,
        VisualizerChoice.Synthwave,
        VisualizerChoice.Fireworks,
        VisualizerChoice.Lightning,
        VisualizerChoice.Lava,
        VisualizerChoice.Constellation,
        VisualizerChoice.Galaxy,
        VisualizerChoice.Kaleidoscope,
        VisualizerChoice.Murmuration,
        VisualizerChoice.Ink,
        VisualizerChoice.Skyline,
        VisualizerChoice.Mandala,
        VisualizerChoice.Clones,
        VisualizerChoice.TimeWarp,
        VisualizerChoice.Aura,
        VisualizerChoice.LedWall,
        VisualizerChoice.Stardust,
        VisualizerChoice.HallOfMirrors,
        VisualizerChoice.LightPainting,
        VisualizerChoice.Scanner,
        VisualizerChoice.Shatter,
        VisualizerChoice.Spotlight,
        VisualizerChoice.InfinityMirror,
        VisualizerChoice.Water);

    public static readonly TheoryData<VisualizerChoice> CameraScenes = new(
        VisualizerChoice.Clones,
        VisualizerChoice.TimeWarp,
        VisualizerChoice.Aura,
        VisualizerChoice.LedWall,
        VisualizerChoice.Stardust,
        VisualizerChoice.HallOfMirrors,
        VisualizerChoice.LightPainting,
        VisualizerChoice.Scanner,
        VisualizerChoice.Shatter,
        VisualizerChoice.Spotlight,
        VisualizerChoice.InfinityMirror,
        VisualizerChoice.Water);

    [Theory]
    [MemberData(nameof(CameraScenes))]
    public void CameraVisualisationsShowTheWaitingAnimationUntilPicturesAndMasksArrive(VisualizerChoice choice)
    {
        var stage = Stage(48, 30);
        // An absent picture is valid during camera warm-up; the stage must still draw something.
        stage.Context.Camera = null;
        stage.Context.Mask = null;
        var waiting = Play(stage, choice, new SyntheticMusic(120, 1), frames: 8);
        Assert.True(Brightness(waiting[^1]) > 0, $"{choice} should show its waiting animation.");

        // A picture can become available before an outline; this is another waiting state.
        stage = Stage(48, 30);
        stage.Context.Mask = null;
        var withoutOutline = Play(stage, choice, new SyntheticMusic(120, 1), frames: 8);
        Assert.True(Brightness(withoutOutline[^1]) > 0, $"{choice} should tolerate an absent outline.");
    }

    [Fact]
    public void TheInfinityMirrorRecyclesItsOldestBeatWithoutGrowingUnbounded()
    {
        var stage = Stage(24, 16);
        // The mirror remembers sixteen beats. More than sixteen must exercise frame eviction.
        var frames = Play(stage, VisualizerChoice.InfinityMirror, new SyntheticMusic(120, 22));
        Assert.True(Brightness(frames[^1]) > 0);
    }

    private static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>();

    [Theory]
    [InlineData(1)]
    [InlineData(48000)]
    [InlineData(int.MaxValue)]
    public void NonFiniteAudioCannotPoisonTheStage(int sampleRate)
    {
        var stage = Stage();
        var samples = Enumerable.Repeat(float.NaN, VisualStage.SoundSize).ToArray();
        samples[0] = float.PositiveInfinity;
        samples[1] = float.NegativeInfinity;
        for (var i = 0; i < 5; i++)
        {
            Assert.True(stage.Draw(VisualizerChoice.Halo, Defaults, samples, samples, sampleRate, TimeSpan.FromMilliseconds(30)));
        }

        Assert.All(stage.Context.Left, value => Assert.Equal(0, value));
        Assert.All(stage.Context.Right, value => Assert.Equal(0, value));
        Assert.True(float.IsFinite(stage.Canvas.AverageLuma()));
        Assert.True(float.IsFinite(stage.Pulse.Snare.Level));
    }

    [Fact]
    [Capability("AU-18")]
    public void UnavailableAndOffModesResetWithoutLeavingASceneStuck()
    {
        var stage = Stage();
        float[] silence = new float[VisualStage.SoundSize];
        Assert.False(stage.Draw(VisualizerChoice.Off, Defaults, silence, silence, 48000, TimeSpan.FromSeconds(1d / 30)));
        Assert.True(stage.Draw(VisualizerChoice.Halo, Defaults, silence, silence, 48000, TimeSpan.FromSeconds(1d / 30)));
        Assert.False(stage.Draw(VisualizerChoice.Off, Defaults, silence, silence, 48000, TimeSpan.FromSeconds(1d / 30)));
        Assert.True(stage.Draw(VisualizerChoice.Mirror, Defaults, silence, silence, 48000, TimeSpan.FromSeconds(1d / 30)));
        Assert.True(float.IsFinite(stage.Canvas.AverageLuma()));
        stage.Reset();
        Assert.True(stage.Draw(VisualizerChoice.Halo, Defaults, silence, silence, 48000, TimeSpan.FromSeconds(1d / 30)));
    }

    [Theory]
    [Capability("AU-18")]
    [InlineData(VisualizerChoice.Halo)]
    [InlineData(VisualizerChoice.Mirror)]
    [InlineData(VisualizerChoice.Aurora)]
    [InlineData(VisualizerChoice.BeatEdit)]
    public void MonoAndStereoSourcesRenderWithinBoundsWithoutARequiredCamera(VisualizerChoice choice)
    {
        var left = new float[VisualStage.SoundSize];
        var right = new float[VisualStage.SoundSize];
        for (var i = 0; i < left.Length; i++)
        {
            left[i] = (float)(0.55 * Math.Sin(i * Math.PI / 19));
            right[i] = (float)(0.35 * Math.Sin(i * Math.PI / 31));
        }

        var mono = Stage(96, 54);
        mono.Context.Camera = null;
        mono.Context.Mask = null;
        Assert.True(mono.Draw(choice, Defaults, left, left, 48000, TimeSpan.FromSeconds(1d / 30)));
        Assert.Equal(mono.Context.Left, mono.Context.Right);
        Assert.True(float.IsFinite(mono.Canvas.AverageLuma()));

        var stereo = Stage(96, 54);
        stereo.Context.Camera = null;
        stereo.Context.Mask = null;
        Assert.True(stereo.Draw(choice, Defaults, left, right, 48000, TimeSpan.FromSeconds(1d / 30)));
        Assert.NotEqual(stereo.Context.Left.ToArray(), stereo.Context.Right.ToArray());
        Assert.True(float.IsFinite(stereo.Canvas.AverageLuma()));

        stereo.Context.Camera = new VisualPicture([0, 0, 0], 1, 1);
        Assert.True(stereo.Draw(choice, Defaults, left, right, 48000, TimeSpan.FromSeconds(1d / 30)));
        Assert.True(float.IsFinite(stereo.Canvas.AverageLuma()));
    }

    /// <summary>
    /// Options that cannot change a frame of this test: the camera's own (taken by the camera, not the
    /// scene), and the strobe's limit, which only binds faster than this music (its own test plays faster).
    /// </summary>
    private static readonly HashSet<(VisualizerChoice, string)> CameraOptions = [(VisualizerChoice.Silhouette, "mirror"), (VisualizerChoice.Silhouette, "threshold"), (VisualizerChoice.Strobe, "flashes")];

    private static VisualStage Stage(int width = 96, int height = 54)
    {
        var stage = new VisualStage(width, height, new Random(42));
        stage.Context.Cover = Picture(32, 32, 0);
        stage.Context.Camera = Picture(40, 30, 1);
        stage.Context.Mask = PersonMask(40, 30);
        (stage.Context.MaskWidth, stage.Context.MaskHeight) = (40, 30);
        return stage;
    }

    /// <summary>Plays <paramref name="music"/> into a stage; gives each frame's pixels.</summary>
    private static List<byte[]> Play(VisualStage stage, VisualizerChoice choice, SyntheticMusic music, IReadOnlyDictionary<string, string>? options = null, int? frames = null)
    {
        var drawn = new List<byte[]>();
        for (var frame = 1; frame <= (frames ?? music.Frames); frame++)
        {
            var (left, right) = music.At(frame);
            stage.Context.Seconds = (double)frame / SyntheticMusic.Fps;
            stage.Context.Progress = stage.Context.Seconds / 60;
            Assert.True(stage.Draw(choice, options ?? Defaults, left, right, SyntheticMusic.Rate, SyntheticMusic.FrameTime));
            drawn.Add([.. stage.Pixels]);
        }

        return drawn;
    }

    private static double Brightness(byte[] pixels)
    {
        long sum = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            sum += pixels[i] + pixels[i + 1] + pixels[i + 2];
        }

        return sum / (pixels.Length / 4 * 3 * 255.0);
    }

    /// <summary>How much the picture changes from frame to frame, on average, 0 to 1.</summary>
    private static double Motion(List<byte[]> frames)
    {
        double sum = 0;
        for (var f = 1; f < frames.Count; f++)
        {
            long change = 0;
            for (var i = 0; i < frames[f].Length; i++)
            {
                change += Math.Abs(frames[f][i] - frames[f - 1][i]);
            }

            sum += change / (frames[f].Length * 255.0);
        }

        return sum / (frames.Count - 1);
    }

    [Fact]
    [Capability("AU-18")]
    public void ResonanceBoundsOverlappingDrumWavesDuringConsecutiveHits()
    {
        var canvas = new Raster(64, 36);
        var pulse = new MusicPulse();
        var context = new VisualContext(canvas, pulse)
        {
            Choice = VisualizerChoice.Resonance,
            Dt = 0,
        };
        var scene = new ResonanceScene();
        context.Prepare();

        // Keep time at zero so the ring lifecycle cannot remove old rings. Four confirmed
        // onsets must exercise the bounded-ring eviction path, even on fast music.
        for (var n = 0; n < 4; n++)
        {
            for (var quiet = 0; quiet < 10; quiet++)
            {
                pulse.Kick.Hear(0, 0, 0.03, listening: true);
            }

            pulse.Kick.Hear(50, 100, 0.2, listening: true);
            Assert.True(pulse.Kick.Hit);
            scene.Draw(context);
        }

        Assert.Equal(3, scene.Waves);
    }

    [Theory]
    [Capability("AU-18")]
    [MemberData(nameof(Scenes))]
    public void MusicLightsTheStageAndMovesItMoreThanSilence(VisualizerChoice choice)
    {
        var options = choice == VisualizerChoice.BeatEdit ? new Dictionary<string, string> { ["beatedit.grain"] = "0" } : Defaults;
        var music = Play(Stage(), choice, new SyntheticMusic(120, 4), options);
        var silence = Play(Stage(), choice, new SyntheticMusic(120, 4, silence: true), options);

        var lit = music.Skip(30).Average(Brightness);
        Assert.InRange(lit, 0.01, 0.75);
        Assert.True(Motion(music) > Motion(silence) * 1.2 + 0.0005, $"{choice} moves {Motion(music):0.0000} with music, {Motion(silence):0.0000} in silence.");
    }

    [Fact]
    [Capability("AU-18")]
    public void LightningKeepsMovingAcrossTheShortIntervalsCapturedByTheWindow()
    {
        // The UI regression samples the live stage around every 170 ms. A storm
        // must not look frozen between brief lightning strikes, even when the
        // synthetic song has a quieter passage.
        var frames = Play(Stage(160, 90), VisualizerChoice.Lightning, new SyntheticMusic(120, 4));
        var samples = new List<byte[]> { frames[60], frames[65], frames[70], frames[75], frames[80], frames[85] };
        Assert.True(Motion(samples) > 0.001, $"Lightning changed only {Motion(samples):0.0000} between moments.");
    }

    [Theory]
    [MemberData(nameof(Scenes))]
    public void EveryOneOfItsOwnSettingsChangesWhatIsDrawn(VisualizerChoice choice)
    {
        var music = new SyntheticMusic(120, 3);
        var plain = Play(Stage(), choice, music);
        foreach (var option in VisualizerOptions.For(choice).Where(option => option.Key != VisualizerOptions.Color && !CameraOptions.Contains((choice, option.Key))))
        {
            var values = option.Kind switch
            {
                VisualOptionKind.Toggle => [option.Default == 0 ? 1.0 : 0.0],
                VisualOptionKind.Choice => Enumerable.Range(0, option.Choices!.Count).Select(i => (double)i).Where(i => i != option.Default),
                _ => [option.Default == option.Maximum ? option.Minimum : option.Maximum],
            };
            foreach (var value in values)
            {
                var changed = VisualizerOptions.With(Defaults, choice, option.Key, value);
                var drawn = Play(Stage(), choice, music, changed);
                Assert.False(drawn.Zip(plain).All(pair => pair.First.SequenceEqual(pair.Second)), $"{choice}'s \"{option.Label}\" at {value.ToString(CultureInfo.InvariantCulture)} draws the same as at its default.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Scenes))]
    public void ItsOwnColourIsTheColourItDrawsIn(VisualizerChoice choice)
    {
        var red = VisualizerOptions.With(VisualizerOptions.With(Defaults, choice, VisualizerOptions.Colors, (int)VisualPalette.OneColor), choice, VisualizerOptions.Color, 0xFF0000);
        if (choice == VisualizerChoice.BeatEdit)
        {
            // The edit shows a picture; with no camera or cover it paints its own, in its colours.
            red = VisualizerOptions.With(VisualizerOptions.With(VisualizerOptions.With(red, choice, "source", 1), choice, "grain", 0), choice, "style", 3);
        }

        var stage = Stage();
        if (choice is VisualizerChoice.BeatEdit or VisualizerChoice.Vinyl)
        {
            stage.Context.Cover = null;
        }

        var frame = Play(stage, choice, new SyntheticMusic(120, 2), red)[^1];
        var (r, g, b) = (0L, 0L, 0L);
        for (var i = 0; i < frame.Length; i += 4)
        {
            (b, g, r) = (b + frame[i], g + frame[i + 1], r + frame[i + 2]);
        }

        Assert.True(r > g && r > b, $"{choice} drew red {r}, green {g}, blue {b}.");
    }

    [Fact]
    [Capability("AU-18")]
    public void ResonanceRingsExpireAfterTheDrumsStop()
    {
        var stage = Stage();
        Play(stage, VisualizerChoice.Resonance, new SyntheticMusic(120, 2));
        var scene = Assert.IsType<ResonanceScene>(SceneOf(stage));
        Assert.True(scene.Waves > 0);

        Play(stage, VisualizerChoice.Resonance, new SyntheticMusic(120, 5, silence: true));
        Assert.Equal(0, scene.Waves);
    }

    [Fact]
    public void TheRidgelinesKeepOnlyAsManyRidgesAsAsked()
    {
        // Twelve ridges, laid three a second: after six seconds the oldest have gone.
        var stage = Stage();
        Play(stage, VisualizerChoice.Ridgelines, new SyntheticMusic(120, 6), VisualizerOptions.With(Defaults, VisualizerChoice.Ridgelines, "lines", 12));
        var scene = SceneOf(stage);
        var ridges = (System.Collections.ICollection)scene.GetType().GetField("_ridges", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(scene)!;
        Assert.Equal(12, ridges.Count);
    }

    [Fact]
    [Capability("AU-18")]
    public void TheStrobeNeverFlashesFasterThanItsLimit()
    {
        // 300 beats a minute: five a second, more than the limit of three.
        var stage = Stage();
        Play(stage, VisualizerChoice.Strobe, new SyntheticMusic(300, 6));
        var strobe = Assert.IsType<StrobeScene>(SceneOf(stage));
        Assert.InRange(strobe.Flashes, 10, (6 * 3) + 1);

        var limited = Stage();
        Play(limited, VisualizerChoice.Strobe, new SyntheticMusic(300, 6), VisualizerOptions.With(Defaults, VisualizerChoice.Strobe, "flashes", 1));
        Assert.InRange(Assert.IsType<StrobeScene>(SceneOf(limited)).Flashes, 3, 7);
    }

    [Fact]
    [Capability("AU-18")]
    public void TheBeatEditCutsOnTheBeatAndFlashesOnlyWhenAllowed()
    {
        var stage = Stage();
        var frames = Play(stage, VisualizerChoice.BeatEdit, new SyntheticMusic(300, 7, quietFrom: 2, quietUntil: 5));
        var edit = Assert.IsType<BeatEditScene>(SceneOf(stage));
        Assert.InRange(edit.Flashes, 1, (7 * 3) + 1);

        var calm = Stage();
        Play(calm, VisualizerChoice.BeatEdit, new SyntheticMusic(300, 7, quietFrom: 2, quietUntil: 5), VisualizerOptions.With(Defaults, VisualizerChoice.BeatEdit, "flash", 0));
        Assert.Equal(0, Assert.IsType<BeatEditScene>(SceneOf(calm)).Flashes);

        // Every style, through a breakdown and its drop (which splits the screen in four for the styles that do).
        foreach (var style in Enumerable.Range(0, 5))
        {
            var styled = VisualizerOptions.With(Defaults, VisualizerChoice.BeatEdit, "style", style);
            var styledStage = Stage(64, 36);
            Assert.NotEmpty(Play(styledStage, VisualizerChoice.BeatEdit, new SyntheticMusic(128, 9, quietFrom: 3, quietUntil: 6.2), styled));
            Assert.InRange(Assert.IsType<BeatEditScene>(SceneOf(styledStage)).Grade, 0, 6);
        }

        Assert.True(frames.Count > 0);
        Assert.NotNull(VisualScene.For(VisualizerChoice.BeatEdit));
    }

    [Fact]
    [Capability("AU-18")]
    public void BeatEditFollowsAConfidentSilhouetteButIgnoresNoiseAndBadMasks()
    {
        const int Width = 100, Height = 80;
        var mask = new byte[Width * Height];
        for (var y = 20; y < 70; y++)
        {
            for (var x = 70; x < 90; x++)
            {
                mask[(y * Width) + x] = 255;
            }
        }

        var subject = BeatEditScene.SubjectCentre(mask, Width, Height);
        Assert.NotNull(subject);
        Assert.InRange(subject.Value.X, 0.79, 0.81);
        Assert.InRange(subject.Value.Y, 0.55, 0.57);

        var noise = new byte[mask.Length];
        noise[3] = 255;
        Assert.Null(BeatEditScene.SubjectCentre(noise, Width, Height));
        Assert.Null(BeatEditScene.SubjectCentre(new byte[mask.Length], Width, Height));
        Assert.Null(BeatEditScene.SubjectCentre(Enumerable.Repeat((byte)255, mask.Length).ToArray(), Width, Height));
        Assert.Null(BeatEditScene.SubjectCentre(mask, Width, 0));
        Assert.Null(BeatEditScene.SubjectCentre(mask, -1, Height));
        Assert.Null(BeatEditScene.SubjectCentre(mask.AsSpan(0, 10), Width, Height));

        var follow = Stage();
        follow.Context.Mask = PersonMask(40, 30);
        var options = VisualizerOptions.With(Defaults, VisualizerChoice.BeatEdit, "grain", 0);
        var following = Play(follow, VisualizerChoice.BeatEdit, new SyntheticMusic(120, 1), options);
        var still = Stage();
        options = VisualizerOptions.With(options, VisualizerChoice.BeatEdit, "follow", 0);
        var untracked = Play(still, VisualizerChoice.BeatEdit, new SyntheticMusic(120, 1), options);
        Assert.NotEqual(following[^1], untracked[^1]);
    }

    [Fact]
    public void TheGradesAreDifferentLooks()
    {
        var (color, dark, bright) = (new Rgb(0.2f, 0.5f, 0.8f), new Rgb(0.05f, 0.1f, 0.2f), new Rgb(1, 0.7f, 0.3f));
        var grades = Enumerable.Range(0, 7).Select(grade => BeatEditScene.Graded(grade, color, dark, bright)).ToList();
        Assert.Equal(7, grades.Distinct().Count());
        Assert.Equal((0.8f, 0.5f, 0.2f), (MathF.Round(grades[6].R, 3), MathF.Round(grades[6].G, 3), MathF.Round(grades[6].B, 3)));
        var mono = grades[1];
        Assert.True(mono.R == mono.G && mono.G == mono.B);
    }

    [Fact]
    public void TheSilhouetteWaitsForTheCameraAndDrawsEveryStyle()
    {
        var waiting = Stage();
        waiting.Context.Mask = null;
        var frames = Play(waiting, VisualizerChoice.Silhouette, new SyntheticMusic(120, 1));
        Assert.True(Brightness(frames[^1]) > 0, "A breath of light while the camera starts.");

        foreach (var style in Enumerable.Range(0, 5))
        {
            var stage = Stage();
            var drawn = Play(stage, VisualizerChoice.Silhouette, new SyntheticMusic(120, 2), VisualizerOptions.With(VisualizerOptions.With(Defaults, VisualizerChoice.Silhouette, "style", style), VisualizerChoice.Silhouette, "background", 1));
            Assert.True(Brightness(drawn[^1]) > 0.005, $"Style {style} draws the listener.");
        }
    }

    [Fact]
    public void TheStageFitsTheWindowAndOnlyDrawsItsOwnScenes()
    {
        Assert.Equal((16, 9), VisualStage.SizeFor(0, double.NaN));
        Assert.Equal((1920, 1080), VisualStage.SizeFor(1920, 1080));
        Assert.Equal((1080, 1920), VisualStage.SizeFor(1080, 1920));
        Assert.Equal((2, 1920), VisualStage.SizeFor(2, 1000000));
        Assert.Equal((16, 9), VisualStage.SizeFor(double.PositiveInfinity, 1080));
        Assert.Equal((16, 9), VisualStage.SizeFor(1920, double.PositiveInfinity));
        Assert.Equal((1920, 1080), VisualStage.SizeFor(1920, 1080, double.NaN));
        Assert.Equal((320, 180), VisualStage.SizeFor(320, 180));

        // A weaker computer's cap; never above full HD, never below a usable size.
        Assert.Equal((854, 480), VisualStage.SizeFor(1920, 1080, maxSide: 854));
        Assert.Equal((1920, 1080), VisualStage.SizeFor(1920, 1080, maxSide: 4000));
        Assert.Equal((16, 9), VisualStage.SizeFor(1920, 1080, maxSide: 1));
        Assert.False(VisualScene.Draws(VisualizerChoice.Spectrum));
        Assert.True(VisualScene.Draws(VisualizerChoice.BeatEdit));

        var stage = Stage(32, 18);
        var (left, right) = new SyntheticMusic(120, 1).At(10);
        Assert.False(stage.Draw(VisualizerChoice.Spectrum, Defaults, left, right, 48_000, TimeSpan.Zero));
        Assert.True(stage.Draw(VisualizerChoice.Silhouette, Defaults, left, right, 48_000, TimeSpan.Zero));
        Assert.True(stage.Draw(VisualizerChoice.Halo, Defaults, left, right, 48_000, TimeSpan.Zero));
        Assert.Same(stage.Pulse, stage.Context.Pulse);
        stage.Reset();
        Assert.Throws<ArgumentNullException>(() => stage.Draw(VisualizerChoice.Vinyl, null!, left, right, 48_000, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => stage.Draw(VisualizerChoice.Vinyl, Defaults, [0], right, 48_000, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => stage.Draw(VisualizerChoice.Vinyl, Defaults, left, [0], 48_000, TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => new VisualContext(null!, new MusicPulse()));
        Assert.Throws<ArgumentNullException>(() => new VisualContext(new Raster(2, 2), null!));
        Assert.Null(VisualScene.For(VisualizerChoice.Meters));
        Assert.All(Scenes.Select(row => VisualScene.For(row.Data)!), scene => Assert.Throws<ArgumentNullException>(() => scene.Draw(null!)));
    }

    [Fact]
    public void TheSoftwareStageMatchesScreenDensityAndAdaptsToItsFrameBudget()
    {
        Assert.Equal((1200, 675), VisualStage.SizeFor(800, 450, scale: 1.5));
        Assert.Equal((1920, 1080), VisualStage.SizeFor(3840, 2160));
        Assert.Equal((768, 432), VisualStage.SizeFor(3840, 2160, quality: 0.4));
        Assert.Equal((1920, 1080), VisualStage.SizeFor(1280, 720, scale: 1.5));
        Assert.Equal((800, 450), VisualStage.SizeFor(800, 450, scale: 0));
        Assert.Equal((800, 450), VisualStage.SizeFor(800, 450, scale: double.NaN));
        Assert.Equal((1920, 1080), VisualStage.SizeFor(3840, 2160, quality: double.NaN));
        Assert.Equal((768, 432), VisualStage.SizeFor(3840, 2160, quality: -100));
        Assert.Equal((400, 225), VisualStage.SizeFor(800, 450, quality: 0.5));
        Assert.Equal((600, 338), VisualStage.SizeFor(800, 450, quality: 0.5, scale: 1.5));
        Assert.Equal((1920, 1080), VisualStage.SizeFor(800, 450, scale: double.MaxValue));
        Assert.Equal((1920, 960), VisualStage.SizeFor(double.MaxValue, double.MaxValue / 2, scale: 2));

        var stage = Stage(96, 54);
        Assert.Equal(1, stage.Quality);
        Assert.False(stage.WantsResize);
        stage.Took(50);
        Assert.True(stage.Quality < 1);
        Assert.True(stage.WantsResize);
        Assert.True(stage.DrawMilliseconds > 0);
        var reduced = stage.Quality;
        stage.Resize(96, 54);
        Assert.False(stage.WantsResize);
        for (var i = 0; i < 80; i++)
        {
            stage.Took(100);
        }

        Assert.Equal(VisualStage.LowestQuality, stage.Quality, 8);
        for (var i = 0; i < 80; i++)
        {
            stage.Took(1);
        }

        Assert.True(stage.Quality > reduced);
        Assert.True(stage.Quality <= 1);
        stage.Resize(120, 68);
        Assert.Equal((120, 68), (stage.Canvas.Width, stage.Canvas.Height));
        Assert.Equal(120 * 68 * 4, stage.Pixels.Length);
        Assert.False(stage.WantsResize);
        stage.Took(17);
        Assert.InRange(stage.DrawMilliseconds, 0, 30);
    }

    [Fact]
    public void TheToneArmReachesTheNeedleOrAsNearAsItCan()
    {
        // Pivot 10 to the right of the middle, an arm of 10: a needle 10 out is reached above the line.
        var (x, y) = VinylScene.Reach(0, 0, 10, 10, 0, 10);
        Assert.Equal(5, x, 6);
        Assert.Equal(-Math.Sqrt(75), y, 6);

        // A needle the arm cannot reach: the nearest point of its circle, on the line to the pivot.
        Assert.Equal((2.0, 0.0), VinylScene.Reach(0, 0, 2, 100, 0, 10));
    }

    [Fact]
    [Capability("AU-18")]
    public void TheWholeCameraSilhouetteFitsAWideMusicStageWithoutCuttingOffHeadOrFeet()
    {
        Assert.Equal((3.0, 20.0, 0.0), SilhouetteScene.Fit(160, 90, 40, 30));
        Assert.Equal((2.0, 0.0, 20.0), SilhouetteScene.Fit(80, 100, 40, 30));
    }

    [Fact]
    public void ARecordSkippedAlongWritesOnlyTheGrooveItReached()
    {
        var stage = Stage();
        var music = new SyntheticMusic(120, 2);
        var frames = Play(stage, VisualizerChoice.Vinyl, music);

        // A jump of minutes: the record carries on from the new place without cutting all the way round.
        stage.Context.Seconds = 200;
        var (left, right) = music.At(30);
        Assert.True(stage.Draw(VisualizerChoice.Vinyl, Defaults, left, right, SyntheticMusic.Rate, SyntheticMusic.FrameTime));
        stage.Context.Progress = double.NaN;
        Assert.True(stage.Draw(VisualizerChoice.Vinyl, Defaults, left, right, SyntheticMusic.Rate, SyntheticMusic.FrameTime));
        Assert.NotEmpty(frames);
    }

    /// <summary>
    /// Writes a few frames of every scene as PNG files when REXPLAYER_VISUAL_DUMP names a folder,
    /// for a person to look at; does nothing otherwise.
    /// </summary>
    [Fact]
    public void FramesCanBeWrittenOutForAPersonToLookAt()
    {
        if (Environment.GetEnvironmentVariable("REXPLAYER_VISUAL_DUMP") is not { Length: > 0 } folder)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        foreach (var choice in Scenes.Select(row => row.Data))
        {
            var stage = Stage(560, 315);
            stage.Context.Camera = Picture(160, 120, 1);
            stage.Context.Mask = PersonMask(160, 120);
            (stage.Context.MaskWidth, stage.Context.MaskHeight) = (160, 120);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var frames = Play(stage, choice, new SyntheticMusic(124, 13, quietFrom: 6, quietUntil: 10.5));
            File.AppendAllText(Path.Combine(folder, "timings.txt"), $"{choice}: {clock.Elapsed.TotalMilliseconds / frames.Count:0.0} ms a frame{Environment.NewLine}");
            foreach (var at in new[] { 90, 150, 316, 321, 330 })
            {
                using var frame = VideoFrame.Rent(PixelFormat.Bgra32, 560, 315);
                for (var y = 0; y < 315; y++)
                {
                    frames[at].AsSpan(y * 560 * 4, 560 * 4).CopyTo(frame.Row(0, y));
                }

                using var file = File.Create(Path.Combine(folder, $"{choice}-{at}.png"));
                Rex.Media.Video.PngWriter.Write(file, frame);
            }
        }
    }

    private static VisualScene SceneOf(VisualStage stage) =>
        (VisualScene)typeof(VisualStage).GetField("_scene", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(stage)!;

    /// <summary>A picture with a gradient and a bright square, different for each <paramref name="seed"/>.</summary>
    private static VisualPicture Picture(int width, int height, int seed)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = ((y * width) + x) * 4;
                var square = x > width / 3 && x < width * 2 / 3 && y > height / 3 && y < height * 2 / 3;
                pixels[at] = (byte)(square ? 230 : x * 255 / width);
                pixels[at + 1] = (byte)(square ? 200 : (y * 255 / height) ^ (seed * 90));
                pixels[at + 2] = (byte)(square ? 120 : 80 + (seed * 60));
                pixels[at + 3] = 255;
            }
        }

        return new VisualPicture(pixels, width, height);
    }

    /// <summary>A figure: a head and a body, standing in the middle.</summary>
    private static byte[] PersonMask(int width, int height)
    {
        var mask = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (dx, dy) = ((x - (width / 2.0)) / width, (y - (height * 0.28)) / height);
                var head = (dx * dx) + (dy * dy) < 0.012;
                var body = y > height * 0.42 && Math.Abs(dx) < 0.18 + ((y - (height * 0.42)) / height * 0.2);
                mask[(y * width) + x] = head || body ? (byte)255 : (byte)0;
            }
        }

        return mask;
    }

    [Theory]
    [Capability("AU-18")]
    [InlineData(60)]
    [InlineData(144)]
    public void TrailsLastAsLongAndDriftAsFarAtAnyFrameRate(int fps)
    {
        // The trails were faded, zoomed and moved once a frame: at sixty frames a second a ripple's
        // trail was gone twice as fast as at thirty, and the window draws at sixty when it can.
        static (float Light, double Moved) Trailed(int rate)
        {
            // A lit pixel left to fade, and a lit column left to drift right.
            var (fading, drifting) = (new Raster(41, 41), new Raster(41, 41));
            var frames = (int)Math.Round(rate / 5.0);
            var (fade, drift) = (new VisualContext(fading, new MusicPulse()) { Dt = 0.2 / frames }, new VisualContext(drifting, new MusicPulse()) { Dt = 0.2 / frames });
            fading.R[(20 * 41) + 20] = 1;
            for (var y = 0; y < 41; y++)
            {
                drifting.G[(y * 41) + 10] = 1;
            }

            for (var frame = 0; frame < frames; frame++)
            {
                fade.Trail(0.6, 1, 0, 0, 0);
                fading.Settle();
                drift.Trail(1, 1, 0, 0.5, 0);
                drifting.Settle();
            }

            // Where the column's light is now, along the middle row.
            var row = Enumerable.Range(0, 41).Select(x => (double)drifting.G[(20 * 41) + x]).ToList();
            return (fading.R[(20 * 41) + 20], row.Select((g, x) => g * x).Sum() / row.Sum());
        }

        var (light, moved) = Trailed(30);
        var (lightAt, movedAt) = Trailed(fps);
        Assert.InRange(lightAt, light * 0.95f, light * 1.05f);
        Assert.InRange(movedAt, moved - 0.1, moved + 0.1);
        Assert.InRange(moved, 12.9, 13.1);
    }

    [Fact]
    [Capability("AU-18")]
    public void TheRoomBehindTheSilhouetteIsOnlyWhereTheCameraSees()
    {
        // A 4:3 camera fitted to a wide stage leaves bars either side. The room was sampled there
        // from the picture's edge, drawing its edge column out across each bar.
        var stage = Stage(160, 90);
        var room = new byte[40 * 30 * 4];
        for (var y = 0; y < 30; y++)
        {
            room[(y * 40 * 4) + 0] = room[(y * 40 * 4) + 1] = room[(y * 40 * 4) + 2] = 255;
        }

        stage.Context.Camera = new VisualPicture(room, 40, 30);
        Play(stage, VisualizerChoice.Silhouette, new SyntheticMusic(120, 1), VisualizerOptions.With(Defaults, VisualizerChoice.Silhouette, "background", 1), frames: 3);
        var bar = ((45 * 160) + 5) * 4;
        Assert.True(stage.Pixels[bar] + stage.Pixels[bar + 1] + stage.Pixels[bar + 2] < 6, "The bar beside the picture is dark.");
        var edge = ((45 * 160) + 21) * 4;
        Assert.True(stage.Pixels[edge + 1] > 0, "The picture's own edge column shows.");
    }

    [Fact]
    [Capability("AU-18")]
    public void TheRecordsGrooveAndWaveFollowTheSensitivity()
    {
        // Vinyl drew the sound as heard, before the sensitivity: turning it up or down did nothing.
        double Drawn(double sensitivity)
        {
            var stage = Stage();
            var options = VisualizerOptions.With(Defaults, VisualizerChoice.Vinyl, VisualizerOptions.Sensitivity, sensitivity);
            return Brightness(Play(stage, VisualizerChoice.Vinyl, new SyntheticMusic(120, 2), options)[^1]);
        }

        Assert.True(Drawn(3) > Drawn(0.3) * 1.02, $"{Drawn(0.3):0.0000} against {Drawn(3):0.0000}.");
    }

    [Fact]
    [Capability("AU-18")]
    public void TheEmbersSwirlTurnsOnSmoothlyThroughTheBeats()
    {
        // The swirl's turn was the beats counted plus the phase of the clock: a beat landing a little
        // before the clock's own counted it early, and the swirl jumped half a turn and back.
        var music = new SyntheticMusic(120, 8, humanize: 0.02);
        var pulse = new MusicPulse();
        var context = new VisualContext(new Raster(64, 36), pulse) { Choice = VisualizerChoice.Embers, Dt = 1.0 / SyntheticMusic.Fps, Random = new Random(3) };
        var scene = new EmbersScene();
        var (last, largest) = (0.0, 0.0);
        for (var frame = 1; frame <= music.Frames; frame++)
        {
            pulse.Update(music.Mono(frame), SyntheticMusic.Rate, music.Step);
            context.Prepare();
            scene.Draw(context);
            largest = frame > 1 ? Math.Max(largest, Math.Abs(scene.Turn - last)) : 0;
            last = scene.Turn;
        }

        Assert.True(pulse.Locked);
        Assert.True(largest < Math.PI / 4, $"The swirl turned {largest:0.00} radians in a frame.");
        Assert.InRange(scene.Turn, Math.PI * 12, Math.PI * 17);
    }

    [Fact]
    [Capability("AU-18")]
    public void TheBeatMetersSquareShowsOnANarrowStageAndGoesOutOnReset()
    {
        var stage = Stage(48, 27);
        var options = VisualizerOptions.With(Defaults, VisualizerChoice.Halo, VisualizerOptions.Meter, 1);
        var music = new SyntheticMusic(120, 4);
        var square = ((22 * 48) + 46) * 4;
        var lit = false;
        for (var frame = 1; frame <= music.Frames && !lit; frame++)
        {
            var (left, right) = music.At(frame);
            stage.Draw(VisualizerChoice.Halo, options, left, right, SyntheticMusic.Rate, music.Step);
            lit = stage.Pulse.Beat && stage.Pixels[square] > 200;
        }

        Assert.True(lit, "A beat lights the square even 48 pixels across.");

        // After a reset the square is dim again at once, not still lit by the last song's beat.
        stage.Reset();
        var silence = new float[VisualStage.SoundSize];
        stage.Draw(VisualizerChoice.Halo, options, silence, silence, SyntheticMusic.Rate, TimeSpan.Zero);
        Assert.InRange(stage.Pixels[square], (byte)1, (byte)60);
    }
}
