using Rex.Media.AppCore.Visuals;

namespace Rex.Media.Tests.AppCore;

/// <summary>The colour a music page's header is tinted with, chosen from its cover and set deep enough for white words.</summary>
public sealed class DominantColourTests
{
    private static byte[] Pixels(params (Tint Colour, int Count, byte Alpha)[] runs)
    {
        var bytes = new List<byte>();
        foreach (var (colour, count, alpha) in runs)
        {
            for (var n = 0; n < count; n++)
            {
                bytes.AddRange([colour.B, colour.G, colour.R, alpha]);
            }
        }

        return [.. bytes];
    }

    [Fact]
    public void AVividPatchBeatsALargerGreyOneButNotALargerVividOne()
    {
        var orange = new Tint(0xE0, 0x70, 0x10);
        var grey = new Tint(0x80, 0x80, 0x80);
        var blue = new Tint(0x10, 0x40, 0xD0);

        Assert.Equal(orange, DominantColour.Pick(Pixels((grey, 100, 255), (orange, 60, 255))));
        Assert.Equal(blue, DominantColour.Pick(Pixels((orange, 40, 255), (blue, 70, 255))));
    }

    [Fact]
    public void BlackWhiteAndSeeThroughPixelsHardlyCount()
    {
        var teal = new Tint(0x10, 0x90, 0x90);
        Assert.Equal(teal, DominantColour.Pick(Pixels((new Tint(0, 0, 0), 300, 255), (new Tint(255, 255, 255), 300, 255), (teal, 20, 255), (new Tint(255, 0, 0), 500, 0))));
        // All black and white still gives a colour of its own; the averages of one bucket are kept.
        Assert.Equal(new Tint(5, 5, 5), DominantColour.Pick(Pixels((new Tint(4, 4, 4), 1, 255), (new Tint(6, 6, 6), 1, 255))));
        Assert.Equal(DominantColour.Fallback, DominantColour.Pick(Pixels((teal, 3, 10))));
        Assert.Equal(DominantColour.Fallback, DominantColour.Pick([]));
        Assert.Throws<ArgumentException>(() => DominantColour.Pick(new byte[3]));
    }

    [Fact]
    public void AHeaderTintKeepsItsHueAtADepthWhiteWordsReadOn()
    {
        var pale = new Tint(0xF0, 0xC0, 0xC8);
        var deep = DominantColour.ForHeader(pale);
        Assert.InRange(DominantColour.Luminance(deep), DominantColour.MostLuminance - 0.002, DominantColour.MostLuminance + 0.002);
        Assert.True(deep.R > deep.G && deep.R > deep.B, "Still pink: " + deep);

        // A dark colour is lifted to the least depth, a bright one brought down to the most.
        var yellow = DominantColour.ForHeader(new Tint(0x10, 0x10, 0x02));
        Assert.InRange(DominantColour.Luminance(yellow), DominantColour.LeastLuminance - 0.002, DominantColour.LeastLuminance + 0.002);
        var bright = DominantColour.ForHeader(new Tint(0xFF, 0xFF, 0x00));
        Assert.InRange(DominantColour.Luminance(bright), DominantColour.MostLuminance - 0.002, DominantColour.MostLuminance + 0.002);

        var fine = new Tint(0x30, 0x50, 0x70);
        Assert.Equal(fine, DominantColour.ForHeader(fine));
        Assert.Equal(DominantColour.Fallback, DominantColour.ForHeader(new Tint(0, 0, 0)));
        Assert.Equal(1, DominantColour.Luminance(new Tint(255, 255, 255)), 6);
    }

    [Fact]
    public void ATintReadsBackFromItsText()
    {
        var tint = new Tint(0x0A, 0xB0, 0xFF);
        Assert.Equal("#0AB0FF", tint.ToString());
        Assert.Equal(tint, DominantColour.Parse("#0AB0FF"));
        Assert.Null(DominantColour.Parse("#0AB0F"));
        Assert.Null(DominantColour.Parse("#GGGGGG"));
        Assert.Null(DominantColour.Parse(null));
    }
}
