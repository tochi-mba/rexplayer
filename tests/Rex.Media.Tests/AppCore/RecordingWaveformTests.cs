using Rex.Media.AppCore.Library;

namespace Rex.Media.Tests.AppCore;

public sealed class RecordingWaveformTests
{
    [Fact]
    public void PeakHandlesSilenceClippingMultichannelLikeSamplesAndBadFloats()
    {
        Assert.Equal(0f, RecordingWaveform.Peak([]));
        Assert.Equal(0f, RecordingWaveform.Peak([0, 0, 0]));
        Assert.Equal(0.75f, RecordingWaveform.Peak([-0.75f, 0.1f, 0.2f]));
        Assert.Equal(1f, RecordingWaveform.Peak([float.PositiveInfinity, -3, float.NaN]));
        Assert.Equal(0f, RecordingWaveform.Peak([float.NaN, float.NegativeInfinity]));
    }

    [Fact]
    public void TimelineIsClampedForMissingDurationsAndLargeRecordings()
    {
        Assert.Equal(TimeSpan.Zero, RecordingWaveform.At(TimeSpan.Zero, 2, 10));
        Assert.Equal(TimeSpan.Zero, RecordingWaveform.At(TimeSpan.FromHours(1), 2, 0));
        Assert.Equal(TimeSpan.Zero, RecordingWaveform.At(TimeSpan.FromHours(1), -5, 10));
        Assert.Equal(TimeSpan.FromMinutes(30), RecordingWaveform.At(TimeSpan.FromHours(1), 5, 10));
        Assert.Equal(TimeSpan.FromHours(1), RecordingWaveform.At(TimeSpan.FromHours(1), 200, 10));

        Assert.Equal(-1, RecordingWaveform.Current(TimeSpan.Zero, TimeSpan.Zero, 16));
        Assert.Equal(-1, RecordingWaveform.Current(TimeSpan.FromHours(1), TimeSpan.Zero, 0));
        Assert.Equal(0, RecordingWaveform.Current(TimeSpan.FromHours(1), TimeSpan.FromHours(-1), 16));
        Assert.Equal(8, RecordingWaveform.Current(TimeSpan.FromHours(1), TimeSpan.FromMinutes(30), 16));
        Assert.Equal(15, RecordingWaveform.Current(TimeSpan.FromHours(1), TimeSpan.FromHours(4), 16));
        Assert.Equal(15, RecordingWaveform.Current(TimeSpan.FromDays(20), TimeSpan.FromDays(20), 16));
    }
}
