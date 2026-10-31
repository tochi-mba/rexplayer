using Rex.Media.Audio;

namespace Rex.Media.Tests.Audio;

public sealed class DeviceFollowPolicyTests
{
    [Theory]
    [InlineData(true, EndpointRole.Multimedia, "headphones", "speakers", true)]
    [InlineData(true, EndpointRole.Multimedia, "SPEAKERS", "speakers", false)]
    [InlineData(true, EndpointRole.Console, "headphones", "speakers", false)]
    [InlineData(true, EndpointRole.Communications, "headset", "speakers", false)]
    [InlineData(false, EndpointRole.Multimedia, "microphone", "speakers", false)]
    [InlineData(true, EndpointRole.Multimedia, null, "speakers", true)]
    [InlineData(true, EndpointRole.Multimedia, "headphones", null, true)]
    public void OnlyANewMediaDefaultForPlaybackMovesTheSound(bool playback, EndpointRole role, string? newDevice, string? current, bool moves)
    {
        Assert.Equal(moves, DeviceFollowPolicy.ShouldMove(playback, role, newDevice, current));
    }

    [Theory]
    [InlineData(unchecked((int)0x88890004), true)]
    [InlineData(unchecked((int)0x88890010), true)]
    [InlineData(unchecked((int)0x88890026), true)]
    [InlineData(unchecked((int)0x80070005), false)]
    [InlineData(0, false)]
    public void OnlyALostDeviceMovesTheSoundOnAFailure(int hresult, bool lost)
    {
        Assert.Equal(lost, DeviceFollowPolicy.IsDeviceLost(hresult));
    }

    [Fact]
    public void TheClockCarriesOnAcrossDevices()
    {
        Assert.Equal(48_000 + 960, DeviceFollowPolicy.Heard(48_000, 960));
        Assert.Equal(960, DeviceFollowPolicy.Heard(-5, 960));
        Assert.Equal(48_000, DeviceFollowPolicy.Heard(48_000, -1));
    }

    [Fact]
    public void AMissingDeviceIsTriedAgainEveryHalfSecond()
    {
        Assert.False(DeviceFollowPolicy.ShouldRetry(TimeSpan.FromMilliseconds(499)));
        Assert.True(DeviceFollowPolicy.ShouldRetry(DeviceFollowPolicy.Retry));
    }
}
