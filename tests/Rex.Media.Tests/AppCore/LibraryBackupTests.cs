using System.Text;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>Backing the library and its playlists up, and bringing them back (LIB-10).</summary>
public sealed class LibraryBackupTests
{
    private static readonly DateTime Monday = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly string Music = Path.Combine("D:" + Path.DirectorySeparatorChar, "Music");

    private static string In(string name) => Path.Combine(Music, name);

    private static MediaLibrary Library(params (string Name, int Plays)[] files)
    {
        var library = new MediaLibrary(RexStore.InMemory());
        library.AddFolder(Music);
        library.Scan(Music, files.Select(file => new LibraryFile(In(file.Name), 1, Monday)), _ => LibraryKind.Music);
        foreach (var (name, plays) in files)
        {
            for (var i = 0; i < plays; i++)
            {
                library.Played(In(name));
            }
        }

        return library;
    }

    private static ControllerHarness Harness() => new(autoPlay: false, settings: new PlayerSettings { TitleSeconds = 0 });

    [Fact]
    [Capability("LIB-10")]
    public void ALibraryAndItsPlaylistsComeBackFromTheirBackup()
    {
        var library = Library(("Sungba.mp3", 3), ("Terminator.mp3", 0));
        using var before = Harness();
        before.Controller.CreatePlaylist("Asake", [new PlaylistItem(In("Sungba.mp3")) { Title = "Sungba", Artist = "Asake" }, new PlaylistItem(In("Terminator.mp3"))]);
        before.Controller.CreatePlaylist("Asake?", []);

        var files = LibraryBackup.Export(library, before.Controller.NamedPlaylists);

        Assert.Equal([LibraryBackup.FileName, Path.Combine("Playlists", "Asake.m3u8"), Path.Combine("Playlists", "Asake_.m3u8")], files.Select(file => file.Path));
        var m3u8 = Encoding.UTF8.GetString(files[1].Bytes);
        Assert.Contains(In("Sungba.mp3"), m3u8, StringComparison.Ordinal);
        Assert.Contains("Asake - Sungba", m3u8, StringComparison.Ordinal);

        // Into an empty player: everything comes back.
        var restored = new MediaLibrary(RexStore.InMemory());
        using var after = Harness();
        var summary = LibraryBackup.Restore(LibraryBackup.Read(files[0].Bytes), restored, after.Controller);

        Assert.Equal(new BackupSummary(1, 2, 2), summary);
        Assert.Equal([Music], restored.Folders);
        Assert.Equal(3, restored.Entry(In("Sungba.mp3"))!.Plays);
        Assert.Equal(["Asake", "Asake?"], after.Controller.NamedPlaylists.Select(playlist => playlist.Name));
        Assert.Equal(("Sungba", "Asake"), (after.Controller.NamedPlaylists[0].Items[0].Title, after.Controller.NamedPlaylists[0].Items[0].Artist));

        // Again: nothing is made twice.
        Assert.Equal(new BackupSummary(0, 0, 0), LibraryBackup.Restore(LibraryBackup.Read(files[0].Bytes), restored, after.Controller));
        Assert.Equal(2, after.Controller.NamedPlaylists.Count);
    }

    [Fact]
    [Capability("LIB-10")]
    public void ARestoreAddsToWhatIsThereAndKeepsTheMostPlayed()
    {
        var backup = LibraryBackup.Read(LibraryBackup.Export(Library(("Sungba.mp3", 5), ("Joha.mp3", 1)), [new NamedPlaylist("x", "Asake", [new QueuedItem(In("Joha.mp3"), null, null, TimeSpan.Zero, null)])])[0].Bytes);
        var library = Library(("Sungba.mp3", 2), ("Joha.mp3", 4), ("Lonely At The Top.mp3", 0));
        using var harness = Harness();
        harness.Controller.CreatePlaylist("Asake", [new PlaylistItem(In("Sungba.mp3"))]);

        var summary = LibraryBackup.Restore(backup, library, harness.Controller);

        // Both files were known; Sungba takes the backup's count, Joha keeps its own; the playlist differs, so it joins.
        Assert.Equal(new BackupSummary(0, 0, 1), summary);
        Assert.Equal((5, 4, 3), (library.Entry(In("Sungba.mp3"))!.Plays, library.Entry(In("Joha.mp3"))!.Plays, library.Entries.Count));
        Assert.Equal(["Asake", "Asake (2)"], harness.Controller.NamedPlaylists.Select(playlist => playlist.Name));
        Assert.Equal(PlaylistItem.TitleOf(In("Joha.mp3")), harness.Controller.NamedPlaylists[1].Items[0].Title);
        Assert.Equal(0, library.Restore([]));
    }

    [Fact]
    public void WhatIsNoBackupIsRefusedAndNamesAreMadeSafe()
    {
        Assert.Throws<FormatException>(() => LibraryBackup.Read("not json"u8.ToArray()));
        Assert.Throws<FormatException>(() => LibraryBackup.Read("null"u8.ToArray()));
        Assert.Throws<FormatException>(() => LibraryBackup.Read("""{ "version": 0 }"""u8.ToArray()));
        var newer = Assert.Throws<FormatException>(() => LibraryBackup.Read("""{ "version": 99 }"""u8.ToArray()));
        Assert.Contains("newer rexplayer", newer.Message, StringComparison.Ordinal);

        // A backup whose lists are missing reads as empty ones; blank entries are passed over.
        var sparse = LibraryBackup.Read("""{ "version": 1, "playlists": [ { "id": "a", "name": " " }, { "id": "b", "name": "Empty" } ], "folders": [ " " ] }"""u8.ToArray());
        Assert.Empty(sparse.Entries);
        using var harness = Harness();
        Assert.Equal(new BackupSummary(0, 0, 1), LibraryBackup.Restore(sparse, new MediaLibrary(RexStore.InMemory()), harness.Controller));

        Assert.Equal("Mix_ 2", LibraryBackup.SafeName("Mix: 2"));

        // Two names that make the same file name each keep a file of their own.
        var twins = LibraryBackup.Export(new MediaLibrary(RexStore.InMemory()), [new NamedPlaylist("a", "Mix?", []), new NamedPlaylist("b", "Mix:", [])]);
        Assert.Equal([Path.Combine("Playlists", "Mix_.m3u8"), Path.Combine("Playlists", "Mix_ (2).m3u8")], twins.Skip(1).Select(file => file.Path));
        Assert.Equal("Playlist", LibraryBackup.SafeName(" ... "));
        Assert.Equal("a_b", LibraryBackup.SafeName("a\tb"));
        Assert.Throws<ArgumentNullException>(() => LibraryBackup.SafeName(null!));
        Assert.Throws<ArgumentNullException>(() => LibraryBackup.Read(null!));
        Assert.Throws<ArgumentNullException>(() => LibraryBackup.Export(null!, []));
        Assert.Throws<ArgumentNullException>(() => LibraryBackup.Export(new MediaLibrary(RexStore.InMemory()), null!));
        Assert.Throws<ArgumentNullException>(() => LibraryBackup.Restore(null!, new MediaLibrary(RexStore.InMemory()), harness.Controller));
        Assert.Throws<ArgumentNullException>(() => LibraryBackup.Restore(sparse, null!, harness.Controller));
        Assert.Throws<ArgumentNullException>(() => LibraryBackup.Restore(sparse, new MediaLibrary(RexStore.InMemory()), null!));
        Assert.Throws<ArgumentNullException>(() => new MediaLibrary(RexStore.InMemory()).Restore(null!));
    }
}
