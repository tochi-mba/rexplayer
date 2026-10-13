using System.Runtime.Versioning;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Video.D3D11;
using static Rex.Media.TestKit.SubjectScenes;

namespace Rex.Media.Windows.Tests;

/// <summary>Click to lock: a click becomes the box around it the tracker can follow best, or a truthful refusal.</summary>
[SupportedOSPlatform("windows8.0")]
public sealed class SubjectClickSelectionTests
{
    private const int Width = 640;
    private const int Height = 360;

    private static Thing Red(int left, int top, int width, int height) => new(left, top, width, height, 50, 60, 200);

    private static Thing Yellow(int left, int top, int width, int height) => new(left, top, width, height, 40, 190, 220);

    private static double Fit(SubjectEditSession session, int left, int top, int width, int height) =>
        Overlap((session.Region.Left * Width, session.Region.Top * Height, session.Region.Width * Width, session.Region.Height * Height),
            (left, top, width, height));

    public static TheoryData<string, bool, int, int, int, int, int, int> Objects => new()
    {
        { "small on a wall", false, 300, 160, 24, 28, 312, 174 },
        { "tall on a wall", false, 280, 130, 60, 84, 310, 172 },
        { "large on a wall", false, 220, 90, 150, 170, 295, 175 },
        { "small in leaves", true, 300, 160, 26, 28, 313, 174 },
        { "tall in leaves", true, 280, 130, 60, 84, 310, 172 },
        { "large in leaves", true, 220, 90, 150, 170, 295, 175 },
        { "clicked near its corner", false, 280, 130, 60, 84, 287, 140 },
        { "clicked near its edge in leaves", true, 280, 130, 60, 84, 334, 205 },
        { "cut off by the picture's edge", false, 0, 100, 50, 90, 10, 150 },
    };

    [Theory]
    [MemberData(nameof(Objects))]
    public void AClickLocksTheWholeObjectAroundIt(string name, bool textured, int left, int top, int width, int height, int x, int y)
    {
        using var picture = Draw(Width, Height, textured, Red(left, top, width, height));
        var session = new SubjectEditSession();

        Assert.True(session.SelectAround(x / (float)Width, y / (float)Height));
        Assert.True(session.Locked);
        Assert.False(session.Tracking);
        Assert.StartsWith("Acquiring", session.Status, StringComparison.Ordinal);
        Assert.Null(session.Process(picture));

        Assert.True(session.Tracking, $"{name}: {session.Status}");
        Assert.True(Fit(session, left, top, width, height) > 0.6, $"{name}: {session.Region}");
        Assert.True(session.LastSelectionMilliseconds > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALookalikeBesideTheClickedObjectIsNotChosen(bool textured)
    {
        using var picture = Draw(Width, Height, textured, Red(240, 140, 50, 70), Red(320, 140, 50, 70));
        var session = new SubjectEditSession();

        session.SelectAround(345f / Width, 172f / Height);
        session.Process(picture);

        Assert.True(session.Tracking, session.Status);
        Assert.True(Fit(session, 320, 140, 50, 70) > 0.6, session.Region.ToString());
    }

    [Fact]
    public void AClickOnAPlainWallIsRefusedAndSaysWhy()
    {
        using var picture = Draw(Width, Height, false, Red(40, 40, 50, 50));
        var session = new SubjectEditSession();

        Assert.True(session.SelectAround(0.6f, 0.6f));
        Assert.Null(session.Process(picture));

        Assert.False(session.Locked);
        Assert.False(session.Tracking);
        Assert.StartsWith("Nothing to lock around the click", session.Status, StringComparison.Ordinal);
        // The refused click is forgotten: the next picture does not lock anything either.
        Assert.Null(session.Process(picture));
        Assert.False(session.Locked);
    }

    [Fact]
    public void AClickOnLeavesWithNothingInThemIsRefused()
    {
        using var picture = Draw(Width, Height, true);
        var session = new SubjectEditSession();

        session.SelectAround(0.5f, 0.5f);
        session.Process(picture);

        Assert.False(session.Locked);
        Assert.StartsWith("Nothing to lock around the click", session.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void AClickOnARepeatingPatternIsRefusedAsAmbiguous()
    {
        var tiles = new List<Thing>();
        for (var y = 0; y < Height; y += 30)
        {
            for (var x = 0; x < Width; x += 30)
            {
                tiles.Add(new Thing(x, y, 26, 26, 200, 90, 40));
            }
        }

        using var picture = Draw(Width, Height, false, [.. tiles]);
        var session = new SubjectEditSession();

        session.SelectAround(0.5f, 0.5f);
        session.Process(picture);

        Assert.False(session.Locked);
        Assert.StartsWith("Similar-looking regions around the click", session.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void AClickOutsideThePictureIsRefusedAtOnce()
    {
        var session = new SubjectEditSession();

        Assert.False(session.SelectAround(float.NaN, 0.5f));
        Assert.False(session.SelectAround(0.5f, 1.5f));
        Assert.False(session.Locked);
        Assert.Equal("Click inside the picture to select a subject.", session.Status);
    }

    [Fact]
    public void APausedRedrawSettlesTheClickOnThePictureShown()
    {
        using var picture = Draw(Width, Height, false, Yellow(280, 130, 60, 84));
        var session = new SubjectEditSession();

        session.SelectAround(310f / Width, 172f / Height);
        Assert.Null(session.Process(picture, advanceTracking: false));

        Assert.True(session.Tracking, session.Status);
        Assert.True(Fit(session, 280, 130, 60, 84) > 0.6, session.Region.ToString());
    }

    [Fact]
    public void ADrawnBoxReplacesAPendingClick()
    {
        using var picture = Draw(Width, Height, false, Yellow(280, 130, 60, 84), Red(40, 40, 50, 50));
        var session = new SubjectEditSession();

        session.SelectAround(310f / Width, 172f / Height);
        session.Select(40f / Width, 40f / Height, 50f / Width, 50f / Height);
        session.Process(picture);

        Assert.True(session.Tracking);
        Assert.True(Fit(session, 40, 40, 50, 50) > 0.95, session.Region.ToString());
    }

    [Fact]
    public void TheChosenBoxIsFollowedAsTheObjectMoves()
    {
        var session = new SubjectEditSession();
        session.SelectAround(310f / Width, 172f / Height);
        for (var step = 0; step < 12; step++)
        {
            using var picture = Draw(Width, Height, true, Yellow(280 + step * 3, 130 + step, 60, 84));
            picture.Pts = MediaTime.FromSeconds(1 + step / 25.0);
            session.Process(picture);
            Assert.True(session.Tracking, $"step {step}: {session.Status}");
        }

        Assert.True(Fit(session, 280 + 33, 130 + 11, 60, 84) > 0.6, session.Region.ToString());
    }
}
