using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// The window-driving pipe commands (protocol 2) against a pretend window, so every shell answers
/// scripts the same way and fails in the same plain words.
/// </summary>
public sealed class ShellAutomationTests
{
    private sealed class FakeSurface : IShellSurface
    {
        public List<string> Log { get; } = [];

        public string? LastSnapshot { get; private set; }

        public IReadOnlyList<UiControl> Controls() =>
        [
            new UiControl { Id = "PlayPauseButton", Kind = "Button", Name = "Play", Enabled = true },
            new UiControl { Id = "NextButton", Kind = "Button", Name = "Next", Enabled = false },
            new UiControl { Id = "VolumeSlider", Kind = "Slider", Name = "Volume", Value = 80 },
            new UiControl { Id = "ShuffleButton", Kind = "ToggleButton", Name = "Shuffle", Checked = false },
            new UiControl { Id = "Osd", Kind = "TextBlock", Text = "Volume 80 %", Visible = false },
        ];

        public IReadOnlyList<UiMenuItem> Menus() =>
        [
            new UiMenuItem
            {
                Id = "MediaMenu",
                Title = "Media",
                Kind = "menu",
                Items = [new UiMenuItem { Id = "Command-open-file", Title = "Open a file", Kind = "command", Shortcut = "Ctrl+O", Checked = null, Enabled = true }, new UiMenuItem { Id = "", Kind = "separator" }, new UiMenuItem { Id = "Command-mute", Title = "Mute", Kind = "toggle", Checked = true, Enabled = false }],
            },
        ];

        public bool Press(string id) => Remember("press", id) && id is "PlayPauseButton" or "Command-open-file";

        public bool Toggle(string id) => Remember("toggle", id) && id == "ShuffleButton";

        public bool SetValue(string id, double value) => Remember("set", id + "=" + value.ToString(System.Globalization.CultureInfo.InvariantCulture)) && id == "VolumeSlider";

        public bool SendChord(KeyChord chord) => Remember("key", chord.ToString()) && chord.Key == "Space";

        public string? Text(string id) => id == "Osd" ? "Volume 80 %" : null;

        public Task SnapshotAsync(string path)
        {
            LastSnapshot = path;
            return Task.CompletedTask;
        }

        private bool Remember(string what, string detail)
        {
            Log.Add(what + " " + detail);
            return true;
        }
    }

    private static Task<AutomationReply> Ask(FakeSurface surface, string command, string? target = null, double? value = null, string? chord = null, string? path = null) =>
        ShellAutomation.AnswerAsync(new AutomationRequest { Command = command, Target = target, Value = value, Chord = chord, Path = path }, surface);

    [Fact]
    public void OnlyTheUiCommandsAreTheWindowsToAnswer()
    {
        Assert.True(ShellAutomation.IsUiRequest("ui.tree"));
        Assert.False(ShellAutomation.IsUiRequest("status"));
        Assert.False(ShellAutomation.IsUiRequest(null));
        Assert.All(ShellAutomation.Commands, command => Assert.True(ShellAutomation.IsUiRequest(command)));
    }

    [Fact]
    [Capability("TOOL-03")]
    public async Task TheTreeAndTheMenusComeBackAsTheShellReportsThem()
    {
        var surface = new FakeSurface();

        var tree = await Ask(surface, "ui.tree");
        var menus = await Ask(surface, "ui.menu");

        Assert.True(tree.Ok);
        var controls = tree.Controls!;
        Assert.Equal(["PlayPauseButton", "NextButton", "VolumeSlider", "ShuffleButton", "Osd"], controls.Select(control => control.Id));
        Assert.False(controls[1].Enabled);
        Assert.Equal(80, controls[2].Value);
        Assert.False(controls[4].Visible);
        Assert.True(menus.Ok);
        var media = Assert.Single(menus.Menus!);
        Assert.Equal(("MediaMenu", "menu", 3), (media.Id, media.Kind, media.Items.Count));
        Assert.Equal((true, false), (media.Items[2].Checked, media.Items[2].Enabled));
        Assert.Equal("Ctrl+O", media.Items[0].Shortcut);
        Assert.Equal("separator", media.Items[1].Kind);
    }

    [Fact]
    public async Task ActionsReachTheShellAndSayWhenNothingAnswersToTheName()
    {
        var surface = new FakeSurface();

        Assert.True((await Ask(surface, "ui.press", "PlayPauseButton")).Ok);
        Assert.True((await Ask(surface, "ui.press", "Command-open-file")).Ok);
        Assert.True((await Ask(surface, "ui.toggle", "ShuffleButton")).Ok);
        Assert.True((await Ask(surface, "ui.set", "VolumeSlider", 35)).Ok);
        var missing = await Ask(surface, "ui.press", "NoSuchButton");
        Assert.False(missing.Ok);
        Assert.Contains("NoSuchButton", missing.Error, StringComparison.Ordinal);
        Assert.False((await Ask(surface, "ui.toggle", "PlayPauseButton")).Ok);
        Assert.False((await Ask(surface, "ui.set", "PlayPauseButton", 1)).Ok);
        Assert.Equal(["press PlayPauseButton", "press Command-open-file", "toggle ShuffleButton", "set VolumeSlider=35", "press NoSuchButton", "toggle PlayPauseButton", "set PlayPauseButton=1"], surface.Log);
    }

    [Theory]
    [InlineData("ui.press", null)]
    [InlineData("ui.press", " ")]
    [InlineData("ui.toggle", null)]
    [InlineData("ui.set", null)]
    [InlineData("ui.text", "")]
    public async Task ActionsWithoutATargetAreRefusedBeforeTheShellIsAsked(string command, string? target)
    {
        var surface = new FakeSurface();

        var reply = await Ask(surface, command, target, value: 1);

        Assert.False(reply.Ok);
        Assert.Contains("needs a target", reply.Error, StringComparison.Ordinal);
        Assert.Empty(surface.Log);
    }

    [Fact]
    public async Task SettingNeedsAValue()
    {
        var surface = new FakeSurface();

        var reply = await Ask(surface, "ui.set", "VolumeSlider");

        Assert.False(reply.Ok);
        Assert.Equal("ui.set needs a value.", reply.Error);
        Assert.Empty(surface.Log);
    }

    [Fact]
    public async Task KeysGoThroughTheWindowsOwnShortcuts()
    {
        var surface = new FakeSurface();

        Assert.True((await Ask(surface, "ui.key", chord: "space")).Ok);
        var unbound = await Ask(surface, "ui.key", chord: "Ctrl+Alt+Shift+Q");
        var nonsense = await Ask(surface, "ui.key", chord: "Hyper+Q");

        Assert.Equal(["key Space", "key Ctrl+Alt+Shift+Q"], surface.Log);
        Assert.Equal("Ctrl+Alt+Shift+Q runs nothing here.", unbound.Error);
        Assert.Contains("is not a shortcut", nonsense.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TextIsReadFromTheControlThatShowsIt()
    {
        var surface = new FakeSurface();

        Assert.Equal("Volume 80 %", (await Ask(surface, "ui.text", "Osd")).Text);
        Assert.False((await Ask(surface, "ui.text", "PlayPauseButton")).Ok);
    }

    [Fact]
    public async Task ASnapshotIsSavedWhereTheScriptAsks()
    {
        var surface = new FakeSurface();
        var path = Path.Combine(Path.GetTempPath(), "window.png");

        var saved = await Ask(surface, "ui.snapshot", path: path);
        var nowhere = await Ask(surface, "ui.snapshot", path: " ");

        Assert.Equal((true, path, path), (saved.Ok, saved.Text, surface.LastSnapshot));
        Assert.False(nowhere.Ok);
        Assert.Contains("needs a path", nowhere.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownUiCommandNamesTheOnesThereAre()
    {
        var reply = await Ask(new FakeSurface(), "ui.dance");

        Assert.False(reply.Ok);
        Assert.Contains("ui.snapshot", reply.Error, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentNullException>(() => ShellAutomation.AnswerAsync(null!, new FakeSurface()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ShellAutomation.AnswerAsync(new AutomationRequest { Command = "ui.tree" }, null!));
    }

    [Fact]
    public async Task TheNewFieldsTravelThroughThePipeAndAnOldClientIsStillAnswered()
    {
        var pipe = "rexplayer-test-" + Guid.NewGuid().ToString("N")[..12];
        using var stop = new CancellationTokenSource();
        var surface = new FakeSurface();
        var serving = AutomationPipe.ServeAsync(pipe, request => ShellAutomation.IsUiRequest(request.Command)
            ? ShellAutomation.AnswerAsync(request, surface)
            : Task.FromResult(new AutomationReply { Ok = true, State = "Idle" }), stop.Token);

        var tree = await AutomationPipe.SendAsync(pipe, new AutomationRequest { Command = "ui.tree" }, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var set = await AutomationPipe.SendAsync(pipe, new AutomationRequest { Command = "ui.set", Target = "VolumeSlider", Value = 12.5 }, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var old = await AutomationPipe.SendAsync(pipe, new AutomationRequest { Command = "status", ProtocolVersion = 1 }, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var future = await AutomationPipe.SendAsync(pipe, new AutomationRequest { Command = "status", ProtocolVersion = 3 }, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await stop.CancelAsync();
        await serving;

        var listed = tree!.Controls!;
        Assert.Equal(5, listed.Count);
        Assert.Equal("Volume 80 %", listed[4].Text);
        Assert.Equal(2, tree.ProtocolVersion);
        Assert.True(set!.Ok);
        Assert.Contains("set VolumeSlider=12.5", surface.Log);
        Assert.Equal("Idle", old!.State);
        Assert.Contains("protocol 2", future!.Error, StringComparison.Ordinal);
    }
}
