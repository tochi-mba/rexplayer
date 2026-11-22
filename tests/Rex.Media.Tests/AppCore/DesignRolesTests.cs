using System.Text.RegularExpressions;
using System.Xml.Linq;
using Rex.Media.AppCore.Design;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed class DesignRolesTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void EveryRoleIsTheTokenItsThemeNames()
    {
        Assert.Equal(DesignTokens.DarkInk[1], DesignRoles.Color(DesignRole.Canvas, dark: true));
        Assert.Equal(DesignTokens.LightInk[0], DesignRoles.Color(DesignRole.Canvas, dark: false));
        Assert.Equal(DesignTokens.DarkInk[2], DesignRoles.Color(DesignRole.Surface, dark: true));
        Assert.Equal(DesignTokens.LightInk[1], DesignRoles.Color(DesignRole.Surface, dark: false));
        Assert.Equal(DesignTokens.DarkInk[3], DesignRoles.Color(DesignRole.Raised, dark: true));
        Assert.Equal(DesignTokens.LightInk[2], DesignRoles.Color(DesignRole.Raised, dark: false));
        Assert.Equal(DesignTokens.DarkInk[3], DesignRoles.Color(DesignRole.Hairline, dark: true));
        Assert.Equal(DesignTokens.LightInk[3], DesignRoles.Color(DesignRole.Hairline, dark: false));
        Assert.Equal(DesignTokens.DarkText[0], DesignRoles.Color(DesignRole.TextPrimary, dark: true));
        Assert.Equal(DesignTokens.LightText[0], DesignRoles.Color(DesignRole.TextPrimary, dark: false));
        Assert.Equal(DesignTokens.DarkText[1], DesignRoles.Color(DesignRole.TextSecondary, dark: true));
        Assert.Equal(DesignTokens.LightText[1], DesignRoles.Color(DesignRole.TextSecondary, dark: false));
        Assert.Equal(DesignTokens.DarkText[2], DesignRoles.Color(DesignRole.TextTertiary, dark: true));
        Assert.Equal(DesignTokens.LightText[2], DesignRoles.Color(DesignRole.TextTertiary, dark: false));
    }

    [Fact]
    public void ARoleThatDoesNotExistIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DesignRoles.Color((DesignRole)99, dark: true));
    }

    [Fact]
    public void TheStageAndItsOverlaysAreBuiltFromTheDarkTokens()
    {
        Assert.Equal(0xFF000000u, DesignRoles.Stage);
        Assert.Equal(DesignTokens.DarkInk[0], DesignRoles.StageSurface);
        Assert.Equal(DesignTokens.WithAlpha(DesignTokens.DarkInk[0], DesignTokens.OverlayAlpha), DesignRoles.Overlay);
        Assert.Equal(DesignTokens.WithAlpha(DesignTokens.DarkInk[0], DesignTokens.StrongOverlayAlpha), DesignRoles.StrongOverlay);
        Assert.Equal(DesignTokens.DarkText[0], DesignRoles.OverlayText);
        Assert.Equal(DesignTokens.DarkText[1], DesignRoles.OverlaySecondaryText);
        Assert.Equal(DesignTokens.DarkText[2], DesignRoles.OverlayTertiaryText);
    }

    // The Windows shell spells its colours in App.xaml and the Mac shell reads DesignRoles: this is
    // what keeps the two shells drawing with the same colours.
    [Fact]
    public void TheWindowsThemeSpellsTheSameColourForEveryRole()
    {
        var app = XDocument.Load(RepoPaths.Combine("src", "Rex.Media.App", "App.xaml"));
        string Theme(string theme, string key) => app.Descendants()
            .Single(d => (string?)d.Attribute(Xaml + "Key") == theme)
            .Elements().Single(e => (string?)e.Attribute(Xaml + "Key") == key)
            .Attribute("Color")!.Value;
        string Shared(string key) => app.Descendants()
            .Single(e => (string?)e.Attribute(Xaml + "Key") == key && e.Parent!.Attribute(Xaml + "Key") is null)
            .Attribute("Color")!.Value;

        var brushes = new (string Key, DesignRole Role)[]
        {
            ("LibraryCanvasBrush", DesignRole.Canvas),
            ("LibrarySurfaceBrush", DesignRole.Surface),
            ("RexRaisedBrush", DesignRole.Raised),
            ("RexHairlineBrush", DesignRole.Hairline),
            ("RexTextPrimaryBrush", DesignRole.TextPrimary),
            ("RexTextSecondaryBrush", DesignRole.TextSecondary),
            ("RexTextTertiaryBrush", DesignRole.TextTertiary),
        };
        foreach (var (theme, dark) in new[] { ("Dark", true), ("Light", false) })
        {
            foreach (var (key, role) in brushes)
            {
                Assert.True(DesignTokens.Hex(DesignRoles.Color(role, dark)) == Theme(theme, key), $"{theme} {key} is not {role}");
            }
        }

        Assert.Equal(DesignTokens.Hex(DesignRoles.StageSurface), Shared("RexStageSurfaceBrush"));
        Assert.Equal(DesignTokens.Hex(DesignRoles.Overlay), Shared("RexOverlayBrush"));
        Assert.Equal(DesignTokens.Hex(DesignRoles.StrongOverlay), Shared("RexStrongOverlayBrush"));
        Assert.Equal(DesignTokens.Hex(DesignRoles.OverlayText), Shared("RexOverlayTextBrush"));
        Assert.Equal(DesignTokens.Hex(DesignRoles.OverlaySecondaryText), Shared("RexOverlaySecondaryTextBrush"));
        Assert.Equal(DesignTokens.Hex(DesignRoles.OverlayTertiaryText), Shared("RexOverlayTertiaryTextBrush"));
    }

    [Fact]
    public void TextIsReadableOnEverySurfaceARoleNamesInBothThemes()
    {
        foreach (var dark in new[] { true, false })
        {
            foreach (var back in new[] { DesignRole.Canvas, DesignRole.Surface, DesignRole.Raised })
            {
                var behind = DesignRoles.Color(back, dark);
                var where = $"{back} {(dark ? "dark" : "light")}";
                Assert.True(DesignTokens.Contrast(DesignRoles.Color(DesignRole.TextPrimary, dark), behind) >= 7, where);
                Assert.True(DesignTokens.Contrast(DesignRoles.Color(DesignRole.TextSecondary, dark), behind) >= 4.5, where);
            }
        }
    }

    // The Mac shell cannot be built here, so its theme is read as text: every colour it draws with
    // must come through DesignRoles or DesignTokens, never a value of its own.
    [Fact]
    public void TheMacThemeTakesItsColoursFromTheRoles()
    {
        var source = File.ReadAllText(RepoPaths.Combine("src", "Rex.Media.App.Mac", "MacTheme.cs"));

        Assert.Contains("DesignRoles.Color(", source, StringComparison.Ordinal);
        Assert.Empty(Regex.Matches(source, "0x[0-9A-Fa-f]{8}"));
        Assert.Single(Regex.Matches(source, @"NSColor\.From(Rgb|Srgb)"));
        Assert.Matches(@"Color\(uint argb\)\s*=>\s*NSColor\.FromSrgb\(", source);
    }
}
