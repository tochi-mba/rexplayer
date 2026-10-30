using System.Net;
using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class ModelSetTests
{
    private static readonly byte[] First = TestModels.Bytes(700, 5);
    private static readonly byte[] Second = TestModels.Bytes(900, 6);
    private static readonly VisionModel One = TestModels.Model("one", TestModels.File("one.onnx", First));
    private static readonly VisionModel Two = TestModels.Model("two", TestModels.File("two.onnx", Second));

    private static Dictionary<Uri, byte[]> Files => new() { [One.Files[0].Url] = First, [Two.Files[0].Url] = Second };

    private static ModelDownloader Downloader(HttpMessageHandler server, ModelStore store) =>
        new(new HttpClient(server), store, _ => long.MaxValue / 2, new RetryPolicy(1, TimeSpan.Zero, TimeSpan.Zero), (_, _) => Task.CompletedTask);

    [Fact]
    public async Task TheSetDownloadsWhatIsMissingAndBecomesReady()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        TestModels.Install(store, One, new Dictionary<string, byte[]> { ["one.onnx"] = First });
        var set = new ModelSet(store, [One, Two]);
        var changes = 0;
        set.Changed += () => changes++;
        Assert.Equal([InstallPhase.Installed, InstallPhase.NotInstalled], set.Statuses.Select(status => status.Phase));
        Assert.False(set.Ready);
        var server = FakeServer.Serving(Files);

        await set.DownloadAsync(Downloader(server, store), TestContext.Current.CancellationToken);

        Assert.True(set.Ready);
        Assert.Equal(Two.Files[0].Url, Assert.Single(server.Requests).RequestUri);
        Assert.True(changes > 0);
        Assert.False(set.View.CanDownload);
        Assert.Same(set.Models[1], Two);
    }

    [Fact]
    public async Task TheFirstFailureStopsTheRest()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        var set = new ModelSet(store, [One, Two]);
        var server = new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await set.DownloadAsync(Downloader(server, store), TestContext.Current.CancellationToken);

        Assert.Equal([InstallPhase.Failed, InstallPhase.NotInstalled], set.Statuses.Select(status => status.Phase));
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task RemovalWaitsForTheDownloadAndThenDeletesEverything()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        var set = new ModelSet(store, [One, Two]);
        var reached = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var server = new FakeServer(request =>
        {
            reached.TrySetResult();
            release.Task.Wait(TestContext.Current.CancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Files[request.RequestUri!]) };
        });

        var download = Task.Run(() => set.DownloadAsync(Downloader(server, store), TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        await reached.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(set.Remove);
        release.SetResult();
        await download;
        Assert.True(set.Ready);

        set.Remove();

        Assert.Equal([InstallPhase.NotInstalled, InstallPhase.NotInstalled], set.Statuses.Select(status => status.Phase));
        Assert.False(Directory.Exists(store.Folder(One)));
    }

    [Fact]
    public async Task ArgumentsAreChecked()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);

        Assert.Throws<ArgumentNullException>(() => new ModelSet(null!, [One]));
        Assert.Throws<ArgumentNullException>(() => new ModelSet(store, (IReadOnlyList<VisionModel>)null!));
        Assert.Throws<ArgumentException>(() => new ModelSet(store, []));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new ModelSet(store, [One]).DownloadAsync(null!, TestContext.Current.CancellationToken));
    }
}
