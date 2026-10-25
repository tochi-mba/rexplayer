using Rex.Media.AppCore.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>A library view is built once for each search, however the box reports its changes (LIB-05).</summary>
public sealed class LibrarySearchTextTests
{
    [Fact]
    [Capability("LIB-05")]
    public void ClearingTheBoxWhileChangingViewDoesNotBuildTheNewViewAgain()
    {
        var search = new LibrarySearchText();
        Assert.Equal("", search.Built);
        Assert.Equal("asake", search.Build("  asake "));
        Assert.False(search.Changes("asake"));
        Assert.False(search.Changes("asake  "));

        // A view change clears the box and builds the new view at once; the box's late report of
        // the clearing finds that view already built.
        Assert.True(search.Changes(""));
        Assert.Equal("", search.Build(""));
        Assert.False(search.Changes(""));
        Assert.False(search.Changes("   "));

        // Typing still builds, and so does clearing the box by itself.
        Assert.True(search.Changes("sungba"));
        Assert.Equal("sungba", search.Build("sungba"));
        Assert.True(search.Changes(""));
    }

    [Fact]
    public void BuildingTheSameSearchAgainKeepsTheStringItHas()
    {
        var search = new LibrarySearchText();
        var built = search.Build("terminator");
        Assert.Same(built, search.Build(" terminator "));
    }

    [Fact]
    public void TheBoxAlwaysHoldsText()
    {
        var search = new LibrarySearchText();
        Assert.Throws<ArgumentNullException>(() => search.Build(null!));
        Assert.Throws<ArgumentNullException>(() => search.Changes(null!));
    }
}
