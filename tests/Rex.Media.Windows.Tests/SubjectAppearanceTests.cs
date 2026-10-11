using System.Runtime.Versioning;
using Rex.Media.Primitives;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests;

[SupportedOSPlatform("windows8.0")]
public sealed class SubjectAppearanceTests
{
    private static VideoFrame Picture(int size, double seconds, int x = 30, int y = 20, int height = -1)
    {
        if (height < 0) height = size;
        var frame = VideoFrame.Rent(PixelFormat.Bgra32, 160, 100);
        frame.Pts = MediaTime.FromSeconds(seconds);
        for (var row = 0; row < frame.Height; row++)
        {
            var pixels = frame.Row(0, row);
            for (var col = 0; col < frame.Width; col++)
            {
                var item = col >= x && col < x + size && row >= y && row < y + height;
                var sx = item ? (col - x) * 16 / size : 0;
                var sy = item ? (row - y) * 16 / height : 0;
                pixels[col * 4] = (byte)(item ? 130 + sx % 3 * 20 : 25);
                pixels[col * 4 + 1] = (byte)(item ? 30 + sy % 4 * 20 : 40);
                pixels[col * 4 + 2] = (byte)(item ? 210 : 60);
                pixels[col * 4 + 3] = 255;
            }
        }

        return frame;
    }

    [Fact]
    public void LocalTrackingAdjustsItsBoxWhenTheSubjectWalksAway()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 160f, 20 / 100f, 32 / 160f, 32 / 100f);
        for (var size = 32; size >= 16; size--)
        {
            using var frame = Picture(size, 1 + (32 - size) * 0.04);
            tracker.Process(frame);
            Assert.True(tracker.Tracking, $"{tracker.Status} {tracker.Confidence} {tracker.Region}");
            Assert.InRange(tracker.Region.Width * 160, size - 3, size + 3);
            Assert.InRange(tracker.Region.Left * 160, 28, 32);
        }
    }

    [Theory]
    [InlineData(11)]
    [InlineData(17)]
    [InlineData(19)]
    [InlineData(23)]
    [InlineData(29)]
    public void ReturnSizesBetweenTheOldSearchLevelsAreLocated(int size)
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 160f, 20 / 100f, 16 / 160f, 16 / 100f);
        using var first = Picture(16, 1);
        tracker.Process(first);
        using var missing = Picture(0, 1.04);
        tracker.Process(missing);
        Assert.False(tracker.Tracking);
        for (var i = 0; i < 12; i++)
        {
            using var returned = Picture(size, 1.08 + i * 0.04, 100, 50);
            tracker.Process(returned);
        }

        Assert.True(tracker.Tracking, $"{tracker.Status} {tracker.Confidence} {tracker.Region}");
        Assert.InRange(tracker.Region.Left * 160, 97, 103);
        Assert.InRange(tracker.Region.Top * 100, 47, 53);
        Assert.InRange(tracker.Region.Width * 160, size - 3, size + 3);
    }
    [Fact]
    public void ReturningTextureDoesNotRequireItsOriginalBackgroundContrast()
    {
        using var tracker = new SubjectEditSession();
        tracker.Select(30 / 160f, 20 / 100f, 16 / 160f, 16 / 100f);
        using var first = Picture(16, 1);
        tracker.Process(first);
        using var missing = Picture(0, 1.04);
        tracker.Process(missing);
        Assert.False(tracker.Tracking);
        for (var i = 0; i < 8; i++)
        {
            using var returned = Picture(16, 1.08 + i * 0.04, 100, 50);
            for (var y = 0; y < returned.Height; y++)
            {
                var row = returned.Row(0, y);
                for (var x = 0; x < returned.Width; x++)
                {
                    if (x >= 100 && x < 116 && y >= 50 && y < 66) continue;
                    row[x * 4] = 150;
                    row[x * 4 + 1] = 60;
                    row[x * 4 + 2] = 210;
                }
            }
            tracker.Process(returned);
        }
        Assert.True(tracker.Tracking, tracker.Status);
        Assert.InRange(tracker.Region.Left * 160, 97, 103);
        Assert.InRange(tracker.Region.Top * 100, 47, 53);
        Assert.InRange(tracker.Region.Width * 160, 13, 19);
    }
    [Fact]
    public void RecoveryDoesNotSquashAnOversizedTallTemplateToFitTheFrame()
    {
        using var tracker = new SubjectEditSession();
        tracker.Select(30 / 160f, 20 / 100f, 16 / 160f, 64 / 100f);
        using var first = Picture(16, 1, height: 64);
        tracker.Process(first);
        using var missing = Picture(0, 1.04);
        tracker.Process(missing);
        Assert.False(tracker.Tracking);
        for (var i = 0; i < 8; i++)
        {
            using var distorted = Picture(40, 1.08 + i * 0.04, 100, 0, 100);
            tracker.Process(distorted);
        }
        Assert.False(tracker.Tracking);
    }
}
