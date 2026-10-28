using Rex.Media.AppCore.Design;
using Rex.Media.AppCore.Visuals;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// Words over a banner (LIB-05): the ink that reads on a backdrop, the scrim a picture of unknown
/// colours needs under them, and a cover's colour deepened just enough to be a plate for them.
/// </summary>
public sealed class LegibilityTests
{
    private const uint Black = 0xFF000000;
    private const uint White = 0xFFFFFFFF;

    private static uint Argb(Tint tint) => 0xFF000000 | ((uint)tint.R << 16) | ((uint)tint.G << 8) | tint.B;

    [Fact]
    public void AColourLaidOverAnotherBlendsByItsAlphaAndLeavesNothingSeeThrough()
    {
        Assert.Equal(0xFF808080u, Legibility.Over(0x80FFFFFF, Black));
        Assert.Equal(0xFF123456u, Legibility.Over(0xFF123456, White));
        Assert.Equal(0xFF654321u, Legibility.Over(0x00123456, 0xFF654321));
        // What lies under counts as opaque, whatever its own alpha says.
        Assert.Equal(0xFFABCDEFu, Legibility.Over(0x00000000, 0x00ABCDEF));
        Assert.Equal([Legibility.Over(0x99102030, Black), Legibility.Over(0x99102030, White)], Legibility.OverAnyPicture(0x99102030));
    }

    [Fact]
    public void TheWorstContrastIsTheWeakestBackdropsAndThereIsAlwaysOne()
    {
        var grey = 0xFF777777u;
        Assert.Equal(DesignTokens.Contrast(White, grey), Legibility.WorstContrast(White, [Black, grey]), 9);
        Assert.Throws<ArgumentException>(() => Legibility.WorstContrast(White, []));
    }

    [Fact]
    [Capability("LIB-05")]
    public void LightWordsGoOnDarkBackdropsAndDarkWordsOnLightOnes()
    {
        Assert.Equal(DesignTokens.DarkText[1], Legibility.LightWords);
        Assert.Equal(DesignTokens.LightText[1], Legibility.DarkWords);
        Assert.True(Legibility.LightWordsRead([DesignTokens.DarkInk[2]]));
        Assert.True(Legibility.LightWordsRead([Argb(DominantColour.Fallback)]));
        Assert.False(Legibility.LightWordsRead([DesignTokens.LightInk[1]]));
        Assert.False(Legibility.LightWordsRead([DesignTokens.LightInk[0], 0xFFC0C0C0]));

        // Words that must read on every part of a backdrop take the ink whose worst part is better.
        Assert.Equal(
            Legibility.WorstContrast(Legibility.LightWords, [Black, White]) >= Legibility.WorstContrast(Legibility.DarkWords, [Black, White]),
            Legibility.LightWordsRead([Black, White]));
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData(true)]
    [InlineData(false)]
    public void AScrimJustOpaqueEnoughKeepsSecondaryWordsReadableOverAnyPicture(bool lightWords)
    {
        var (scrim, words) = lightWords
            ? (DesignTokens.DarkInk[0], Legibility.LightWords)
            : (DesignTokens.LightInk[1], Legibility.DarkWords);
        var alpha = Legibility.ScrimAlpha(scrim, words, Legibility.BodyContrast);

        Assert.InRange(alpha, (byte)0xA0, (byte)0xD0);
        Assert.True(Legibility.WorstContrast(words, Legibility.OverAnyPicture(DesignTokens.WithAlpha(scrim, alpha))) >= Legibility.BodyContrast);
        Assert.True(Legibility.WorstContrast(words, Legibility.OverAnyPicture(DesignTokens.WithAlpha(scrim, (byte)(alpha - 1)))) < Legibility.BodyContrast);
        Assert.Equal(lightWords, Legibility.LightWordsRead(Legibility.OverAnyPicture(DesignTokens.WithAlpha(scrim, alpha))));

        // A large title needs less, so the same scrim serves it too.
        Assert.True(Legibility.ScrimAlpha(scrim, words, Legibility.TitleContrast) < alpha);
    }

    [Fact]
    public void AScrimThatCannotMakeWordsReadIsOpaque()
    {
        Assert.Equal((byte)0xFF, Legibility.ScrimAlpha(White, White, Legibility.BodyContrast));
    }

    [Fact]
    [Capability("LIB-05")]
    public void ACoverColourIsDeepenedOnlyAsFarAsLightWordsNeed()
    {
        // Already deep enough: kept exactly.
        Assert.Equal(DesignTokens.DarkInk[2], Legibility.Toward(DesignTokens.DarkInk[2], DesignTokens.DarkInk[0], Legibility.LightWords, Legibility.BodyContrast));

        // A pale pink becomes a deeper pink, just past what the words need.
        var plate = Legibility.Toward(0xFFF0C0C8, DesignTokens.DarkInk[0], Legibility.LightWords, Legibility.BodyContrast);
        Assert.InRange(DesignTokens.Contrast(Legibility.LightWords, plate), Legibility.BodyContrast, Legibility.BodyContrast + 0.2);
        Assert.True(((plate >> 16) & 0xFF) > ((plate >> 8) & 0xFF) && ((plate >> 16) & 0xFF) > (plate & 0xFF), "Still pink: " + DesignTokens.Hex(plate));
    }

    [Fact]
    public void EveryHeaderTintBecomesAPlateLightWordsReadOn()
    {
        for (var red = 0; red <= 255; red += 51)
        {
            for (var green = 0; green <= 255; green += 51)
            {
                for (var blue = 0; blue <= 255; blue += 51)
                {
                    var tint = DominantColour.ForHeader(new Tint((byte)red, (byte)green, (byte)blue));
                    var plate = Legibility.Toward(Argb(tint), DesignTokens.DarkInk[0], Legibility.LightWords, Legibility.BodyContrast);
                    Assert.True(DesignTokens.Contrast(Legibility.LightWords, plate) >= Legibility.BodyContrast, tint.ToString());
                    Assert.True(Legibility.LightWordsRead([plate]), tint.ToString());
                }
            }
        }
    }

    [Fact]
    public void AColourIsMovedAllTheWayWhenEvenThatIsNotEnough()
    {
        // White words on a grey just short of readable only get worse toward white.
        Assert.Equal(White, Legibility.Toward(0xFF777777, White, White, Legibility.BodyContrast));
    }
}
