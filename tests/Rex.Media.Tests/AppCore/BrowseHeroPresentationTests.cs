using Rex.Media.AppCore.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>Photography must never compromise Windows system high-contrast colors.</summary>
public sealed class BrowseHeroPresentationTests
{
    [Theory]
    [Capability("LIB-05")]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, true, true, false, true)]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, true, false, true, false)]
    public void SystemContrastOverridesPhotographyAndFades(bool highContrast, bool animations,
        bool expectedBackdrop, bool expectedSystemColors, bool expectedFade)
    {
        Assert.Equal(
            new BrowseHeroPresentation(expectedBackdrop, expectedSystemColors, expectedFade),
            BrowseHeroPresentation.For(highContrast, animations));
    }
}
