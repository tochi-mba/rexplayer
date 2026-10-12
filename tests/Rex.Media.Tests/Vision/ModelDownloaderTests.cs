using System.Net;
using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class ModelDownloaderTests
{
    private static readonly byte[] First = TestModels.Bytes(5000, 3);
    private static readonly byte[] Second = TestModels.Bytes(3000, 4);
    private static readonly VisionModel Model = TestModels.Model("pair", TestModels.File("a.onnx", First), TestModels.File("b.onnx", Second));
    private static readonly RetryPolicy Quick = new(3, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1));

    private static Dictionary<Uri, byte[]> Files => new() { [Model.Files[0].Url] = First, [Model.Files[1].Url] = Second };

    private static ModelDownloader Downloader(HttpMessageHandler server, ModelStore store, long free = long.MaxValue / 2) =>
        new(new HttpClient(server), store, _ => free, Quick, (_, _) => Task.CompletedTask);

    [Fact]
    public async Task AModelIsFetchedVerifiedAndMovedIntoPlace()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        var machine = new InstallStateMachine();
        var phases = new List<InstallPhase>();
        machine.Changed += status => phases.Add(status.Phase);
        using var http = new HttpClient(FakeServer.Serving(Files));

        await new ModelDownloader(http, store, _ => long.MaxValue / 2).InstallAsync(Model, machine, TestContext.Current.CancellationToken);

        Assert.Equal(InstallPhase.Installed, machine.Status.Phase);
        Assert.Equal(InstallPhase.Installed, store.Inspect(Model));
        Assert.Equal(First, File.ReadAllBytes(store.PathOf(Model, Model.Files[0])));
        Assert.False(File.Exists(store.PartialPathOf(Model, Model.Files[0])));
        Assert.Equal([InstallPhase.Downloading, InstallPhase.Verifying, InstallPhase.Installed], phases.Distinct());
    }

    [Fact]
    public async Task ArgumentsAreChecked()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        using var http = new HttpClient();
        var downloader = new ModelDownloader(http, store, _ => 0);

        Assert.Throws<ArgumentNullException>(() => new ModelDownloader(null!, store, _ => 0));
        Assert.Throws<ArgumentNullException>(() => new ModelDownloader(http, null!, _ => 0));
        Assert.Throws<ArgumentNullException>(() => new ModelDownloader(http, store, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => downloader.InstallAsync(null!, new InstallStateMachine(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => downloader.InstallAsync(Model, null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => downloader.InstallAsync(TestModels.Model("copyleft", "AGPL-3.0", Model.Files[0]), new InstallStateMachine(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NothingIsFetchedWithoutRoomOnTheDisk()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        var server = FakeServer.Serving(Files);
        var machine = new InstallStateMachine();

        await Downloader(server, store, free: -1).InstallAsync(Model, machine, TestContext.Current.CancellationToken);

        Assert.Equal(InstallPhase.Failed, machine.Status.Phase);
        Assert.Contains("not enough free space", machine.Status.Reason, StringComparison.Ordinal);
        Assert.Contains("0 KB is free", machine.Status.Reason, StringComparison.Ordinal);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task APartialFileIsResumedWithAByteRange()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        Directory.CreateDirectory(store.Folder(Model));
        await File.WriteAllBytesAsync(store.PartialPathOf(Model, Model.Files[0]), First[..1234], TestContext.Current.CancellationToken);
        var server = FakeServer.Serving(Files);
        var machine = new InstallStateMachine();

        await Downloader(server, store).InstallAsync(Model, machine, TestContext.Current.CancellationToken);

        Assert.Equal(InstallPhase.Installed, machine.Status.Phase);
        Assert.Equal(1234, server.Requests[0].Headers.Range!.Ranges.Single().From);
        Assert.Null(server.Requests[1].Headers.Range);
    }

    [Fact]
    public async Task AServerThatIgnoresTheRangeStartsTheFileAgain()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        Directory.CreateDirectory(store.Folder(Model));
        await File.WriteAllBytesAsync(store.PartialPathOf(Model, Model.Files[0]), new byte[100], TestContext.Current.CancellationToken);
        var server = new FakeServer(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Files[request.RequestUri!]) });
        var machine = new InstallStateMachine();

        await Downloader(server, store).InstallAsync(Model, machine, TestContext.Current.CancellationToken);

        Assert.Equal(InstallPhase.Installed, machine.Status.Phase);
    }

    [Fact]
    public async Task AnUnsatisfiableRangeDeletesThePartialFileAndTriesAgain()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        Directory.CreateDirectory(store.Folder(Model));
        await File.WriteAllBytesAsync(store.PartialPathOf(Model, Model.Files[0]), new byte[100], TestContext.Current.CancellationToken);
        var server = new FakeServer(request => request.Headers.Range is not null
            ? new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Files[request.RequestUri!]) });
        var machine = new InstallStateMachine();

        await Downloader(server, store).InstallAsync(Model, machine, TestContext.Current.CancellationToken);

        Assert.Equal(InstallPhase.Installed, machine.Status.Phase);
        Assert.Equal(3, server.Requests.Count);
    }

    [Fact]
    public async Task ATooLongPartialFileIsDiscardedAndAFinishedOneIsKept()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        Directory.CreateDirectory(store.Folder(Model));
        await File.WriteAllBytesAsync(store.PartialPathOf(Model, Model.Files[0]), new byte[First.Length + 10], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(store.PartialPathOf(Model, Model.Files[1]), Second, TestContext.Current.CancellationToken);
        var server = FakeServer.Serving(Files);
        var machine = new InstallStateMachine();

        await Downloader(server, store).InstallAsync(Model, machine, TestContext.Current.CancellationToken);

        Assert.Equal(InstallPhase.Installed, machine.Status.Phase);
        Assert.Equal(Model.Files[0].Url, Assert.Single(server.Requests).RequestUri);
    }

    [Fact]
    public async Task AFileAlreadyInPlaceIsNotFetchedAgain()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        Directory.CreateDirectory(store.Folder(Model));
        await File.WriteAllBytesAsync(store.PathOf(Model, Model.Files[1]), Second, TestContext.Current.CancellationToken);
        var server = FakeServer.Serving(Files);
        var machine = new InstallStateMachine();

        await Downloader(server, store).InstallAsync(Model, machine, TestContext.Current.CancellationToken);

        Assert.Equal(InstallPhase.Installed, machine.Status.Phase);
        Assert.Equal(Model.Files[0].Url, Assert.Single(server.Requests).RequestUri);
    }

    [Fact]
    public async Task ChangedBytesAreRefusedAndDeleted()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        var tampered = Files;
        tampered[Model.Files[1].Url] = new byte[Second.Length];
        var machine = new InstallStateMachine();

        await Downloader(FakeServer.Serving(tampered), store).InstallAsync(Model, machine, TestContext.Current.CancellationToken);

        Assert.Equal(InstallPhase.Failed, machine.Status.Phase);
        Assert.Contains("b.onnx did not match its published fingerprint", machine.Status.Reason, StringComparison.Ordinal);
        Assert.False(File.Exists(store.PartialPathOf(Model, Model.Files[1])));
        Assert.Equal(InstallPhase.NotInstalled, store.Inspect(Model));
    }

    [Fact]
    public async Task MoreBytesThanTheModelHasStopTheDownload()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        var server = new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[First.Length + 1]) });
        var machine = new InstallStateMachine();

        await Downloader(server, store).InstallAsync(Model, machine, TestContext.Current.CancellationToken);

        Assert.Equal("The server sent more data than the model should have. The download was stopped.", machine.Status.Reason);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task ADownloadThatEndsEarlyIsResumedUntilTheAttemptsRunOut()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        var server = new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[10]) });
        var machine = new InstallStateMachine();

        await Downloader(server, store).InstallAsync(Model, machine, TestContext.Current.CancellationToken);

        Assert.Equal(InstallPhase.Failed, machine.Status.Phase);
        Assert.Equal("The download stopped: The download of a.onnx ended early.", machine.Status.Reason);
        Assert.Equal(3, server.Requests.Count);
    }

    [Fact]
    public async Task AMissingFileIsNotRetried()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        var server = FakeServer.Serving(new Dictionary<Uri, byte[]>());
        var machine = new InstallStateMachine();

        await Downloader(server, store).InstallAsync(Model, machine, TestContext.Current.CancellationToken);

        Assert.Equal("The server answered 404 (NotFound). Try again later.", machine.Status.Reason);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task ADroppedConnectionIsRetriedThenReported()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        var server = new FakeServer(_ => throw new HttpRequestException("reset"));
        var machine = new InstallStateMachine();

        await Downloader(server, store).InstallAsync(Model, machine, TestContext.Current.CancellationToken);

        Assert.Equal("The connection failed. Check the network and try again.", machine.Status.Reason);
        Assert.Equal(3, server.Requests.Count);
    }

    [Fact]
    public async Task CancellingKeepsThePartialFileAndRestoresThePhase()
    {
        using var folder = new TempFolder();
        var store = new ModelStore(folder.Path);
        using var cancel = new CancellationTokenSource();
        var server = new FakeServer(request =>
        {
            cancel.Cancel();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Files[request.RequestUri!]) };
        });
        var machine = new InstallStateMachine(InstallPhase.UpdateAvailable);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Downloader(server, store).InstallAsync(Model, machine, cancel.Token));

        Assert.Equal(InstallPhase.UpdateAvailable, machine.Status.Phase);
    }

    [Fact]
    public void FailuresAreDescribedInPlainWords()
    {
        Assert.Equal("The connection failed. Check the network and try again.", ModelDownloader.Describe(new TaskCanceledException()));
        Assert.Equal("rexplayer may not write to its models folder.", ModelDownloader.Describe(new UnauthorizedAccessException()));
        Assert.Equal("The download stopped: disk full", ModelDownloader.Describe(new IOException("disk full")));
    }
}
