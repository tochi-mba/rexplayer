using Rex.Media.AppCore.Captions;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore.Captions;

public sealed class CaptionGlyphsTests
{
    [Theory]
    [Capability("OSD-11")]
    [InlineData("mute", "🔇")]
    [InlineData("MUTE!", "🔇")]
    [InlineData("“phone,”", "📱")]
    [InlineData("fire...", "🔥")]
    [InlineData("problem", "⚠️")]
    [InlineData("videos", "🎬")]
    public void AWordWithAGlyphIsFoundWhateverItsCaseOrPunctuation(string word, string glyph)
    {
        Assert.Equal(glyph, CaptionGlyphs.For(word));
    }

    [Theory]
    [InlineData("creators")]
    [InlineData("")]
    [InlineData("...")]
    [InlineData("firefly")]
    public void OtherWordsHaveNone(string word)
    {
        Assert.Null(CaptionGlyphs.For(word));
    }

    [Fact]
    public void NullIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => CaptionGlyphs.For(null!));
    }
}
