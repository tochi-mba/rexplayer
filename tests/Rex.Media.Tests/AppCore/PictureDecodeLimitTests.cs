using Rex.Media.AppCore.Library;

namespace Rex.Media.Tests.AppCore;

public sealed class PictureDecodeLimitTests
{
    [Theory]
    [InlineData(2400u, 1600u, 320, 320, 0)]
    [InlineData(1600u, 2400u, 320, 0, 320)]
    [InlineData(16000u, 50u, 320, 320, 0)]
    [InlineData(50u, 16000u, 320, 0, 320)]
    [InlineData(1024u, 1024u, 240, 240, 0)]
    [InlineData(0u, 0u, 240, 240, 0)]
    [InlineData(50u, 16000u, 10000, 0, 2048)]
    [InlineData(1u, 1u, -20, 1, 0)]
    public void PreviewsBoundTheLongSideNotJustWidth(uint width, uint height, int requested, int expectedWidth, int expectedHeight)
    {
        Assert.Equal(new PictureDecodeLimit(expectedWidth, expectedHeight),
            PictureDecodeLimit.For(width, height, requested));
    }
}
