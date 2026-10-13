using System.Runtime.Versioning;
using Rex.Media.Primitives;
using Rex.Media.Video;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests;

/// <summary>A loose selection includes a speckled pillar as well as the moving dark subject.</summary>
[SupportedOSPlatform("windows8.0")]
public sealed class SubjectTrackingWallTests
{
    private static VideoFrame Picture(int figureLeft, double seconds)
    {
        var frame = VideoFrame.Rent(PixelFormat.Bgra32, 320, 180);
        frame.Pts = MediaTime.FromSeconds(seconds);
        for (var y = 0; y < frame.Height; y++)
        {
            var row = frame.Row(0, y);
            for (var x = 0; x < frame.Width; x++)
            {
                var at = x * 4;
                var pillar = x >= 160 && x < 202;
                var jacket = x >= figureLeft && x < figureLeft + 47 && y >= 65 && y < 155;
                var head = x >= figureLeft + 12 && x < figureLeft + 34 && y >= 42 && y < 70;
                var grey = (byte)(84 + (x * 7 + y * 11) % 9);
                var speckle = (byte)(26 + (x * 23 + y * 17) % 64);
                // The jacket has a faint seam, while the stationary pillar carries more texture.
                (byte B, byte G, byte R) colour = head ? ((byte)76, (byte)108, (byte)151)
                    : jacket ? ((byte)(20 + (x - figureLeft == 25 ? 9 : 0)), (byte)21, (byte)23)
                    : pillar ? (speckle, speckle, speckle) : (grey, grey, grey);
                (row[at], row[at + 1], row[at + 2], row[at + 3]) = (colour.B, colour.G, colour.R, 255);
            }
        }

        return frame;
    }

    [Fact]
    public void TrackingFollowsTheMovingSubjectInsteadOfTheSpeckledPillar()
    {
        using var tracker = new SubjectEditSession();
        // The pillar is intentionally inside the loose selection, as in the reported failure.
        tracker.Select(94 / 320f, 38 / 180f, 103 / 320f, 120 / 180f);
        using (var first = Picture(102, 1))
        {
            tracker.Process(first);
        }

        Assert.True(tracker.Tracking, tracker.Status);
        for (var i = 1; i <= 5; i++)
        {
            using var frame = Picture(102 - 6 * i, 1 + i * 0.04);
            tracker.Process(frame);
            Assert.True(tracker.Tracking, tracker.Status);
        }

        Assert.True(tracker.Region.Left * 320 < 83, $"Tracked {tracker.Region}, not the moving subject.");
    }
}
