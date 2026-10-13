using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Captions;

public sealed class CaptionCommandsTests
{
    [Fact]
    [Capability("OSD-11")]
    public void TheToggleTurnsCaptionsOnAndOffAndSaysSo()
    {
        var on = CaptionCommands.Apply(CommandCatalog.ToggleKineticCaptions, new KineticCaptionSettings())!.Value;
        Assert.Equal((true, "Kinetic captions on"), (on.Settings.On, on.Message));
        var off = CaptionCommands.Apply(CommandCatalog.ToggleKineticCaptions, on.Settings)!.Value;
        Assert.Equal((false, "Kinetic captions off"), (off.Settings.On, off.Message));
        Assert.Equal("Ctrl+Shift+V", CommandCatalog.Find(CommandCatalog.ToggleKineticCaptions)!.DefaultShortcuts.Single().ToString());
    }

    [Fact]
    [Capability("OSD-11")]
    public void EnergyStepsWithinItsRangeAndTurnsCaptionsOn()
    {
        var settings = new KineticCaptionSettings { Energy = 290 };
        var livelier = CaptionCommands.Apply(CommandCatalog.KineticCaptionsLivelier, settings)!.Value;
        Assert.Equal((true, 300, "Kinetic caption energy 300%"), (livelier.Settings.On, livelier.Settings.Energy, livelier.Message));
        var calmer = CaptionCommands.Apply(CommandCatalog.KineticCaptionsCalmer, new KineticCaptionSettings { Energy = 40 })!.Value;
        Assert.Equal(25, calmer.Settings.Energy);
    }

    [Fact]
    [Capability("OSD-11")]
    public void LayoutAndWordSourceCycleThroughEveryChoiceAndBack()
    {
        var settings = new KineticCaptionSettings();
        var layouts = new List<string>();
        var words = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var (next, message) = CaptionCommands.Apply(CommandCatalog.CycleCaptionWords, settings)!.Value;
            words.Add(message);
            settings = next;
        }

        for (var i = 0; i < 3; i++)
        {
            var (next, message) = CaptionCommands.Apply(CommandCatalog.CycleCaptionLayout, settings)!.Value;
            layouts.Add(message);
            settings = next;
        }

        Assert.Equal(
            [
                "Kinetic caption words: the subtitles",
                "Kinetic caption words: the transcript",
                "Kinetic caption words: the subtitles, timed by the transcript",
                "Kinetic caption words: automatic",
            ],
            words);
        Assert.Equal(["Kinetic captions: stacked at the bottom", "Kinetic captions: centred", "Kinetic captions: scattered"], layouts);
        Assert.True(settings.On);

        var looks = new List<string>();
        for (var i = 0; i < Enum.GetValues<CaptionPreset>().Length; i++)
        {
            var (next, message) = CaptionCommands.Apply(CommandCatalog.CycleCaptionStyle, settings)!.Value;
            looks.Add(message);
            settings = next;
        }

        Assert.Equal("Kinetic captions: Sing-along", looks[0]);
        Assert.Equal("Kinetic captions: Creator cut", looks[^1]);
        Assert.Equal(looks.Count, looks.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(CaptionPreset.Creator, settings.Preset);
    }

    [Fact]
    public void OtherCommandsAreNotHandledAndNullIsRefused()
    {
        Assert.Null(CaptionCommands.Apply(CommandCatalog.PlayPause, new KineticCaptionSettings()));
        Assert.All(CaptionCommands.Ids, id => Assert.NotNull(CommandCatalog.Find(id)));
        Assert.Throws<ArgumentNullException>(() => CaptionCommands.Apply(null!, new KineticCaptionSettings()));
        Assert.Throws<ArgumentNullException>(() => CaptionCommands.Apply(CommandCatalog.PlayPause, null!));
    }

    [Fact]
    [Capability("OSD-11")]
    public void SettingsFromADamagedFileAreBroughtIntoRange()
    {
        var damaged = new KineticCaptionSettings
        {
            Words = (CaptionWordSource)99,
            Energy = 9000,
            Accent = unchecked((int)0xFF123456),
            Colors = (CaptionColors)99,
            Layout = (CaptionLayoutChoice)99,
            TranscriptModel = (TranscriptModelChoice)99,
            TranscriptLanguage = " EN ",
        };
        var normal = new PlayerSettings { KineticCaptions = damaged }.Normalize().KineticCaptions;
        Assert.Equal(
            (CaptionWordSource.Automatic, 300, 0x123456, CaptionColors.Warm, CaptionLayoutChoice.Scatter, TranscriptModelChoice.Base, "en"),
            (normal.Words, normal.Energy, normal.Accent, normal.Colors, normal.Layout, normal.TranscriptModel, normal.TranscriptLanguage));
        Assert.Equal("", (damaged with { TranscriptLanguage = "english" }).Normalize().TranscriptLanguage);
        Assert.Equal("", (damaged with { TranscriptLanguage = null! }).Normalize().TranscriptLanguage);
        Assert.Equal("", (damaged with { TranscriptLanguage = "e1" }).Normalize().TranscriptLanguage);
        Assert.NotNull((new PlayerSettings() with { KineticCaptions = null! }).Normalize().KineticCaptions);
    }
}
