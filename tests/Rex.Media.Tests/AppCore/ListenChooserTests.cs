using Rex.Media.AppCore.Player;
using Rex.Media.Settings;

namespace Rex.Media.Tests.AppCore;

public sealed class ListenChooserTests
{
    [Fact]
    public void PlayingAppsComeFirstOncePerProgramAndNeverRexplayer()
    {
        SoundApp[] sessions =
        [
            new(10, "chrome", "", false, 0),
            new(11, "chrome", "Asake - Lonely At The Top - YouTube", true, 0.6f),
            new(20, "spotify", "Spotify Premium", true, 0.3f),
            new(30, "rexplayer", "rexplayer", true, 0.9f),
            new(40, "discord", "", false, 0),
            new(50, "", "Nameless", true, 1),
        ];

        var offered = ListenChooser.Offered(sessions, ownProcess: 30);

        Assert.Equal(["chrome", "spotify", "discord"], offered.Select(app => app.Name));
        Assert.Equal(11u, offered[0].ProcessId);
        Assert.Throws<ArgumentNullException>(() => ListenChooser.Offered(null!, 0));
    }

    [Fact]
    public void ARememberedProgramIsFoundByNameWithSoundFirst()
    {
        SoundApp[] running =
        [
            new(1, "Spotify", "", false, 0),
            new(2, "spotify", "Spotify Premium", false, 0),
            new(3, "spotify", "", true, 0.2f),
            new(4, "chrome", "", true, 1),
        ];

        Assert.Equal(3u, ListenChooser.Find(running, "SPOTIFY")!.Value.ProcessId);
        Assert.Equal(2u, ListenChooser.Find(running[..2], "spotify")!.Value.ProcessId);
        Assert.Null(ListenChooser.Find(running, "foobar"));
        Assert.Throws<ArgumentNullException>(() => ListenChooser.Find(null!, "x"));
    }

    [Fact]
    public void RexplayersOwnMusicAlwaysComesFirst()
    {
        Assert.False(ListenChooser.DrawsOtherSound(ListenTo.Player, playerPlaying: false));
        Assert.False(ListenChooser.DrawsOtherSound(ListenTo.App, playerPlaying: true));
        Assert.True(ListenChooser.DrawsOtherSound(ListenTo.Everything, playerPlaying: false));
    }

    [Theory]
    [InlineData(ListenTo.App, true, false, false, ListenState.Listening, "Listening to Spotify")]
    [InlineData(ListenTo.App, true, true, false, ListenState.Quiet, "Spotify is quiet")]
    [InlineData(ListenTo.App, false, false, false, ListenState.Waiting, "Waiting for Spotify to play")]
    [InlineData(ListenTo.App, true, false, true, ListenState.PlayerTookOver, "Listening to rexplayer (Spotify again when it stops)")]
    [InlineData(ListenTo.Everything, false, false, false, ListenState.Listening, "Listening to everything")]
    [InlineData(ListenTo.Everything, false, true, false, ListenState.Quiet, "Nothing is playing")]
    [InlineData(ListenTo.Everything, false, false, true, ListenState.PlayerTookOver, "Listening to rexplayer (everything again when it stops)")]
    public void TheChipSaysWhatIsHeard(ListenTo choice, bool found, bool quiet, bool playing, ListenState state, string words)
    {
        Assert.Equal(state, ListenChooser.State(choice, found, quiet, playing));
        Assert.Equal(words, ListenChooser.Chip(choice, "Spotify", state));
        Assert.Equal("", ListenChooser.Chip(ListenTo.Player, "Spotify", state));
    }

    [Fact]
    public void LabelsUseAShortTitleElseThePrettyName()
    {
        Assert.Equal("Spotify Premium", new SoundApp(1, "spotify", "Spotify Premium", true, 0).Label);
        Assert.Equal("Spotify", new SoundApp(1, "spotify", "", true, 0).Label);
        Assert.Equal("Chrome", new SoundApp(1, "chrome", new string('x', 41), true, 0).Label);
        Assert.Equal("the app", ListenChooser.Pretty(""));
    }

    [Fact]
    public void AnUnknownOrNamelessChoiceListensToRexplayerAgain()
    {
        Assert.Equal(ListenTo.Player, new PlayerSettings { ListenTo = ListenTo.App, ListenApp = "" }.Normalize().ListenTo);
        Assert.Equal(ListenTo.Player, new PlayerSettings { ListenTo = (ListenTo)9 }.Normalize().ListenTo);
        Assert.Equal(ListenTo.App, new PlayerSettings { ListenTo = ListenTo.App, ListenApp = "spotify" }.Normalize().ListenTo);
        Assert.Equal("", new PlayerSettings { ListenApp = null! }.Normalize().ListenApp);
    }
}
