using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class ModelStoreTests
{
    private static readonly byte[] First = TestModels.Bytes(300, 1);
    private static readonly byte[] Second = TestModels.Bytes(200, 2);
    private static readonly VisionModel Model = TestModels.Model("pair", TestModels.File("a.onnx", First), TestModels.File("b.onnx", Second));
    private static readonly Dictionary<string, byte[]> Contents = new() { ["a.onnx"] = First, ["b.onnx"] = Second };

    [Fact]
    public void TheStoreLivesUnderTheAppsDataFolder()
    {
        Assert.Equal(Path.Combine("C:", "Data", "REX", "rexplayer", "models"), ModelStore.DefaultRoot(Path.Combine("C:", "Data")));
        Assert.Throws<ArgumentException>(() => new ModelStore(" "));
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        Assert.Equal(Path.Combine(folder.Path, "pair", "a.onnx") + ".part", store.PartialPathOf(Model, Model.Files[0]));
    }

    [Fact]
    public void AModelIsInstalledOnlyWhenItsManifestAndFilesMatchTheCatalogue()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        Assert.Equal(InstallPhase.NotInstalled, store.Inspect(Model));

        TestModels.Install(store, Model, Contents);
        Assert.Equal(InstallPhase.Installed, store.Inspect(Model));
        Assert.True(store.HasVerified(Model, Model.Files[0]));
        Assert.Equal(0, store.Remaining(Model));

        File.WriteAllBytes(store.PathOf(Model, Model.Files[1]), [1, 2, 3]);
        Assert.Equal(InstallPhase.NotInstalled, store.Inspect(Model));
        Assert.False(store.HasVerified(Model, Model.Files[1]));
        Assert.Equal(Second.Length, store.Remaining(Model));
    }

    [Fact]
    public void AnOlderManifestIsAnUpdateWhileItsFilesRemain()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        var older = TestModels.Model("pair", TestModels.File("a.onnx", Second));
        TestModels.Install(store, older, new Dictionary<string, byte[]> { ["a.onnx"] = Second });

        Assert.Equal(InstallPhase.UpdateAvailable, store.Inspect(Model));

        File.Delete(store.PathOf(older, older.Files[0]));
        Assert.Equal(InstallPhase.NotInstalled, store.Inspect(Model));
    }

    [Fact]
    public void AFileWithTheRightLengthButOtherBytesIsNotVerified()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        TestModels.Install(store, Model, Contents);

        File.WriteAllBytes(store.PathOf(Model, Model.Files[0]), new byte[First.Length]);

        Assert.False(store.HasVerified(Model, Model.Files[0]));
    }

    [Fact]
    public void MalformedManifestLinesAreIgnored()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        Directory.CreateDirectory(store.Folder(Model));
        File.WriteAllLines(Path.Combine(store.Folder(Model), ModelStore.ManifestName), ["short a.onnx", "no-space-at-all", new string('a', 64) + " "]);

        Assert.Equal(InstallPhase.NotInstalled, store.Inspect(Model));
    }

    [Fact]
    public void PartialDownloadsCountTowardsWhatRemains()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        Directory.CreateDirectory(store.Folder(Model));
        File.WriteAllBytes(store.PartialPathOf(Model, Model.Files[0]), First[..100]);
        File.WriteAllBytes(store.PartialPathOf(Model, Model.Files[1]), new byte[Second.Length + 50]);

        Assert.Equal(100, store.PartialBytes(Model, Model.Files[0]));
        Assert.Equal(First.Length - 100, store.Remaining(Model));
    }

    [Fact]
    public void RemovingDeletesEverythingAndIsSafeTwice()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        TestModels.Install(store, Model, Contents);

        store.Remove(Model);
        store.Remove(Model);

        Assert.False(Directory.Exists(store.Folder(Model)));
        Assert.Equal(InstallPhase.NotInstalled, store.Inspect(Model));
    }

    [Fact]
    public void HashesAreLowerCaseSha256()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "abc");
        File.WriteAllText(path, "abc");

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", ModelStore.HashOf(path));
    }
}
