using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

public sealed class VideoGeometryTests
{
    [Fact]
    public void WithNeitherPresetTheWholePictureShowsAtItsOwnShape()
    {
        var (source, across, down) = VideoGeometry.Shape(720, 576, new Rational(16, 15), null, null);

        Assert.Equal(SourceRect.Full, source);
        Assert.Equal((720L * 16, 576L * 15), (across, down));
    }

    [Fact]
    [Capability("VID-05")]
    public void AnAspectRatioStretchesTheWholePicture()
    {
        var (source, across, down) = VideoGeometry.Shape(640, 480, new Rational(1, 1), new Rational(16, 9), null);

        Assert.Equal(SourceRect.Full, source);
        Assert.Equal((16L, 9L), (across, down));
        Assert.Equal((0, 60, 1280, 720), VideoLayout.FitAspect(across, down, 1280, 840));
    }

    [Fact]
    [Capability("VID-06")]
    public void ACropWiderThanThePictureCutsTheTopAndBottomEqually()
    {
        var (source, across, down) = VideoGeometry.Shape(1920, 1080, new Rational(1, 1), null, new Rational(235, 100));

        Assert.Equal((47L, 20L), (across, down));
        Assert.Equal(0, source.Left);
        Assert.Equal(1, source.Right);
        Assert.Equal(1 - source.Bottom, source.Top, 6);
        Assert.Equal((16.0 / 9) / 2.35, source.Bottom - source.Top, 5);
    }

    [Fact]
    public void ACropNarrowerThanThePictureCutsTheSidesEqually()
    {
        var (source, across, down) = VideoGeometry.Shape(1920, 1080, new Rational(1, 1), null, new Rational(4, 3));

        Assert.Equal((4L, 3L), (across, down));
        Assert.Equal((0f, 1f), (source.Top, source.Bottom));
        Assert.Equal((4.0 / 3) / (16.0 / 9), source.Right - source.Left, 5);
        Assert.Equal(1 - source.Right, source.Left, 6);
    }

    [Fact]
    public void ACropAppliesToTheShapeAnAspectRatioGave()
    {
        var (source, _, _) = VideoGeometry.Shape(640, 480, new Rational(1, 1), new Rational(16, 9), new Rational(16, 9));

        Assert.Equal(SourceRect.Full with { Top = 0, Bottom = 1 }, source with { Top = 0, Bottom = 1 });
        Assert.Equal(1f, source.Right - source.Left, 5);
    }

    [Fact]
    public void ImpossibleValuesFallBackToSomethingSensible()
    {
        Assert.Equal((SourceRect.Full, 1L, 1L), VideoGeometry.Shape(0, 0, new Rational(0, 1), new Rational(0, 1), new Rational(0, 1)));
        Assert.Equal((0, 0, 10, 0), VideoLayout.FitAspect(0, 1, 10, 0));
    }

    [Fact]
    public void ThePresetsCycleAndStartWithThePicturesOwn()
    {
        Assert.Null(VideoGeometry.AspectRatios[0].Ratio);
        Assert.Null(VideoGeometry.Crops[0].Ratio);
        Assert.Equal("16:9", VideoGeometry.Next(VideoGeometry.AspectRatios, VideoGeometry.AspectRatios[0]).Name);
        Assert.Equal("None", VideoGeometry.Next(VideoGeometry.Crops, VideoGeometry.Crops[^1]).Name);
        Assert.Equal("Default", VideoGeometry.Next(VideoGeometry.AspectRatios, new ShapePreset("Gone", new Rational(7, 3))).Name);
        Assert.Throws<ArgumentNullException>(() => VideoGeometry.Next(null!, VideoGeometry.Crops[0]));
    }
}
