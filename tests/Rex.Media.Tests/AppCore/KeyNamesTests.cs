using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The shortcut sheet's key names (A11Y-02): keycaps people recognise and words a screen reader says.</summary>
public sealed class KeyNamesTests
{
    [Theory]
    [Capability("A11Y-02")]
    [InlineData("Space", "Space", "Space")]
    [InlineData("Enter", "Enter", "Enter")]
    [InlineData("Escape", "Esc", "Escape")]
    [InlineData("PageUp", "Page up", "Page up")]
    [InlineData("PageDown", "Page down", "Page down")]
    [InlineData("Left", "\u2190", "Left arrow")]
    [InlineData("Right", "\u2192", "Right arrow")]
    [InlineData("Up", "\u2191", "Up arrow")]
    [InlineData("Down", "\u2193", "Down arrow")]
    [InlineData("Plus", "+", "Plus")]
    [InlineData("Minus", "-", "Minus")]
    [InlineData("Equals", "=", "Equals")]
    [InlineData("LeftBracket", "[", "Left bracket")]
    [InlineData("RightBracket", "]", "Right bracket")]
    [InlineData("Slash", "/", "Slash")]
    [InlineData("Comma", ",", "Comma")]
    [InlineData("Period", ".", "Period")]
    [InlineData("MediaPlayPause", "Play/pause key", "Play/pause key")]
    [InlineData("MediaStop", "Stop key", "Stop key")]
    [InlineData("MediaNext", "Next track key", "Next track key")]
    [InlineData("MediaPrevious", "Previous track key", "Previous track key")]
    [InlineData("F11", "F11", "F11")]
    [InlineData("M", "M", "M")]
    [InlineData("0", "0", "0")]
    public void EveryKeyHasTheNamePeopleKnowIt(string key, string keycap, string spoken)
    {
        var chord = KeyChord.Parse(key);

        Assert.Equal([keycap], KeyNames.Keycaps(chord));
        Assert.Equal(spoken, KeyNames.Spoken(chord));
    }

    [Fact]
    [Capability("A11Y-02")]
    public void ModifiersComeFirstInEachPlatformsOrderAndWords()
    {
        var chord = KeyChord.Parse("Ctrl+Alt+Shift+Right");

        Assert.Equal(["Ctrl", "Alt", "Shift", "\u2192"], KeyNames.Keycaps(chord));
        Assert.Equal(["Option", "Shift", "Cmd", "\u2192"], KeyNames.Keycaps(chord, ShortcutStyle.Mac));
        Assert.Equal("Ctrl+Alt+Shift+Right arrow", KeyNames.Spoken(chord));
        Assert.Equal("Option+Shift+Cmd+Right arrow", KeyNames.Spoken(chord, ShortcutStyle.Mac));
        Assert.Equal(["Shift", "="], KeyNames.Keycaps(KeyChord.Parse("Shift+Equals")));
        Assert.Equal("Ctrl+P", KeyNames.Spoken(KeyChord.Parse("Ctrl+P")));
    }

    [Fact]
    [Capability("A11Y-02")]
    public void ACommandsShortcutsAreSaidAsAlternatives()
    {
        var keymap = new Keymap();

        Assert.Equal("Space or Play/pause key", KeyNames.Spoken(keymap.ShortcutsFor(CommandCatalog.PlayPause)));
        Assert.Equal("Plus or Shift+Equals", KeyNames.Spoken(keymap.ShortcutsFor(CommandCatalog.Faster)));
        Assert.Equal("", KeyNames.Spoken([]));
        Assert.Throws<ArgumentNullException>(() => KeyNames.Spoken((IEnumerable<KeyChord>)null!));
    }

    [Theory]
    [InlineData(RepeatMode.Off, "Repeat off")]
    [InlineData(RepeatMode.All, "Repeat all")]
    [InlineData(RepeatMode.One, "Repeat one")]
    public void TheRepeatItemSaysTheModeItIsIn(RepeatMode mode, string title)
    {
        Assert.Equal(title, CommandCatalog.RepeatTitle(mode));
        Assert.DoesNotContain(':', CommandCatalog.Find(CommandCatalog.CycleRepeat)!.Title);
    }
}
