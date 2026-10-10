using Rex.Media.TestKit;
using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

/// <summary>Zooming into the picture and moving about in it, without ever leaving it (VID-07).</summary>
public sealed class PictureViewTests
{
    [Fact]
    [Capability("VID-07")]
    public void TheWholePictureIsTheStartAndTheViewStaysInsideThePicture()
    {
        Assert.Equal((1d, 0.5, 0.5), (PictureView.Whole.Zoom, PictureView.Whole.CenterX, PictureView.Whole.CenterY));
        Assert.False(PictureView.Whole.IsZoomed);
        Assert.Equal((0d, 0d, 1d, 1d), PictureView.Whole.Visible);

        // A centre too near an edge is moved in until the view fits; the zoom keeps between 1 and 16.
        var corner = new PictureView(4, 0, 1);
        Assert.Equal((0.125, 0.875), (corner.CenterX, corner.CenterY));
        Assert.Equal(PictureView.MaxZoom, new PictureView(100, 0.5, 0.5).Zoom);
        Assert.Equal(1, new PictureView(0.2, 0.9, 0.1).Zoom);
        Assert.Equal((1d, 0.5, 0.5), (new PictureView(double.NaN, double.NaN, double.PositiveInfinity).Zoom, new PictureView(2, double.NaN, 0.5).CenterX, new PictureView(2, 0.5, double.NaN).CenterY));
    }

    [Fact]
    [Capability("VID-07")]
    public void ZoomingAtAPointKeepsThatPointWhereItIs()
    {
        // Zoomed in twice at the top left quarter of the screen: the point there stays there.
        var view = PictureView.Whole.ZoomAt(2, 0.25, 0.25);
        Assert.Equal((2d, 0.375, 0.375), (view.Zoom, view.CenterX, view.CenterY));
        var (left, top, width, _) = view.Visible;
        Assert.Equal(0.25, left + (0.25 * width), 10);
        Assert.Equal(0.25, top + (0.25 * width), 10);

        // Out again, past the whole picture, is the whole picture.
        Assert.Equal(PictureView.Whole, view.ZoomAt(0.25, 0.9, 0.1));
        Assert.Equal(PictureView.MaxZoom, view.ZoomAt(1000, 2, -1).Zoom);
    }

    [Fact]
    [Capability("VID-07")]
    public void TheKeysStepThroughTheZoomsAndMoveTheView()
    {
        Assert.Equal(1.5, PictureView.Whole.Step(1).Zoom);
        Assert.Equal(2, PictureView.Whole.Step(1).Step(1).Zoom);
        Assert.Equal(1, new PictureView(3, 0.5, 0.5).Step(-1).Step(-1).Step(-1).Step(-1).Zoom);
        Assert.Equal(PictureView.MaxZoom, new PictureView(16, 0.5, 0.5).Step(1).Zoom);
        Assert.Equal(new PictureView(3, 0.4, 0.4), new PictureView(3, 0.4, 0.4).Step(0));

        // Dragging the picture right by half a view looks left by half a view.
        var dragged = new PictureView(2, 0.5, 0.5).PanBy(0.5, -0.5);
        Assert.Equal((0.25, 0.75), (dragged.CenterX, dragged.CenterY));
        Assert.Equal((0.75, 0.25), (dragged.CenteredOn(0.9, 0.1).CenterX, dragged.CenteredOn(0.9, 0.1).CenterY));
    }

    [Fact]
    [Capability("VID-07")]
    public void TheViewIsAPartOfTheCroppedPicture()
    {
        var cropped = new SourceRect(0.1f, 0.2f, 0.9f, 0.8f);

        Assert.Equal(cropped, PictureView.Whole.Within(cropped));
        var quarter = new PictureView(2, 0.25, 0.75).Within(cropped);
        Assert.Equal(0.1f, quarter.Left, 5);
        Assert.Equal(0.5f, quarter.Top, 5);
        Assert.Equal(0.5f, quarter.Right, 5);
        Assert.Equal(0.8f, quarter.Bottom, 5);
    }

    [Fact]
    [Capability("VID-07")]
    public void FollowingASubjectEasesIntoViewWithContextAndNeverLeavesTheFrame()
    {
        var view = PictureView.Whole;
        for (var frame = 0; frame < 20; frame++)
        {
            view = view.Follow(0.85, 0.15, 0.12, 0.2);
        }

        Assert.InRange(view.Zoom, 1.8, 1.851);
        Assert.InRange(view.CenterX, 0.72, 0.74);
        Assert.InRange(view.CenterY, 0.26, 0.28);
        var (left, top, width, height) = view.Visible;
        Assert.InRange(left, 0, 1 - width);
        Assert.InRange(top, 0, 1 - height);

        // A large selection stays entirely in view rather than zooming through it.
        var large = PictureView.Whole.Follow(0.5, 0.5, 0.9, 0.9);
        Assert.Equal(PictureView.Whole, large);
    }

    [Fact]
    [Capability("VID-07")]
    public void FollowHoldsSteadyAgainstSmallLocationNoiseAndRespondsToRealMovement()
    {
        var stable = new PictureView(1.7, 0.5, 0.5);
        for (var frame = 0; frame < 80; frame++)
        {
            stable = stable.Follow(frame % 2 == 0 ? 0.511 : 0.489, 0.49, 0.15, 0.15);
        }

        Assert.Equal(0.5, stable.CenterX);
        Assert.Equal(0.5, stable.CenterY);
        Assert.InRange(stable.Zoom, 1.849, 1.851);

        var moved = stable.Follow(0.82, 0.18, 0.15, 0.15);
        Assert.True(moved.CenterX > stable.CenterX);
        Assert.True(moved.CenterY < stable.CenterY);
        Assert.True(moved.CenterX < 0.82);
        Assert.True(moved.CenterY > 0.18);
    }

    [Fact]
    [Capability("VID-07")]
    public void ASubjectLossGraduallyRevealsTheWholePictureBeforeReturnZoom()
    {
        var view = new PictureView(1.85, 0.72, 0.27);
        var first = view.Reveal();
        Assert.InRange(first.Zoom, 1.5, 1.7);
        Assert.InRange(first.CenterX, 0.5, view.CenterX);
        for (var i = 0; i < 24; i++)
        {
            view = view.Reveal();
        }

        Assert.InRange(view.Zoom, 1, 1.001);
        Assert.InRange(view.CenterX, 0.499, 0.501);
        Assert.InRange(view.CenterY, 0.499, 0.501);
        var returning = view.Follow(0.78, 0.42, 0.18, 0.2);
        Assert.True(returning.Zoom > view.Zoom);
        Assert.True(returning.CenterX > view.CenterX);
    }

    [Fact]
    [Capability("VID-07")]
    public void TheNavigatorSitsSmallInTheTopLeftAtThePicturesShape()
    {
        // A wide window: a fifth of its width, as wide as the picture is.
        var (x, y, width, height) = PictureView.Navigator(2000, 1000, 16, 9);
        Assert.Equal((20d, 20d), (x, y));
        Assert.Equal(400, width, 6);
        Assert.Equal(225, height, 6);

        // A tall picture in a short window: a quarter of the height instead.
        var tall = PictureView.Navigator(1000, 400, 9, 16);
        Assert.Equal(100, tall.Height, 6);
        Assert.Equal(56.25, tall.Width, 6);

        // An unknown shape is taken as 16 : 9.
        Assert.Equal(PictureView.Navigator(1000, 1000, 16, 9), PictureView.Navigator(1000, 1000, 0, 0));
    }
}
