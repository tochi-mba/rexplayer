using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

/// <summary>When Subject Lock asks the optional models, and which of their outlines it accepts.</summary>
public sealed class SubjectModelPolicyTests
{
    private static FrameBudget Measured(params double[] milliseconds)
    {
        var budget = new FrameBudget();
        // The first call warms the model up and is not counted.
        budget.Record(TimeSpan.FromSeconds(60));
        foreach (var value in milliseconds)
        {
            budget.Record(TimeSpan.FromMilliseconds(value));
        }

        return budget;
    }

    private static PictureMask Rectangle(int width, int height, int left, int top, int across, int down, double score = 0.9)
    {
        var values = new byte[width * height];
        for (var y = top; y < top + down; y++)
        {
            values.AsSpan(y * width + left, across).Fill(255);
        }

        return new PictureMask(values, width, height, score);
    }

    [Fact]
    public void AModelWorksOnlyOnAPausedPictureAndOnlyWhileItsWaitIsBearable()
    {
        Assert.Throws<ArgumentNullException>(() => SubjectModelPolicy.UseOnPicture(true, true, null!));
        // Unmeasured: tried once, to learn what it costs here.
        Assert.True(SubjectModelPolicy.UseOnPicture(available: true, paused: true, new FrameBudget()));
        // Seconds a picture, as measured on a laptop with integrated graphics, is a wait worth making.
        Assert.True(SubjectModelPolicy.UseOnPicture(true, true, Measured(4_000, 9_000, 6_500)));
        Assert.False(SubjectModelPolicy.UseOnPicture(true, true, Measured(45_000, 50_000)));
        Assert.False(SubjectModelPolicy.UseOnPicture(true, paused: false, new FrameBudget()));
        Assert.False(SubjectModelPolicy.UseOnPicture(available: false, true, new FrameBudget()));
    }

    [Fact]
    public void ASecondOpinionWhilePlayingNeedsAMeasuredModelWithinTheInteractiveBudget()
    {
        Assert.Throws<ArgumentNullException>(() => SubjectModelPolicy.SecondOpinion(true, null!, 24));
        Assert.False(SubjectModelPolicy.SecondOpinion(true, new FrameBudget(), 24));
        Assert.False(SubjectModelPolicy.SecondOpinion(true, Measured(4_000, 9_000), 24));
        Assert.True(SubjectModelPolicy.SecondOpinion(true, Measured(400, 700), 24));
        Assert.True(SubjectModelPolicy.SecondOpinion(true, Measured(5, 6), 24));
        Assert.False(SubjectModelPolicy.SecondOpinion(false, Measured(5, 6), 24));
    }

    [Fact]
    public void AClickOutlineIsAcceptedOnlyWhenConfidentAndTheSizeOfAnObject()
    {
        Assert.Throws<ArgumentNullException>(() => SubjectModelPolicy.Judge(null!));
        Assert.Equal(MaskVerdict.Accepted, SubjectModelPolicy.Judge(Rectangle(320, 180, 100, 40, 40, 90)));
        Assert.Equal(MaskVerdict.Empty, SubjectModelPolicy.Judge(new PictureMask(new byte[320 * 180], 320, 180, 0.99)));
        Assert.Equal(MaskVerdict.NotConfident, SubjectModelPolicy.Judge(Rectangle(320, 180, 100, 40, 40, 90, score: 0.59)));
        Assert.Equal(MaskVerdict.Accepted, SubjectModelPolicy.Judge(Rectangle(320, 180, 100, 40, 40, 90, score: SubjectModelPolicy.MinimumScore)));
        // A fragment: too few pixels, or a sliver too thin to follow.
        Assert.Equal(MaskVerdict.TooSmall, SubjectModelPolicy.Judge(Rectangle(320, 180, 10, 10, 15, 15)));
        Assert.Equal(MaskVerdict.TooSmall, SubjectModelPolicy.Judge(Rectangle(320, 180, 10, 10, 8, 150)));
        Assert.Equal(MaskVerdict.TooSmall, SubjectModelPolicy.Judge(Rectangle(320, 180, 10, 10, 150, 8)));
        // On a 4K picture the floor is a share of the picture, not a fixed count.
        Assert.Equal(MaskVerdict.TooSmall, SubjectModelPolicy.Judge(Rectangle(3840, 2160, 10, 10, 30, 30)));
        // Most of the picture is the background or the whole scene, not an object in it.
        Assert.Equal(MaskVerdict.TooLarge, SubjectModelPolicy.Judge(Rectangle(320, 180, 0, 0, 320, 100)));
    }
}
