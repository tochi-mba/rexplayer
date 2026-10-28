using Rex.Media.AppCore.Shell;

namespace Rex.Media.Tests.AppCore;

public sealed class OnScreenMessageTests
{
    [Theory]
    [InlineData("The snapshot could not be taken: access denied")]
    [InlineData("That movie version is no longer available.")]
    [InlineData("This episode is missing, incomplete or unavailable locally.")]
    [InlineData("That local file or folder could not be found.")]
    [InlineData("A file that rexplayer CANNOT open.")]
    public void AProblemIsShownEvenWithMessagesOff(string text)
    {
        Assert.True(OnScreenMessage.IsProblem(text));
        Assert.True(OnScreenMessage.Shows(text, messagesOn: false));
    }

    [Theory]
    [InlineData("Zoom 2x")]
    [InlineData("Volume 40%")]
    [InlineData("Snapshot saved")]
    public void AnOrdinaryMessageFollowsTheSetting(string text)
    {
        Assert.False(OnScreenMessage.IsProblem(text));
        Assert.False(OnScreenMessage.Shows(text, messagesOn: false));
        Assert.True(OnScreenMessage.Shows(text, messagesOn: true));
    }

    [Fact]
    public void AShortMessageStaysTheShortestTime()
    {
        Assert.Equal(OnScreenMessage.Shortest, OnScreenMessage.Duration("Zoom 2x"));
    }

    [Fact]
    public void AShortProblemStaysLongEnoughToRead()
    {
        Assert.Equal(OnScreenMessage.ShortestProblem, OnScreenMessage.Duration("Could not save."));
    }

    [Fact]
    public void ALongerMessageStaysLongerUpToALimit()
    {
        var medium = new string('a', 60);
        Assert.Equal(TimeSpan.FromSeconds(4), OnScreenMessage.Duration(medium));
        Assert.Equal(OnScreenMessage.Longest, OnScreenMessage.Duration(new string('a', 500)));
    }
}
