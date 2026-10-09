using Rex.Media.AppCore.Player;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Audio;

/// <summary>Every visualisation's own settings (AU-18), and the camera silhouette's picking out of a person.</summary>
public sealed class VisualizerOptionsTests
{
    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

    [Fact]
    [Capability("AU-18")]
    public void EveryVisualisationHasColoursSensitivityAndItsOwnSettings()
    {
        foreach (var choice in Enum.GetValues<VisualizerChoice>())
        {
            var options = VisualizerOptions.For(choice);
            Assert.Equal([VisualizerOptions.Colors, VisualizerOptions.Color, VisualizerOptions.Sensitivity], options.Take(3).Select(option => option.Key));
            Assert.All(options, option => Assert.False(string.IsNullOrWhiteSpace(option.Label)));
            Assert.Equal(options.Count, options.Select(option => option.Key).Distinct().Count());
            Assert.All(options.Where(option => option.Kind == VisualOptionKind.Choice), option => Assert.NotEmpty(option.Choices!));
        }

        Assert.Contains(VisualizerOptions.For(VisualizerChoice.Vinyl), option => option.Key == "speed");
        Assert.Contains(VisualizerOptions.For(VisualizerChoice.Silhouette), option => option.Key == "mirror");
        Assert.Equal(3, VisualizerOptions.For(VisualizerChoice.Meters).Count);

        // The strobe follows the pitch unless told otherwise; the rest use Windows' accent.
        Assert.Equal((int)VisualPalette.Pitch, VisualizerOptions.Choice(None, VisualizerChoice.Strobe, VisualizerOptions.Colors));
        Assert.Equal((int)VisualPalette.Accent, VisualizerOptions.Choice(None, VisualizerChoice.Halo, VisualizerOptions.Colors));
    }

    [Fact]
    [Capability("AU-18")]
    public void SettingsAreKeptPerVisualisationAndReadSafely()
    {
        var options = VisualizerOptions.With(None, VisualizerChoice.Halo, "rays", 64);
        options = VisualizerOptions.With(options, VisualizerChoice.Halo, "spin", 0);
        options = VisualizerOptions.With(options, VisualizerChoice.Vinyl, "speed", 2);
        options = VisualizerOptions.With(options, VisualizerChoice.Vinyl, VisualizerOptions.Color, 0x123456);

        Assert.Equal(64, VisualizerOptions.Number(options, VisualizerChoice.Halo, "rays"));
        Assert.False(VisualizerOptions.Toggle(options, VisualizerChoice.Halo, "spin"));
        Assert.True(VisualizerOptions.Toggle(options, VisualizerChoice.Vinyl, "arm"));
        Assert.Equal(2, VisualizerOptions.Choice(options, VisualizerChoice.Vinyl, "speed"));
        Assert.Equal(0x123456, VisualizerOptions.Rgb(options, VisualizerChoice.Vinyl, VisualizerOptions.Color));
        Assert.Equal(0xFF3B30, VisualizerOptions.Rgb(options, VisualizerChoice.Halo, VisualizerOptions.Color));

        // Out of range, unreadable or not a choice: the nearest in range, or the default.
        var wild = new Dictionary<string, string> { ["halo.rays"] = "9000", ["halo.thickness"] = "thick", ["vinyl.speed"] = "7", ["mirror.columns"] = "NaN", ["embers.amount"] = "1.5", ["vinyl.depth"] = "-1" };
        Assert.Equal(128, VisualizerOptions.Number(wild, VisualizerChoice.Halo, "rays"));
        Assert.Equal(4, VisualizerOptions.Number(wild, VisualizerChoice.Halo, "thickness"));
        Assert.Equal(0, VisualizerOptions.Choice(wild, VisualizerChoice.Vinyl, "speed"));
        Assert.Equal(96, VisualizerOptions.Number(wild, VisualizerChoice.Mirror, "columns"));
        Assert.Equal(20, VisualizerOptions.Number(wild, VisualizerChoice.Embers, "amount"));
        Assert.Equal(0.04, VisualizerOptions.Number(wild, VisualizerChoice.Vinyl, "depth"));
        Assert.Equal(0, VisualizerOptions.Choice(new Dictionary<string, string> { ["vinyl.speed"] = "1.5" }, VisualizerChoice.Vinyl, "speed"));
        Assert.Equal(0, VisualizerOptions.Choice(new Dictionary<string, string> { ["vinyl.speed"] = "-1" }, VisualizerChoice.Vinyl, "speed"));

        // Resetting one visualisation leaves the others.
        var reset = VisualizerOptions.Reset(options, VisualizerChoice.Halo);
        Assert.Equal(48, VisualizerOptions.Number(reset, VisualizerChoice.Halo, "rays"));
        Assert.Equal(2, VisualizerOptions.Choice(reset, VisualizerChoice.Vinyl, "speed"));

        Assert.Throws<ArgumentException>(() => VisualizerOptions.Number(None, VisualizerChoice.Halo, "no-such"));
        Assert.Throws<ArgumentException>(() => VisualizerOptions.With(None, VisualizerChoice.Meters, "rays", 1));
        Assert.Throws<ArgumentNullException>(() => VisualizerOptions.With(null!, VisualizerChoice.Halo, "rays", 1));
        Assert.Throws<ArgumentNullException>(() => VisualizerOptions.Reset(null!, VisualizerChoice.Halo));
        Assert.Throws<ArgumentNullException>(() => VisualizerOptions.Number(null!, VisualizerChoice.Halo, "rays"));
    }

    [Theory]
    [Capability("AU-18")]
    [InlineData(VisualPalette.Accent, 0.5, 0, 255, 10, 20, 30)]
    [InlineData(VisualPalette.Pitch, 0, 0, 255, 255, 0, 0)]
    [InlineData(VisualPalette.Pitch, 1, 0, 255, 128, 0, 255)]
    [InlineData(VisualPalette.Rainbow, 0, 0, 255, 255, 0, 0)]
    [InlineData(VisualPalette.Rainbow, 0, 10, 255, 0, 255, 255)]
    [InlineData(VisualPalette.Warm, 0, 0, 255, 255, 21, 0)]
    [InlineData(VisualPalette.Cool, 0, 0, 255, 38, 219, 255)]
    [InlineData(VisualPalette.OneColor, 0.3, 0, 255, 0x12, 0x34, 0x56)]
    public void EachPaletteColoursByPitchTimeOrChoice(VisualPalette palette, double share, double seconds, byte a, byte r, byte g, byte b) =>
        Assert.Equal(new Argb(a, r, g, b), Visualizers.PaletteColor(palette, share, seconds, new Argb(255, 10, 20, 30), 0x123456));

    [Fact]
    public void TheOptionsLiveInTheSettingsAndAreMended()
    {
        var settings = new PlayerSettings { VisualOptions = new Dictionary<string, string> { ["halo.rays"] = "64" } };
        Assert.Equal(64, VisualizerOptions.Number(settings.Normalize().VisualOptions, VisualizerChoice.Halo, "rays"));
        Assert.Empty(new PlayerSettings { VisualOptions = null! }.Normalize().VisualOptions);
    }

    /// <summary>A picture of the room: a gentle left-to-right gradient.</summary>
    private static byte[] Room(int width, int height) => [.. Enumerable.Range(0, width * height).Select(i => (byte)(40 + (i % width * 2)))];

    [Fact]
    [Capability("AU-18")]
    public void ThePersonIsWhatDiffersFromTheRoomOnceItHasBeenWatched()
    {
        const int Width = 20, Height = 16;
        var mask = new SilhouetteMask(Width, Height);
        var room = Room(Width, Height);
        for (var i = 0; i < SilhouetteMask.LearningPictures; i++)
        {
            Assert.True(mask.IsLearning);
            Assert.Equal(0, mask.Update(room, 0.12));
        }

        Assert.False(mask.IsLearning);

        // Someone steps in: a bright block, with one stray speck of noise elsewhere.
        var person = (byte[])room.Clone();
        for (var y = 4; y < 12; y++)
        {
            for (var x = 6; x < 12; x++)
            {
                person[(y * Width) + x] = 230;
            }
        }

        person[(1 * Width) + 17] = 250;
        var share = mask.Update(person, 0.12);

        Assert.Equal(255, mask.Mask[(8 * Width) + 8]);
        Assert.Equal(0, mask.Mask[(1 * Width) + 17]);
        Assert.Equal(0, mask.Mask[(14 * Width) + 2]);
        Assert.InRange(share, 0.1, 0.2);
        Assert.True(SilhouetteMask.IsEdge(mask.Mask, Width, Height, 6, 8));
        Assert.False(SilhouetteMask.IsEdge(mask.Mask, Width, Height, 8, 8));
        Assert.False(SilhouetteMask.IsEdge(mask.Mask, Width, Height, 2, 2));

        // At the picture's border a marked pixel is an edge.
        var full = new SilhouetteMask(3, 3);
        for (var i = 0; i <= SilhouetteMask.LearningPictures; i++)
        {
            full.Update(i < SilhouetteMask.LearningPictures ? new byte[9] : Enumerable.Repeat((byte)255, 9).ToArray(), double.NaN);
        }

        Assert.True(SilhouetteMask.IsEdge(full.Mask, 3, 3, 0, 0));
        Assert.True(SilhouetteMask.IsEdge(full.Mask, 3, 3, 2, 1));
        Assert.False(SilhouetteMask.IsEdge(full.Mask, 3, 3, 1, 1));

        mask.Reset();
        Assert.True(mask.IsLearning);
        Assert.All(mask.Mask, value => Assert.Equal(0, value));
        Assert.Throws<ArgumentException>(() => mask.Update(new byte[5], 0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SilhouetteMask(2, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SilhouetteMask(10, 2));
    }

    [Fact]
    public void CameraPicturesAreShrunkToTheirBrightnessAndMirrored()
    {
        // 4x2 BGRA with a padded stride: white, black, red, green on top; blue and grays below.
        byte[] picture =
        [
            255, 255, 255, 255, 0, 0, 0, 255, 0, 0, 255, 255, 0, 255, 0, 255, 9, 9,
            255, 0, 0, 255, 128, 128, 128, 255, 64, 64, 64, 255, 0, 0, 0, 255, 9, 9,
        ];

        Assert.Equal([255, 0, 76, 149, 28, 128, 64, 0], SilhouetteMask.Brightness(picture, 4, 2, 18, 4, 2, mirror: false));
        Assert.Equal([149, 76, 0, 255, 0, 64, 128, 28], SilhouetteMask.Brightness(picture, 4, 2, 18, 4, 2, mirror: true));
        Assert.Equal([255, 76], SilhouetteMask.Brightness(picture, 4, 2, 18, 2, 1, mirror: false));
        Assert.Throws<ArgumentException>(() => SilhouetteMask.Brightness(picture.AsSpan(0, 20), 4, 2, 18, 2, 1, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => SilhouetteMask.Brightness(picture, 4, 2, 8, 2, 1, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => SilhouetteMask.Brightness(picture, 0, 2, 18, 2, 1, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => SilhouetteMask.Brightness(picture, 4, 0, 18, 2, 1, false));
    }
}
