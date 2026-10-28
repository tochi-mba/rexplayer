using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// What the library files each file of a folder as (LIB-05, META-06): an album's cover or a show's
/// poster is its music's or video's art, while a picture of the same name among photos is a picture.
/// </summary>
public sealed class LibraryKindsTests
{
    private static string At(params string[] parts) => Path.Combine(["media", .. parts]);

    [Fact]
    [Capability("LIB-05")]
    [Capability("META-06")]
    public void AnAlbumsCoverBesideItsSongsIsItsArtNotAPicture()
    {
        var song = At("Lungu Boy", "01 Sungba.mp3");
        var cover = At("Lungu Boy", "cover.jpg");
        var folder = At("Lungu Boy", "Folder.PNG");
        var windows = At("Lungu Boy", "AlbumArtSmall.jpg");
        var booklet = At("Lungu Boy", "booklet page 2.jpg");
        var notes = At("Lungu Boy", "notes.txt");
        var scan = At("Lungu Boy", "Scans", "front.jpg");
        var kinds = LibraryKinds.Of([song, cover, folder, windows, booklet, notes, scan]);

        Assert.Equal(LibraryKind.Music, kinds.KindOf(song));
        Assert.Null(kinds.KindOf(cover));
        Assert.Null(kinds.KindOf(folder));
        Assert.Null(kinds.KindOf(windows));
        Assert.True(kinds.IsArtwork(cover));

        // A picture not named as art is the album's own picture; a text file is nothing.
        Assert.Equal(LibraryKind.Picture, kinds.KindOf(booklet));
        Assert.False(kinds.IsArtwork(booklet));
        Assert.Null(kinds.KindOf(notes));

        // Only the folder the songs are in: a folder of scans inside it holds no music.
        Assert.Equal(LibraryKind.Picture, kinds.KindOf(scan));
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheSameNamesAmongPhotosAloneArePictures()
    {
        var cover = At("Photos", "cover.jpg");
        var folder = At("Photos", "folder.jpg");
        var poster = At("Photos", "poster.png");
        var beach = At("Photos", "beach.jpg");
        var kinds = LibraryKinds.Of([cover, folder, poster, beach]);

        Assert.All(new[] { cover, folder, poster, beach }, picture => Assert.Equal(LibraryKind.Picture, kinds.KindOf(picture)));
        Assert.False(kinds.IsArtwork(cover));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AFilmsAndAShowsArtBelongToThemEvenAboveTheSeasonFolders()
    {
        var film = At("Films", "Small Film (2023)", "Small.Film.2023.mkv");
        var filmPoster = At("Films", "Small Film (2023)", "poster.jpg");
        var stills = At("Films", "Small Film (2023)", "fanart.jpg");
        var showPoster = At("Harbour Lights", "poster.jpg");
        var seasonPoster = At("Harbour Lights", "season01-poster.jpg");
        var pilot = At("Harbour Lights", "Season 1", "Harbour.Lights.S01E01.mkv");
        var second = At("Harbour Lights", "Season 1", "Harbour.Lights.S01E02.mkv");
        var seasonFolder = At("Harbour Lights", "Season 1", "folder.jpg");
        var episodeStill = At("Harbour Lights", "Season 1", "Harbour.Lights.S01E01.jpg");
        var extras = At("Harbour Lights", "Extras", "cover.jpg");
        var kinds = LibraryKinds.Of([film, filmPoster, stills, showPoster, seasonPoster, pilot, second, seasonFolder, episodeStill, extras]);

        Assert.Equal(LibraryKind.Video, kinds.KindOf(film));
        Assert.Equal(LibraryKind.Video, kinds.KindOf(second));
        Assert.All(new[] { filmPoster, showPoster, seasonPoster, seasonFolder }, art => Assert.Null(kinds.KindOf(art)));

        // Names rexplayer never shows as anything's art stay pictures, as does art beside no media.
        Assert.All(new[] { stills, episodeStill, extras }, picture => Assert.Equal(LibraryKind.Picture, kinds.KindOf(picture)));
    }

    [Fact]
    [Capability("LIB-05")]
    public void ALibrarysFolderAboveASeasonIsNoShowsFolder()
    {
        // A library's folder is no show's ("TV Shows" above a season folder), yet art beside a film is still art.
        var episode = At("TV Shows", "Season 1", "Pilot.mkv");
        var shelfPoster = At("TV Shows", "poster.jpg");
        var film = At("Movies", "Film.mp4");
        var moviesPoster = At("Movies", "poster.jpg");
        var kinds = LibraryKinds.Of([episode, shelfPoster, film, moviesPoster]);

        Assert.Equal(LibraryKind.Picture, kinds.KindOf(shelfPoster));
        Assert.Null(kinds.KindOf(moviesPoster));
    }

    [Fact]
    [Capability("LIB-05")]
    public void APhotoNamedAfterAClipBesideItIsStillAPhoto()
    {
        // A camera's still and clip pair keep both; only a folder-art name is art.
        var still = At("Camera", "IMG_0001.JPG");
        var clip = At("Camera", "IMG_0001.MOV");
        var thumbnail = At("Camera", "folder.jpg");
        var kinds = LibraryKinds.Of([still, clip, thumbnail]);

        Assert.Equal(LibraryKind.Picture, kinds.KindOf(still));
        Assert.Equal(LibraryKind.Video, kinds.KindOf(clip));
        Assert.Null(kinds.KindOf(thumbnail));
    }

    [Fact]
    [Capability("LIB-05")]
    public void AnAlbumStillDownloadingOwnsItsCover()
    {
        var partial = At("Lungu Boy", "01 Sungba.mp3.part");
        var cover = At("Lungu Boy", "cover.jpg");
        var kinds = LibraryKinds.Of([partial, cover]);

        Assert.Equal(LibraryKind.Music, kinds.KindOf(partial));
        Assert.Null(kinds.KindOf(cover));
    }

    [Fact]
    public void NothingIsAskedOfNothing()
    {
        var kinds = LibraryKinds.Of([]);

        Assert.Throws<ArgumentNullException>(() => LibraryKinds.Of(null!));
        Assert.Throws<ArgumentNullException>(() => kinds.KindOf(null!));
        Assert.Throws<ArgumentNullException>(() => kinds.IsArtwork(null!));
    }
}
