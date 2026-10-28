using Rex.Media.AppCore.Shell;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>Finding a setting in Preferences (UI-10): what matches, and the section a search shows.</summary>
public sealed class SettingsSearchTests
{
    private static readonly string[] Sections = ["Playback", "Video and pictures", "Music and artwork", "Subtitles", "Library"];

    [Fact]
    [Capability("UI-10")]
    public void ASearchMatchesWhateverTheCaseAndAccents()
    {
        Assert.True(SettingsSearch.Says("Subtitle font", "sUbTiTlE"));
        Assert.True(SettingsSearch.Says("Caf\u00E9 music", "cafe"));
        Assert.True(SettingsSearch.Says("Artwork detail (%)", "detail (%"));
        Assert.False(SettingsSearch.Says("Subtitle font", "colour"));
        Assert.False(SettingsSearch.Says(null, "anything"));
        Assert.Throws<ArgumentNullException>(() => SettingsSearch.Says("text", null!));
    }

    [Fact]
    [Capability("UI-10")]
    public void ASectionNamedByTheSearchOutranksOneThatOnlyMentionsIt()
    {
        // Video and pictures mentions subtitles too, but "subtitle" means the Subtitles section.
        Assert.Equal("Subtitles", SettingsSearch.Section(Sections, ["Video and pictures", "Subtitles"], "Playback", " subtitle "));
    }

    [Fact]
    [Capability("UI-10")]
    public void TheSectionShownStaysWhileItMatchesElseTheFirstMatchIsShown()
    {
        Assert.Equal("Library", SettingsSearch.Section(Sections, ["Music and artwork", "Library"], "Library", "cover"));
        Assert.Equal("Music and artwork", SettingsSearch.Section(Sections, ["Library", "Music and artwork"], "Playback", "cover"));
    }

    [Fact]
    [Capability("UI-10")]
    public void AnEmptySearchLeavesTheSectionAloneAndNoMatchShowsNone()
    {
        Assert.Equal("Library", SettingsSearch.Section(Sections, Sections, "Library", "  "));
        Assert.Null(SettingsSearch.Section(Sections, [], "Library", "a setting that does not exist"));
        Assert.Throws<ArgumentNullException>(() => SettingsSearch.Section(null!, [], "Library", "x"));
        Assert.Throws<ArgumentNullException>(() => SettingsSearch.Section(Sections, null!, "Library", "x"));
        Assert.Throws<ArgumentNullException>(() => SettingsSearch.Section(Sections, [], "Library", null!));
    }
}
