using System.Xml.Linq;
using Rex.Media.AppCore.Design;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed class DesignTokensTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void TextOnTheSignalIsReadable()
    {
        Assert.True(DesignTokens.Contrast(DesignTokens.Signal, DesignTokens.OnSignal) >= 7);
    }

    [Fact]
    public void PrimaryAndSecondaryTextAreReadableOnEveryInkTheyCanSitOn()
    {
        foreach (var ink in DesignTokens.DarkInk)
        {
            Assert.True(DesignTokens.Contrast(DesignTokens.DarkText[0], ink) >= 7, DesignTokens.Hex(ink));
            Assert.True(DesignTokens.Contrast(DesignTokens.DarkText[1], ink) >= 4.5, DesignTokens.Hex(ink));
        }

        foreach (var ink in DesignTokens.LightInk[..3])
        {
            Assert.True(DesignTokens.Contrast(DesignTokens.LightText[0], ink) >= 7, DesignTokens.Hex(ink));
            Assert.True(DesignTokens.Contrast(DesignTokens.LightText[1], ink) >= 4.5, DesignTokens.Hex(ink));
        }
    }

    [Fact]
    public void ContrastIsSymmetricAndRunsFromOneToTwentyOne()
    {
        Assert.Equal(1, DesignTokens.Contrast(DesignTokens.Signal, DesignTokens.Signal), 6);
        Assert.Equal(21, DesignTokens.Contrast(0xFF000000, 0xFFFFFFFF), 6);
        Assert.Equal(DesignTokens.Contrast(DesignTokens.Tool, DesignTokens.DarkInk[0]), DesignTokens.Contrast(DesignTokens.DarkInk[0], DesignTokens.Tool), 9);
    }

    [Fact]
    public void AlphaIsReplacedAndTheColourKept()
    {
        Assert.Equal(0xC00B0F0Cu, DesignTokens.WithAlpha(DesignTokens.DarkInk[0], DesignTokens.OverlayAlpha));
        Assert.Equal("#C00B0F0C", DesignTokens.Hex(DesignTokens.WithAlpha(DesignTokens.DarkInk[0], DesignTokens.OverlayAlpha)));
    }

    [Fact]
    public void ScalesRiseInOrder()
    {
        Assert.Equal([.. DesignTokens.Radii.Order()], DesignTokens.Radii);
        Assert.Equal([.. DesignTokens.Spacing.Order()], DesignTokens.Spacing);
        Assert.Equal([.. DesignTokens.TypeSizes.Order()], DesignTokens.TypeSizes);
        Assert.Equal([.. DesignTokens.MotionMilliseconds.Order()], DesignTokens.MotionMilliseconds);
        Assert.Equal(120, DesignTokens.KickerTracking);
        Assert.Equal(0xFFFF7C60u, DesignTokens.Attention);
        Assert.Equal([.. DesignTokens.ScrimAlphas.Order()], DesignTokens.ScrimAlphas);
    }

    [Fact]
    public void TheWindowsThemeDrawsWithTheseValues()
    {
        var app = XDocument.Load(RepoPaths.Combine("src", "Rex.Media.App", "App.xaml"));
        string Theme(string theme, string key) => app.Descendants()
            .Single(d => (string?)d.Attribute(Xaml + "Key") == theme)
            .Elements().Single(e => (string?)e.Attribute(Xaml + "Key") == key)
            .Attribute("Color")!.Value;
        string Shared(string key) => app.Descendants()
            .Single(e => (string?)e.Attribute(Xaml + "Key") == key && e.Parent!.Attribute(Xaml + "Key") is null)
            .Attribute("Color")!.Value;
        string Value(string key) => app.Descendants().Single(e => (string?)e.Attribute(Xaml + "Key") == key).Value;

        foreach (var (theme, ink, text) in new[] { ("Dark", DesignTokens.DarkInk, DesignTokens.DarkText), ("Light", DesignTokens.LightInk, DesignTokens.LightText) })
        {
            var canvas = theme == "Dark" ? ink[1] : ink[0];
            var surface = theme == "Dark" ? ink[2] : ink[1];
            var raised = theme == "Dark" ? ink[3] : ink[2];
            var hairline = ink[3];
            Assert.Equal(DesignTokens.Hex(canvas), Theme(theme, "LibraryCanvasBrush"));
            Assert.Equal(DesignTokens.Hex(surface), Theme(theme, "LibrarySurfaceBrush"));
            Assert.Equal(DesignTokens.Hex(raised), Theme(theme, "RexRaisedBrush"));
            Assert.Equal(DesignTokens.Hex(hairline), Theme(theme, "RexHairlineBrush"));
            Assert.Equal(DesignTokens.Hex(text[0]), Theme(theme, "RexTextPrimaryBrush"));
            Assert.Equal(DesignTokens.Hex(text[1]), Theme(theme, "RexTextSecondaryBrush"));
            Assert.Equal(DesignTokens.Hex(text[2]), Theme(theme, "RexTextTertiaryBrush"));
            Assert.Equal(DesignTokens.Hex(DesignTokens.Signal), Theme(theme, "RexSignalBrush"));
            Assert.Equal(DesignTokens.Hex(DesignTokens.OnSignal), Theme(theme, "RexOnSignalBrush"));
        }

        Assert.Equal(DesignTokens.Hex(DesignTokens.Signal), Value("SignalColor"));
        Assert.Equal(DesignTokens.Hex(DesignTokens.Tool), Shared("RexToolBrush"));
        Assert.Equal(DesignTokens.Hex(DesignTokens.Attention), Shared("RexAttentionBrush"));
        Assert.Equal(DesignTokens.Hex(DesignTokens.WithAlpha(DesignTokens.DarkInk[0], DesignTokens.OverlayAlpha)), Shared("RexOverlayBrush"));
        Assert.Equal(DesignTokens.Hex(DesignTokens.WithAlpha(DesignTokens.DarkInk[0], DesignTokens.StrongOverlayAlpha)), Shared("RexStrongOverlayBrush"));
        Assert.Equal(DesignTokens.Hex(DesignTokens.DarkInk[0]), Shared("RexStageSurfaceBrush"));
        Assert.Equal(DesignTokens.Hex(DesignTokens.DarkText[0]), Shared("RexOverlayTextBrush"));
        Assert.Equal(DesignTokens.Hex(DesignTokens.DarkText[1]), Shared("RexOverlaySecondaryTextBrush"));
        Assert.Equal(DesignTokens.Hex(DesignTokens.DarkText[2]), Shared("RexOverlayTertiaryTextBrush"));
        Assert.Equal(DesignTokens.Hex(DesignTokens.OverlayFill), Shared("RexOverlayFillBrush"));
        Assert.Equal(DesignTokens.Hex(DesignTokens.OverlayHairline), Shared("RexOverlayHairlineBrush"));
        Assert.Equal(DesignTokens.Hex(DesignTokens.ToolFill), Shared("RexToolFillBrush"));
        Assert.Equal(DesignTokens.Hex(DesignTokens.WithAlpha(DesignTokens.DarkInk[0], DesignTokens.ScrimAlphas[2])), Shared("RexScrimBrush"));
        Assert.Equal(
            DesignTokens.ScrimAlphas.Select(a => DesignTokens.Hex(DesignTokens.WithAlpha(DesignTokens.DarkInk[0], a))),
            new[] { "RexScrimClearColor", "RexScrimSoftColor", "RexScrimColor", "RexScrimStrongColor" }.Select(Value));
        Assert.Equal(["4", "8", "12", "16"], new[] { "RexChipRadius", "RexSmallRadius", "RexCardRadius", "RexPageRadius" }.Select(Value));
        Assert.Equal(DesignTokens.Spacing.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)), Enumerable.Range(1, 5).Select(i => Value($"RexSpace{i}")));
        Assert.Equal(DesignTokens.TypeSizes.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)), new[] { "RexMicroFontSize", "RexCaptionFontSize", "RexBodyFontSize", "RexSectionFontSize", "RexTitleFontSize", "RexDisplayFontSize" }.Select(Value));
        Assert.Equal(DesignTokens.KickerTracking.ToString(System.Globalization.CultureInfo.InvariantCulture), Value("RexKickerTracking"));
    }
}
