using Rex.Media.AppCore.Library;

namespace Rex.Media.Tests.AppCore;

public sealed class PodcastNotesTests
{
    [Fact]
    public void OnlyExplicitNotesSidecarsAreRecognized()
    {
        Assert.Equal(["Shows/episode.notes.md", "Shows/episode.notes.txt"],
            PodcastNotes.Sidecars("Shows/episode.mp3"));
        Assert.Throws<ArgumentException>(() => PodcastNotes.Sidecars(""));
    }

    [Fact]
    public void MarkupIsOnlyInertTextWithUnicodeAndKeyboardReadableLineBreaks()
    {
        var original = "Intro\r\n<script>alert(1)</script>\nمرحبا\tworld\0\u0001";
        Assert.Equal("Intro\n<script>alert(1)</script>\nمرحبا\tworld",
            PodcastNotes.Display(original));
        Assert.Equal("a\nb", PodcastNotes.Display("a\rb"));
        Assert.Equal("a\nb", PodcastNotes.Display("a\nb"));
        Assert.Equal("", PodcastNotes.Display(""));
        Assert.Throws<ArgumentNullException>(() => PodcastNotes.Display(null!));
    }

    [Fact]
    public void OversizedNotesCannotMaterializeAnUnboundedDialog()
    {
        var text = new string('n', PodcastNotes.MaxVisibleCharacters + 10);
        var visible = PodcastNotes.Display(text);
        Assert.StartsWith(new string('n', PodcastNotes.MaxVisibleCharacters), visible, StringComparison.Ordinal);
        Assert.EndsWith("Notes truncated for display.", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("truncated", PodcastNotes.Display(new string('n', PodcastNotes.MaxVisibleCharacters)),
            StringComparison.Ordinal);
    }
}
