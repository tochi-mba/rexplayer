using Rex.Media.AppCore.Updates;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed class ReleaseNotesTests
{
    /// <summary>Notes a hosting service generates from merged changes, as the 0.17.0 release had.</summary>
    private const string Generated = """
        <!-- Release notes generated using configuration in .github/release.yml -->

        ## What's Changed
        * Six picture effects, five looks and a once-per-process shader compile (0.17.0) by @someone in https://github.com/example/rexplayer/pull/41
        * **Faster seeking** in long files (#42)
        * Fix the [seek bar](https://example.com/docs) tooltip in #43

        ## New Contributors
        * @newcomer made their first contribution in https://github.com/example/rexplayer/pull/40

        **Full Changelog**: https://github.com/example/rexplayer/compare/v0.16.0...v0.17.0
        """;

    /// <summary>The changelog's own words, wrapped at a hundred columns.</summary>
    private const string Changelog = """
        # Changelog

        ## 0.19.0 - 2026-10-13

        ### Added

        - **Click to lock picks the right box.** One click on a person, a face or an object now
          selects the box around it that Subject Lock can follow best.

        ### Improved

        - **Faster tracking.** Following a visible subject does less repeated work each picture.

        ## 0.18.0 - 2026-10-12

        ### Added

        - **Episode skip controls.** Dedicated Next episode actions.
        """;

    [Fact]
    [Capability("TOOL-09")]
    public void GeneratedNotesLoseTheirHeadingsCreditsAndLinks()
    {
        // The update bar showed these raw: "What's Changed", each change's author and link, the
        // first-time contributor and the "Full Changelog" link.
        Assert.Equal(
            "- Six picture effects, five looks and a once-per-process shader compile (0.17.0)\n- **Faster seeking** in long files\n- Fix the seek bar tooltip",
            ReleaseNotes.Clean(Generated, "0.17.0"));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void TheChangelogsSectionForTheVersionIsPreferredAndItsWrappedLinesJoined()
    {
        Assert.Equal(
            "### Added\n- **Click to lock picks the right box.** One click on a person, a face or an object now selects the box around it that Subject Lock can follow best.\n### Improved\n- **Faster tracking.** Following a visible subject does less repeated work each picture.",
            ReleaseNotes.Clean(Changelog, "0.19.0"));

        // A version the notes have no section for: everything is kept, but for the headings that
        // have nothing under them.
        var all = ReleaseNotes.Clean(Changelog, "0.20.0");
        Assert.StartsWith("### Added\n- **Click to lock picks the right box.**", all, StringComparison.Ordinal);
        Assert.EndsWith("### Added\n- **Episode skip controls.** Dedicated Next episode actions.", all, StringComparison.Ordinal);

        // The last section runs to the end.
        Assert.Equal("### Added\n- **Episode skip controls.** Dedicated Next episode actions.", ReleaseNotes.Clean(Changelog, "0.18.0"));
    }

    [Fact]
    public void TidyNotesAreLeftAsTheyAre()
    {
        foreach (var notes in new[] { ReleaseNotes.Clean(Generated, "0.17.0"), ReleaseNotes.Clean(Changelog, "0.19.0") })
        {
            Assert.Equal(notes, ReleaseNotes.Clean(notes, ""));
            Assert.Equal(notes, ReleaseNotes.Clean(notes, "0.19.0"));
        }
    }

    [Fact]
    public void MarkdownMarksGoAndParagraphsStayApart()
    {
        var markdown = """
            # Highlights #

            A **bold** and *gentle* and _quiet_ and __strong__ `code` line
            that carries on, with ![a picture](https://example.com/a.png) and <b>tags</b>.

            1. Numbered
            2) Also numbered
            + Plus
            • Bullet already

            <!--
            hidden
            -->
            https://example.com/only-a-link
            snake_case_names stay
            """;

        Assert.Equal(
            "### Highlights\nA bold and gentle and quiet and strong code line that carries on, with and tags.\n- Numbered\n- Also numbered\n- Plus\n- Bullet already\nsnake_case_names stay",
            ReleaseNotes.Clean(markdown, "1.0.0"));
    }

    [Fact]
    public void HeadingsWithNothingUnderThemGo()
    {
        Assert.Equal("### Kept\n- one", ReleaseNotes.Clean("## Empty\n## Also empty\n## Kept\n- one\n## Last", ""));
        Assert.Equal("", ReleaseNotes.Clean("## What's Changed\n**Full Changelog**: https://x", "1.0.0"));
        Assert.Equal("", ReleaseNotes.Clean("", "1.0.0"));
    }

    [Fact]
    public void AVersionHeadingInTheNotesIsNotRepeated()
    {
        Assert.Equal("- Faster", ReleaseNotes.Clean("## rexplayer 0.19.0\n- Faster", "0.19.0"));
        Assert.Equal("- Faster", ReleaseNotes.Clean("## [v0.19.0]\n- Faster", "0.19.0"));
    }

    [Fact]
    public void LongNotesKeepWholeBlocksWithinTheLimit()
    {
        var block = "- " + new string('x', 998);
        var notes = ReleaseNotes.Clean(string.Join("\n", Enumerable.Repeat(block, 40)), "");

        Assert.True(notes.Length <= PendingUpdate.MaxNotesLength);
        Assert.Equal(PendingUpdate.MaxNotesLength / 1001, notes.Split('\n').Length);
        Assert.All(notes.Split('\n'), line => Assert.Equal(block, line));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void NotesAreDrawnAsHeadingsBulletsWithBoldLabelsAndParagraphs()
    {
        var blocks = ReleaseNotes.Blocks(ReleaseNotes.Clean(Changelog, "0.19.0") + "\n\nThanks for reading.");
        Assert.Equal(
            [
                new NoteBlock(NoteKind.Heading, "Added"),
                new NoteBlock(NoteKind.Bullet, "One click on a person, a face or an object now selects the box around it that Subject Lock can follow best.", "Click to lock picks the right box."),
                new NoteBlock(NoteKind.Heading, "Improved"),
                new NoteBlock(NoteKind.Bullet, "Following a visible subject does less repeated work each picture.", "Faster tracking."),
                new NoteBlock(NoteKind.Paragraph, "Thanks for reading."),
            ],
            blocks);
        Assert.Equal([new NoteBlock(NoteKind.Bullet, "Plain")], ReleaseNotes.Blocks("- Plain"));
        Assert.Empty(ReleaseNotes.Blocks(""));
    }

    [Fact]
    public void NotesSavedByAnOlderVersionReadTidily()
    {
        // Older versions kept plain text with the generated heading and bullets as dots.
        const string saved = "What's Changed\n\u2022 Six picture effects\n\u2022 @newcomer made their first contribution\nFull Changelog: v0.16.0...v0.17.0";
        Assert.Equal([new NoteBlock(NoteKind.Bullet, "Six picture effects")], ReleaseNotes.Blocks(saved));
    }

    [Fact]
    public void NothingIsNotes()
    {
        Assert.Throws<ArgumentNullException>(() => ReleaseNotes.Clean(null!, ""));
        Assert.Throws<ArgumentNullException>(() => ReleaseNotes.Clean("", null!));
        Assert.Throws<ArgumentNullException>(() => ReleaseNotes.Blocks(null!));
    }
}
