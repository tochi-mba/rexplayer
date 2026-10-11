using System.Runtime.Versioning;
using Rex.Media.Primitives;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests;

/// <summary>Safety invariants around invalid selections, frame changes and preview state.</summary>
[SupportedOSPlatform("windows8.0")]
public sealed class SubjectEditSessionSafetyTests
{
    private static VideoFrame Picture(double seconds, bool withTarget, int width = 128, int height = 80)
    {
        var frame = VideoFrame.Rent(PixelFormat.Bgra32, width, height);
        frame.Pts = MediaTime.FromSeconds(seconds);
        for (var y = 0; y < height; y++)
        {
            var row = frame.Row(0, y);
            for (var x = 0; x < width; x++)
            {
                var subject = withTarget && x is >= 30 and < 46 && y is >= 20 and < 36;
                var i = x * 4;
                row[i] = subject ? (byte)(130 + ((x - 30) % 3) * 20) : (byte)25;
                row[i + 1] = subject ? (byte)(30 + ((y - 20) % 4) * 20) : (byte)40;
                row[i + 2] = subject ? (byte)210 : (byte)60;
                row[i + 3] = 255;
            }
        }

        return frame;
    }

    [Fact]
    public void UhdReacquisitionRefinesLargeSelectionsWithABoundedComparisonBudget()
    {
        var tracker = new SubjectEditSession();
        using var frame = VideoFrame.Rent(PixelFormat.Bgra32, 3840, 2160);
        frame.Plane(0).Fill(100);
        frame.Pts = MediaTime.FromSeconds(1);
        // A broad selection exercises the worst refinement radius, not just tiny fixtures.
        for (var y = 200; y < 1800; y++)
        {
            var row = frame.Row(0, y);
            for (var x = 400; x < 3200; x++)
            {
                row[x * 4] = (byte)(120 + x / 90 % 3 * 20);
                row[x * 4 + 1] = (byte)(30 + y / 90 % 4 * 20);
                row[x * 4 + 2] = 210;
            }
        }

        tracker.Select(400 / 3840f, 200 / 2160f, 2800 / 3840f, 1600 / 2160f);
        Assert.Null(tracker.Process(frame));
        Assert.True(tracker.Tracking);
        frame.Plane(0).Fill(20);
        frame.Pts = MediaTime.FromSeconds(1.04);
        Assert.Null(tracker.Process(frame));
        Assert.False(tracker.Tracking);
        frame.Plane(0).Fill(100);
        for (var i = 0; i < 4; i++)
        {
            frame.Pts = MediaTime.FromSeconds(1.08 + i * 0.04);
            Assert.Null(tracker.Process(frame));
        }

        Assert.InRange(tracker.LastSearchComparisons, 1, 100_000);
        Assert.False(tracker.Tracking);
    }

    [Fact]
    public void ShortForwardSeekClearsIdentityEvenWhenTimestampsLookContinuous()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var first = Picture(1, withTarget: true);
        first.Generation = 7;
        tracker.Process(first);
        tracker.SetErase(true);
        using var afterSeek = Picture(1.04, withTarget: true);
        afterSeek.Generation = 8;
        Assert.Null(tracker.Process(afterSeek));
        Assert.False(tracker.Locked);
        Assert.False(tracker.Tracking);
        Assert.False(tracker.Erase);
        Assert.Equal(0, tracker.EstimatedPixels);
        Assert.Contains("generation", tracker.Status, StringComparison.Ordinal);

        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        Assert.Null(tracker.Process(afterSeek));
        Assert.True(tracker.Tracking);
    }

    [Fact]
    public void PausedPreviewDoesNotAdvanceTrackingOrConfirmAReturn()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var first = Picture(1, withTarget: true);
        tracker.Process(first);
        tracker.SetErase(true);
        var region = tracker.Region;
        for (var i = 0; i < 12; i++)
        {
            tracker.Refine(i, 50);
            using var preview = tracker.Process(first, advanceTracking: false);
            Assert.NotNull(preview);
            Assert.Equal(region, tracker.Region);
            Assert.Equal(1, tracker.Confidence);
        }

        using var lost = Picture(1.04, withTarget: false);
        Assert.Null(tracker.Process(lost));
        Assert.False(tracker.Tracking);
        using var returned = Picture(1.08, withTarget: true);
        for (var i = 0; i < 32; i++)
        {
            Assert.Null(tracker.Process(returned, advanceTracking: false));
            Assert.False(tracker.Tracking);
        }
    }

    [Fact]
    public void FeaturelessInitialSelectionCannotAcquireAReplacementObjectWithoutReselection()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var featureless = Picture(1, withTarget: false);
        Assert.Null(tracker.Process(featureless));
        Assert.True(tracker.Locked);
        Assert.False(tracker.Tracking);
        Assert.Contains("too little distinguishing detail", tracker.Status, StringComparison.OrdinalIgnoreCase);

        // An unrelated textured object later moves into exactly those coordinates.
        // Never treat that as the original user selection.
        for (var i = 0; i < 12; i++)
        {
            using var later = Picture(1.04 + i * 0.04, withTarget: true);
            Assert.Null(tracker.Process(later));
            Assert.True(tracker.Locked);
            Assert.False(tracker.Tracking);
            Assert.Equal(0, tracker.Confidence);
        }

        tracker.SetErase(true);
        Assert.False(tracker.Erase);

        // The explicit reselect is the only way to acquire that texture.
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var chosen = Picture(1.6, withTarget: true);
        Assert.Null(tracker.Process(chosen));
        Assert.True(tracker.Tracking);
        Assert.Equal(1, tracker.Confidence);
    }

    [Fact]
    public void OversizedPictureClearsLockAndErasureRatherThanRetainingUnsafePreview()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var first = Picture(1, withTarget: true);
        tracker.Process(first);
        Assert.True(tracker.Tracking);
        tracker.SetErase(true);
        Assert.True(tracker.Erase);

        // One pixel beyond the 3840 x 2160 tracking budget must be rejected safely.
        using var oversized = VideoFrame.Rent(PixelFormat.Bgra32, 3841, 2160);
        oversized.Pts = MediaTime.FromSeconds(1.04);
        Assert.Null(tracker.Process(oversized));
        Assert.False(tracker.Locked);
        Assert.False(tracker.Tracking);
        Assert.False(tracker.Erase);
        Assert.Equal(0, tracker.Confidence);
        Assert.Contains("too large", tracker.Status, StringComparison.OrdinalIgnoreCase);

        tracker.SetErase(true);
        Assert.False(tracker.Erase);
        using var later = Picture(1.08, withTarget: true);
        Assert.Null(tracker.Process(later));
        Assert.False(tracker.Tracking);
    }

    [Fact]
    public void ManualReselectionAfterDimensionChangeStartsWithFreshFrameAndTimestamp()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var first = Picture(4, withTarget: true);
        tracker.Process(first);
        Assert.True(tracker.Tracking);

        using var resized = Picture(4.04, withTarget: true, width: 192, height: 120);
        Assert.Null(tracker.Process(resized));
        Assert.False(tracker.Locked);

        tracker.Select(30 / 192f, 20 / 120f, 16 / 192f, 16 / 120f);
        using var restarted = Picture(1, withTarget: true, width: 192, height: 120);
        Assert.Null(tracker.Process(restarted));
        Assert.True(tracker.Locked);
        Assert.True(tracker.Tracking);
        Assert.Equal(1, tracker.Confidence);
    }

    [Fact]
    public void RejectsNonBgraPictureWhilePreservingOriginalSelection()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var invalid = VideoFrame.Rent(PixelFormat.Nv12, 128, 80);
        Assert.Throws<ArgumentException>(() => tracker.Process(invalid));
        Assert.True(tracker.Locked);
        Assert.False(tracker.Tracking);

        using var valid = Picture(1, withTarget: true);
        Assert.Null(tracker.Process(valid));
        Assert.True(tracker.Tracking);
    }
}
