using Rex.Media.Primitives;

namespace Rex.Media.Tests.Primitives;

public sealed class ColorCodeTests
{
    [Theory]
    [InlineData(1, ColorPrimaries.Bt709)]
    [InlineData(5, ColorPrimaries.Bt601Pal)]
    [InlineData(6, ColorPrimaries.Bt601Ntsc)]
    [InlineData(7, ColorPrimaries.Bt601Ntsc)]
    [InlineData(9, ColorPrimaries.Bt2020)]
    [InlineData(12, ColorPrimaries.DisplayP3)]
    [InlineData(2, ColorPrimaries.Unspecified)]
    public void PrimariesFollowTheCodePoints(int code, ColorPrimaries primaries)
    {
        Assert.Equal(primaries, ColorInfo.FromCodes(code, 2, 2, false).Primaries);
    }

    [Theory]
    [InlineData(1, ColorTransfer.Bt709)]
    [InlineData(6, ColorTransfer.Bt709)]
    [InlineData(14, ColorTransfer.Bt709)]
    [InlineData(15, ColorTransfer.Bt709)]
    [InlineData(8, ColorTransfer.Linear)]
    [InlineData(13, ColorTransfer.Srgb)]
    [InlineData(16, ColorTransfer.Pq)]
    [InlineData(18, ColorTransfer.Hlg)]
    [InlineData(2, ColorTransfer.Unspecified)]
    public void TransfersFollowTheCodePoints(int code, ColorTransfer transfer)
    {
        Assert.Equal(transfer, ColorInfo.FromCodes(2, code, 2, false).Transfer);
    }

    [Theory]
    [InlineData(0, ColorMatrix.Rgb)]
    [InlineData(1, ColorMatrix.Bt709)]
    [InlineData(5, ColorMatrix.Bt601)]
    [InlineData(6, ColorMatrix.Bt601)]
    [InlineData(9, ColorMatrix.Bt2020NonConstant)]
    [InlineData(10, ColorMatrix.Bt2020NonConstant)]
    [InlineData(2, ColorMatrix.Unspecified)]
    public void MatricesFollowTheCodePoints(int code, ColorMatrix matrix)
    {
        Assert.Equal(matrix, ColorInfo.FromCodes(2, 2, code, false).Matrix);
    }

    [Fact]
    public void TheRangeIsCarriedAndHdrIsRecognised()
    {
        var hdr = ColorInfo.FromCodes(9, 16, 9, true);

        Assert.True(hdr.FullRange);
        Assert.True(hdr.IsHdr);
        Assert.False(ColorInfo.FromCodes(1, 1, 1, false).IsHdr);
    }
}
