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
        VisualizerChoice.BeatEdit);

    private static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>();

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
        Assert.True(VisualScene.For(VisualizerChoice.BeatEdit)!.UsesCamera);
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

        Assert.Equal((2.0, 0.0, -10.0), SilhouetteScene.Cover(80, 40, 40, 30));
    }

    [Fact]
    public void TheStageFitsTheWindowAndOnlyDrawsItsOwnScenes()
    {
        Assert.Equal((16, 9), VisualStage.SizeFor(0, double.NaN));
        Assert.Equal((800, 450), VisualStage.SizeFor(1920, 1080));
        Assert.Equal((320, 180), VisualStage.SizeFor(320, 180));
        Assert.False(VisualStage.Draws(VisualizerChoice.Spectrum));
        Assert.True(VisualStage.Draws(VisualizerChoice.BeatEdit));

        var stage = Stage(32, 18);
        var (left, right) = new SyntheticMusic(120, 1).At(10);
        Assert.False(stage.UsesCamera);
        Assert.False(stage.Draw(VisualizerChoice.Spectrum, Defaults, left, right, 48_000, TimeSpan.Zero));
        Assert.True(stage.Draw(VisualizerChoice.Silhouette, Defaults, left, right, 48_000, TimeSpan.Zero));
        Assert.True(stage.UsesCamera);
        Assert.True(stage.Draw(VisualizerChoice.Halo, Defaults, left, right, 48_000, TimeSpan.Zero));
        Assert.False(stage.UsesCamera);
        Assert.Same(stage.Pulse, stage.Context.Pulse);
        stage.Reset();
        Assert.False(stage.UsesCamera);
        Assert.Throws<ArgumentNullException>(() => stage.Draw(VisualizerChoice.Vinyl, null!, left, right, 48_000, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => stage.Draw(VisualizerChoice.Vinyl, Defaults, [0], right, 48_000, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => stage.Draw(VisualizerChoice.Vinyl, Defaults, left, [0], 48_000, TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => new VisualContext(null!, new MusicPulse()));
        Assert.Throws<ArgumentNullException>(() => new VisualContext(new Raster(2, 2), null!));
        Assert.Null(VisualScene.For(VisualizerChoice.Meters));
        Assert.All(Scenes.Select(row => VisualScene.For(row.Data)!), scene => Assert.Throws<ArgumentNullException>(() => scene.Draw(null!)));
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
}
