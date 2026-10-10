using System.Runtime.Versioning;
using Rex.Media.Primitives;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests;

/// <summary>Non-destructive selected-region tracking, conservative lost-target handling and fill provenance.</summary>
[SupportedOSPlatform("windows8.0")]
public sealed class SubjectEditSessionTests
{
    private static VideoFrame Picture(int x = 30, int y = 20, double seconds = 1, int size = 16, int lighting = 0)
    {
        var frame = VideoFrame.Rent(PixelFormat.Bgra32, 128, 80);
        frame.Pts = MediaTime.FromSeconds(seconds);
        for (var row = 0; row < frame.Height; row++)
        {
            var data = frame.Row(0, row);
            for (var col = 0; col < frame.Width; col++)
            {
                var i = col * 4;
                var item = col >= x && col < x + size && row >= y && row < y + size;
                var blue = item ? 130 + (((col - x) * 16 / size) % 3) * 20 : 25;
                var green = item ? 30 + (((row - y) * 16 / size) % 4) * 20 : 40;
                data[i] = (byte)Math.Clamp(blue + lighting, 0, 255);
                data[i + 1] = (byte)Math.Clamp(green + lighting, 0, 255);
                data[i + 2] = (byte)Math.Clamp((item ? 210 : 60) + lighting, 0, 255);
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
    public void TrackingReusesSamplesInsteadOfAllocatingATemplatePerFrame()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var frame = Picture();
        Assert.Null(tracker.Process(frame));
        Assert.Null(tracker.Process(frame));
        Assert.True(tracker.Tracking);

        // Warm up the search path before measuring temporary allocations.
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 60; i++)
        {
            Assert.Null(tracker.Process(frame));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.InRange(allocated, 0, 70_000);
        Assert.True(tracker.Tracking);
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
    public void RecoversTheSameTargetAfterItLeavesTheFrameAndReturnsElsewhere()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var original = Picture();
        tracker.Process(original);
        Assert.True(tracker.Tracking);

        tracker.SetErase(true);
        using var absent = Picture(-50, -50, 1.04);
        Assert.Null(tracker.Process(absent));
        Assert.False(tracker.Tracking);
        Assert.False(tracker.Erase);
        Assert.True(tracker.Locked);

        // Keep presenting new images: the original identity reference survives a loss.
        for (var i = 0; i < 8; i++)
        {
            using var returned = Picture(70, 28, 1.08 + i * 0.04);
            Assert.Null(tracker.Process(returned));
        }

        Assert.True(tracker.Tracking);
        Assert.InRange(tracker.Region.Left * 128, 69, 71);
        Assert.InRange(tracker.Region.Top * 80, 27, 29);
        Assert.False(tracker.Erase);
        Assert.Contains("Tracking", tracker.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ALongAbsenceRetainsTheOriginalFingerprintAndRequiresTwoSightings()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var initial = Picture();
        tracker.Process(initial);
        Assert.True(tracker.Tracking);
        tracker.SetErase(true);

        // More than fifty absent pictures must not erase the identity reference.
        for (var i = 0; i < 52; i++)
        {
            using var empty = Picture(-50, -50, 1.04 + i * 0.04);
            Assert.Null(tracker.Process(empty));
            Assert.True(tracker.Locked);
            Assert.False(tracker.Tracking);
            Assert.False(tracker.Erase);
        }

        // One fleeting match is not enough to move the viewport onto a possible lookalike.
        for (var i = 0; i < 4; i++)
        {
            using var returned = Picture(70, 28, 3.2 + i * 0.04);
            Assert.Null(tracker.Process(returned));
        }

        Assert.False(tracker.Tracking);
        Assert.Contains("verifying", tracker.Status, StringComparison.OrdinalIgnoreCase);
        for (var i = 0; i < 4; i++)
        {
            using var returned = Picture(70, 28, 3.36 + i * 0.04);
            Assert.Null(tracker.Process(returned));
        }

        Assert.True(tracker.Tracking);
        Assert.InRange(tracker.Region.Left * 128, 69, 71);
        Assert.False(tracker.Erase);
        Assert.Contains("found again", tracker.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReacquisitionCanMatchAChangedSizeWithoutLosingTheOriginalSelection()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var initial = Picture();
        tracker.Process(initial);
        using var absent = Picture(-50, -50, 1.04);
        tracker.Process(absent);
        for (var i = 0; i < 8; i++)
        {
            // Same coloured texture, now closer to the camera and elsewhere in frame.
            using var returned = Picture(65, 25, 1.08 + i * 0.04, size: 20);
            Assert.Null(tracker.Process(returned));
        }

        Assert.True(tracker.Tracking);
        Assert.InRange(tracker.Region.Left * 128, 64, 67);
        Assert.InRange(tracker.Region.Width * 128, 18, 21);
    }

    [Fact]
    public void ReacquiresAChangingBrightnessTargetAfterItMovesBetweenSightings()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var initial = Picture();
        tracker.Process(initial);
        tracker.SetErase(true);
        using var missing = Picture(-50, -50, 1.04);
        tracker.Process(missing);
        Assert.False(tracker.Tracking);

        // A globally brighter video frame is still the same distinctive texture.
        // The object can also move between the two periodically sampled sightings.
        for (var i = 0; i < 12; i++)
        {
            using var returned = Picture(i < 4 ? 70 : 110, 28, 1.08 + i * 0.04, lighting: 35);
            Assert.Null(tracker.Process(returned));
        }

        Assert.True(tracker.Locked);
        Assert.True(tracker.Tracking);
        Assert.False(tracker.Erase);
        Assert.InRange(tracker.Region.Left * 128, 108, 111);
        Assert.Contains("found again", tracker.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ABrighterAndLargerReturnMayRestoreTheOriginalTexture()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var initial = Picture();
        tracker.Process(initial);
        using var missing = Picture(-50, -50, 1.04);
        tracker.Process(missing);
        for (var i = 0; i < 12; i++)
        {
            using var returned = Picture(66, 24, 1.08 + i * 0.04, size: 24, lighting: 25);
            Assert.Null(tracker.Process(returned));
        }

        Assert.True(tracker.Tracking);
        Assert.InRange(tracker.Region.Left * 128, 64, 68);
        Assert.InRange(tracker.Region.Width * 128, 22, 27);
    }

    [Fact]
    public void ALightingChangeDoesNotMakeTwoIdenticalReturnTargetsUnambiguous()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var initial = Picture();
        tracker.Process(initial);
        using var missing = Picture(-50, -50, 1.04);
        tracker.Process(missing);

        for (var i = 0; i < 12; i++)
        {
            using var frame = Picture(70, 20, 1.08 + i * 0.04, lighting: 35);
            for (var y = 0; y < 16; y++)
            {
                var source = frame.Row(0, 20 + y);
                var destination = frame.Row(0, 50 + y);
                source.Slice(70 * 4, 16 * 4).CopyTo(destination.Slice(18 * 4, 16 * 4));
            }

            Assert.Null(tracker.Process(frame));
        }

        Assert.True(tracker.Locked);
        Assert.False(tracker.Tracking);
    }

    [Theory]
    [InlineData(8, 110, 60, 0)]
    [InlineData(10, 50, 25, 0)]
    [InlineData(12, 60, 26, 0)]
    [InlineData(14, 7, 12, -15)]
    [InlineData(16, 0, 0, 0)]
    [InlineData(16, 104, 55, 35)]
    [InlineData(16, 112, 64, 0)]
    [InlineData(20, 53, 30, 15)]
    [InlineData(24, 68, 22, 25)]
    [InlineData(28, 40, 35, -10)]
    [InlineData(32, 45, 29, 0)]
    public void AReturnedTargetMayChangeSizeLightingAndPositionWithoutLosingItsSelection(
        int size, int x, int y, int lighting)
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var first = Picture();
        tracker.Process(first);
        Assert.True(tracker.Tracking);

        using var missing = Picture(-50, -50, 1.04);
        tracker.Process(missing);
        Assert.False(tracker.Tracking);

        for (var i = 0; i < 24; i++)
        {
            using var returned = Picture(x, y, 1.08 + i * 0.04, size, lighting);
            Assert.Null(tracker.Process(returned));
        }

        Assert.True(tracker.Locked);
        Assert.True(tracker.Tracking);
        Assert.False(tracker.Erase);
        Assert.InRange(tracker.Region.Left * 128, x - 3, x + 3);
        Assert.InRange(tracker.Region.Top * 80, y - 3, y + 3);
        Assert.InRange(tracker.Region.Width * 128, size - 4, size + 4);
    }

    [Fact]
    public void LongOcclusionThenChangedDistanceAndLightingCanRestoreTheOriginalTarget()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var first = Picture();
        tracker.Process(first);
        tracker.SetErase(true);

        for (var i = 0; i < 120; i++)
        {
            using var hidden = Picture(-50, -50, 1.04 + i * 0.04, lighting: i % 2 == 0 ? -10 : 15);
            Assert.Null(tracker.Process(hidden));
            Assert.True(tracker.Locked);
            Assert.False(tracker.Tracking);
            Assert.False(tracker.Erase);
        }

        for (var i = 0; i < 24; i++)
        {
            using var returned = Picture(65, 25, 5.84 + i * 0.04, size: 24, lighting: 25);
            Assert.Null(tracker.Process(returned));
        }

        Assert.True(tracker.Tracking);
        Assert.InRange(tracker.Region.Width * 128, 20, 28);
        Assert.False(tracker.Erase);
    }

    [Fact]
    public void LostTrackingCannotReenableRemovalBeforeTheOriginalTargetIsFound()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var first = Picture();
        tracker.Process(first);
        tracker.SetErase(true);
        using var hidden = Picture(-50, -50, 1.04);
        tracker.Process(hidden);
        Assert.False(tracker.Tracking);
        tracker.SetErase(true);
        Assert.False(tracker.Erase);
        using var next = Picture(-50, -50, 1.08);
        Assert.Null(tracker.Process(next));

        for (var i = 0; i < 12; i++)
        {
            using var returned = Picture(70, 28, 1.12 + i * 0.04);
            Assert.Null(tracker.Process(returned));
        }

        Assert.True(tracker.Tracking);
        Assert.False(tracker.Erase);
    }

    [Fact]
    public void ResolutionChangeRequiresFreshSelectionRatherThanAcquiringAnUnrelatedRegion()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var first = Picture();
        tracker.Process(first);
        tracker.SetErase(true);
        Assert.True(tracker.Tracking);

        using var resized = VideoFrame.Rent(PixelFormat.Bgra32, 192, 120);
        resized.Pts = MediaTime.FromSeconds(1.04);
        Assert.Null(tracker.Process(resized));
        Assert.False(tracker.Locked);
        Assert.False(tracker.Tracking);
        Assert.False(tracker.Erase);
        Assert.Contains("dimensions changed", tracker.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Null(tracker.Process(resized));
    }

    [Fact]
    public void ASceneWithoutTheOriginalTextureNeverRestoresTheLock()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var first = Picture();
        tracker.Process(first);
        for (var i = 0; i < 36; i++)
        {
            using var unrelated = VideoFrame.Rent(PixelFormat.Bgra32, 128, 80);
            unrelated.Pts = MediaTime.FromSeconds(1.04 + i * 0.04);
            for (var y = 0; y < unrelated.Height; y++)
            {
                unrelated.Row(0, y).Fill(100);
            }

            Assert.Null(tracker.Process(unrelated));
            Assert.False(tracker.Tracking);
            Assert.True(tracker.Locked);
        }
    }

    [Fact]
    public void ATransientCandidateCannotRestoreTrackingAfterItDisappears()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var original = Picture();
        tracker.Process(original);
        using var absent = Picture(-50, -50, 1.04);
        tracker.Process(absent);
        for (var i = 0; i < 4; i++)
        {
            using var brief = Picture(70, 28, 1.08 + i * 0.04);
            tracker.Process(brief);
        }

        Assert.False(tracker.Tracking);
        for (var i = 0; i < 4; i++)
        {
            using var missing = Picture(-50, -50, 1.24 + i * 0.04);
            tracker.Process(missing);
        }

        Assert.True(tracker.Locked);
        Assert.False(tracker.Tracking);
        Assert.Equal(0, tracker.Confidence);
    }

    [Fact]
    public void TwoMatchingReturnCandidatesDoNotSilentlySwitchTheSelectedIdentity()
    {
        var tracker = new SubjectEditSession();
        tracker.Select(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        using var initial = Picture();
        tracker.Process(initial);
        using var absent = Picture(-50, -50, 1.04);
        tracker.Process(absent);

        for (var i = 0; i < 8; i++)
        {
            using var ambiguous = Picture(70, 20, 1.08 + i * 0.04);
            // Add a second, pixel-identical subject at a distant location.
            for (var y = 0; y < 16; y++)
            {
                var source = ambiguous.Row(0, 20 + y);
                var destination = ambiguous.Row(0, 50 + y);
                source.Slice(70 * 4, 16 * 4).CopyTo(destination.Slice(18 * 4, 16 * 4));
            }

            Assert.Null(tracker.Process(ambiguous));
        }

        Assert.False(tracker.Tracking);
        Assert.True(tracker.Locked);
        Assert.Equal(0, tracker.EstimatedPixels);
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
    public void GPUVideoPresenterReacquiresTheOriginalSubjectAfterItReturnsLargerAndBrighter()
    {
        using var presenter = D3D11Presenter.Offscreen(128, 80);
        using var initial = Picture();
        presenter.Present(initial);
        presenter.SelectSubject(30 / 128f, 20 / 80f, 16 / 128f, 16 / 80f);
        presenter.Present(initial);
        Assert.True(presenter.SubjectStatus().Tracking);

        using var absent = Picture(-50, -50, 1.04);
        presenter.Present(absent);
        Assert.False(presenter.SubjectStatus().Tracking);

        for (var i = 0; i < 16; i++)
        {
            using var returned = Picture(65, 25, 1.08 + i * 0.04, size: 24, lighting: 25);
            presenter.Present(returned);
        }

        var status = presenter.SubjectStatus();
        Assert.True(status.Locked);
        Assert.True(status.Tracking);
        Assert.False(status.Erase);
        Assert.InRange(status.Left * 128, 62, 68);
        Assert.InRange(status.Width * 128, 20, 28);
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
