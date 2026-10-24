using Rex.Media.AppCore.Player;

namespace Rex.Media.Tests.AppCore;

public sealed class QueueScrollPolicyTests
{
    [Fact]
    public void QueueScrollFollowsPlaybackOnlyWhenNeeded()
    {
        var current = new PlaylistItem("same.flac");
        var duplicate = new PlaylistItem("same.flac");
        Assert.False(QueueScrollPolicy.ShouldFollow(false, true, null, current));
        Assert.False(QueueScrollPolicy.ShouldFollow(true, false, null, null));
        Assert.False(QueueScrollPolicy.ShouldFollow(true, false, current, current));
        Assert.True(QueueScrollPolicy.ShouldFollow(true, true, current, current));
        Assert.True(QueueScrollPolicy.ShouldFollow(true, false, current, duplicate));
        Assert.True(QueueScrollPolicy.ShouldFollow(true, false, null, current));
    }
}
