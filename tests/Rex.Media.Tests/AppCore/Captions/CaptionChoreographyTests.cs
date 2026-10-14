using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Player;
using Rex.Media.Subtitles;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Captions;

public sealed class CaptionChoreographyTests
{
    private static readonly Area Area = new(0, 0, 1080, 1920);
    private const double Font = 60;
    private static readonly SpeechFrame Plain = new(0.5f, 0.5f, true, 3, false, 0);

    private static SubtitleWord Word(string text, SubtitleRun? style = null) => new(text, style ?? new SubtitleRun(""), null, 0);

    [Fact]
    [Capability("OSD-11")]
    public void HowAWordWasSaidDecidesItsSizeCaseColourAndMotion()
    {
        var choreography = new CaptionChoreography(CaptionStyle.Default);
        choreography.Reveal(Word("Why"), Plain with { Loudness = 0.1f }, Stress.Hush, 0);
        choreography.Reveal(Word("do"), Plain, Stress.Plain, 0.2);
        choreography.Reveal(Word("crazy"), Plain with { Pitch = 0.9f }, Stress.Plain, 0.4);
        choreography.Reveal(Word("creators"), Plain with { Loudness = 0.9f }, Stress.Shout, 0.6);
        choreography.Reveal(Word("big!"), Plain with { Loudness = 1 }, Stress.Peak, 0.8);

        var scene = choreography.Scene(2, Area, Font).ToDictionary(word => word.Id);
        var (hush, plain, lilt, shout, peak) = (scene[0], scene[1], scene[2], scene[3], scene[4]);

        Assert.Equal(("why", 0.8, 0.8), (hush.Text, hush.Size, hush.Opacity));
        Assert.Equal((1.0, CaptionStyle.Default.Text, false), (plain.Size, plain.Color, plain.Bold));
        Assert.Equal((1.4, CaptionStyle.Default.Accent, true, true, -2.0), (lilt.Size, lilt.Color, lilt.Italic, lilt.Serif, lilt.Rotation));
        Assert.Equal(("CREATORS", true, 0.6), (shout.Text, shout.Bold, shout.Glow));
        Assert.InRange(shout.Size, 1.8, 2.6);
        Assert.Equal(("BIG!", 2.6), (peak.Text, peak.Size));
        // Long after, the peak's flash and shake are over.
        Assert.Equal((0.0, 0.0), (peak.Flash, peak.Shake));
        // A shout sits on a row of its own, below the words before it.
        Assert.True(shout.Y > lilt.Y && peak.Y > shout.Y);
        Assert.Equal(plain.Y, lilt.Y);
        Assert.True(lilt.X > plain.X);
    }

    [Fact]
    [Capability("OSD-11")]
    public void APeakFlashesShakesAndPopsInWithAnOvershoot()
    {
        var choreography = new CaptionChoreography(CaptionStyle.Default with { Energy = 2 });
        choreography.Reveal(Word("now"), Plain, Stress.Peak, 1);

        var first = Assert.Single(choreography.Scene(1, Area, Font));
        Assert.Equal(1, first.Flash);
        Assert.Equal(0.24, first.Shake, 3);
        // Energy doubles how far the voice moves the word: 1 + 1.6 * 2.
        Assert.Equal(4.2 * 0.6, first.Size, 3);
        var overshoot = Assert.Single(choreography.Scene(1 + (CaptionChoreography.PopSeconds / 2), Area, Font));
        Assert.Equal(4.2 * 1.35, overshoot.Size, 3);
        Assert.Equal(4.2, overshoot.Settled, 3);
        Assert.Equal(4.2, Assert.Single(choreography.Scene(2, Area, Font)).Size, 3);
    }

    [Fact]
    [Capability("OSD-11")]
    public void APauseFadesTheClusterAndTheNextStartsSomewhereElse()
    {
        var choreography = new CaptionChoreography(CaptionStyle.Default);
        choreography.Reveal(Word("first"), Plain, Stress.Plain, 0);
        var before = Assert.Single(choreography.Scene(0.5, Area, Font));
        choreography.Clear(1);
        Assert.False(choreography.IsOpen);
        Assert.False(choreography.IsEmpty);
        Assert.Equal(0.5, Assert.Single(choreography.Scene(1 + (CaptionChoreography.FadeSeconds / 2), Area, Font)).Opacity, 3);
        Assert.Empty(choreography.Scene(2, Area, Font));
        Assert.True(choreography.IsEmpty);

        choreography.Reveal(Word("second"), Plain, Stress.Plain, 3);
        var after = Assert.Single(choreography.Scene(3.5, Area, Font));
        Assert.Equal(2, choreography.Clusters);
        Assert.NotEqual((before.X, before.Y), (after.X, after.Y));
    }

    [Fact]
    [Capability("OSD-11")]
    public void TheClusterGoesWhereItCoversNoFaceAndEveryWordIsReadable()
    {
        // A face over the whole top half: the cluster, asked for near the top, goes below it.
        var picture = PictureSpace.Read(
            PictureSpaceTests.Picture((x, y) => y < 90 ? ((byte)224, (byte)172, (byte)140) : ((byte)245, (byte)245, (byte)240), 160, 180),
            160,
            180,
            640);
        var choreography = new CaptionChoreography(CaptionStyle.Default);
        choreography.Reveal(Word("hello"), Plain, Stress.Plain, 0);
        choreography.Reveal(Word("there"), Plain, Stress.Plain, 0.2);

        var scene = choreography.Scene(1, Area, Font, picture: picture);
        Assert.All(scene, word => Assert.True(word.Y > Area.Height * 0.55, $"{word.Text} at {word.Y}"));
        // The picture there is bright: white text gets a dark plate.
        Assert.All(scene, word => Assert.True(word.Backing.Plate >= 0.6, $"{word.Backing}"));

        // Its place holds as the cluster grows, even when the picture changes.
        choreography.Reveal(Word("again"), Plain, Stress.Plain, 0.4);
        var grown = choreography.Scene(1.2, Area, Font, picture: PictureSpace.Empty());
        Assert.Equal(scene[0].Y, grown[0].Y);

        // Without avoiding the picture, the cluster sits where the layout puts it.
        var free = new CaptionChoreography(CaptionStyle.Default with { AvoidPicture = false });
        free.Reveal(Word("hello"), Plain, Stress.Plain, 0);
        Assert.True(Assert.Single(free.Scene(1, Area, Font, picture: picture)).Y < Area.Height * 0.4);
    }

    [Fact]
    [Capability("OSD-11")]
    public void AFaceComingUnderTheClusterMovesItOffWithoutAJump()
    {
        var clear = PictureSpace.Empty();
        var choreography = new CaptionChoreography(CaptionStyle.Default);
        choreography.Reveal(Word("stay"), Plain, Stress.Plain, 0);
        choreography.Reveal(Word("here"), Plain, Stress.Plain, 0.2);
        var before = choreography.Scene(1, Area, Font, picture: clear);
        var at = (before[0].X, before[0].Y);

        // A face arrives exactly where the cluster sits.
        var face = PictureSpace.Read(
            PictureSpaceTests.Picture((x, y) => ((byte)40, (byte)40, (byte)48), 320, 180),
            320, 180, 1280,
            faces: [new Area(Math.Max(0, (at.X / Area.Width) - 0.1), Math.Max(0, (at.Y / Area.Height) - 0.1), 0.45, 0.25)]);
        var soon = choreography.Scene(1.02, Area, Font, picture: face);
        var later = choreography.Scene(1.6, Area, Font, picture: face);

        // It starts moving at once, by a little, and is well away soon after.
        var moved = Math.Abs(soon[0].X - at.X) + Math.Abs(soon[0].Y - at.Y);
        var gone = Math.Abs(later[0].X - at.X) + Math.Abs(later[0].Y - at.Y);
        Assert.True(moved > 0 && moved < gone / 3, $"{moved} then {gone}");
        Assert.False(face.CoversAFace(Math.Min(later[0].X, later[1].X) / Area.Width, (later[0].Y - (Font / 2)) / Area.Height, 0.2, 0.05));
    }

    [Fact]
    public void TextComingUnderTheClusterMovesItOffWithoutAJump()
    {
        var choreography = new CaptionChoreography(CaptionStyle.Default);
        choreography.Reveal(Word("stay"), Plain, Stress.Plain, 0);
        choreography.Reveal(Word("readable"), Plain, Stress.Plain, 0.2);
        var before = choreography.Scene(1, Area, Font, picture: PictureSpace.Empty());
        var at = (X: before[0].X / Area.Width, Y: before[0].Y / Area.Height);
        var text = PictureSpace.Read(
            PictureSpaceTests.Picture((x, y) =>
                Math.Abs((x / 320.0) - at.X) < 0.28 && Math.Abs((y / 180.0) - at.Y) < 0.12 && (x / 3) % 2 == 0
                    ? ((byte)250, (byte)250, (byte)250)
                    : ((byte)10, (byte)10, (byte)10)),
            320, 180, 1280);

        var soon = choreography.Scene(1.02, Area, Font, picture: text);
        var later = choreography.Scene(1.6, Area, Font, picture: text);
        var started = Math.Abs(soon[0].X - before[0].X) + Math.Abs(soon[0].Y - before[0].Y);
        var finished = Math.Abs(later[0].X - before[0].X) + Math.Abs(later[0].Y - before[0].Y);

        Assert.True(started > 0 && started < finished, $"{started} then {finished}");
        Assert.False(text.CoversProtected(Math.Min(later[0].X, later[1].X) / Area.Width, (later[0].Y - (Font / 2)) / Area.Height, 0.3, 0.08));
    }

    [Fact]
    public void ConfettiMovesWhenProtectedContentAppearsUnderAWord()
    {
        var choreography = new CaptionChoreography(CaptionStyle.Default with { Layout = CaptionLayout.Confetti });
        choreography.Reveal(Word("clear"), Plain, Stress.Plain, 0);
        var before = Assert.Single(choreography.Scene(1, Area, Font, picture: PictureSpace.Empty()));
        var width = CaptionChoreography.WidthEm(before.Text, before.Font, before.Bold) * before.Settled * Font / Area.Width;
        var height = before.Settled * Font * 1.2 / Area.Height;
        var left = (before.X / Area.Width) - (width / 2);
        var top = (before.Y / Area.Height) - (height / 2);
        var face = PictureSpace.Read(
            PictureSpaceTests.Picture((x, y) => ((byte)40, (byte)40, (byte)48), 320, 180),
            320, 180, 1280,
            faces: [new Area(left, top, width, height)]);

        var after = Assert.Single(choreography.Scene(1.1, Area, Font, picture: face));
        var afterLeft = (after.X / Area.Width) - (width / 2);
        var afterTop = (after.Y / Area.Height) - (height / 2);
        Assert.NotEqual((before.X, before.Y), (after.X, after.Y));
        Assert.False(face.CoversProtected(afterLeft, afterTop, width, height));
    }

    [Fact]
    [Capability("OSD-11")]
    public void WordsWrapToReadableRowsShrinkToFitAndAreNeverTooSmall()
    {
        var narrow = new Area(0, 0, 400, 800);
        var choreography = new CaptionChoreography(CaptionStyle.Default);
        foreach (var (word, i) in "a lot of people watch videos on mute".Split(' ').Select((word, i) => (word, i)))
        {
            choreography.Reveal(Word(word), Plain, Stress.Plain, i * 0.1);
        }

        choreography.Reveal(Word("extraordinarily"), Plain, Stress.Peak, 1);
        var scene = choreography.Scene(3, narrow, 10);
        // A 10-pixel font on an 800-pixel area is lifted to a readable size.
        var em = 800 * CaptionChoreography.SmallestShare / 0.8;
        Assert.True(scene.Select(word => word.Y).Distinct().Count() >= 3);
        Assert.All(scene, word => Assert.InRange(word.X, 0, narrow.Width));
        var big = scene[^1];
        // The peak is shrunk until it fits the row: 15 letters at 0.58 em.
        Assert.True(big.Size * 15 * 0.58 * em <= narrow.Width * 0.8 + 0.001, $"{big.Size}");
        Assert.Equal("🔇", scene.Single(word => word.Text == "mute").Glyph);
    }

    [Fact]
    public void AFullClusterLetsItsOldestWordsGoAndTheFilesOwnStyleIsKept()
    {
        var choreography = new CaptionChoreography(CaptionStyle.Default with { Glyphs = false, UpperCase = false, Layout = CaptionLayout.Stack });
        for (var i = 0; i < CaptionChoreography.MostWords + 1; i++)
        {
            choreography.Reveal(Word("w" + i, new SubtitleRun("", Bold: true, Italic: true, Color: 0x00FF00)), Plain, i == 0 ? Stress.Shout : Stress.Plain, i * 0.05);
        }

        var scene = choreography.Scene(0.75 + CaptionChoreography.FadeSeconds, Area, Font);
        Assert.Equal(CaptionChoreography.MostWords - 4, scene.Count);
        Assert.Equal("w5", scene[0].Text);
        Assert.All(scene, word => Assert.Equal((new Argb(255, 0, 255, 0), true, true), (word.Color, word.Bold, word.Italic)));

        // The stack and centre layouts centre their rows.
        var centre = new CaptionChoreography(CaptionStyle.Default with { Layout = CaptionLayout.Centre });
        centre.Reveal(Word("mid"), Plain, Stress.Plain, 0);
        var word = Assert.Single(centre.Scene(1, Area, Font));
        Assert.Equal(Area.Width / 2, word.X, 0);
        Assert.Null(word.Glyph);
        Assert.Equal(new Backing(0.6, 0), word.Backing);
    }

    [Fact]
    public void ResetAndEmptyAreasShowNothingAndBadInputIsRefusedOrTamed()
    {
        var choreography = new CaptionChoreography(CaptionStyle.Default with { Energy = double.NaN });
        choreography.Reveal(Word("x"), Plain, Stress.Shout, 0);
        Assert.Empty(choreography.Scene(1, new Area(0, 0, 0, 100), Font));
        Assert.Equal(1.8, Assert.Single(choreography.Scene(1, Area, Font)).Size, 3);
        choreography.Reset();
        Assert.True(choreography.IsEmpty);
        Assert.Throws<ArgumentNullException>(() => choreography.Reveal(null!, Plain, Stress.Plain, 0));
        Assert.Throws<ArgumentNullException>(() => new CaptionChoreography(null!));
        Assert.Equal(1, CaptionChoreography.Pop(double.NaN));
        Assert.Equal(0.6, CaptionChoreography.Pop(-1));
    }

    [Fact]
    public void AWordLongAfterTheLastIsStretchedAndPopsInSlowly()
    {
        var choreography = new CaptionChoreography(CaptionStyle.Default);
        choreography.Reveal(Word("one"), Plain, Stress.Plain, 0);
        choreography.Reveal(Word("two"), Plain, Stress.Plain, 0.2);
        choreography.Reveal(Word("threeee"), Plain, Stress.Plain, 1.5);
        var scene = choreography.Scene(1.5 + CaptionChoreography.PopSeconds, Area, Font);
        // A plain word has popped in fully by now; the stretched one is still on its way.
        Assert.NotEqual(1, scene[2].Size);
    }

    [Fact]
    public void HighlightDimsEveryLiveWordExceptTheNewest()
    {
        var choreography = new CaptionChoreography(CaptionStyle.Default with { Highlight = true });
        choreography.Reveal(Word("first"), Plain, Stress.Plain, 0);
        choreography.Reveal(Word("second"), Plain, Stress.Plain, 0.1);

        var scene = choreography.Scene(1, Area, Font);

        Assert.Equal(0.55, scene[0].Opacity, 2);
        Assert.Equal(1, scene[1].Opacity);
    }
}
