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
    [InlineData("Cmd+O", KeyModifiers.Ctrl, "O")]
    [InlineData("command+shift+o", KeyModifiers.Ctrl | KeyModifiers.Shift, "O")]
    [InlineData("Option+Left", KeyModifiers.Alt, "Left")]
    [InlineData("opt+cmd+1", KeyModifiers.Ctrl | KeyModifiers.Alt, "1")]
    public void ChordsParseWhateverTheCaseAndOrder(string text, KeyModifiers modifiers, string key)
    {
        Assert.Equal(new KeyChord(modifiers, key), KeyChord.Parse(text));
    }

    [Fact]
    public void TheMacsWordsNameTheSameKeysSoOneSettingsFileFitsEveryPlatform()
    {
        // A chord typed the Mac way is stored the canonical way, and reads back on either platform.
        Assert.Equal("Ctrl+Alt+Shift+O", KeyChord.Parse("Shift+Option+Cmd+O").ToString());
        Assert.Null(KeyChord.TryParse("Cmd+Ctrl+O"));
    }

    [Theory]
    [InlineData("Ctrl+O", "Ctrl+O", "Cmd+O")]
    [InlineData("Ctrl+Alt+Shift+Right", "Ctrl+Alt+Shift+Right", "Option+Shift+Cmd+Right")]
    [InlineData("Alt+1", "Alt+1", "Option+1")]
    [InlineData("Shift+N", "Shift+N", "Shift+N")]
    [InlineData("Space", "Space", "Space")]
    public void AChordIsShownInEachPlatformsOwnWords(string text, string windows, string mac)
    {
        var chord = KeyChord.Parse(text);
        Assert.Equal(windows, chord.Display(ShortcutStyle.Windows));
        Assert.Equal(mac, chord.Display(ShortcutStyle.Mac));
    }

    [Fact]
    public void MenuLabelsFollowThePlatformsStyle()
    {
        var keymap = new Keymap();
        Assert.Equal("Ctrl+T", keymap.Label(CommandCatalog.GoToTime));
        Assert.Equal("Cmd+T", keymap.Label(CommandCatalog.GoToTime, ShortcutStyle.Mac));
        Assert.Equal("", keymap.Label(CommandCatalog.ToggleAlwaysOnTop, ShortcutStyle.Mac));
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
    public void UnsupportedOnlineLyricsRetrievalAndEditingHaveNoPublicCommands()
    {
        // The current app reads embedded and local sidecar lyrics, and only offers
        // a show/hide toggle. An unimplemented network or editing workflow must not
        // appear in menus, key bindings or the automation command catalogue.
        var lyricsCommands = CommandCatalog.All
            .Where(command => command.Id.Contains("lyric", StringComparison.OrdinalIgnoreCase))
            .Select(command => command.Id)
            .ToArray();
        Assert.Equal([CommandCatalog.ToggleLyrics], lyricsCommands);
        Assert.Null(CommandCatalog.Find("fetch-online-lyrics"));
        Assert.Null(CommandCatalog.Find("edit-lyrics"));
    }

    [Fact]
    public void UnimplementedMediaDownloadControlsAreNotExposedByTheCommandCatalogue()
    {
        // Downloading existing library files is recognized and tracked, but it does
        // not mean rexplayer has implemented a remote media download action.
        Assert.DoesNotContain(CommandCatalog.All, command =>
            command.Id.StartsWith("download-", StringComparison.OrdinalIgnoreCase)
            || command.Id.StartsWith("fetch-media-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void JumpsAreNamedByTheLengthsChosenInPreferences()
    {
        var defaults = new Rex.Media.Settings.PlayerSettings();
        string Title(string id, Rex.Media.Settings.PlayerSettings settings) => CommandCatalog.TitleOf(CommandCatalog.Find(id)!, settings);

        // The catalogue's own titles are what the defaults name, so the site's shortcut page agrees.
        foreach (var id in new[] { CommandCatalog.JumpForwardVeryShort, CommandCatalog.JumpBackVeryShort, CommandCatalog.JumpForwardShort, CommandCatalog.JumpBackShort, CommandCatalog.JumpForwardMedium, CommandCatalog.JumpBackMedium, CommandCatalog.JumpForwardLong, CommandCatalog.JumpBackLong })
        {
            Assert.Equal(CommandCatalog.Find(id)!.Title, Title(id, defaults));
        }

        var chosen = defaults with { VeryShortJumpSeconds = 1, ShortJumpSeconds = 30, MediumJumpSeconds = 90, LongJumpSeconds = 3600 };
        Assert.Equal("Jump forward 1 second", Title(CommandCatalog.JumpForwardVeryShort, chosen));
        Assert.Equal("Jump back 30 seconds", Title(CommandCatalog.JumpBackShort, chosen));
        Assert.Equal("Jump forward 90 seconds", Title(CommandCatalog.JumpForwardMedium, chosen));
        Assert.Equal("Jump back 1 hour", Title(CommandCatalog.JumpBackLong, chosen));
        Assert.Equal("Jump forward 2 minutes", Title(CommandCatalog.JumpForwardLong, chosen with { LongJumpSeconds = 120 }));
        Assert.Equal("Play or pause", Title(CommandCatalog.PlayPause, chosen));
        Assert.Equal("0 seconds", CommandCatalog.Length(0));
        Assert.Throws<ArgumentNullException>(() => CommandCatalog.TitleOf(null!, defaults));
        Assert.Throws<ArgumentNullException>(() => CommandCatalog.TitleOf(CommandCatalog.All[0], null!));
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
            ["no-such-command"] = "Ctrl+Alt+Shift+J",
        });

        Assert.Equal([KeyChord.Parse("Ctrl+Enter")], keymap.ShortcutsFor(CommandCatalog.ToggleFullScreen));
        Assert.Null(keymap.CommandFor(KeyChord.Parse("F11")));
        Assert.Null(keymap.CommandFor(KeyChord.Parse("M")));
        Assert.Equal(CommandCatalog.Stop, keymap.CommandFor(KeyChord.Parse("S")));
        Assert.Null(keymap.CommandFor(KeyChord.Parse("Ctrl+Alt+Shift+J")));
    }

    [Fact]
    public void AChordGivenToTwoCommandsIsReportedAndTheFirstKeepsIt()
    {
        var keymap = new Keymap(new Dictionary<string, string> { [CommandCatalog.Snapshot] = "Space" });

        var conflict = Assert.Single(keymap.Conflicts);
        Assert.Equal((KeyChord.Parse("Space"), CommandCatalog.PlayPause, CommandCatalog.Snapshot), conflict);
        Assert.Equal(CommandCatalog.PlayPause, keymap.CommandFor(KeyChord.Parse("Space")));
    }

    [Theory]
    [InlineData(0x41, "A")]
    [InlineData(0x5A, "Z")]
    [InlineData(0x30, "0")]
    [InlineData(0x39, "9")]
    [InlineData(0x60, "0")]
    [InlineData(0x69, "9")]
    [InlineData(0x70, "F1")]
    [InlineData(0x87, "F24")]
    [InlineData(0x20, "Space")]
    [InlineData(0x25, "Left")]
    [InlineData(0xBF, "Slash")]
    [InlineData(0xB3, "MediaPlayPause")]
    [InlineData(0x6B, "Plus")]
    [InlineData(0xBD, "Minus")]
    public void WindowsKeyCodesHaveTheirKeyNames(int virtualKey, string name)
    {
        Assert.Equal(name, VirtualKeys.Name(virtualKey));
        Assert.True(KeyChord.IsKey(name));
    }

    [Fact]
    public void KeysWithoutANameMakeNoChord()
    {
        Assert.Null(VirtualKeys.Name(0x10));
        Assert.Null(VirtualKeys.Chord(0x10, ctrl: false, alt: false, shift: true));
        Assert.Equal(KeyChord.Parse("Ctrl+Alt+Shift+O"), VirtualKeys.Chord(0x4F, ctrl: true, alt: true, shift: true));
        Assert.Equal(KeyChord.Parse("Space"), VirtualKeys.Chord(0x20, ctrl: false, alt: false, shift: false));
    }
}
