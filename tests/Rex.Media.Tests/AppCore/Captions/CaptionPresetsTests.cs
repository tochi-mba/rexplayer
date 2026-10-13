using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Player;
using Rex.Media.Settings;
using Rex.Media.Subtitles;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Captions;

public sealed class CaptionPresetsTests
{
    private static readonly Area Area = new(0, 0, 1080, 1920);
    private static readonly SpeechFrame Plain = new(0.5f, 0.5f, true, 3, false, 0);

    private static SubtitleWord Word(string text) => new(text, new SubtitleRun(""), null, 0);

    [Fact]
    [Capability("OSD-11")]
    public void EveryPresetHasItsOwnLookNameAndDescription()
    {
        var presets = Enum.GetValues<CaptionPreset>().Where(preset => preset != CaptionPreset.Custom).ToList();
        var styles = presets.Select(preset => CaptionPresets.Style(preset, CaptionStyle.Default)).ToList();
        Assert.Equal(styles.Count, styles.Distinct().Count());
        Assert.Equal(presets.Count, presets.Select(CaptionPresets.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(presets, preset => Assert.EndsWith(".", CaptionPresets.Describe(preset), StringComparison.Ordinal));
        Assert.Equal("Custom", CaptionPresets.Name(CaptionPreset.Custom));
        Assert.Equal("Your own mix of the choices below.", CaptionPresets.Describe(CaptionPreset.Custom));
        // Between them the presets use every font, case and motion.
        Assert.Equal(Enum.GetValues<CaptionFont>().Length, styles.Select(style => style.Font).Distinct().Count());
        Assert.Equal(Enum.GetValues<CaptionCase>().Length, styles.Select(style => style.Case).Distinct().Count());
        Assert.Equal(Enum.GetValues<CaptionMotion>().Length, styles.Select(style => style.Motion).Distinct().Count());
    }

    [Fact]
    [Capability("OSD-11")]
    public void TheUsersEnergyGlyphsAndPictureChoiceLayOverAPresetAndCustomIsTheirOwn()
    {
        var own = CaptionStyle.Default with { Energy = 2, Glyphs = false, AvoidPicture = false, Font = CaptionFont.Mono, Tilt = 3 };
        var comic = CaptionPresets.Style(CaptionPreset.Comic, own);
        Assert.Equal((CaptionFont.Display, 2.6, false, false, 2.5), (comic.Font, comic.Energy, comic.Glyphs, comic.AvoidPicture, comic.Tilt));
        Assert.Same(own, CaptionPresets.Style(CaptionPreset.Custom, own));
        Assert.Equal(CaptionStyle.Default with { Energy = 2, Glyphs = false, AvoidPicture = false }, CaptionPresets.Style((CaptionPreset)99, own));
        Assert.Throws<ArgumentNullException>(() => CaptionPresets.Style(CaptionPreset.Neon, null!));

        var settings = new PlayerSettings { KineticCaptions = new KineticCaptionSettings { Preset = CaptionPreset.Typewriter, Energy = 50 } };
        var style = CaptionDirector.StyleFor(settings);
        Assert.Equal((CaptionMotion.Typewriter, CaptionCase.Lower, 0.4), (style.Motion, style.Case, style.Energy));
        var custom = CaptionDirector.StyleFor(new PlayerSettings { KineticCaptions = new KineticCaptionSettings { Preset = CaptionPreset.Custom, Font = CaptionFontChoice.Rounded, Case = CaptionCaseChoice.Upper, Motion = CaptionMotionChoice.Drop, Highlight = true, Flicker = true, Tilt = 250, Outline = 40, PlateAlways = true } });
        Assert.Equal((CaptionFont.Rounded, CaptionCase.Upper, CaptionMotion.Drop, true, true, 2.5, 0.4, true), (custom.Font, custom.Case, custom.Motion, custom.Highlight, custom.Flicker, custom.Tilt, custom.Outline, custom.PlateAlways));
        var damaged = new KineticCaptionSettings { Preset = (CaptionPreset)77, Font = (CaptionFontChoice)77, Case = (CaptionCaseChoice)77, Motion = (CaptionMotionChoice)77, Tilt = -5, Outline = 900 }.Normalize();
        Assert.Equal((CaptionPreset.Creator, CaptionFontChoice.Sans, CaptionCaseChoice.AsSaid, CaptionMotionChoice.Pop, 0, 200), (damaged.Preset, damaged.Font, damaged.Case, damaged.Motion, damaged.Tilt, damaged.Outline));
    }

    [Theory]
    [Capability("OSD-11")]
    [InlineData(CaptionMotion.Pop, 0.0, 0.6, 0.0, 0.0, "hello")]
    [InlineData(CaptionMotion.Pop, 1.0, 1.0, 0.0, 0.0, "hello")]
    [InlineData(CaptionMotion.Slide, 0.0, 1.0, -0.8, 0.0, "hello")]
    [InlineData(CaptionMotion.Slide, 1.0, 1.0, 0.0, 0.0, "hello")]
    [InlineData(CaptionMotion.Drop, 0.0, 1.0, 0.0, -1.2, "hello")]
    [InlineData(CaptionMotion.Bounce, 1.0, 1.0, 0.0, 0.0, "hello")]
    [InlineData(CaptionMotion.Typewriter, 0.0, 1.0, 0.0, 0.0, "h")]
    [InlineData(CaptionMotion.Typewriter, 1.0, 1.0, 0.0, 0.0, "hello")]
    public void EachMotionArrivesInItsOwnWay(CaptionMotion motion, double t, double scale, double x, double y, string shown)
    {
        var (actualScale, actualX, actualY, text) = CaptionChoreography.Arrive(motion, t, "hello", t == 1 ? 1 : 0);
        Assert.Equal((scale, x, y, shown), (Math.Round(actualScale, 3), Math.Round(actualX, 3), Math.Round(actualY, 3), text));
    }

    [Fact]
    public void ABounceRisesPartWayAndATypewriterTypesAtItsPace()
    {
        var (scale, _, y, _) = CaptionChoreography.Arrive(CaptionMotion.Bounce, 0.3, "x", 0.05);
        Assert.True(y < 0 && scale > 1, $"{y} {scale}");
        Assert.Equal("hel", CaptionChoreography.Arrive(CaptionMotion.Typewriter, 0.5, "hello", 0.08).Text);
        Assert.Equal("", CaptionChoreography.Arrive(CaptionMotion.Typewriter, 0, "hello", -1).Text);
        Assert.Equal(1, CaptionChoreography.Arrive(CaptionMotion.Pop, double.NaN, "x", 0).Scale);
        Assert.Throws<ArgumentNullException>(() => CaptionChoreography.Arrive(CaptionMotion.Pop, 0, null!, 0));
    }

    [Fact]
    [Capability("OSD-11")]
    public void ASingAlongLightsOnlyTheWordBeingSungAndPlatesEveryWord()
    {
        var choreography = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Karaoke, CaptionStyle.Default));
        choreography.Reveal(Word("lonely"), Plain, Stress.Plain, 0);
        choreography.Reveal(Word("at"), Plain, Stress.Hush, 0.3);
        choreography.Reveal(Word("the"), Plain, Stress.Plain, 0.6);
        var scene = choreography.Scene(2, Area, 60);
        Assert.Equal(["LONELY", "AT", "THE"], scene.Select(word => word.Text));
        Assert.Equal([0.55, 0.44, 1.0], scene.Select(word => Math.Round(word.Opacity, 3)));
        Assert.All(scene, word => Assert.True(word.Backing.Plate >= 0.55));
        Assert.All(scene, word => Assert.Equal((CaptionFont.Rounded, 0.0), (word.Font, word.Rotation)));
    }

    [Fact]
    [Capability("OSD-11")]
    public void AGlitchFlickersAsAPeakLandsAndDropsInFromAbove()
    {
        var choreography = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Glitch, CaptionStyle.Default));
        choreography.Reveal(Word("now"), Plain with { Loudness = 1 }, Stress.Peak, 0);
        var early = Assert.Single(choreography.Scene(0.02, Area, 60));
        Assert.True(early.OffsetY < 0 && early.Opacity < 1, $"{early.OffsetY} {early.Opacity}");
        var flickers = Enumerable.Range(0, 40).Select(i => Assert.Single(choreography.Scene(0.2 + (i * 0.005), Area, 60)).Opacity).ToList();
        Assert.Contains(flickers, opacity => opacity < 0.5);
        Assert.Contains(flickers, opacity => opacity > 0.9);
        var settled = Assert.Single(choreography.Scene(1, Area, 60));
        Assert.Equal(("NOW", 0.0, 1.0), (settled.Text, settled.OffsetY, settled.Opacity));
        Assert.NotEqual(0, settled.Rotation);
    }

    [Fact]
    public void ATypewriterTypesLowerCaseLettersOutAndMinimalSlidesIn()
    {
        var typewriter = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Typewriter, CaptionStyle.Default));
        typewriter.Reveal(Word("HELLO"), Plain, Stress.Shout, 0);
        Assert.Equal("he", Assert.Single(typewriter.Scene(0.04, Area, 60)).Text);
        Assert.Equal("hello", Assert.Single(typewriter.Scene(1, Area, 60)).Text);

        var minimal = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Minimal, CaptionStyle.Default));
        minimal.Reveal(Word("Hi"), Plain, Stress.Plain, 0);
        var sliding = Assert.Single(minimal.Scene(CaptionChoreography.PopSeconds / 2, Area, 60));
        Assert.True(sliding.OffsetX < 0 && sliding.Opacity < 1 && sliding.Text == "hi", $"{sliding}");
    }
}
