using Rex.Media.AppCore.Library;
using Rex.Media.Settings;

namespace Rex.Media.Tests.AppCore;

public sealed class LibraryPlacesTests
{
    [Fact]
    public void AKeyNamesTheViewTheGroupItsPageAndTheSearchApart()
    {
        Assert.Equal("Songs", LibraryPlaces.Key("Songs"));
        Assert.Equal("Albums\nLungu Boy\nAsake", LibraryPlaces.Key("Albums", "Lungu Boy", "Asake"));
        Assert.Equal("Artists\nAsake\n", LibraryPlaces.Key("Artists", "Asake"));
        Assert.Equal("Artists\nAsake\n\nWork of Art", LibraryPlaces.Key("Artists", "Asake", null, "Work of Art"));
        Assert.Equal("TvShows\nThe Show\n\nSeason 2", LibraryPlaces.Key(new LibraryRoute("TvShows", "The Show", null, "Season 2")));

        // A page without a group is no page; a search sits apart from the route it narrows.
        Assert.Equal("Songs", LibraryPlaces.Key("Songs", page: "Season 2"));
        Assert.Equal("Songs\n?terminator", LibraryPlaces.Key("Songs", search: "  terminator "));
        Assert.Equal("Songs", LibraryPlaces.Key("Songs", search: "   "));
        Assert.True(LibraryPlaces.IsSearch(LibraryPlaces.Key("Songs", search: "terminator")));
        Assert.False(LibraryPlaces.IsSearch("Songs"));
        Assert.Equal("Songs", LibraryPlaces.Unsearched(LibraryPlaces.Key("Songs", search: "terminator")));
        Assert.Equal("Albums\nLungu Boy\nAsake", LibraryPlaces.Unsearched("Albums\nLungu Boy\nAsake"));
        Assert.Throws<ArgumentException>(() => LibraryPlaces.Key(" "));
        Assert.Throws<ArgumentNullException>(() => LibraryPlaces.Key((LibraryRoute)null!));
        Assert.Throws<ArgumentNullException>(() => LibraryPlaces.IsSearch(null!));
        Assert.Throws<ArgumentNullException>(() => LibraryPlaces.Unsearched(null!));
    }

    [Fact]
    public void APlaceIsKeptPerRouteAndAnEmptyOneForgetsTheRoute()
    {
        var places = new LibraryPlaces();
        Assert.Null(places.PlaceOf("Songs"));
        Assert.True(places.Remember("Songs", new LibraryPlace("file:terminator", "file:sungba")));
        Assert.False(places.Remember("Songs", new LibraryPlace("file:terminator", "file:sungba")), "The same place again changes nothing.");
        Assert.Equal(new LibraryPlace("file:terminator", "file:sungba"), places.PlaceOf("Songs"));
        Assert.True(places.Remember("Songs", new LibraryPlace(Selected: "file:sungba")));
        Assert.Equal(new LibraryPlace(null, "file:sungba"), places.PlaceOf("Songs"));

        Assert.True(places.Remember("Songs", new LibraryPlace()));
        Assert.Null(places.PlaceOf("Songs"));
        Assert.False(places.Remember("Songs", new LibraryPlace(" ", "", -3)), "Blank names and a negative scroll are no place.");
        Assert.Throws<ArgumentException>(() => places.Remember("", new LibraryPlace("a")));
        Assert.Throws<ArgumentNullException>(() => places.Remember("Songs", null!));
    }

    [Fact]
    public void ThePlacesKeptForTheSettingsAreTheNewestWithoutSearches()
    {
        var places = new LibraryPlaces(new Dictionary<string, LibraryPlace>
        {
            ["Albums"] = new(Offset: 40),
            ["Home"] = new(Offset: 120, Shelves: [0, 300]),
            [LibraryPlaces.Key("Songs", search: "asake")] = new("file:joha"),
        });
        for (var i = 0; i < LibraryPlace.Most; i++)
        {
            Assert.True(places.Remember("Songs\n" + i, new LibraryPlace("file:" + i)));
        }

        // Albums was touched again, so Home is the oldest and the one dropped past the cap.
        Assert.True(places.Remember("Albums", new LibraryPlace(Offset: 80)));
        var snapshot = places.Snapshot();
        Assert.Equal(LibraryPlace.Most, snapshot.Count);
        Assert.False(snapshot.ContainsKey("Home"));
        Assert.Equal(80, snapshot["Albums"].Offset);
        Assert.Equal("Albums", snapshot.Keys.Last());
        Assert.DoesNotContain(snapshot.Keys, LibraryPlaces.IsSearch);
        Assert.Equal(new LibraryPlace("file:joha"), places.PlaceOf(LibraryPlaces.Key("Songs", search: "asake")));

        places.Forget();
        Assert.Empty(places.Snapshot());
        Assert.Null(places.PlaceOf("Albums"));
    }

    [Fact]
    public void TheLastSearchOfAViewLastsTheSessionOnly()
    {
        var places = new LibraryPlaces();
        Assert.Equal("", places.SearchOf("Songs"));
        places.RememberSearch("Songs", "terminator");
        places.RememberSearch("Albums", "lungu");
        Assert.Equal("terminator", places.SearchOf("Songs"));
        Assert.Equal("lungu", places.SearchOf("Albums"));
        places.RememberSearch("Songs", " ");
        Assert.Equal("", places.SearchOf("Songs"));
        Assert.Empty(places.Snapshot());
        places.Forget();
        Assert.Equal("", places.SearchOf("Albums"));
        Assert.Throws<ArgumentException>(() => places.RememberSearch("", "x"));
    }

    [Fact]
    public void ARebuiltViewStaysAnchoredOnAChosenLineThenOnTheTopLineThatSurvived()
    {
        var after = new HashSet<string>(StringComparer.Ordinal) { "file:a", "file:c", "file:d" };
        Assert.Equal("file:c", LibraryPlaces.SurvivingAnchor(["file:b", "file:c", "file:a"], after));
        Assert.Equal("file:a", LibraryPlaces.SurvivingAnchor([null, "file:a"], after));
        Assert.Null(LibraryPlaces.SurvivingAnchor(["file:b", null], after));
        Assert.Null(LibraryPlaces.SurvivingAnchor([], after));
        Assert.Throws<ArgumentNullException>(() => LibraryPlaces.SurvivingAnchor(null!, after));
        Assert.Throws<ArgumentNullException>(() => LibraryPlaces.SurvivingAnchor([], null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void DynamicShowAndArtistCountsNeverBecomePartOfThePersistentRouteIdentity()
    {
        var before = new LibraryRoute("TvShows", "North Shore", "2 seasons · 4 episodes", "Season 2");
        var after = new LibraryRoute("TvShows", "North Shore", "3 seasons · 7 episodes", "Season 2");
        Assert.Equal(LibraryPlaces.Key(before), LibraryPlaces.Key(after));
        Assert.Equal("TvShows\nNorth Shore\n\nSeason 2", LibraryPlaces.Key(before));
        Assert.Null(LibraryPlaces.StableGroupDetail("Artists", "12 songs"));
        Assert.Null(LibraryPlaces.StableGroupDetail("Genres", "45 albums"));
        Assert.Null(LibraryPlaces.StableGroupDetail("TvShows", "14 episodes"));

        // Same album title by two artists must remain two distinct pages.
        var artistOne = new LibraryRoute("Albums", "Greatest Hits", "First Artist");
        var artistTwo = new LibraryRoute("Albums", "Greatest Hits", "Second Artist");
        Assert.NotEqual(LibraryPlaces.Key(artistOne), LibraryPlaces.Key(artistTwo));
        Assert.Equal("First Artist", LibraryPlaces.StableGroupDetail("Albums", "First Artist"));

        var places = new LibraryPlaces();
        var oldKey = LibraryPlaces.Key(before);
        Assert.True(places.Remember(oldKey, new LibraryPlace(Selected: "file:D:/NorthShore/S02E01.mkv")));
        Assert.Equal("file:D:/NorthShore/S02E01.mkv", places.PlaceOf(LibraryPlaces.Key(after))!.Selected);
    }

    [Fact]
    public void APlaceNormalizesItsNamesAndPixels()
    {
        var place = new LibraryPlace(" ", "", double.NaN, [-1, double.PositiveInfinity, 12.5]).Normalize();
        Assert.Equal(new LibraryPlace(null, null, 0, [0, 0, 12.5]).Shelves, place.Shelves);
        Assert.Null(place.Anchor);
        Assert.Null(place.Selected);
        Assert.Equal(0, place.Offset);
        Assert.False(place.IsEmpty, "A shelf scrolled sideways is a place.");
        Assert.True(new LibraryPlace(Shelves: [0, 0]).IsEmpty);
        Assert.Null(new LibraryPlace(Shelves: []).Normalize().Shelves);
        Assert.True(new LibraryPlace().IsEmpty);
        Assert.False(new LibraryPlace(Offset: 1).IsEmpty);
    }
}
