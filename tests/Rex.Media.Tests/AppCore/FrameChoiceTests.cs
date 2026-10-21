using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Video;

namespace Rex.Media.Tests.AppCore;

/// <summary>The frame that stands for a video on its card, and the poster cut from it (LIB-05).</summary>
public sealed class FrameChoiceTests
{
    private static TimeSpan Minutes(double minutes) => TimeSpan.FromMinutes(minutes);

    private static CreditFrameSample Sample(double mean, double dark, double bright, params float[] rows) =>
        new(TimeSpan.Zero, mean, dark, bright, rows);

    [Fact]
    [Capability("LIB-05")]
    public void AnEpisodeIsShownByAMomentWellPastItsOpeningTitles()
    {
        // A 22-minute episode: the opening (two minutes at most) and the last tenth are skipped, and
        // the first moment tried is a third of the way through what lies between.
        var tried = FrameChoice.Candidates(Minutes(22));
        Assert.Equal(3, tried.Count);
        var credits = Minutes(22) * 0.9;
        Assert.Equal(Minutes(2) + ((credits - Minutes(2)) * 0.3), tried[0]);
        Assert.All(tried, moment => Assert.InRange(moment, Minutes(2), credits));
        Assert.True(tried[1] < tried[0] && tried[0] < tried[2]);

        // A short clip skips its first tenth.
        Assert.Equal(TimeSpan.FromSeconds(6) + (TimeSpan.FromSeconds(48) * 0.3), FrameChoice.Candidates(TimeSpan.FromMinutes(1))[0]);
    }

    [Fact]
    [Capability("LIB-05")]
    public void KnownSectionsBoundTheMomentsTried()
    {
        var tried = FrameChoice.Candidates(Minutes(22), introEnd: Minutes(3), creditsStart: Minutes(20.5));
        Assert.Equal(Minutes(3) + (Minutes(17.5) * 0.3), tried[0]);
        Assert.All(tried, moment => Assert.InRange(moment, Minutes(3), Minutes(20.5)));

        // Sections that make no sense (beyond the end, or crossed) fall back to the usual bounds.
        Assert.Equal(FrameChoice.Candidates(Minutes(22)), FrameChoice.Candidates(Minutes(22), introEnd: Minutes(30), creditsStart: Minutes(40)));
        Assert.Equal(FrameChoice.Candidates(Minutes(22)), FrameChoice.Candidates(Minutes(22), introEnd: TimeSpan.Zero, creditsStart: Minutes(1)));
        var (opening, credits) = (Minutes(22) / 10, Minutes(22) * 0.9);
        Assert.Equal(opening + ((credits - opening) * 0.3), FrameChoice.Candidates(Minutes(22), introEnd: Minutes(21), creditsStart: null)[0]);
    }

    [Fact]
    [Capability("LIB-05")]
    public void AVeryShortOrUnmeasuredVideoIsShownByItsMiddleOrItsStart()
    {
        Assert.Equal([TimeSpan.FromMilliseconds(200)], FrameChoice.Candidates(TimeSpan.FromMilliseconds(400)));
        Assert.Equal([TimeSpan.Zero], FrameChoice.Candidates(null));
        Assert.Equal([TimeSpan.Zero], FrameChoice.Candidates(TimeSpan.Zero));
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData(0.45, 0.2, 0.05, true)] // An ordinary scene.
    [InlineData(0.02, 0.99, 0, false)] // Black.
    [InlineData(0.35, 0.9, 0.01, false)] // Mostly black: a fade.
    [InlineData(0.97, 0, 0.96, false)] // White.
    public void BlackBlownOutAndFlatFramesAreNotShown(double mean, double dark, double bright, bool strong)
    {
        Assert.Equal(strong, FrameChoice.Strong(Sample(mean, dark, bright, 0.2f, 0.6f, 0.4f)));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AFlatColourIsNotShownAndTheBestOfWeakFramesIsKnown()
    {
        Assert.False(FrameChoice.Strong(Sample(0.5, 0, 0, 0.5f, 0.51f, 0.5f)));
        Assert.False(FrameChoice.Strong(Sample(0.5, 0, 0)));
        Assert.True(FrameChoice.Score(Sample(0.3, 0.6, 0, 0.1f, 0.5f)) > FrameChoice.Score(Sample(0.05, 0.95, 0, 0.1f, 0.12f)));
        Assert.Throws<ArgumentNullException>(() => FrameChoice.Strong(null!));
        Assert.Throws<ArgumentNullException>(() => FrameChoice.Score(null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void APosterIsCutAroundTheMostDetailedPartOfTheFrame()
    {
        // 160x60 inside a frame: a poster is 40 wide. Detail sits in the eighth of eight strips.
        var area = new PictureArea(10, 5, 160, 60);
        var detail = new double[8];
        detail[7] = 9;
        Assert.Equal(new PictureArea(130, 5, 40, 60), FrameChoice.PosterArea(area, detail));

        // Detail in the middle: the poster centres on it.
        Array.Clear(detail);
        detail[3] = detail[4] = 5;
        Assert.Equal(new PictureArea(70, 5, 40, 60), FrameChoice.PosterArea(area, detail));

        // Even detail: the first run wins.
        Assert.Equal(new PictureArea(10, 5, 40, 60), FrameChoice.PosterArea(area, new double[8]));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AFrameAlreadyPosterShapedIsKeptWhole()
    {
        var upright = new PictureArea(0, 0, 40, 72);
        Assert.Equal(upright, FrameChoice.PosterArea(upright, new double[8]));
        var wide = new PictureArea(0, 0, 128, 72);
        Assert.Equal(wide, FrameChoice.PosterArea(wide, []));
        Assert.Throws<ArgumentNullException>(() => FrameChoice.PosterArea(wide, null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void DetailIsMeasuredInUprightStrips()
    {
        // Flat on the left half, alternating black and white columns on the right.
        using var frame = VideoFrame.Rent(PixelFormat.Bgra32, 32, 8);
        for (var y = 0; y < 8; y++)
        {
            var row = frame.Row(0, y);
            for (var x = 0; x < 32; x++)
            {
                var value = x < 16 ? (byte)128 : (byte)(x % 2 == 0 ? 0 : 255);
                row[x * 4] = row[(x * 4) + 1] = row[(x * 4) + 2] = value;
                row[(x * 4) + 3] = 255;
            }
        }

        var detail = FrameChoice.ColumnDetail(frame, new PictureArea(0, 0, 32, 8), 4);
        Assert.Equal(0, detail[0]);
        Assert.True(detail[3] > 1000 && detail[2] > 1000);
        Assert.Equal(new PictureArea(26, 0, 5, 8), FrameChoice.PosterArea(new PictureArea(0, 0, 32, 8), detail));

        using var yuv = VideoFrame.Rent(PixelFormat.Nv12, 32, 8);
        Assert.Throws<ArgumentException>(() => FrameChoice.ColumnDetail(yuv, new PictureArea(0, 0, 32, 8), 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameChoice.ColumnDetail(frame, new PictureArea(0, 0, 32, 8), 0));
        Assert.Throws<ArgumentNullException>(() => FrameChoice.ColumnDetail(null!, new PictureArea(0, 0, 32, 8), 4));
    }
}
