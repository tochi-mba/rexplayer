using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The hotkey editor's rules (UI-11): rebinding, conflicts, resets, the mouse, and a file to keep them in.</summary>
public sealed class ShortcutEditingTests
{
    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>(StringComparer.Ordinal);

    [Fact]
    [Capability("UI-11")]
    public void AShortcutTakenFromAnotherCommandLeavesItItsOthers()
    {
        // Ctrl+O belongs to opening a file; giving it to the playlist takes it from there.
        var change = ShortcutEditing.Bind(None, CommandCatalog.TogglePlaylist, KeyChord.Parse("Ctrl+O"));

        Assert.Equal(CommandCatalog.OpenFile, change.TakenFrom);
        var keymap = new Keymap(change.Changes);
        Assert.Equal(CommandCatalog.TogglePlaylist, keymap.CommandFor(KeyChord.Parse("Ctrl+O")));
        Assert.Empty(keymap.ShortcutsFor(CommandCatalog.OpenFile));
        Assert.Empty(keymap.Conflicts);

        // Play or pause has two (Space and the media key): taking one leaves the other.
        var media = ShortcutEditing.Bind(None, CommandCatalog.Mute, KeyChord.Parse("Space"));
        Assert.Equal([KeyChord.Parse("MediaPlayPause")], new Keymap(media.Changes).ShortcutsFor(CommandCatalog.PlayPause));

        // A free chord, or one the command already has, takes nothing.
        Assert.Null(ShortcutEditing.Bind(None, CommandCatalog.Mute, KeyChord.Parse("Ctrl+Alt+F12")).TakenFrom);
        Assert.Null(ShortcutEditing.Bind(None, CommandCatalog.PlayPause, KeyChord.Parse("Space")).TakenFrom);
    }

    [Fact]
    [Capability("UI-11")]
    public void ShortcutsAreClearedAndResetOneByOne()
    {
        var cleared = ShortcutEditing.Clear(None, CommandCatalog.Mute);
        Assert.Empty(new Keymap(cleared).ShortcutsFor(CommandCatalog.Mute));
        Assert.True(ShortcutEditing.IsChanged(cleared, CommandCatalog.Mute));

        var reset = ShortcutEditing.Reset(cleared, CommandCatalog.Mute);
        Assert.False(ShortcutEditing.IsChanged(reset, CommandCatalog.Mute));
        Assert.Equal(new Keymap().ShortcutsFor(CommandCatalog.Mute), new Keymap(reset).ShortcutsFor(CommandCatalog.Mute));

        // Resetting one whose shortcut another now has shows as a conflict for the user to settle.
        var moved = ShortcutEditing.Bind(None, CommandCatalog.TogglePlaylist, KeyChord.Parse("Ctrl+O")).Changes;
        Assert.Single(new Keymap(ShortcutEditing.Reset(moved, CommandCatalog.OpenFile)).Conflicts);

        Assert.Throws<ArgumentException>(() => ShortcutEditing.Clear(None, "no-such-command"));
        Assert.Throws<ArgumentNullException>(() => ShortcutEditing.Reset(None, null!));
        Assert.Throws<ArgumentNullException>(() => ShortcutEditing.Bind(null!, CommandCatalog.Mute, KeyChord.Parse("M")));
        Assert.Throws<ArgumentNullException>(() => ShortcutEditing.Clear(null!, CommandCatalog.Mute));
        Assert.Throws<ArgumentNullException>(() => ShortcutEditing.Reset(null!, CommandCatalog.Mute));
        Assert.Throws<ArgumentNullException>(() => ShortcutEditing.IsChanged(null!, CommandCatalog.Mute));
    }

    [Fact]
    [Capability("UI-11")]
    public void TheKeyboardAndMouseAreExportedAndImported()
    {
        var settings = new PlayerSettings
        {
            Shortcuts = new Dictionary<string, string> { [CommandCatalog.Mute] = "Ctrl+M", [CommandCatalog.Stop] = "" },
            GlobalShortcuts = [CommandCatalog.PlayPause],
            Wheel = WheelChoice.Seek,
            SidewaysWheel = WheelChoice.None,
            MiddleButton = MiddleButtonChoice.FullScreen,
            SideButtons = SideButtonChoice.JumpBackForward,
            Volume = 0.5,
        };

        var text = ShortcutEditing.Export(settings);
        Assert.Contains("\"wheel\": \"Seek\"", text, StringComparison.Ordinal);
        var imported = ShortcutEditing.Import(new PlayerSettings { Volume = 0.8 }, text);

        Assert.Equal(settings.Shortcuts, imported.Shortcuts);
        Assert.Equal(settings.GlobalShortcuts, imported.GlobalShortcuts);
        Assert.Equal((WheelChoice.Seek, WheelChoice.None, MiddleButtonChoice.FullScreen, SideButtonChoice.JumpBackForward), (imported.Wheel, imported.SidewaysWheel, imported.MiddleButton, imported.SideButtons));
        Assert.Equal(0.8, imported.Volume);

        // Commands this version does not know are left out; what is no such file is refused.
        var foreign = ShortcutEditing.Import(new PlayerSettings(), """{ "shortcuts": { "beam-me-up": "Ctrl+B", "mute": "M" }, "globalShortcuts": ["warp", "mute"] }""");
        Assert.Equal(["mute"], foreign.Shortcuts.Keys);
        Assert.Equal(["mute"], foreign.GlobalShortcuts);
        var empty = ShortcutEditing.Import(new PlayerSettings(), """{ "shortcuts": null, "globalShortcuts": null }""");
        Assert.Empty(empty.Shortcuts);
        Assert.Throws<FormatException>(() => ShortcutEditing.Import(new PlayerSettings(), "not json"));
        Assert.Throws<FormatException>(() => ShortcutEditing.Import(new PlayerSettings(), "null"));
        Assert.Throws<FormatException>(() => ShortcutEditing.Import(new PlayerSettings(), null!));
        Assert.Throws<ArgumentNullException>(() => ShortcutEditing.Export(null!));
        Assert.Throws<ArgumentNullException>(() => ShortcutEditing.Import(null!, text));
    }

    [Fact]
    [Capability("UI-11")]
    public void TheMouseDoesWhatItIsSetTo()
    {
        Assert.Equal(CommandCatalog.VolumeUp, ShortcutEditing.WheelCommand(WheelChoice.Volume, forward: true));
        Assert.Equal(CommandCatalog.VolumeDown, ShortcutEditing.WheelCommand(WheelChoice.Volume, forward: false));
        Assert.Equal(CommandCatalog.JumpForwardVeryShort, ShortcutEditing.WheelCommand(WheelChoice.Seek, forward: true));
        Assert.Equal(CommandCatalog.JumpBackVeryShort, ShortcutEditing.WheelCommand(WheelChoice.Seek, forward: false));
        Assert.Null(ShortcutEditing.WheelCommand(WheelChoice.None, forward: true));

        Assert.Equal(CommandCatalog.PlayPause, ShortcutEditing.MiddleButtonCommand(MiddleButtonChoice.PlayPause));
        Assert.Equal(CommandCatalog.ToggleFullScreen, ShortcutEditing.MiddleButtonCommand(MiddleButtonChoice.FullScreen));
        Assert.Equal(CommandCatalog.Mute, ShortcutEditing.MiddleButtonCommand(MiddleButtonChoice.Mute));
        Assert.Null(ShortcutEditing.MiddleButtonCommand(MiddleButtonChoice.None));

        Assert.Equal((CommandCatalog.Previous, CommandCatalog.Next), (ShortcutEditing.SideButtonCommand(SideButtonChoice.PreviousNext, false), ShortcutEditing.SideButtonCommand(SideButtonChoice.PreviousNext, true)));
        Assert.Equal((CommandCatalog.JumpBackShort, CommandCatalog.JumpForwardShort), (ShortcutEditing.SideButtonCommand(SideButtonChoice.JumpBackForward, false), ShortcutEditing.SideButtonCommand(SideButtonChoice.JumpBackForward, true)));
        Assert.Null(ShortcutEditing.SideButtonCommand(SideButtonChoice.None, true));
    }

    [Fact]
    public void BadMouseSettingsAreMendedAndGlobalShortcutsTidied()
    {
        var mended = new PlayerSettings
        {
            Wheel = (WheelChoice)9,
            SidewaysWheel = (WheelChoice)9,
            MiddleButton = (MiddleButtonChoice)9,
            SideButtons = (SideButtonChoice)9,
            GlobalShortcuts = ["mute", "mute", " ", null!],
        }.Normalize();

        Assert.Equal((WheelChoice.Volume, WheelChoice.Seek, MiddleButtonChoice.PlayPause, SideButtonChoice.PreviousNext), (mended.Wheel, mended.SidewaysWheel, mended.MiddleButton, mended.SideButtons));
        Assert.Equal(["mute"], mended.GlobalShortcuts);
        Assert.Empty(new PlayerSettings { GlobalShortcuts = null! }.Normalize().GlobalShortcuts);
    }

    [Theory]
    [InlineData("A", 0x41)]
    [InlineData("7", 0x37)]
    [InlineData("F5", 0x74)]
    [InlineData("F24", 0x87)]
    [InlineData("Minus", 0xBD)]
    [InlineData("Plus", 0x6B)]
    [InlineData("MediaPlayPause", 0xB3)]
    [InlineData("Space", 0x20)]
    [InlineData("Nothing", null)]
    [InlineData("F25", null)]
    [InlineData("", null)]
    public void KeysHaveTheCodesWindowsKnowsThemBy(string name, int? code)
    {
        Assert.Equal(code, VirtualKeys.Code(name));
        if (code is { } known)
        {
            Assert.Equal(name, VirtualKeys.Name(known));
        }
    }
}
