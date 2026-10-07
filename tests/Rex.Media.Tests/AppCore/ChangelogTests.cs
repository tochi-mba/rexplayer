using Rex.Media.AppCore;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed class ChangelogTests
{
    private const string Text = """
        # Changelog

        Intro.

        ## 0.4.0 - 2026-10-08

        ### Added

        - **The window.** Plays things,
          wrapped onto a second line.
        - Plain item.

        ### Fixed

        - A fix.

        ## 0.3.0 - 2026-10-06

        - Old.
        """;

    [Fact]
    [Capability("UI-15")]
    public void OneVersionsSectionReadsAsPlainSentences()
    {
        Assert.Equal(
            "Added\n\u2022 The window. Plays things, wrapped onto a second line.\n\u2022 Plain item.\nFixed\n\u2022 A fix.",
            Changelog.Section(Text, "0.4.0"));
    }

    [Fact]
    public void TheLastSectionRunsToTheEndAndUnknownVersionsHaveNone()
    {
        Assert.Equal("\u2022 Old.", Changelog.Section(Text, "0.3.0"));
        Assert.Null(Changelog.Section(Text, "9.9.9"));
        Assert.Equal("Loose text", Changelog.Section("## 1.0.0\nLoose\ntext", "1.0.0"));
        Assert.Throws<ArgumentNullException>(() => Changelog.Section(null!, "1.0.0"));
    }

    [Fact]
    public void TheShippedChangelogHasASectionForTheCurrentVersion()
    {
        var props = File.ReadAllText(RepoPaths.Combine("Directory.Build.props"));
        var version = System.Text.RegularExpressions.Regex.Match(props, "<Version>(.+?)</Version>").Groups[1].Value;

        Assert.False(string.IsNullOrEmpty(Changelog.Section(File.ReadAllText(RepoPaths.Combine("CHANGELOG.md")), version)));
    }
}
