using Rex.Media.AppCore.Captions;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Captions;

public sealed class PictureTimelineTests
{
    private static PictureSpace FaceAt(int column) =>
        PictureSpace.Read(PictureSpaceTests.Picture((x, y) => x / 10 == column && y < 60 ? ((byte)224, (byte)172, (byte)140) : ((byte)40, (byte)40, (byte)48)), 320, 180, 1280);

    [Fact]
    [Capability("OSD-11")]
    public void AStretchTakesThePictureShowingAsItStartsAndEveryOneWithin()
    {
        var timeline = new PictureTimeline();
        Assert.Null(timeline.Over(0, 5));
        Assert.Equal(double.NegativeInfinity, timeline.ReadTo);

        timeline.Add(1, FaceAt(2));
        timeline.Add(2, FaceAt(15));
        timeline.Add(3, FaceAt(29));
        Assert.Equal(3, timeline.Count);
        Assert.Equal(3, timeline.ReadTo);

        // From 1.5 to 2.5: the picture from 1 still showing, and the one at 2.
        var stretch = timeline.Over(1.5, 2.5)!;
        Assert.Equal(1, stretch.Importance[2]);
        Assert.Equal(1, stretch.Importance[15]);
        Assert.Equal(0, stretch.Importance[29]);

        // Just one picture: itself.
        Assert.Same(timeline.Over(3.5, 4), timeline.Over(3.2, 9));
        // Before anything was read, only the readings within count.
        Assert.Equal(1, timeline.Over(0, 1)!.Importance[2]);
    }

    [Fact]
    public void OldReadingsAreForgottenButTheOneShowingIsKept()
    {
        var timeline = new PictureTimeline();
        for (var at = 0; at < 30; at++)
        {
            timeline.Add(at, PictureSpace.Empty());
        }

        timeline.Forget(25);
        // Kept: the latest of those over ten seconds back (14), then 15 onwards.
        Assert.Equal(16, timeline.Count);
        Assert.NotNull(timeline.Over(14.5, 14.6));
        timeline.Clear();
        Assert.Equal(0, timeline.Count);
        Assert.Throws<ArgumentNullException>(() => timeline.Add(0, null!));
    }
}
