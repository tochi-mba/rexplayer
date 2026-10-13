using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>What opening the library does to playback, and the words of the library's menus and confirmations (LIB-05).</summary>
public sealed class LibraryActionsTests
{
    [Theory]
    [InlineData(true, true, true, true, LibraryOpenAction.Pause)]
    [InlineData(true, true, false, true, LibraryOpenAction.Continue)]
    [InlineData(true, false, false, true, LibraryOpenAction.Pause)]
    [InlineData(true, false, true, true, LibraryOpenAction.Pause)]
    [InlineData(false, true, true, true, LibraryOpenAction.Continue)]
    [InlineData(false, false, true, true, LibraryOpenAction.Continue)]
    [InlineData(true, true, true, false, LibraryOpenAction.Continue)]
    [InlineData(true, false, false, false, LibraryOpenAction.Continue)]
    [Capability("LIB-05")]
    public void OpeningTheLibraryPausesAVideoAndLetsASongPlayOnAsTheSettingsSay(bool pause, bool keepAudio, bool hasVideo, bool playing, LibraryOpenAction expected)
    {
        Assert.Equal(expected, LibraryOpening.Decide(pause, keepAudio, hasVideo, playing));
    }

    [Fact]
    public void TheSettingsPauseVideoAndKeepMusicPlayingByDefault()
    {
        var settings = new PlayerSettings();
        Assert.True(settings.PauseWhenLibraryOpens);
        Assert.True(settings.KeepAudioPlayingInLibrary);
        var changed = (settings with { PauseWhenLibraryOpens = false, KeepAudioPlayingInLibrary = false }).Normalize();
        Assert.Equal((false, false), (changed.PauseWhenLibraryOpens, changed.KeepAudioPlayingInLibrary));
    }

    [Fact]
    [Capability("LIB-05")]
    public void MenuWordsCountTheItemsChosen()
    {
        Assert.Equal("1 item", LibraryActions.Count(1, "item"));
        Assert.Equal("3 files", LibraryActions.Count(3, "file"));
        Assert.Equal("Play", LibraryActions.MenuText("Play", 1));
        Assert.Equal("Play", LibraryActions.MenuText("Play", 0));
        Assert.Equal("Play 3 items", LibraryActions.MenuText("Play", 3));
        Assert.Equal("Play next", LibraryActions.MenuText("Play", 1, tail: "next"));
        Assert.Equal("Play 2 episodes next", LibraryActions.MenuText("Play", 2, "episode", "next"));
        Assert.Throws<ArgumentNullException>(() => LibraryActions.Count(1, null!));
        Assert.Throws<ArgumentNullException>(() => LibraryActions.MenuText(null!, 1));
    }

    [Fact]
    [Capability("LIB-05")]
    public void ConfirmationsNameTheItemsAndSayWhatHappensToTheFiles()
    {
        var one = LibraryActions.DeletePrompt(["Sungba.mp3"]);
        Assert.Equal("Delete Sungba.mp3?", one.Title);
        Assert.Equal("It goes to the Recycle Bin, where it can be brought back, and the library forgets it.", one.Body);

        var many = LibraryActions.DeletePrompt(["a", "b", "c", "d", "e", "f", "g"]);
        Assert.Equal("Delete 7 files?", many.Title);
        Assert.Equal("a\nb\nc\nd\ne\nand 2 more\n\nThey go to the Recycle Bin, where they can be brought back, and the library forgets them.", many.Body);
        Assert.Equal("a\nb\n\nThey go to the Recycle Bin, where they can be brought back, and the library forgets them.", LibraryActions.DeletePrompt(["a", "b"]).Body);

        Assert.Equal("Remove Sungba.mp3 from the library?", LibraryActions.RemovePrompt(["Sungba.mp3"]).Title);
        Assert.Equal("The file stays where it is; the library stops showing it until its folder is taken out of the library and added again.", LibraryActions.RemovePrompt(["Sungba.mp3"]).Body);
        Assert.Equal("Remove 2 items from the library?", LibraryActions.RemovePrompt(["a", "b"]).Title);
        Assert.Equal("a\nb\n\nThe files stay where they are; the library stops showing them until their folders are taken out of the library and added again.", LibraryActions.RemovePrompt(["a", "b"]).Body);

        Assert.Equal(new Prompt("Delete Mix?", "The playlist goes; the files in it stay where they are."), LibraryActions.DeletePlaylistPrompt(["Mix"]));
        Assert.Equal(new Prompt("Delete 2 playlists?", "Mix\nRoad\n\nThe playlists go; the files in them stay where they are."), LibraryActions.DeletePlaylistPrompt(["Mix", "Road"]));

        Assert.Throws<ArgumentNullException>(() => LibraryActions.DeletePrompt(null!));
        Assert.Throws<ArgumentNullException>(() => LibraryActions.RemovePrompt(null!));
        Assert.Throws<ArgumentNullException>(() => LibraryActions.DeletePlaylistPrompt(null!));
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheDeleteKeyDeletesOnlyTheFilesALinesMenuWouldDelete()
    {
        // Delete on a watched folder's line used to offer to delete every file under the folder,
        // where its menu only stops watching it.
        var song = new LibraryEntry { Path = Path.Combine(Path.GetTempPath(), "Sungba.mp3"), Kind = LibraryKind.Music, Title = "Sungba" };
        Assert.True(LibraryActions.DeletesFiles(song));
        Assert.True(LibraryActions.DeletesFiles(new LibraryGroup("Work Of Art", null, [song])));
        Assert.False(LibraryActions.DeletesFiles(Path.GetTempPath()));
        Assert.False(LibraryActions.DeletesFiles(new NamedPlaylist("1", "Mix", [])));
        Assert.False(LibraryActions.DeletesFiles(null));
    }
}
