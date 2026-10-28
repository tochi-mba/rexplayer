using Rex.Media.AppCore.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// How much of the library its header takes (LIB-05): compact on every view of a short library and
/// on a scrolled view, a name on pages with no banner of their own, search only where it searches
/// the view, actions beside the compact title when they fit, and a billboard that leaves a shelf in view.
/// </summary>
public sealed class LibraryHeaderLayoutTests
{
    [Theory]
    [Capability("LIB-05")]
    [InlineData(700, 0, false, false)]
    [InlineData(560, 0, false, false)]
    [InlineData(549, 0, false, true)]
    [InlineData(400, 0, false, true)]
    [InlineData(0, 0, false, false)]
    [InlineData(700, 28, false, false)]
    [InlineData(700, 29, false, true)]
    [InlineData(700, 0, true, true)]
    [InlineData(0, 0, true, true)]
    public void TheHeaderIsCompactOnAShortLibraryAScrolledViewAndAPageWithOnlyAName(double height, double scrolled, bool titleOnly, bool compact)
    {
        Assert.Equal(compact, LibraryHeaderLayout.Compact(height, scrolled, titleOnly, compactNow: false));
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(0.5, true)]
    [InlineData(0, false)]
    public void ACompactHeaderExpandsOnlyBackAtTheTop(double scrolled, bool compact)
    {
        // A view that compacting made short enough to scroll back under the threshold stays compact.
        Assert.Equal(compact, LibraryHeaderLayout.Compact(700, scrolled, titleOnly: false, compactNow: true));
        Assert.False(LibraryHeaderLayout.Compact(700, scrolled, titleOnly: false, compactNow: false));
    }

    [Theory]
    [InlineData(false, 200, true)]
    [InlineData(true, 0, true)]
    [InlineData(true, 319, false)]
    [InlineData(true, 320, true)]
    [InlineData(true, 600, true)]
    public void OnlyHomeInATinyLibraryDropsItsSearchAndViewOptions(bool home, double height, bool offered)
    {
        Assert.Equal(offered, LibraryHeaderLayout.OffersViewOptions(home, height));
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData(false, "", true)]
    [InlineData(false, "asa", true)]
    [InlineData(true, "", false)]
    [InlineData(true, "   ", false)]
    [InlineData(true, "asa", true)]
    public void APageAboutOneThingLeavesOutTheLibrarySearchUnlessItHoldsOne(bool onePage, string typed, bool offered)
    {
        Assert.Equal(offered, LibraryHeaderLayout.OffersSearch(onePage, typed));
    }

    [Theory]
    [InlineData(600, 400, true)]
    [InlineData(560, 400, true)]
    [InlineData(559, 400, false)]
    [InlineData(300, 0, true)]
    [InlineData(100, 0, false)]
    public void CompactActionsSitBesideTheTitleOnlyWhenTheTitleKeepsItsRoom(double room, double actions, bool beside)
    {
        Assert.Equal(beside, LibraryHeaderLayout.ActionsBesideTitle(room, actions));
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData(0, 320)]
    [InlineData(-5, 320)]
    [InlineData(300, 248)]
    [InlineData(390, 248)]
    [InlineData(450, 290)]
    [InlineData(900, 320)]
    public void TheBillboardLeavesTheFirstShelfInView(double shelves, double height)
    {
        Assert.Equal(height, LibraryHeaderLayout.BillboardHeight(shelves));
    }
}
