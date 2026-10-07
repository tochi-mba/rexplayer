using Rex.Media.AppCore.Commands;

namespace Rex.Media.Tests.AppCore;

public sealed class CommandTests
{
    [Theory]
    [InlineData("Space", KeyModifiers.None, "Space")]
    [InlineData("ctrl+shift+o", KeyModifiers.Ctrl | KeyModifiers.Shift, "O")]
    [InlineData("Shift + Ctrl + o", KeyModifiers.Ctrl | KeyModifiers.Shift, "O")]
    [InlineData("Control+Alt+left", KeyModifiers.Ctrl | KeyModifiers.Alt, "Left")]
    [InlineData("alt+1", KeyModifiers.Alt, "1")]
    [InlineData("f11", KeyModifiers.None, "F11")]
    [InlineData("F24", KeyModifiers.None, "F24")]
    [InlineData("Ctrl+slash", KeyModifiers.Ctrl, "Slash")]
    [InlineData("mediaplaypause", KeyModifiers.None, "MediaPlayPause")]
    public void ChordsParseWhateverTheCaseAndOrder(string text, KeyModifiers modifiers, string key)
    {
        Assert.Equal(new KeyChord(modifiers, key), KeyChord.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Hyper+A")]
    [InlineData("F0")]
    [InlineData("F25")]
    [InlineData("F01")]
    [InlineData("Fx")]
    [InlineData("Banana")]
    [InlineData("!")]
    public void TextThatIsNoChordIsRefused(string? text)
    {
        Assert.Null(KeyChord.TryParse(text));
        if (text is not null)
        {
            Assert.Throws<FormatException>(() => KeyChord.Parse(text));
        }
    }

    [Fact]
    public void ChordsAreWrittenInOneCanonicalOrder()
    {
        Assert.Equal("Ctrl+Alt+Shift+Right", KeyChord.Parse("shift+alt+ctrl+right").ToString());
        Assert.Equal("Space", KeyChord.Parse("space").ToString());
    }

    [Fact]
    public void EveryCommandHasAUniqueIdATitleAndNoDefaultShortcutClash()
    {
        var ids = CommandCatalog.All.Select(command => command.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(CommandCatalog.All, command => Assert.False(string.IsNullOrWhiteSpace(command.Title)));
        Assert.Empty(new Keymap().Conflicts);
        Assert.Equal("Play or pause", CommandCatalog.Find(CommandCatalog.PlayPause)!.Title);
        Assert.Null(CommandCatalog.Find("no-such-command"));
    }

    [Fact]
    public void TheDefaultsMapKeysToCommands()
    {
        var keymap = new Keymap();

        Assert.Equal(CommandCatalog.PlayPause, keymap.CommandFor(KeyChord.Parse("Space")));
        Assert.Equal(CommandCatalog.ToggleFullScreen, keymap.CommandFor(KeyChord.Parse("F11")));
        Assert.Equal(CommandCatalog.JumpForwardLong, keymap.CommandFor(KeyChord.Parse("Ctrl+Alt+Right")));
        Assert.Null(keymap.CommandFor(KeyChord.Parse("Ctrl+Alt+Shift+Z")));
        Assert.Equal("F", keymap.Label(CommandCatalog.ToggleFullScreen));
        Assert.Equal("", keymap.Label(CommandCatalog.ToggleAlwaysOnTop));
        Assert.Empty(keymap.ShortcutsFor("no-such-command"));
    }

    [Fact]
    public void TheUsersChangesReplaceOrRemoveDefaultsAndBadOnesAreIgnored()
    {
        var keymap = new Keymap(new Dictionary<string, string>
        {
            [CommandCatalog.ToggleFullScreen] = "Ctrl+Enter",
            [CommandCatalog.Mute] = "",
            [CommandCatalog.Stop] = "not a chord",
            ["no-such-command"] = "Ctrl+K",
        });

        Assert.Equal([KeyChord.Parse("Ctrl+Enter")], keymap.ShortcutsFor(CommandCatalog.ToggleFullScreen));
        Assert.Null(keymap.CommandFor(KeyChord.Parse("F11")));
        Assert.Null(keymap.CommandFor(KeyChord.Parse("M")));
        Assert.Equal(CommandCatalog.Stop, keymap.CommandFor(KeyChord.Parse("S")));
        Assert.Null(keymap.CommandFor(KeyChord.Parse("Ctrl+K")));
    }

    [Fact]
    public void AChordGivenToTwoCommandsIsReportedAndTheFirstKeepsIt()
    {
        var keymap = new Keymap(new Dictionary<string, string> { [CommandCatalog.Snapshot] = "Space" });

        var conflict = Assert.Single(keymap.Conflicts);
        Assert.Equal((KeyChord.Parse("Space"), CommandCatalog.PlayPause, CommandCatalog.Snapshot), conflict);
        Assert.Equal(CommandCatalog.PlayPause, keymap.CommandFor(KeyChord.Parse("Space")));
    }
}
