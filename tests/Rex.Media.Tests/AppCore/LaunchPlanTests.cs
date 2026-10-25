using Rex.Media.AppCore;

namespace Rex.Media.Tests.AppCore;

public sealed class LaunchPlanTests
{
    [Fact]
    public void ALaunchThatOpensMediaReadiesThePictureDecodersAndSoundFirst()
    {
        Assert.Equal(
            [LaunchWork.Pictures, LaunchWork.Memory, LaunchWork.Decoders, LaunchWork.AudioDevice, LaunchWork.Library],
            LaunchPlan.Order(opensMedia: true, realAudio: true));
    }

    [Fact]
    public void AnEmptyLaunchReadsWhatItShowsFirst()
    {
        Assert.Equal(
            [LaunchWork.Memory, LaunchWork.Library, LaunchWork.Pictures, LaunchWork.Decoders, LaunchWork.AudioDevice],
            LaunchPlan.Order(opensMedia: false, realAudio: true));
    }

    [Fact]
    public void ALeanLaunchWithNothingToOpenReadsOnlyTheStores()
    {
        Assert.Equal([LaunchWork.Memory, LaunchWork.Library], LaunchPlan.Order(opensMedia: false, realAudio: true, lean: true));
    }

    [Fact]
    public void ALeanLaunchThatOpensMediaStillReadiesEverythingItPlaysWith()
    {
        Assert.Equal(LaunchPlan.Order(opensMedia: true, realAudio: true), LaunchPlan.Order(opensMedia: true, realAudio: true, lean: true));
    }

    [Fact]
    public void TheLibraryLooksThroughItsFoldersOnlyOnceTheLaunchHasSettled()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), LaunchPlan.FirstLibraryScan);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithoutARealAudioDeviceThereIsNoneToWarm(bool opensMedia)
    {
        var order = LaunchPlan.Order(opensMedia, realAudio: false);

        Assert.DoesNotContain(LaunchWork.AudioDevice, order);
        Assert.Equal(4, order.Count);
        Assert.Contains(LaunchWork.Memory, order);
        Assert.Contains(LaunchWork.Library, order);
    }
}
