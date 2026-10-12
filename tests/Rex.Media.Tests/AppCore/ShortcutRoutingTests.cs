using Rex.Media.AppCore.Commands;

namespace Rex.Media.Tests.AppCore;

public sealed class ShortcutRoutingTests
{
    [Theory]
    [InlineData("Enter")]
    [InlineData("Left")]
    [InlineData("Right")]
    [InlineData("Up")]
    [InlineData("Down")]
    [InlineData("Home")]
    [InlineData("End")]
    [InlineData("PageUp")]
    [InlineData("PageDown")]
    [InlineData("Shift+Down")]
    [InlineData("Ctrl+Shift+Home")]
    [InlineData("Ctrl+A")]
    [InlineData("Delete")]
    [InlineData("Backspace")]
    [InlineData("F2")]
    public void ListNavigationDoesNotSeekChangeVolumeOrRunCustomPlaybackBindings(string chord) =>
        Assert.True(ShortcutRouting.ListOwns(KeyChord.Parse(chord)));

    [Theory]
    [InlineData("Alt+Left")]
    [InlineData("Ctrl+Alt+A")]
    [InlineData("A")]
    [InlineData("Space")]
    [InlineData("Ctrl+O")]
    [InlineData("MediaPlayPause")]
    [InlineData("Escape")]
    public void OtherCommandsRemainAvailable(string chord) =>
        Assert.False(ShortcutRouting.ListOwns(KeyChord.Parse(chord)));
}
