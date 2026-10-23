using Rex.Media.AppCore.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>What an empty library view says, and the ways out it offers (LIB-05).</summary>
public sealed class LibraryEmptyStateTests
{
    [Fact]
    [Capability("LIB-05")]
    public void AViewWhoseFiltersHideEverythingNamesThemAndOffersToClearThem()
    {
        // A filtered view used to claim the library held nothing of its kind.
        var state = LibraryEmptyState.For(LibraryEmptyView.Media, searching: false, hasFolders: true, filters: "Unwatched · Videos", scanning: false);
        Assert.Equal(new LibraryEmptyState("Nothing here matches these filters: Unwatched · Videos.", false, true), state);
        Assert.True(LibraryEmptyState.For(LibraryEmptyView.ContinueWatching, false, true, "Finished", false).OffersClearFilters);
        Assert.Equal(new LibraryEmptyState("Nothing in the library matches that with these filters: Music.", false, true),
            LibraryEmptyState.For(LibraryEmptyView.Media, searching: true, hasFolders: true, filters: "Music", scanning: false));
    }

    [Fact]
    [Capability("LIB-05")]
    public void EachViewSaysWhatWouldAppearInIt()
    {
        Assert.Equal(new LibraryEmptyState("Nothing in the library matches that.", false, false), LibraryEmptyState.For(LibraryEmptyView.Media, true, true, null, true));
        Assert.Equal("Videos you stop part-way through wait here.", LibraryEmptyState.For(LibraryEmptyView.ContinueWatching, false, true, null, false).Text);
        Assert.Equal("What you play from the library shows here.", LibraryEmptyState.For(LibraryEmptyView.RecentlyPlayed, false, true, "Music", false).Text);
        Assert.Equal(new LibraryEmptyState("Playlists you keep (Media, Playlists) show here.", false, false), LibraryEmptyState.For(LibraryEmptyView.Playlists, false, false, null, false));
        Assert.Equal(new LibraryEmptyState("There is nothing of this kind in the library's folders.", false, false), LibraryEmptyState.For(LibraryEmptyView.Media, false, true, null, false));
        Assert.True(LibraryEmptyState.For(LibraryEmptyView.Folders, false, true, null, false).OffersFolders);
    }

    [Fact]
    [Capability("LIB-05")]
    public void FailedLibraryScanNeverClaimsThatTheDriveIsEmpty()
    {
        const string problem = "A library folder could not be read. Reconnect its drive or check permissions, then rescan. Known history was kept.";
        Assert.Equal(problem, LibraryEmptyState.For(LibraryEmptyView.Media,
            searching: false, hasFolders: true, filters: null, scanning: false, scanProblem: problem).Text);
        Assert.Equal("Looking through your folders...", LibraryEmptyState.For(LibraryEmptyView.Media,
            searching: false, hasFolders: true, filters: null, scanning: true, scanProblem: problem).Text);
        Assert.Equal("Nothing here matches these filters: Music.", LibraryEmptyState.For(LibraryEmptyView.Media,
            searching: false, hasFolders: true, filters: "Music", scanning: false, scanProblem: problem).Text);
        Assert.Equal("The library shows the music, videos and pictures in the folders you choose, and keeps up as files come and go.",
            LibraryEmptyState.For(LibraryEmptyView.Media,
                searching: false, hasFolders: false, filters: null, scanning: false, scanProblem: problem).Text);
    }

    [Fact]
    [Capability("LIB-05")]
    public void ALibraryWithoutFoldersOffersSomeAndAScanSaysItIsLooking()
    {
        var welcome = LibraryEmptyState.For(LibraryEmptyView.Media, false, hasFolders: false, filters: "Music", scanning: true);
        Assert.Equal(new LibraryEmptyState("The library shows the music, videos and pictures in the folders you choose, and keeps up as files come and go.", true, false), welcome);
        Assert.True(LibraryEmptyState.For(LibraryEmptyView.Folders, false, false, null, false).OffersFolders);
        Assert.Equal("Looking through your folders...", LibraryEmptyState.For(LibraryEmptyView.Media, false, true, null, scanning: true).Text);
    }
}
