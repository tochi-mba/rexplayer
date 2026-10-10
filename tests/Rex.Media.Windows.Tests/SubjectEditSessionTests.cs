using Rex.Media.Primitives;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests;

/// <summary>Non-destructive selected-region tracking, conservative lost-target handling and fill provenance.</summary>
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
