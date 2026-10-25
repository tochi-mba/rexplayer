using Rex.Media.AppCore.Library;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>How each library view starts, and Browse as Movies' and TV shows' layout until another is picked (LIB-05).</summary>
public sealed class LibraryViewDefaultsTests
{
    [Theory]
    [Capability("LIB-05")]
    [InlineData("Movies", LibraryLook.Browse)]
    [InlineData("TvShows", LibraryLook.Browse)]
    [InlineData("Songs", LibraryLook.List)]
    [InlineData("TvShows page", LibraryLook.List)]
    [InlineData("Authors book", LibraryLook.List)]
    [InlineData("Search", LibraryLook.List)]
    [InlineData("Pictures", LibraryLook.Grid)]
    [InlineData("Videos", LibraryLook.Grid)]
    [InlineData("TvShows group", LibraryLook.Grid)]
    [InlineData("Albums", LibraryLook.Grid)]
    public void EachViewStartsInTheLayoutThatSuitsIt(string key, LibraryLook look)
    {
        Assert.Equal(look, LibraryViewDefaults.For(key).Look);
        Assert.Equal(LibraryViewDefaults.For(key), LibraryViewDefaults.Resolve(key, null));
    }

    [Fact]
    [Capability("LIB-05")]
    public void PicturesAndVideosStartWithTheirOwnCardSizesAndGroups()
    {
        Assert.Equal(new LibraryViewChoice(LibraryLook.Grid, CardSize: 200, Grouping: LibraryGrouping.Folder), LibraryViewDefaults.For("Pictures"));
        Assert.Equal(200, LibraryViewDefaults.For("Movies").CardSize);
        Assert.Equal(180, LibraryViewDefaults.For("ContinueWatching").CardSize);
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData("Movies")]
    [InlineData("TvShows")]
    public void CardsSavedWithoutBeingPickedOpenAsBrowse(string key)
    {
        // Kept from before Browse, or saved when only the order or card size changed.
        var kept = new LibraryViewChoice(LibraryLook.Grid, LibrarySort.Year, CardSize: 260);
        Assert.Equal(kept with { Look = LibraryLook.Browse }, LibraryViewDefaults.Resolve(key, kept));
    }

    [Theory]
    [Capability("LIB-05")]
    [InlineData(LibraryLook.Grid, true)]
    [InlineData(LibraryLook.List, false)]
    [InlineData(LibraryLook.Wall, false)]
    [InlineData(LibraryLook.Collage, false)]
    [InlineData(LibraryLook.Browse, false)]
    public void ALayoutThePersonPickedIsKept(LibraryLook look, bool chosen)
    {
        var kept = new LibraryViewChoice(look, LookChosen: chosen);
        Assert.Same(kept, LibraryViewDefaults.Resolve("Movies", kept));
    }

    [Fact]
    [Capability("LIB-05")]
    public void OtherViewsKeepCardsSavedWithoutAPick()
    {
        var kept = new LibraryViewChoice(LibraryLook.Grid, CardSize: 300);
        Assert.Same(kept, LibraryViewDefaults.Resolve("Songs", kept));
        Assert.Same(kept, LibraryViewDefaults.Resolve("TvShows group", kept));
    }

    [Fact]
    [Capability("LIB-05")]
    public void ChangingTheLayoutMarksItPickedAndOtherChangesDoNot()
    {
        var browse = new LibraryViewChoice(LibraryLook.Browse);
        var grid = LibraryViewDefaults.Keep(browse, browse with { Look = LibraryLook.Grid });
        Assert.True(grid.LookChosen);
        Assert.Equal(LibraryLook.Grid, LibraryViewDefaults.Resolve("Movies", grid).Look);

        // Only the order changes: the layout stays unpicked, and a pick stays a pick.
        var sorted = LibraryViewDefaults.Keep(browse, browse with { Sort = LibrarySort.Title });
        Assert.False(sorted.LookChosen);
        Assert.True(LibraryViewDefaults.Keep(grid, grid with { CardSize = 240 }).LookChosen);

        Assert.Throws<ArgumentNullException>(() => LibraryViewDefaults.Keep(null!, browse));
        Assert.Throws<ArgumentNullException>(() => LibraryViewDefaults.Keep(browse, null!));
    }
}
