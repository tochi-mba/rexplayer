using Rex.Media.Engine;
using Rex.Media.Primitives;

namespace Rex.Media.Tests.Engine;

public sealed class FirstPictureTests
{
    private static MediaTime Ms(int milliseconds) => MediaTime.FromMilliseconds(milliseconds);

    [Theory]
    [InlineData(false, false, 1000, 1000, true)]
    [InlineData(false, false, 1500, 1000, true)]
    [InlineData(false, false, 1501, 1000, false)]
    [InlineData(false, true, 9000, 1000, true)]
    [InlineData(true, true, 1000, 1000, false)]
    [InlineData(true, false, 1000, 1000, false)]
    [InlineData(false, false, 400, 1000, true)]
    public void TheFirstPictureIsShownAtOnceWhenItIsDueSoonOrTheClockIsHeld(bool shownBefore, bool paused, int due, int now, bool expected)
    {
        Assert.Equal(expected, FirstPicture.ShowAtOnce(shownBefore, paused, Ms(due), Ms(now)));
    }

    [Theory]
    [InlineData(false, 10_000, 18_000, true)]
    [InlineData(false, 17_750, 18_000, true)]
    [InlineData(false, 17_751, 18_000, false)]
    [InlineData(true, 10_000, 18_000, false)]
    public void AKeyframeFarFromAPreciseTargetIsShownOnTheWay(bool shownBefore, int end, int target, bool expected)
    {
        Assert.Equal(expected, FirstPicture.ShowOnTheWay(shownBefore, Ms(end), Ms(target)));
    }

    [Fact]
    public void TheLeadIsAboutTheTimeTheSoundTakesToStart()
    {
        Assert.Equal(Ms(500), FirstPicture.Lead);
        Assert.Equal(Ms(250), FirstPicture.PreviewDistance);
    }
}
