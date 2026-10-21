using Rex.Media.Containers.Tags;
using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.Tests.Library;

/// <summary>
/// What rexplayer keeps on disk never stops it starting or playing: a full disk or a log another
/// window holds, a record that reads but lacks what it must hold, a settings file edited by hand,
/// and a song whose folder vanished while its details were written.
/// </summary>
public sealed class StoreRecoveryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("rexplayer-recovery-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void AStoreThatCannotBeWrittenKeepsItsChangesAndWritesThemAllOnceItCan()
    {
        var path = Path.Combine(_folder, "store.log");
        var store = RexStore.Open(path);
        store.Set("kept", "before");
        File.Delete(path);
        Directory.CreateDirectory(path);
        try
        {
            store.Set("resume/Lonely At The Top", "42");
            Assert.True(store.Remove("kept"));
            Assert.NotNull(store.WriteProblem);
            Assert.Equal("42", store.Get("resume/Lonely At The Top"));
            Assert.Null(store.Get("kept"));
        }
        finally
        {
            Directory.Delete(path);
        }

        store.Set("resume/Sungba", "7");

        Assert.Null(store.WriteProblem);
        var reopened = RexStore.Open(path);
        Assert.Equal("42", reopened.Get("resume/Lonely At The Top"));
        Assert.Equal("7", reopened.Get("resume/Sungba"));
        Assert.Null(reopened.Get("kept"));
        Assert.Equal(0, reopened.Skipped);
    }

    [Fact]
    public void ACompactionThatCannotBeWrittenIsTriedAgainWithTheNextChange()
    {
        var path = Path.Combine(_folder, "store.log");
        var store = RexStore.Open(path);
        store.Set("prefix/a", "1");
        store.Set("other", "2");
        File.Delete(path);
        Directory.CreateDirectory(path);
        try
        {
            Assert.Equal(1, store.RemovePrefix("prefix/"));
            Assert.NotNull(store.WriteProblem);
        }
        finally
        {
            Directory.Delete(path);
        }

        store.Set("other", "3");

        Assert.Null(store.WriteProblem);
        var reopened = RexStore.Open(path);
        Assert.Null(reopened.Get("prefix/a"));
        Assert.Equal("3", reopened.Get("other"));
    }

    [Fact]
    public void LibraryRecordsWithoutAPathOrTitleArePassedOverInsteadOfStoppingTheStart()
    {
        var store = RexStore.InMemory();
        store.Set("folders", """["D:/Music",null,""]""");
        store.Set("hidden", """[null]""");
        store.Set("entry/a", """{"Path":null,"Kind":"Music","Title":"Terminator"}""");
        store.Set("entry/b", """{"Path":"D:/Music/b.mp3","Kind":"Music","Title":null}""");
        store.Set("entry/c", """{"Path":"D:/Music/Sungba.mp3","Kind":"Music","Title":"Sungba"}""");

        var library = new MediaLibrary(store);

        Assert.Equal(["D:/Music"], library.Folders);
        Assert.Equal("Sungba", Assert.Single(library.Entries).Title);
    }

    [Fact]
    public void ASettingsFileEditedByHandWithASettingTwiceStillOpens()
    {
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, """{ "volume": 0.4, "theme": "Dark", "volume": 0.6 }""");

        var settings = SettingsStore.Load(path);

        Assert.Equal(ThemeChoice.Dark, settings.Theme);
        Assert.Equal(0.6, settings.Volume);
    }

    [Fact]
    public void ASongWhoseFolderVanishedWhileItsDetailsWereWrittenIsRefusedNotThrown()
    {
        var gone = Path.Combine(_folder, "unplugged", "Lonely At The Top.mp3");

        var result = TagFileWriter.Write(gone, new TagEdit { Title = "Lonely At The Top", Artist = "Asake" }, TestContext.Current.CancellationToken);

        Assert.Equal(TagWriteStatus.Busy, result.Status);
    }
}
