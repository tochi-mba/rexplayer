using Rex.Media.AppCore.Commands;

namespace Rex.Media.Tests.AppCore;

/// <summary>The Mac's menus carry the same shortcuts as Windows', in the form AppKit takes them.</summary>
public sealed class MacKeyEquivalentsTests
{
    [Theory]
    [InlineData("Ctrl+O", "o", true, false, false)]
    [InlineData("Ctrl+Shift+V", "v", true, false, true)]
    [InlineData("Alt+1", "1", false, true, false)]
    [InlineData("Space", " ", false, false, false)]
    [InlineData("Ctrl+Alt+Shift+K", "k", true, true, true)]
    [InlineData("Ctrl+Plus", "+", true, false, false)]
    [InlineData("Ctrl+0", "0", true, false, false)]
    [InlineData("Enter", "\r", false, false, false)]
    [InlineData("Tab", "\t", false, false, false)]
    [InlineData("Equals", "=", false, false, false)]
    [InlineData("LeftBracket", "[", false, false, false)]
    [InlineData("RightBracket", "]", false, false, false)]
    [InlineData("Slash", "/", false, false, false)]
    [InlineData("Comma", ",", false, false, false)]
    [InlineData("Period", ".", false, false, false)]
    [InlineData("Minus", "-", false, false, false)]
    public void ChordsBecomeTheKeyAndModifiersAMenuItemTakes(string chord, string key, bool command, bool option, bool shift)
    {
        Assert.Equal(new MacKeyEquivalent(key, command, option, shift), MacKeyEquivalents.For(KeyChord.Parse(chord)));
    }

    [Theory]
    [InlineData("Up", 0xF700)]
    [InlineData("Down", 0xF701)]
    [InlineData("Left", 0xF702)]
    [InlineData("Right", 0xF703)]
    [InlineData("F1", 0xF704)]
    [InlineData("F11", 0xF70E)]
    [InlineData("F24", 0xF71B)]
    [InlineData("Home", 0xF729)]
    [InlineData("End", 0xF72B)]
    [InlineData("PageUp", 0xF72C)]
    [InlineData("PageDown", 0xF72D)]
    [InlineData("Insert", 0xF727)]
    [InlineData("Delete", 0xF728)]
    [InlineData("Escape", 0x1B)]
    [InlineData("Backspace", 0x08)]
    public void NamedKeysUseAppKitsOwnCharacters(string chord, int character)
    {
        Assert.Equal(((char)character).ToString(), MacKeyEquivalents.For(KeyChord.Parse(chord))!.Value.Key);
    }

    [Fact]
    public void MediaKeysAreLeftToTheMacAndEveryDefaultShortcutElseHasAnEquivalent()
    {
        Assert.Null(MacKeyEquivalents.For(KeyChord.Parse("MediaPlayPause")));
        var keymap = new Keymap();
        var missing = CommandCatalog.All
            .SelectMany(command => keymap.ShortcutsFor(command.Id))
            .Where(chord => !chord.Key.StartsWith("Media", StringComparison.Ordinal) && MacKeyEquivalents.For(chord) is null)
            .ToList();

        Assert.Empty(missing);
    }
}
