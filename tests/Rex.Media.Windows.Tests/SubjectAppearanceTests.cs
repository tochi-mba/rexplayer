using System.Runtime.Versioning;
using Rex.Media.Primitives;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests;

[SupportedOSPlatform("windows8.0")]
public sealed class SubjectAppearanceTests
{
    [Fact]
    public void FollowingUsesColourToRejectAnIdenticallyShapedDistractor()
    {
        using var tracker = new SubjectEditSession();
        using var first = Picture(12, 1);
        tracker.Select(30 / 160f, 20 / 100f, 12 / 160f, 12 / 100f);
        tracker.Process(first);
        using var moved = Picture(12, 1.04, x: 42);
        for (var y = 20; y < 32; y++)
        {
            var original = first.Row(0, y);
            var next = moved.Row(0, y);
            for (var x = 30; x < 42; x++)
            {
                // Preserve luma and its texture while moving the chromaticity towards blue.
                var b = original[x * 4];
                var r = original[x * 4 + 2];
                next[x * 4] = r;
                next[x * 4 + 1] = (byte)(original[x * 4 + 1] + (r - b) * 48 / 150);
                next[x * 4 + 2] = b;
            }
        }
        tracker.Process(moved);
        Assert.True(tracker.Tracking, tracker.Status);
        Assert.InRange(tracker.Region.Left * 160, 40, 44);
    }

    [Fact]
    public void LocalFollowingCannotGraduallyStretchASelectedSubjectIntoAWideSurface()
    {
        using var tracker = new SubjectEditSession();
        tracker.Select(30 / 160f, 20 / 100f, 16 / 160f, 64 / 100f);
        for (var width = 16; width <= 48; width++)
        {
            using var picture = Picture(width, 1 + (width - 16) * 0.04, height: 64);
            tracker.Process(picture);
            if (!tracker.Tracking) continue;
            var ratio = tracker.Region.Width * 160 / (tracker.Region.Height * 100);
            Assert.InRange(ratio, 0.25 / 1.5, 0.25 * 1.5);
        }
    }

    [Fact]
    public void MatchingColourWithoutTheSelectedTextureIsNotAContinuedLock()
    {
        using var tracker = new SubjectEditSession();
        using var first = Picture(16, 1);
        tracker.Select(30 / 160f, 20 / 100f, 16 / 160f, 16 / 100f);
        tracker.Process(first);
        Assert.True(tracker.Tracking);
        using var flat = VideoFrame.Rent(PixelFormat.Bgra32, 160, 100);
        flat.Pts = MediaTime.FromSeconds(1.04);
        for (var y = 0; y < flat.Height; y++)
        {
            var row = flat.Row(0, y);
            for (var x = 0; x < flat.Width; x++)
            {
                row[x * 4] = 150;
                row[x * 4 + 1] = 60;
                row[x * 4 + 2] = 210;
                row[x * 4 + 3] = 255;
            }
        }
        tracker.Process(flat);
        Assert.False(tracker.Tracking);
        Assert.False(tracker.Erase);
    }

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

        // The only thing resembling the tall selection now fills the frame's height and is far
        // wider than the selection ever was. Recovery may decline it, but it must never widen
        // the box to cover it: a four-to-one selection stretched to two-to-one is no longer a
        // reading of the same thing, and the box it produces would stay wrong from then on.
        if (!tracker.Tracking) return;
        var width = tracker.Region.Width * 160;
        var height = tracker.Region.Height * 100;
        Assert.InRange(width / height, 0.25 / 1.5, 0.25 * 1.5);
        // Whatever it kept has to be on the object, not on the backdrop beside it.
        Assert.InRange(tracker.Region.Left * 160, 100, 140 - width);
    }

    /// <summary>
    /// A subject whose surface repeats - knitted fabric, a plain coat, brushed metal - and a
    /// backdrop it is clearly distinct from. Cloth is the hard case for keeping a box the right
    /// size, because any part of it is a convincing match for the whole.
    /// </summary>
    private static VideoFrame Fabric(double seconds, int x, int y, int width, int height,
        int redCast = 0, int blueCast = 0, bool turned = false)
    {
        var frame = VideoFrame.Rent(PixelFormat.Bgra32, 240, 150);
        frame.Pts = MediaTime.FromSeconds(seconds);
        for (var row = 0; row < frame.Height; row++)
        {
            var pixels = frame.Row(0, row);
            for (var column = 0; column < frame.Width; column++)
            {
                var inside = column >= x && column < x + width && row >= y && row < y + height;
                var across = turned ? x + width - 1 - column : column - x;
                // The weave has a size of its own, as cloth does, so a smaller box is a smaller
                // piece of the same fabric rather than a differently woven one.
                var weave = inside ? across / 3 % 4 * 9 + (row - y) / 4 % 3 * 7 : 0;
                var blue = inside ? 150 + weave : 40 + column / 9 % 3 * 14;
                var green = inside ? 70 + weave : 95 - row / 11 % 3 * 12;
                var red = inside ? 60 + weave : 120 + (column + row) / 13 % 3 * 11;
                pixels[column * 4] = (byte)Math.Clamp(blue + blueCast, 0, 255);
                pixels[column * 4 + 1] = (byte)Math.Clamp(green, 0, 255);
                pixels[column * 4 + 2] = (byte)Math.Clamp(red + redCast, 0, 255);
                pixels[column * 4 + 3] = 255;
            }
        }

        return frame;
    }

    [Fact]
    public void FollowingASelfSimilarSubjectDoesNotLetTheBoxCollapseOntoPartOfIt()
    {
        using var tracker = new SubjectEditSession();
        tracker.Select(80 / 240f, 30 / 150f, 48 / 240f, 72 / 150f);
        // Every frame offers a slightly smaller box that matches just as well, because a piece of
        // this fabric looks like all of it. Nothing rejects any single one of those, so over a few
        // hundred pictures the box walks inwards until it is a thumbnail of a sleeve, the lock is
        // thrown away, and the subject is never found again. Following has to hold its size.
        for (var i = 0; i < 240; i++)
        {
            using var frame = Fabric(1 + i * 0.04, 80 + i % 3, 30 + i % 2, 48, 72);
            tracker.Process(frame);
            Assert.True(tracker.Tracking, $"lost at picture {i}: {tracker.Status}");
        }

        Assert.InRange(tracker.Region.Width * 240, 40, 62);
        Assert.InRange(tracker.Region.Height * 150, 60, 92);
        Assert.InRange(tracker.Region.Left * 240, 74, 88);
    }

    [Fact]
    public void ASelectionLooseAroundTheSubjectSettlesOnItRatherThanDriftingIntoTheBackdrop()
    {
        using var tracker = new SubjectEditSession();
        // A hand-drawn rectangle is never tight. This one hangs 14 pixels off the subject's left
        // side, which is the common way a lock ends up following a piece of pavement.
        tracker.Select(66 / 240f, 24 / 150f, 62 / 240f, 84 / 150f);
        for (var i = 0; i < 120; i++)
        {
            using var frame = Fabric(1 + i * 0.04, 80 + i % 3, 30, 48, 72);
            tracker.Process(frame);
            Assert.True(tracker.Tracking, $"lost at picture {i}: {tracker.Status}");
        }

        // The box may keep the slack the user gave it, but its middle has to stay on the subject.
        var middle = (tracker.Region.Left + tracker.Region.Width / 2) * 240;
        Assert.InRange(middle, 90, 122);
    }

    [Fact]
    public void AColouredLightOverTheSubjectDoesNotPreventItsReturn()
    {
        using var tracker = new SubjectEditSession();
        tracker.Select(80 / 240f, 30 / 150f, 48 / 240f, 72 / 150f);
        using var first = Fabric(1, 80, 30, 48, 72);
        tracker.Process(first);
        Assert.True(tracker.Tracking);
        using var gone = Fabric(1.04, -400, 30, 48, 72);
        tracker.Process(gone);
        Assert.False(tracker.Tracking);

        // The subject walks back in past a shop window, a brake light, a neon sign: the whole
        // surface shifts towards red. Brightness cancels out of the shape reading exactly, but a
        // change of hue does not, so this is the case a structure-only matcher fails.
        for (var i = 0; i < 10; i++)
        {
            using var lit = Fabric(1.08 + i * 0.04, 150, 54, 48, 72, redCast: 55, blueCast: -25);
            tracker.Process(lit);
        }

        Assert.True(tracker.Tracking, tracker.Status);
        Assert.InRange(tracker.Region.Left * 240, 142, 158);
        Assert.InRange(tracker.Region.Top * 150, 46, 62);
    }

    [Fact]
    public void ASubjectThatComesBackTurnedRoundIsStillFound()
    {
        using var tracker = new SubjectEditSession();
        tracker.Select(80 / 240f, 30 / 150f, 48 / 240f, 72 / 150f);
        using var first = Fabric(1, 80, 30, 48, 72);
        tracker.Process(first);
        using var gone = Fabric(1.04, -400, 30, 48, 72);
        tracker.Process(gone);
        Assert.False(tracker.Tracking);

        // Facing the other way, every stripe is on the opposite side. Point-for-point agreement
        // with the remembered view is gone; what survives is the mix of colours it is made of.
        for (var i = 0; i < 10; i++)
        {
            using var back = Fabric(1.08 + i * 0.04, 40, 60, 48, 72, turned: true);
            tracker.Process(back);
        }

        Assert.True(tracker.Tracking, tracker.Status);
        Assert.InRange(tracker.Region.Left * 240, 32, 48);
    }

    [Fact]
    public void ASubjectThatReturnsStretchedByItsPoseIsStillFoundAtItsNewShape()
    {
        using var tracker = new SubjectEditSession();
        tracker.Select(80 / 240f, 30 / 150f, 48 / 240f, 72 / 150f);
        using var first = Fabric(1, 80, 30, 48, 72);
        tracker.Process(first);
        using var gone = Fabric(1.04, -400, 30, 48, 72);
        tracker.Process(gone);
        Assert.False(tracker.Tracking);

        // Arms out, a bag on one shoulder, leaning: a body is not a rigid rectangle, so the box
        // that fits it changes proportion as well as size.
        for (var i = 0; i < 10; i++)
        {
            using var leaning = Fabric(1.08 + i * 0.04, 120, 40, 58, 66);
            tracker.Process(leaning);
        }

        Assert.True(tracker.Tracking, tracker.Status);
        Assert.InRange(tracker.Region.Width * 240, 48, 70);
        Assert.InRange(tracker.Region.Left * 240, 112, 130);
    }
}
