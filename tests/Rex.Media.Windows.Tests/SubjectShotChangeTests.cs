using System.Runtime.Versioning;
using Rex.Media.Primitives;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests;

/// <summary>
/// A cut to another shot ends local tracking; a camera pan, a moving subject or a light coming on
/// does not. Pixel difference alone cannot tell a pan from a cut, so the picture's histogram must
/// move as well.
/// </summary>
[SupportedOSPlatform("windows8.0")]
public sealed class SubjectShotChangeTests
{
    private const int Width = 320;
    private const int Height = 180;
    private const int SubjectLeft = 140;
    private const int SubjectTop = 70;
    private const int SubjectSize = 40;

    /// <summary>
    /// A picture of a textured backdrop seen through a camera panned by <paramref name="pan"/>
    /// pixels, with the subject held in the middle, as a camera following it shows it.
    /// </summary>
    private static VideoFrame Shot(int frameIndex, int pan = 0, int scene = 0, int lighting = 0, bool subject = true)
    {
        var frame = VideoFrame.Rent(PixelFormat.Bgra32, Width, Height);
        frame.Pts = MediaTime.FromSeconds(1 + frameIndex / 25.0);
        for (var y = 0; y < Height; y++)
        {
            var row = frame.Row(0, y);
            for (var x = 0; x < Width; x++)
            {
                var i = x * 4;
                int b, g, r;
                if (subject && x >= SubjectLeft && x < SubjectLeft + SubjectSize && y >= SubjectTop && y < SubjectTop + SubjectSize)
                {
                    // A striped, two-coloured object unlike either backdrop.
                    var stripe = ((x - SubjectLeft) / 5 + (y - SubjectTop) / 8) % 2 == 0;
                    (b, g, r) = stripe ? (200, 60, 150) : (40, 140, 240);
                }
                else
                {
                    var tone = Noise((x + pan) / 6, y / 6, scene);
                    (b, g, r) = scene == 0
                        ? (90 + tone / 2, 110 + tone / 3, 40 + tone / 5)
                        : (30 + tone / 5, 90 + tone / 3, 120 + tone / 2);
                }

                row[i] = (byte)Math.Clamp(b + lighting, 0, 255);
                row[i + 1] = (byte)Math.Clamp(g + lighting, 0, 255);
                row[i + 2] = (byte)Math.Clamp(r + lighting, 0, 255);
                row[i + 3] = 255;
            }
        }

        return frame;
    }

    private static int Noise(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177;
            return (int)((h ^ (h >> 16)) % 200);
        }
    }

    private static SubjectEditSession Locked()
    {
        var session = new SubjectEditSession();
        session.Select(SubjectLeft / (float)Width, SubjectTop / (float)Height,
            SubjectSize / (float)Width, SubjectSize / (float)Height);
        using var first = Shot(0);
        session.Process(first)?.Dispose();
        Assert.True(session.Tracking, session.Status);
        return session;
    }

    [Fact]
    public void APanningCameraIsNotACutAndTrackingHolds()
    {
        using var session = Locked();
        for (var index = 1; index <= 20; index++)
        {
            using var frame = Shot(index, pan: index * 7);
            session.Process(frame)?.Dispose();
            Assert.False(session.IsCut, $"Picture {index}: pan read as a cut ({session.PictureChange:F2}, {session.HistogramChange:F2}).");
            Assert.True(session.Tracking, $"Picture {index}: {session.Status}");
        }
    }

    [Fact]
    public void ALightComingOnIsNotACut()
    {
        using var session = Locked();
        using var brighter = Shot(1, lighting: 45);
        session.Process(brighter)?.Dispose();
        Assert.False(session.IsCut, $"{session.PictureChange:F2}, {session.HistogramChange:F2}");
    }

    [Fact]
    public void ACutToAnotherShotEndsLocalTrackingInsteadOfFollowingWhateverIsUnderTheBox()
    {
        using var session = Locked();
        using var steady = Shot(1);
        session.Process(steady)?.Dispose();
        Assert.True(session.Tracking);

        // The reverse shot: another place entirely, and the subject is not in it.
        using var cut = Shot(2, scene: 1, subject: false);
        session.Process(cut)?.Dispose();
        Assert.True(session.IsCut, $"{session.PictureChange:F2}, {session.HistogramChange:F2}");
        Assert.False(session.Tracking);
        Assert.True(session.Locked, "A cut keeps the selection so the subject can be found again.");
        Assert.Contains("shot changed", session.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ANewSelectionForgetsAnEarlierCut()
    {
        using var session = Locked();
        using var cut = Shot(1, scene: 1, subject: false);
        session.Process(cut)?.Dispose();
        Assert.True(session.IsCut);

        session.Select(SubjectLeft / (float)Width, SubjectTop / (float)Height,
            SubjectSize / (float)Width, SubjectSize / (float)Height);
        using var fresh = Shot(2);
        session.Process(fresh)?.Dispose();
        Assert.False(session.IsCut, "The first picture of a selection has nothing earlier to differ from.");
        Assert.True(session.Tracking, session.Status);
    }
}
