using Rex.Media.Settings;
using Rex.Media.TestKit;
using static Rex.Media.Tests.AppCore.ControllerHarness;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// The playlist changing after the next item was lined up to follow without a gap: the item the
/// engine carries on into is the one the player names, whatever the playlist would pick now.
/// </summary>
public sealed class PlayerControllerQueueTests
{
    [Fact]
    [Capability("PB-14")]
    public void RepeatOneChosenAfterTheNextItemWasQueuedStillNamesTheItemThatPlays()
    {
        using var harness = new ControllerHarness(settings: new PlayerSettings { TitleSeconds = 0 });
        harness.Files["a.wav"] = Count(0, 800);
        harness.Files["b.wav"] = Count(1000, 8000);

        // Shorter than the queueing distance: b is lined up as soon as a opens.
        harness.Controller.Open(["a.wav", "b.wav"]);
        harness.PumpUntil(c => c.Duration > TimeSpan.Zero && c.Item?.Title == "a");
        harness.Controller.Playlist.Repeat = RepeatMode.One;
        harness.PumpUntil(c => c.Item?.Title == "b");

        Assert.Same(harness.Controller.Playlist.Items[1], harness.Controller.Playlist.Current);
    }
}
