using Rex.Media.Primitives;

namespace Rex.Media.Tests.Primitives;

public sealed class VideoEffectsTests
{
    [Fact]
    public void EveryEffectSaysWhatItDoesAndEveryControlSaysWhatItChanges()
    {
        foreach (var effect in Enum.GetValues<VideoEffect>().Where(effect => effect != VideoEffect.Off))
        {
            var description = VideoEffects.Description(effect);
            Assert.False(string.IsNullOrWhiteSpace(description), $"{effect} has no description");
            Assert.EndsWith(".", description);
            foreach (var option in VideoStyleOptions.ForEffect(effect))
            {
                Assert.False(string.IsNullOrWhiteSpace(option.Help), $"{option.Key} has no tooltip");
                Assert.DoesNotContain((char)0x2014, option.Label + option.Help);
                Assert.DoesNotContain((char)0x00B7, option.Label + option.Help);
                Assert.StartsWith($"effect.{effect}.", option.Key, StringComparison.Ordinal);
            }

            Assert.DoesNotContain((char)0x2014, description);
            Assert.DoesNotContain((char)0x00B7, description);
        }

        Assert.Equal("", VideoEffects.Description(VideoEffect.Off));
        Assert.All(Enum.GetValues<VideoLook>().SelectMany(VideoStyleOptions.ForLook), option => Assert.False(string.IsNullOrWhiteSpace(option.Help)));
        Assert.Equal("Brightness (%)", VideoStyleOptions.ForLook(VideoLook.Original)[0].Label);
        Assert.Equal("Look intensity (%)", VideoStyleOptions.ForLook(VideoLook.Cinema)[0].Label);
        Assert.Equal("Brightness (%)", VideoStyleOptions.ForLook((VideoLook)999)[0].Label);
    }

    [Fact]
    public void OffHasNothingToCustomizeAndTheFlatPickerShowsEachGroup()
    {
        Assert.Empty(VideoStyleOptions.ForEffect(VideoEffect.Off));
        Assert.Equal(new EffectControls(0, 100, 0, 0), VideoStyleOptions.Controls(VideoEffect.Off, null, 80));
        Assert.Equal("Off", VideoEffects.PickerLabel(VideoEffect.Off));
        Assert.Equal("Neon contours", VideoEffects.PickerLabel(VideoEffect.NeonEdges));
        Assert.Equal("Ghostwire (Contours)", VideoEffects.PickerLabel(VideoEffect.Ghostwire));
        Assert.Equal("Tape wear (Playful)", VideoEffects.PickerLabel(VideoEffect.TapeWear));
    }

    [Fact]
    public void ModesNameEveryValueTheirControlCanTake()
    {
        foreach (var effect in Enum.GetValues<VideoEffect>())
        {
            var mode = VideoStyleOptions.ForEffect(effect).FirstOrDefault(option => option.Key.EndsWith(".mode", StringComparison.Ordinal));
            var names = VideoStyleOptions.ModeNames(effect);
            Assert.Equal(mode is null ? 0 : mode.Max - mode.Min + 1, names.Count);
        }
    }

    [Fact]
    public void ControlsStartAtTheirDefaultsAndKeepWhatWasSaved()
    {
        var options = VideoStyleOptions.ForEffect(VideoEffect.Kaleidoscope);
        Assert.Equal(new EffectControls(65, 100, 3, 10), VideoStyleOptions.Controls(VideoEffect.Kaleidoscope, null, VideoStyleOptions.SharedStrengthDefault));

        var saved = VideoStyleOptions.With(null, options[2], 7);
        saved = VideoStyleOptions.With(saved, options[3], 0);
        saved = VideoStyleOptions.With(saved, options[0], 40);
        saved = VideoStyleOptions.With(saved, options[1], 150);
        Assert.Equal(new EffectControls(40, 150, 7, 0), VideoStyleOptions.Controls(VideoEffect.Kaleidoscope, saved, 80));

        // Another effect's values never leak in: Halftone still starts from its own defaults.
        Assert.Equal(new EffectControls(90, 100, 15, 60), VideoStyleOptions.Controls(VideoEffect.Halftone, saved, VideoStyleOptions.SharedStrengthDefault));
    }

    [Fact]
    public void AStrengthChangedBeforeEachEffectHadItsOwnStillApplies()
    {
        var neon = VideoStyleOptions.ForEffect(VideoEffect.NeonEdges)[0];
        Assert.Equal(82, VideoStyleOptions.ReadEffect(VideoEffect.NeonEdges, null, neon, 82));
        Assert.Equal(neon.Default, VideoStyleOptions.ReadEffect(VideoEffect.NeonEdges, null, neon, VideoStyleOptions.SharedStrengthDefault));
        Assert.Equal(100, VideoStyleOptions.ReadEffect(VideoEffect.NeonEdges, new Dictionary<string, int>(), neon, 900));
        Assert.Equal(30, VideoStyleOptions.ReadEffect(VideoEffect.NeonEdges, VideoStyleOptions.With(null, neon, 30), neon, 82));

        // The Ghostwire styles always start from their own, higher default.
        var ghost = VideoStyleOptions.ForEffect(VideoEffect.Ghostwire)[0];
        Assert.Equal(96, VideoStyleOptions.ReadEffect(VideoEffect.Ghostwire, null, ghost, 82));
        var mask = VideoStyleOptions.ForEffect(VideoEffect.GhostwireMask)[0];
        Assert.Equal(96, VideoStyleOptions.ReadEffect(VideoEffect.GhostwireMask, null, mask, 82));

        // Only intensities inherit it; detail keeps its own default.
        var detail = VideoStyleOptions.ForEffect(VideoEffect.NeonEdges)[1];
        Assert.Equal(100, VideoStyleOptions.ReadEffect(VideoEffect.NeonEdges, null, detail, 82));
        Assert.Throws<ArgumentNullException>(() => VideoStyleOptions.ReadEffect(VideoEffect.NeonEdges, null, null!, 82));

        // A reset shows the effect's own default, not the old shared strength.
        var reset = VideoStyleOptions.Reset(VideoStyleOptions.With(null, neon, 30), VideoStyleOptions.ForEffect(VideoEffect.NeonEdges));
        Assert.Equal(neon.Default, VideoStyleOptions.ReadEffect(VideoEffect.NeonEdges, reset, neon, 82));
        Assert.False(VideoStyleOptions.Reset(VideoStyleOptions.With(null, VideoStyleOptions.ForLook(VideoLook.Sunset)[0], 10),
            VideoStyleOptions.ForLook(VideoLook.Sunset)).ContainsKey("look.Sunset.intensity"));
    }

    [Theory]
    [InlineData(VideoEffect.GhostwireMotion, 65, 35, 3, false)]
    [InlineData(VideoEffect.GhostwireMotion, 65, 35, 1, false)]
    [InlineData(VideoEffect.GhostwireMotion, 65, 0, 0, true)]
    [InlineData(VideoEffect.GhostwireMotion, 65, 35, 2, true)]
    [InlineData(VideoEffect.MotionSmear, 65, 60, 40, true)]
    [InlineData(VideoEffect.MotionSmear, 65, 0, 40, false)]
    [InlineData(VideoEffect.MotionSmear, 0, 60, 40, false)]
    [InlineData(VideoEffect.ChromaticRift, 65, 0, 1, true)]
    [InlineData(VideoEffect.ChromaticRift, 65, 0, 0, false)]
    [InlineData(VideoEffect.Ghostwire, 100, 100, 100, false)]
    [InlineData(VideoEffect.Off, 100, 100, 100, false)]
    public void OnlyEffectsThatReadThePreviousPictureKeepIt(VideoEffect effect, int strength, int third, int fourth, bool expected) =>
        Assert.Equal(expected, VideoEffects.UsesHistory(effect, strength, third, fourth));

    [Theory]
    [InlineData(1L, 1.00, 1L, 1.04, true)]
    [InlineData(1L, 1.00, 1L, 1.25, true)]
    [InlineData(1L, 1.00, 1L, 1.26, false)]
    [InlineData(1L, 1.00, 1L, 1.00, false)]
    [InlineData(1L, 1.00, 1L, 0.96, false)]
    [InlineData(1L, 1.00, 2L, 1.04, false)]
    [InlineData(null, 1.00, 1L, 1.04, false)]
    [InlineData(1L, double.NaN, 1L, 1.04, false)]
    [InlineData(1L, 1.00, 1L, double.NaN, false)]
    public void HistoryContinuesOnlyIntoTheNextPictureOfTheSamePlayback(long? previousGeneration, double previous, long generation, double now, bool expected) =>
        Assert.Equal(expected, VideoEffects.ContinuesHistory(previousGeneration, previous, generation, now));
}
