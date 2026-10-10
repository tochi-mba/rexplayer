using System.Runtime.Versioning;
using Rex.Media.Primitives;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests;

/// <summary>Non-destructive selected-region tracking, conservative lost-target handling and fill provenance.</summary>
[SupportedOSPlatform("windows8.0")]
public sealed class SubjectEditSessionTests
{
    private static VideoFrame Picture(int x = 30, int y = 20, double seconds = 1)
    {
        var frame = VideoFrame.Rent(PixelFormat.Bgra32, 128, 80);
        frame.Pts = MediaTime.FromSeconds(seconds);
        for (var row = 0; row < frame.Height; row++)
        {
            var data = frame.Row(0, row);
            for (var col = 0; col < frame.Width; col++)
            {
                var i = col * 4;
                var item = col >= x && col < x + 16 && row >= y && row < y + 16;
                data[i] = item ? (byte)(130 + ((col - x) % 3) * 20) : (byte)25;
                data[i + 1] = item ? (byte)(30 + ((row - y) % 4) * 20) : (byte)40;
                data[i + 2] = item ? (byte)210 : (byte)60;
                data[i + 3] = 255;
            }
        }

        return frame;
    }

    [Fact]
    public void TracksTheSameMovingTextureWithoutOverwritingItsOriginalFrame()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var first = Picture();
        Assert.Null(tracker.Process(first));
        Assert.True(tracker.Tracking);
        Assert.Equal(1, tracker.Confidence);
        using var next = Picture(33, 22, 1.04);
        Assert.Null(tracker.Process(next));
        Assert.True(tracker.Tracking);
        Assert.True(tracker.Confidence > 0.38);
        Assert.InRange(tracker.Region.Left * 128, 31, 34);
        Assert.InRange(tracker.Region.Top * 80, 21, 23);

        tracker.SetErase(true);
        using var erased = tracker.Process(next);
        Assert.NotNull(erased);
        Assert.True(tracker.EstimatedPixels > 0);
        Assert.NotEqual(next.Row(0, 24)[36 * 4 + 2], erased.Row(0, 24)[36 * 4 + 2]);
        Assert.Equal((byte)210, next.Row(0, 24)[36 * 4 + 2]);
        tracker.SetErase(false);
        Assert.Null(tracker.Process(next));
        tracker.Reset();
        Assert.False(tracker.Locked);
        Assert.False(tracker.Erase);
        Assert.Null(tracker.Process(next));
    }

    [Fact]
    public void RecoveryUsesPreviouslyExposedPixelsBeforeInventingNewOnes()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var first = Picture();
        tracker.Process(first);
        tracker.SetErase(true);
        using var initially = tracker.Process(first);
        Assert.NotNull(initially);
        var initiallyEstimated = tracker.EstimatedPixels;
        Assert.True(initiallyEstimated > 0);

        // The moving object exposes its old location; only the still-occluded area
        // should need inference. Original frames remain byte-for-byte unchanged.
        using var later = Picture(40, 20, 1.04);
        using var recovered = tracker.Process(later);
        Assert.True(tracker.Tracking);
        Assert.NotNull(recovered);
        Assert.True(tracker.EstimatedPixels < initiallyEstimated);
        Assert.Contains("Estimated fill", tracker.Status, StringComparison.Ordinal);
        Assert.Equal((byte)210, later.Row(0, 24)[42 * 4 + 2]);
    }

    [Fact]
    public void InferenceUsesVerticalAndHorizontalBoundariesAndPreservesLooseRectangleCorners()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(26 / 128f, 16 / 80f, 24 / 128f, 24 / 80f);
        using var source = Picture();
        tracker.Process(source);
        tracker.SetErase(true);
        using var preview = tracker.Process(source);
        Assert.NotNull(preview);
        // Colour-aware border suppression leaves background inside a loose selection alone.
        Assert.Equal(source.Row(0, 17)[27 * 4 + 2], preview.Row(0, 17)[27 * 4 + 2]);
        Assert.NotEqual(source.Row(0, 28)[38 * 4 + 2], preview.Row(0, 28)[38 * 4 + 2]);

        // The sampled fill is also bounded at the picture's edge and is deterministic.
        var edge = new SubjectEditSession();
        edge.Select(0, 0, 0.15f, 0.25f);
        using var original = Picture(x: 0, y: 0);
        edge.Process(original);
        edge.SetErase(true);
        using var edited = edge.Process(original);
        Assert.NotNull(edited);
        Assert.InRange(edge.EstimatedPixels, 1, original.Width * original.Height);
    }

    [Fact]
    public void MaskRefinementHasSafeLimitsAndKeepsTheSourceUnchanged()
    {
        var tracker = new SubjectEditSession();
        tracker.Refine(-100, 200);
        Assert.Equal(0, tracker.Feather);
        Assert.Equal(100, tracker.MaskTolerance);

        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var source = Picture();
        tracker.Process(source);
        tracker.SetErase(true);
        using var sharp = tracker.Process(source);
        Assert.NotNull(sharp);
        var originalPixel = source.Row(0, 26)[36 * 4 + 2];
        var sharpPixel = sharp.Row(0, 26)[36 * 4 + 2];
        Assert.NotEqual(originalPixel, sharpPixel);

        tracker.Refine(12, 0);
        using var feathered = tracker.Process(source);
        Assert.NotNull(feathered);
        Assert.Equal(originalPixel, source.Row(0, 26)[36 * 4 + 2]);
        Assert.NotEqual(sharp.Row(0, 20)[30 * 4 + 2],
            feathered.Row(0, 20)[30 * 4 + 2]);
    }

    [Fact]
    public void MotionSearchFollowsAVisibleTargetBeyondTheOldSmallSearchWindow()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var first = Picture();
        tracker.Process(first);
        using var fast = Picture(41, 20, 1.04);
        tracker.Process(fast);
        Assert.True(tracker.Tracking);
        Assert.InRange(tracker.Region.Left * fast.Width, 40, 42);
    }

    [Fact]
    public void RejectsMissingOrInvalidInputAndUnreliableTimelineChanges()
    {
        var tracker = new SubjectEditSession();
        Assert.Throws<ArgumentNullException>(() => tracker.Process(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Select(-0.1f, 0.2f, 0.2f, 0.2f));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Select(0.2f, 0.2f, 0.005f, 0.2f));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Select(float.NaN, 0.2f, 0.2f, 0.2f));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Select(0.9f, 0.1f, 0.5f, 0.2f));
        tracker.Select(0.2f, 0.2f, 0.2f, 0.2f);
        using var first = Picture(seconds: 4);
        tracker.Process(first);
        using var seek = Picture(seconds: 1);
        Assert.Null(tracker.Process(seek));
        Assert.False(tracker.Locked);
        Assert.Contains("timeline jumped", tracker.Status, StringComparison.OrdinalIgnoreCase);
        tracker.SetErase(true);
        Assert.False(tracker.Erase);
    }

    [Fact]
    public void FreezesOnSuddenTargetLossInsteadOfSwitchingToAnotherObject()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var first = Picture();
        tracker.Process(first);
        tracker.SetErase(true);
        using var missing = Picture(-50, -50, 1.04);
        Assert.Null(tracker.Process(missing));
        Assert.False(tracker.Tracking);
        Assert.False(tracker.Erase);
        Assert.Contains("uncertain", tracker.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FeaturelessPatchDoesNotPretendToLockAnObject()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(0.2f, 0.2f, 0.2f, 0.2f);
        using var frame = Picture(-60, -60);
        Assert.Null(tracker.Process(frame));
        Assert.True(tracker.Locked);
        Assert.False(tracker.Tracking);
        Assert.False(tracker.Erase);
        Assert.Equal(0, tracker.Confidence);
        Assert.Contains("too little distinguishing detail", tracker.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void ReacquiringAfterAResetReplacesTheOldSubjectWithoutModifyingTheSource()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var frame = Picture();
        Assert.Null(tracker.Process(frame));
        Assert.True(tracker.Tracking);
        tracker.SetErase(true);
        using var preview = tracker.Process(frame);
        Assert.NotNull(preview);
        tracker.Reset();
        Assert.False(tracker.Locked);
        tracker.Select(28 / 128f, 18 / 80f, 20 / 128f, 20 / 80f);
        Assert.False(tracker.Erase);
        Assert.Null(tracker.Process(frame));
        Assert.True(tracker.Tracking);
        Assert.Equal((byte)210, frame.Row(0, 24)[36 * 4 + 2]);
    }

    [Fact]
    public void GPUVideoPresenterRendersTheOriginalAgainAfterClearingThePreview()
    {
        using var source = Picture();
        using var presenter = D3D11Presenter.Offscreen(128, 80);
        presenter.Present(source);
        using var original = presenter.ReadBack();
        presenter.SelectSubject(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        presenter.Present(source);
        presenter.SetSubjectErasure(true);
        presenter.Redraw();
        using var erased = presenter.ReadBack();
        Assert.NotEqual(original.Row(0, 24)[36 * 4 + 2], erased.Row(0, 24)[36 * 4 + 2]);
        presenter.ClearSubject();
        presenter.Redraw();
        using var restored = presenter.ReadBack();
        Assert.Equal(original.Row(0, 24)[36 * 4 + 2], restored.Row(0, 24)[36 * 4 + 2]);
    }
}
