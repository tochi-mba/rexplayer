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

    [Fact]
    [Capability("OSD-11")]
    public void WidthsFollowTheTypefaceAndCase()
    {
        Assert.Equal(3.0, CaptionChoreography.WidthEm("HELLO", CaptionFont.Mono, bold: true), 3);
        Assert.True(CaptionChoreography.WidthEm("HELLO", CaptionFont.Serif, bold: false) > CaptionChoreography.WidthEm("hello", CaptionFont.Serif, bold: false));
        Assert.True(CaptionChoreography.WidthEm("HELLO", CaptionFont.Serif, bold: false) > CaptionChoreography.WidthEm("HELLO", CaptionFont.Display, bold: false));
        Assert.True(CaptionChoreography.WidthEm("HELLO", CaptionFont.Sans, bold: true) > CaptionChoreography.WidthEm("HELLO", CaptionFont.Sans, bold: false));
        Assert.Equal(0.3, CaptionChoreography.WidthEm(" ", CaptionFont.Sans, bold: false), 3);
        Assert.True(CaptionChoreography.WidthEm("Ab", CaptionFont.Rounded, bold: false) > 1.1);
        Assert.Throws<ArgumentNullException>(() => CaptionChoreography.WidthEm(null!, CaptionFont.Sans, bold: false));
    }

    [Fact]
    [Capability("OSD-11")]
    public void ConfettiWordsLandClearOfEachOtherAndOfFaces()
    {
        var face = PictureSpace.Read(PictureSpaceTests.Picture((x, y) => ((byte)40, (byte)40, (byte)48), 320, 180), 320, 180, 1280, faces: [new Area(0.3, 0.3, 0.4, 0.4)]);
        var rave = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Rave, CaptionStyle.Default));
        foreach (var (text, i) in "a lot of people watch videos on mute usually".Split(' ').Select((text, i) => (text, i)))
        {
            rave.Reveal(Word(text), Plain, Stress.Plain, i * 0.05);
        }

        var scene = rave.Scene(1, Area, 60, picture: face);
        Assert.Equal(9, scene.Count);
        var boxes = scene.Select(word => (Left: word.X - (CaptionChoreography.WidthEm(word.Text, CaptionFont.Display, word.Bold) * 60 * word.Settled / 2), Top: word.Y - (60 * word.Settled * 0.6), Right: word.X + (CaptionChoreography.WidthEm(word.Text, CaptionFont.Display, word.Bold) * 60 * word.Settled / 2), Bottom: word.Y + (60 * word.Settled * 0.6))).ToList();
        for (var a = 0; a < boxes.Count; a++)
        {
            for (var b = a + 1; b < boxes.Count; b++)
            {
                var overlapX = Math.Min(boxes[a].Right, boxes[b].Right) - Math.Max(boxes[a].Left, boxes[b].Left);
                var overlapY = Math.Min(boxes[a].Bottom, boxes[b].Bottom) - Math.Max(boxes[a].Top, boxes[b].Top);
                Assert.False(overlapX > 6 && overlapY > 6, $"{scene[a].Text} and {scene[b].Text} overlap");
            }
        }

        Assert.All(scene, word => Assert.False(face.CoversAFace((word.X / Area.Width) - 0.01, (word.Y / Area.Height) - 0.01, 0.02, 0.02), $"{word.Text} is on the face"));
    }

    [Fact]
    [Capability("OSD-11")]
    public void ATickerSlidesItsLineLeftAndDropsWordsThatHaveGone()
    {
        var ticker = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Ticker, CaptionStyle.Default));
        ticker.Reveal(Word("breaking"), Plain, Stress.Shout, 0);
        ticker.Reveal(Word("news"), Plain, Stress.Plain, 0.5);
        var early = ticker.Scene(0.6, Area, 60);
        var later = ticker.Scene(2.6, Area, 60);
        Assert.Equal(["BREAKING", "NEWS"], early.Select(word => word.Text));
        Assert.All(early, word => Assert.Equal(1.0, word.Settled));
        Assert.True(later[0].X < early[0].X - (Area.Width * 0.3), $"{early[0].X} then {later[0].X}");
        Assert.True(early[1].X > early[0].X);
        Assert.All(early, word => Assert.True(word.Y > Area.Height * 0.85 && word.Backing.Plate >= 0.55));
        // Off the left edge, a word is gone.
        ticker.Scene(12, Area, 60);
        var far = ticker.Scene(12.5, Area, 60);
        Assert.DoesNotContain(far, word => word.Opacity > 0.01);
    }

    [Fact]
    [Capability("OSD-11")]
    public void StairsClimbFromTheLowerLeftAndGhostsDissolveOnTheirOwn()
    {
        var stairs = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Stairs, CaptionStyle.Default));
        foreach (var (text, i) in new[] { "one", "two", "three" }.Select((text, i) => (text, i)))
        {
            stairs.Reveal(Word(text), Plain, Stress.Plain, i * 0.2);
        }

        var steps = stairs.Scene(1, Area, 60);
        Assert.True(steps[0].X < steps[1].X && steps[1].X < steps[2].X);
        Assert.True(steps[0].Y > steps[1].Y && steps[1].Y > steps[2].Y);
        Assert.All(steps, word => Assert.Equal(CaptionFont.Rounded, word.Font));
        for (var i = 3; i < 40; i++)
        {
            stairs.Reveal(Word("w" + i), Plain, Stress.Plain, 1 + (i * 0.1));
        }

        var tall = stairs.Scene(5.5, Area, 60);
        Assert.All(tall.Where(word => word.Opacity > 0.99), word => Assert.True(word.Y > 0));

        var ghosts = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Ghost, CaptionStyle.Default));
        ghosts.Reveal(Word("Boo"), Plain, Stress.Peak, 0);
        var fresh = Assert.Single(ghosts.Scene(0.05, Area, 60));
        Assert.True(fresh.Opacity < 1 && fresh.Size > 1 && fresh.Text == "boo" && fresh.Flash == 0, $"{fresh}");
        var going = Assert.Single(ghosts.Scene(2.5, Area, 60));
        Assert.True(going.Opacity < 0.6 && going.OffsetY < -0.2, $"{going}");
        Assert.Empty(ghosts.Scene(3, Area, 60));
    }

    [Fact]
    [Capability("OSD-11")]
    public void StampsSlamDownPillsAreTintedMarkerWritesAndWireIsHollowUntilStressed()
    {
        var stamp = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Stamp, CaptionStyle.Default));
        stamp.Reveal(Word("Denied"), Plain, Stress.Plain, 0);
        var landing = Assert.Single(stamp.Scene(0.01, Area, 60));
        Assert.True(landing.Size > 1.8 && landing.Opacity < 0.5 && landing.Text == "DENIED", $"{landing}");
        Assert.Equal(1.0, Assert.Single(stamp.Scene(1, Area, 60)).Size);

        var pills = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Pills, CaptionStyle.Default));
        pills.Reveal(Word("soft"), Plain, Stress.Plain, 0);
        pills.Reveal(Word("LOUD"), Plain with { Loudness = 1 }, Stress.Peak, 0.3);
        var pill = pills.Scene(1, Area, 60);
        Assert.All(pill, word => Assert.True(word.PlateTint && word.Backing.Plate >= 0.9));
        Assert.All(pill, word => Assert.True(word.Color.R + word.Color.G + word.Color.B > 450, $"{word.Color} is not pastel"));
        Assert.NotEqual(pill[0].Color, pill[1].Color);

        var marker = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Marker, CaptionStyle.Default));
        marker.Reveal(Word("note"), Plain, Stress.Plain, 0);
        var written = Assert.Single(marker.Scene(0.05, Area, 60));
        Assert.Equal((CaptionFont.Hand, true, 2), (written.Font, written.PlateTint, written.Text.Length));

        var wire = new CaptionChoreography(CaptionPresets.Style(CaptionPreset.Wire, CaptionStyle.Default));
        wire.Reveal(Word("thin"), Plain, Stress.Plain, 0);
        wire.Reveal(Word("solid"), Plain with { Loudness = 1 }, Stress.Shout, 0.3);
        var wires = wire.Scene(1, Area, 60);
        Assert.True(wires[0].Hollow);
        Assert.False(wires[1].Hollow);
    }

    [Fact]
    [Capability("OSD-11")]
    public void EveryLookKeepsItsWordsReadableOverAnyPicture()
    {
        var bright = PictureSpace.Read(PictureSpaceTests.Picture((x, y) => (x + y) % 2 == 0 ? ((byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0)), 320, 180, 1280);
        var dark = PictureSpace.Read(PictureSpaceTests.Picture((x, y) => ((byte)12, (byte)12, (byte)16)), 320, 180, 1280);
        foreach (var preset in Enum.GetValues<CaptionPreset>().Where(preset => preset != CaptionPreset.Custom))
        {
            foreach (var picture in new[] { bright, dark })
            {
                var choreography = new CaptionChoreography(CaptionPresets.Style(preset, CaptionStyle.Default));
                choreography.Reveal(Word("read"), Plain, Stress.Plain, 0);
                choreography.Reveal(Word("me"), Plain with { Loudness = 1 }, Stress.Peak, 0.3);
                foreach (var word in choreography.Scene(1, Area, 60, picture: picture))
                {
                    var readable = word.Backing.Plate >= 0.35 || word.Backing.Outline >= 0.45 || word.PlateLight || word.PlateTint;
                    Assert.True(readable, $"{preset}: {word.Text} has plate {word.Backing.Plate} and outline {word.Backing.Outline}");
                }
            }
        }

        Assert.Equal(new Argb(255, 140, 140, 140), CaptionChoreography.Pastel(new Argb(255, 0, 0, 0)));
    }
}
