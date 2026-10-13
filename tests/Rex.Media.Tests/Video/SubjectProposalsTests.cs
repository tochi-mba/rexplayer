using Rex.Media.Primitives;
using Rex.Media.Video;
using static Rex.Media.TestKit.SubjectScenes;

namespace Rex.Media.Tests.Video;

/// <summary>The boxes a click is turned into: whole objects, wherever the click lands on them.</summary>
public sealed class SubjectProposalsTests
{
    private const int Width = 640;
    private const int Height = 360;

    private static Thing Red(int left, int top, int width, int height) => new(left, top, width, height, 50, 60, 200);

    private static Thing Blue(int left, int top, int width, int height) => new(left, top, width, height, 200, 90, 40);

    public static TheoryData<string, bool, int, int, int, int, int, int> Objects => new()
    {
        // Name, textured background, the object's box, and the click.
        { "small on a wall", false, 300, 160, 24, 28, 312, 174 },
        { "tall on a wall", false, 280, 130, 60, 84, 310, 172 },
        { "large on a wall", false, 220, 90, 150, 170, 295, 175 },
        { "wide on a wall", false, 240, 150, 120, 70, 300, 185 },
        { "small in leaves", true, 300, 160, 26, 28, 313, 174 },
        { "tall in leaves", true, 280, 130, 60, 84, 310, 172 },
        { "large in leaves", true, 220, 90, 150, 170, 295, 175 },
        { "clicked near its corner", false, 280, 130, 60, 84, 287, 140 },
        { "clicked near its edge in leaves", true, 280, 130, 60, 84, 334, 205 },
        { "cut off by the picture's edge", false, 0, 100, 50, 90, 10, 150 },
        { "in the picture's corner", true, 590, 300, 50, 60, 630, 350 },
    };

    [Theory]
    [MemberData(nameof(Objects))]
    public void TheBestBoxIsTheWholeObjectAroundTheClick(string name, bool textured, int left, int top, int width, int height, int x, int y)
    {
        using var picture = Draw(Width, Height, textured, Red(left, top, width, height));
        var proposals = new SubjectProposals();

        Assert.True(proposals.Find(picture, x / (float)Width, y / (float)Height), name);

        var best = proposals.Proposals[0];
        Assert.True(Overlap((best.Left, best.Top, best.Width, best.Height), (left, top, width, height)) > 0.6,
            $"{name}: {best}");
        Assert.InRange(proposals.Count, 1, SubjectProposals.Capacity);
        Assert.True(proposals.Evaluated > proposals.Count);
    }

    [Fact]
    public void ALookalikeBesideTheClickedObjectDoesNotPullTheBoxAcross()
    {
        // Two identical objects, apart by less than half the width of either.
        using var picture = Draw(Width, Height, false, Red(240, 140, 50, 70), Red(320, 140, 50, 70));
        var proposals = new SubjectProposals();

        proposals.Find(picture, 262f / Width, 172f / Height);

        var best = proposals.Proposals[0];
        Assert.True(Overlap((best.Left, best.Top, best.Width, best.Height), (240, 140, 50, 70)) > 0.6, best.ToString());
    }

    [Fact]
    public void AnObjectOfOneFlatColourIsFoundByGrowingFromTheClick()
    {
        using var picture = Draw(Width, Height, false, new Thing(250, 120, 70, 90, 40, 180, 60, Period: 1000));
        var proposals = new SubjectProposals();

        proposals.Find(picture, 300f / Width, 150f / Height);

        Assert.Contains(proposals.Proposals.ToArray(),
            p => Overlap((p.Left, p.Top, p.Width, p.Height), (250, 120, 70, 90)) > 0.8);
    }

    [Fact]
    public void ADarkObjectOnADarkerBackgroundIsStillSeparated()
    {
        using var picture = Draw(Width, Height, false, new Thing(0, 0, Width, Height, 6, 6, 6, Period: 1000),
            new Thing(280, 140, 60, 70, 30, 30, 110));
        var proposals = new SubjectProposals();

        proposals.Find(picture, 305f / Width, 175f / Height);

        var best = proposals.Proposals[0];
        Assert.True(Overlap((best.Left, best.Top, best.Width, best.Height), (280, 140, 60, 70)) > 0.6, best.ToString());
    }

    [Fact]
    public void AHighDefinitionPictureIsReadOnACoarserGridWithTheSameResult()
    {
        using var picture = Draw(1920, 1080, true, Red(900, 400, 180, 260));
        var proposals = new SubjectProposals();

        proposals.Find(picture, 990 / 1920f, 530 / 1080f);

        var best = proposals.Proposals[0];
        Assert.True(Overlap((best.Left, best.Top, best.Width, best.Height), (900, 400, 180, 260)) > 0.6, best.ToString());
    }

    [Fact]
    public void TheSameClickGivesTheSameBoxesAndAllocatesNothingOnceWarm()
    {
        using var picture = Draw(Width, Height, true, Blue(280, 130, 60, 84));
        var proposals = new SubjectProposals();
        proposals.Find(picture, 0.48f, 0.47f);
        var first = proposals.Proposals.ToArray();

        // JIT and thread-local caches can still initialise on a second call on a busy CI
        // worker. Exercise the hot path repeatedly before measuring its allocations.
        for (var warm = 0; warm < 8; warm++)
        {
            proposals.Find(picture, 0.48f, 0.47f);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        proposals.Find(picture, 0.48f, 0.47f);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(first, proposals.Proposals.ToArray());
    }

    [Fact]
    public void AClickOutsideThePictureProposesNothing()
    {
        using var picture = Draw(Width, Height, false, Red(300, 160, 24, 28));
        var proposals = new SubjectProposals();

        Assert.False(proposals.Find(picture, float.NaN, 0.5f));
        Assert.False(proposals.Find(picture, 0.5f, -0.01f));
        Assert.False(proposals.Find(picture, 1.01f, 0.5f));
        Assert.Equal(0, proposals.Count);
        Assert.True(proposals.Proposals.IsEmpty);
    }

    [Fact]
    public void APictureTooSmallForABoxProposesNothing()
    {
        using var picture = Draw(2, 2, false);
        var proposals = new SubjectProposals();

        Assert.False(proposals.Find(picture, 0.5f, 0.5f));
        Assert.Equal(0, proposals.Count);
    }

    [Fact]
    public void OnlyBgraPicturesAreRead()
    {
        using var picture = VideoFrame.Rent(PixelFormat.Nv12, 16, 16);
        var proposals = new SubjectProposals();

        Assert.Throws<ArgumentException>(() => proposals.Find(picture, 0.5f, 0.5f));
        Assert.Throws<ArgumentNullException>(() => proposals.Find(null!, 0.5f, 0.5f));
    }

    [Fact]
    public void ANeighbourCostsRankOnlyWhenTheTrackerCouldConfuseTheTwo()
    {
        Assert.Equal(0.7, SubjectProposals.Rank(0.7, 0.5), 9);
        Assert.Equal(0.7, SubjectProposals.Rank(0.7, 0.8), 9);
        Assert.Equal(0.5, SubjectProposals.Rank(0.7, 0.9), 9);
        Assert.True(SubjectProposals.StandsOut(0.45));
        Assert.False(SubjectProposals.StandsOut(0.44));
        Assert.False(SubjectProposals.IsAmbiguous(0.89));
        Assert.True(SubjectProposals.IsAmbiguous(0.9));
    }

    [Fact]
    public void OverlapIsIntersectionOverUnion()
    {
        Assert.Equal(1, SubjectProposals.Overlap(0, 0, 10, 10, 0, 0, 10, 10), 9);
        Assert.Equal(25.0 / 175, SubjectProposals.Overlap(0, 0, 10, 10, 5, 5, 15, 15), 9);
        Assert.Equal(0, SubjectProposals.Overlap(0, 0, 10, 10, 20, 20, 30, 30), 9);
        Assert.Equal(0, SubjectProposals.Overlap(0, 0, 0, 0, 0, 0, 0, 0), 9);
    }
}
