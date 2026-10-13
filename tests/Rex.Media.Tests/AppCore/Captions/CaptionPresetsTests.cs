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
        Assert.Equal((CaptionFont.Display, 2.6, false, false, 2.5, true), (comic.Font, comic.Energy, comic.Glyphs, comic.AvoidPicture, comic.Tilt, comic.PlateLight));
        Assert.Same(own, CaptionPresets.Style(CaptionPreset.Custom, own));
        Assert.Equal(CaptionStyle.Default with { Energy = 2, Glyphs = false, AvoidPicture = false }, CaptionPresets.Style((CaptionPreset)99, own));
        Assert.Throws<ArgumentNullException>(() => CaptionPresets.Style(CaptionPreset.Neon, null!));

        var settings = new PlayerSettings { KineticCaptions = new KineticCaptionSettings { Preset = CaptionPreset.Typewriter, Energy = 50 } };
        var style = CaptionDirector.StyleFor(settings);
        Assert.Equal((CaptionMotion.Typewriter, CaptionCase.Lower, 0.3), (style.Motion, style.Case, style.Energy));
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
    public void ASingAlongShowsTheWholeLineDimAndLightsEachWordAsItIsSung()
    {
        var choreography = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Karaoke, CaptionStyle.Default));
        var ids = new[] { "lonely", "at", "the" }.Select(text => choreography.Reveal(Word(text), Plain, Stress.Shout, 0, lit: false)).ToList();
        var waiting = choreography.Scene(0.5, Area, 60);
        // Flat: a shout is no bigger; dim until sung; rounded capitals on a plate, level.
        Assert.Equal(["LONELY", "AT", "THE"], waiting.Select(word => word.Text));
        Assert.All(waiting, word => Assert.Equal((1.0, 0.4, false, CaptionFont.Rounded, 0.0), (word.Size, Math.Round(word.Opacity, 3), word.Lit, word.Font, word.Rotation)));
        Assert.All(waiting, word => Assert.True(word.Backing.Plate >= 0.55));

        choreography.Light(ids[0], 1);
        var sung = choreography.Scene(1 + CaptionChoreography.PopSeconds, Area, 60);
        Assert.Equal((true, 1.0, new Argb(255, 0xFF, 0x4F, 0xA3)), (sung[0].Lit, Math.Round(sung[0].Opacity, 3), sung[0].Color));
        Assert.False(sung[1].Lit);
        // Lighting a word twice, or one that is not there, changes nothing.
        choreography.Light(ids[0], 2);
        choreography.Light(999, 2);
        Assert.Equal(sung[0].Color, choreography.Scene(2.5, Area, 60)[0].Color);
    }

    [Fact]
    [Capability("OSD-11")]
    public void ANeonSignShowsOneWordAtATimeAndARaveScattersEveryWordInItsOwnHue()
    {
        var neon = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Neon, CaptionStyle.Default));
        neon.Reveal(Word("first"), Plain, Stress.Plain, 0);
        neon.Reveal(Word("second"), Plain, Stress.Plain, 0.3);
        var both = neon.Scene(0.31, Area, 60);
        Assert.Equal(2, both.Count);
        Assert.True(both[0].Opacity < 1 && both[1].Opacity <= 1);
        Assert.Equal("SECOND", Assert.Single(neon.Scene(0.3 + CaptionChoreography.FadeSeconds, Area, 60)).Text);

        var rave = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Rave, CaptionStyle.Default));
        for (var i = 0; i < 6; i++)
        {
            rave.Reveal(Word("word" + i), Plain, Stress.Plain, i * 0.1);
        }

        var confetti = rave.Scene(1, Area, 60);
        var again = rave.Scene(1.1, Area, 60);
        Assert.Equal(6, confetti.Select(word => (Math.Round(word.X), Math.Round(word.Y))).Distinct().Count());
        Assert.True(confetti.Select(word => word.Color).Distinct().Count() >= 5);
        // Each keeps its spot (the jitter aside), inside the picture.
        Assert.All(confetti.Zip(again), pair => Assert.True(Math.Abs(pair.First.X - pair.Second.X) < 1 && pair.First.OffsetX != pair.Second.OffsetX));
        Assert.All(confetti, word => Assert.InRange(word.X, 0, Area.Width));
    }

    [Fact]
    [Capability("OSD-11")]
    public void ATypewriterTypesIntoACornerBlockBehindACursorAndHeadlinesAreInkOnWhite()
    {
        var typewriter = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Typewriter, CaptionStyle.Default));
        typewriter.Reveal(Word("HELLO"), Plain, Stress.Shout, 0);
        var typing = Assert.Single(typewriter.Scene(0.04, Area, 60));
        Assert.StartsWith("he", typing.Text, StringComparison.Ordinal);
        Assert.Equal(3, typing.Text.Length);
        var typed = Assert.Single(typewriter.Scene(1, Area, 60));
        Assert.StartsWith("hello", typed.Text, StringComparison.Ordinal);
        Assert.Equal(1.0, typed.Size);
        Assert.True(typed.X < Area.Width * 0.4 && typed.Y > Area.Height * 0.5, $"{typed.X}, {typed.Y}");

        var headline = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Headline, CaptionStyle.Default));
        headline.Reveal(Word("Extra"), Plain with { Loudness = 1 }, Stress.Peak, 0);
        var word = Assert.Single(headline.Scene(1, Area, 60));
        Assert.True(word.PlateLight && word.Backing.Plate >= 0.9 && word.Font == CaptionFont.Serif, $"{word}");
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
        Assert.Equal(("NOW", 1.0), (settled.Text, settled.Opacity));
        Assert.InRange(settled.OffsetY, -0.03, 0.03);
        Assert.NotEqual(0, settled.Rotation);
    }

    [Fact]
    public void ATypewriterTypesLowerCaseLettersOutAndMinimalSlidesIn()
    {
        var typewriter = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Typewriter, CaptionStyle.Default));
        typewriter.Reveal(Word("HELLO"), Plain, Stress.Shout, 0);
        // The cursor blinks after the letters typed so far.
        Assert.Equal("he\u258C", Assert.Single(typewriter.Scene(0.04, Area, 60)).Text);
        Assert.Equal("hello\u2007", Assert.Single(typewriter.Scene(0.75, Area, 60)).Text);

        var minimal = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Minimal, CaptionStyle.Default));
        minimal.Reveal(Word("Hi"), Plain, Stress.Plain, 0);
        var sliding = Assert.Single(minimal.Scene(CaptionChoreography.PopSeconds / 2, Area, 60));
        Assert.True(sliding.OffsetX < 0 && sliding.Opacity < 1 && sliding.Text == "hi", $"{sliding}");
        minimal.Reveal(Word("LOUD"), Plain with { Loudness = 1 }, Stress.Peak, 1);
        var flat = minimal.Scene(2, Area, 60)[^1];
        Assert.Equal(("loud", 1.0, 0.0, CaptionStyle.Default.Text), (flat.Text, flat.Size, flat.Flash, flat.Color));

        var glitch = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Glitch, CaptionStyle.Default));
        glitch.Reveal(Word("feed"), Plain, Stress.Plain, 0);
        Assert.True(Assert.Single(glitch.Scene(1, Area, 60)).Split);
    }
}
