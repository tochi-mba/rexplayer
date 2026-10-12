using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class InpaintTensorsTests
{
    [Fact]
    public void ThePictureBecomesRedGreenBluePlanesOfBytes()
    {
        byte[] pixels = [1, 2, 3, 255, 4, 5, 6, 255, 0, 0];
        var tensor = new byte[6];

        InpaintTensors.Image(new BgraPicture(pixels, 2, 1, 10), tensor);

        Assert.Equal([3, 6, 2, 5, 1, 4], tensor);
        Assert.Throws<ArgumentException>(() => InpaintTensors.Image(new BgraPicture(pixels, 2, 1, 10), new byte[5]));
    }

    [Fact]
    public void TheHoleIsGrownBySquareNeighbourhoods()
    {
        var values = new byte[7 * 5];
        values[(2 * 7) + 3] = 255;
        var hole = new PictureMask(values, 7, 5, 1);
        var exact = new byte[35];
        var grown = new byte[35];

        InpaintTensors.Hole(hole, 0, exact);
        InpaintTensors.Hole(hole, 1, grown);

        Assert.Equal(34, exact.Count(value => value == InpaintTensors.Kept));
        Assert.Equal(0, exact[(2 * 7) + 3]);
        Assert.Equal(26, grown.Count(value => value == InpaintTensors.Kept));
        Assert.Equal(0, grown[(1 * 7) + 2]);
        Assert.Equal(0, grown[(3 * 7) + 4]);
        Assert.Equal(InpaintTensors.Kept, grown[(2 * 7) + 5]);
    }

    [Fact]
    public void AGrowthLongerThanTheRowStillWorks()
    {
        var values = new byte[3];
        values[0] = 255;
        var tensor = new byte[3];

        InpaintTensors.Hole(new PictureMask(values, 3, 1, 1), 10, tensor);

        Assert.Equal([0, 0, 0], tensor);
    }

    [Fact]
    public void TheHoleChecksItsArguments()
    {
        var hole = new PictureMask(new byte[4], 2, 2, 1);

        Assert.Throws<ArgumentNullException>(() => InpaintTensors.Hole(null!, 1, new byte[4]));
        Assert.Throws<ArgumentOutOfRangeException>(() => InpaintTensors.Hole(hole, -1, new byte[4]));
        Assert.Throws<ArgumentException>(() => InpaintTensors.Hole(hole, 1, new byte[3]));
    }

    [Fact]
    public void OnlyPixelsInTheHoleChange()
    {
        byte[] pixels = [1, 2, 3, 200, 4, 5, 6, 201, 9, 9];
        byte[] result = [10, 20, 30, 40, 50, 60];
        byte[] mask = [InpaintTensors.Kept, 0];
        var output = new byte[10];

        InpaintTensors.Compose(new BgraPicture(pixels, 2, 1, 10), result, mask, output);

        Assert.Equal([1, 2, 3, 200, 60, 40, 20, 201, 9, 9], output);
    }

    [Fact]
    public void ComposingChecksItsArguments()
    {
        var pixels = new byte[8];
        var picture = new BgraPicture(pixels, 2, 1, 8);

        Assert.Throws<ArgumentException>(() => InpaintTensors.Compose(new BgraPicture(pixels, 2, 1, 8), new byte[5], new byte[2], new byte[8]));
        Assert.Throws<ArgumentException>(() => InpaintTensors.Compose(new BgraPicture(pixels, 2, 1, 8), new byte[6], new byte[1], new byte[8]));
        Assert.Throws<ArgumentException>(() => InpaintTensors.Compose(new BgraPicture(pixels, 2, 1, 8), new byte[6], new byte[2], new byte[7]));
        Assert.Equal(2, picture.Width);
    }
}
