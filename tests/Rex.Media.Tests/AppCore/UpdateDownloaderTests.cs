using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Rex.Media.AppCore.Updates;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

public sealed class UpdateDownloaderTests : IDisposable
{
    private const string Name = "rexplayer-Setup-0.16.0.exe";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rexplayer-updates-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _installer = [.. Enumerable.Range(0, 200_000).Select(i => (byte)(i * 7))];

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string Checksum => Convert.ToHexString(SHA256.HashData(_installer)).ToLowerInvariant() + "  " + Name + "\n";

    private string Installer => Path.Combine(_folder, Name);

    private PendingUpdate Pending(long? size = null) => new()
    {
        Version = "0.16.0",
        InstallerName = Name,
        InstallerUrl = "https://example.com/setup.exe",
        ChecksumUrl = "https://example.com/setup.exe.sha256",
        Size = size ?? _installer.Length,
    };

    /// <summary>A release host: the checksum, and the installer whole or from a byte range.</summary>
    private sealed class FakeOrigin(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return answer(request);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }

    private FakeOrigin Origin(Func<HttpRequestMessage, HttpResponseMessage>? installer = null, string? checksum = null) => new(request =>
        request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(checksum ?? Checksum) }
            : (installer ?? Serve)(request));

    /// <summary>The installer as a host that honours range requests serves it.</summary>
    private HttpResponseMessage Serve(HttpRequestMessage request)
    {
        if (request.Headers.Range?.Ranges.Single().From is { } from)
        {
            var content = new ByteArrayContent(_installer[(int)from..]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(from, _installer.Length - 1, _installer.Length);
            return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_installer) };
    }

    private UpdateDownloader Downloader(FakeOrigin origin) => new(new HttpClient(origin), _folder) { FreeSpace = _ => long.MaxValue };

    [Fact]
    [Capability("TOOL-09")]
    public void ADownloadIsKeptOnlyWhenItMatchesItsChecksumAndReportsItsProgress()
    {
        var progress = new List<double>();
        var report = Downloader(Origin()).Download(Pending(), progress.Add, CancellationToken.None);

        Assert.Equal(new DownloadReport(DownloadOutcome.Ready), report);
        Assert.Equal(_installer, File.ReadAllBytes(Installer));
        Assert.Equal(Checksum, File.ReadAllText(Installer + ".sha256"));
        Assert.False(File.Exists(Installer + ".part"));
        Assert.Equal(1, progress[^1]);
        Assert.True(progress.Count > 2);
        Assert.True(UpdateDownloader.IsReady(_folder, Pending()));
        Assert.Throws<ArgumentNullException>(() => Downloader(Origin()).Download(null!, null, CancellationToken.None));
    }

    [Fact]
    public void AVerifiedInstallerAlreadyThereIsNotDownloadedAgain()
    {
        var origin = Origin();
        Assert.Equal(DownloadOutcome.Ready, Downloader(origin).Download(Pending(), null, CancellationToken.None).Outcome);
        origin.Requests.Clear();

        Assert.Equal(DownloadOutcome.Ready, Downloader(origin).Download(Pending(), null, CancellationToken.None).Outcome);
        Assert.Single(origin.Requests);

        // A damaged one is replaced.
        File.WriteAllBytes(Installer, [1, 2, 3]);
        Assert.False(UpdateDownloader.IsReady(_folder, Pending()));
        Assert.Equal(DownloadOutcome.Ready, Downloader(origin).Download(Pending(), null, CancellationToken.None).Outcome);
        Assert.Equal(_installer, File.ReadAllBytes(Installer));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void APartialDownloadCarriesOnFromWhereItStopped()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Installer + ".part", _installer[..50_000]);
        var origin = Origin();

        Assert.Equal(DownloadOutcome.Ready, Downloader(origin).Download(Pending(), null, CancellationToken.None).Outcome);
        Assert.Equal(50_000, origin.Requests[^1].Headers.Range!.Ranges.Single().From);
        Assert.Equal(_installer, File.ReadAllBytes(Installer));
    }

    [Fact]
    public void AHostThatIgnoresTheRangeSendsItAllAgain()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Installer + ".part", [9, 9, 9]);
        var origin = Origin(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_installer) });

        Assert.Equal(DownloadOutcome.Ready, Downloader(origin).Download(Pending(), null, CancellationToken.None).Outcome);
        Assert.Equal(_installer, File.ReadAllBytes(Installer));
    }

    [Fact]
    public void APartialFileLongerThanTheInstallerStartsAgain()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Installer + ".part", new byte[_installer.Length + 10]);
        var origin = Origin();

        Assert.Equal(DownloadOutcome.Ready, Downloader(origin).Download(Pending(), null, CancellationToken.None).Outcome);
        Assert.Null(origin.Requests[^1].Headers.Range);
    }

    [Fact]
    public void AWholePartialFileIsVerifiedWithoutAnotherRequest()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Installer + ".part", _installer);
        var origin = Origin();

        Assert.Equal(DownloadOutcome.Ready, Downloader(origin).Download(Pending(), null, CancellationToken.None).Outcome);
        Assert.Single(origin.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestedRangeNotSatisfiable)]
    [InlineData(HttpStatusCode.PartialContent)]
    public void ARangeTheHostCannotServeStartsOverNextTime(HttpStatusCode status)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Installer + ".part", _installer[..1000]);
        var origin = Origin(_ =>
        {
            var content = new ByteArrayContent(_installer[10..]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(10, _installer.Length - 1, _installer.Length);
            return new HttpResponseMessage(status) { Content = content };
        });

        var report = Downloader(origin).Download(Pending(), null, CancellationToken.None);

        Assert.Equal(new DownloadReport(DownloadOutcome.Failed, "the download could not carry on from where it stopped"), report);
        Assert.Equal(0, new FileInfo(Installer + ".part").Length);
    }

    [Fact]
    public void AServerErrorKeepsWhatWasDownloaded()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Installer + ".part", _installer[..1000]);
        var report = Downloader(Origin(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))).Download(Pending(), null, CancellationToken.None);

        Assert.Equal(new DownloadReport(DownloadOutcome.Failed, "the server answered 503"), report);
        Assert.Equal(1000, new FileInfo(Installer + ".part").Length);
    }

    [Fact]
    public void AShortDownloadIsIncompleteAndKeptForNextTime()
    {
        var origin = Origin(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_installer[..5000]) });
        Assert.Equal(new DownloadReport(DownloadOutcome.Failed, "the download was incomplete"), Downloader(origin).Download(Pending(), null, CancellationToken.None));
        Assert.Equal(5000, new FileInfo(Installer + ".part").Length);
    }

    [Fact]
    [Capability("SEC-05")]
    public void ADownloadThatDoesNotMatchItsChecksumIsDeleted()
    {
        var wrong = Convert.ToHexString(SHA256.HashData([1])) + "  " + Name;
        var report = Downloader(Origin(checksum: wrong)).Download(Pending(), null, CancellationToken.None);

        Assert.Equal(new DownloadReport(DownloadOutcome.Mismatch, "the download did not match its published checksum"), report);
        Assert.False(File.Exists(Installer + ".part"));
        Assert.False(File.Exists(Installer));
    }

    [Fact]
    public void AnUnstatedSizeIsTakenFromTheResponseAndAMissingOneStillWorks()
    {
        var progress = new List<double>();
        Assert.Equal(DownloadOutcome.Ready, Downloader(Origin()).Download(Pending(size: 0), progress.Add, CancellationToken.None).Outcome);
        Assert.Equal(1, progress[^1]);

        Dispose();
        var unsized = Origin(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(_installer)) });
        Assert.Equal(DownloadOutcome.Ready, Downloader(unsized).Download(Pending(size: 0), null, CancellationToken.None).Outcome);
    }

    [Fact]
    public void NotEnoughFreeSpaceStopsBeforeWriting()
    {
        var downloader = new UpdateDownloader(new HttpClient(Origin()), _folder) { FreeSpace = _ => UpdateDownloader.SpareBytes };
        Assert.Equal(new DownloadReport(DownloadOutcome.NoSpace, "there is not enough free disk space"), downloader.Download(Pending(), null, CancellationToken.None));
        Assert.Equal(0, new FileInfo(Installer + ".part").Length);
    }

    [Fact]
    public void TheRealFreeSpaceIsAskedOfTheDisk()
    {
        // The temporary folder's disk has room for 200 kB and the spare 64 MB.
        Assert.Equal(DownloadOutcome.Ready, new UpdateDownloader(new HttpClient(Origin()), _folder).Download(Pending(), null, CancellationToken.None).Outcome);
    }

    [Fact]
    [Capability("TOOL-09")]
    public void AnotherWindowDownloadingTheSameFileIsLeftToIt()
    {
        Directory.CreateDirectory(_folder);
        using var held = new FileStream(Installer + ".part", FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        Assert.Equal(DownloadOutcome.Busy, Downloader(Origin()).Download(Pending(), null, CancellationToken.None).Outcome);
    }

    [Fact]
    public void FileErrorsAreToldApart()
    {
        Assert.Equal(new DownloadReport(DownloadOutcome.NoSpace, "the disk is full"), UpdateDownloader.Classify(new IOException("full", unchecked((int)0x80070070))));
        Assert.Equal(DownloadOutcome.NoSpace, UpdateDownloader.Classify(new IOException("full", unchecked((int)0x80070027))).Outcome);
        Assert.Equal(DownloadOutcome.Busy, UpdateDownloader.Classify(new IOException("locked", unchecked((int)0x80070021))).Outcome);
        // Linux reports a file held elsewhere as the bare error number 11 (EWOULDBLOCK). Windows'
        // error 11 is a bad format, and was taken for another window downloading the file.
        Assert.Equal(DownloadOutcome.Busy, UpdateDownloader.Classify(new IOException("locked", 11)).Outcome);
        Assert.Equal(new DownloadReport(DownloadOutcome.Failed, "bad format"), UpdateDownloader.Classify(new IOException("bad format", unchecked((int)0x8007000B))));
        Assert.Equal(new DownloadReport(DownloadOutcome.Failed, "other"), UpdateDownloader.Classify(new IOException("other")));
        Assert.Throws<ArgumentNullException>(() => UpdateDownloader.Classify(null!));
    }

    [Fact]
    public void OfflineOrAMissingChecksumIsAFailureToRetry()
    {
        var offline = new FakeOrigin(_ => throw new HttpRequestException("No such host is known."));
        Assert.Equal(new DownloadReport(DownloadOutcome.Failed, "No such host is known."), Downloader(offline).Download(Pending(), null, CancellationToken.None));

        var missing = new FakeOrigin(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Equal(DownloadOutcome.Failed, Downloader(missing).Download(Pending(), null, CancellationToken.None).Outcome);
    }

    [Fact]
    public void ACancelledDownloadSaysSoAndKeepsItsPart()
    {
        using var cancel = new CancellationTokenSource();
        var origin = Origin(_ =>
        {
            cancel.Cancel();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BlockingStream()) };
        });

        Assert.Equal(new DownloadReport(DownloadOutcome.Cancelled), Downloader(origin).Download(Pending(), null, cancel.Token));
        Assert.True(File.Exists(Installer + ".part"));
    }

    [Fact]
    public void AConnectionThatStopsSendingIsGivenUp()
    {
        var origin = Origin(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BlockingStream()) });
        var downloader = new UpdateDownloader(new HttpClient(origin), _folder) { FreeSpace = _ => long.MaxValue, StallAfter = TimeSpan.FromMilliseconds(200) };

        Assert.Equal(new DownloadReport(DownloadOutcome.Failed, "the connection stopped sending"), downloader.Download(Pending(), null, CancellationToken.None));
    }

    [Fact]
    [Capability("TOOL-09")]
    public void WhileSomethingPlaysTheDownloadKeepsToAGentleSpeed()
    {
        var slept = TimeSpan.Zero;
        var playing = true;
        var downloader = new UpdateDownloader(new HttpClient(Origin()), _folder)
        {
            FreeSpace = _ => long.MaxValue,
            Busy = () => playing,
            BusyBytesPerSecond = 100_000,
            Sleep = wait => slept += wait,
        };

        Assert.Equal(DownloadOutcome.Ready, downloader.Download(Pending(), null, CancellationToken.None).Outcome);

        // 200 kB at 100 kB a second: about two seconds of waiting, spread between the reads.
        Assert.InRange(slept.TotalSeconds, 1.5, 2.01);

        Dispose();
        slept = TimeSpan.Zero;
        playing = false;
        Assert.Equal(DownloadOutcome.Ready, downloader.Download(Pending(), null, CancellationToken.None).Outcome);
        Assert.Equal(TimeSpan.Zero, slept);
    }

    [Fact]
    public void TheRealSleepIsUsedUnlessReplaced()
    {
        var downloader = new UpdateDownloader(new HttpClient(Origin()), _folder) { FreeSpace = _ => long.MaxValue, Busy = () => true, BusyBytesPerSecond = 10_000_000 };
        Assert.Equal(DownloadOutcome.Ready, downloader.Download(Pending(), null, CancellationToken.None).Outcome);
    }

    [Fact]
    public void CleaningKeepsOnlyThePendingUpdatesFiles()
    {
        UpdateDownloader.Clean(_folder, null);
        Directory.CreateDirectory(_folder);
        foreach (var name in new[] { Name, Name + ".part", Name + ".sha256", "rexplayer-Setup-0.15.0.exe", "rexupdate.exe" })
        {
            File.WriteAllText(Path.Combine(_folder, name), "x");
        }

        using (new FileStream(Path.Combine(_folder, "rexupdate.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            UpdateDownloader.Clean(_folder, Pending());
        }

        // Windows keeps an exclusively opened helper; Unix permits unlinking an open file.
        string[] expected = OperatingSystem.IsWindows()
            ? [Name, Name + ".part", Name + ".sha256", "rexupdate.exe"]
            : [Name, Name + ".part", Name + ".sha256"];
        Assert.Equal(expected, Directory.GetFiles(_folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        UpdateDownloader.Clean(_folder, null);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public void AnInstallerWithoutItsChecksumOrUnreadableIsNotReady()
    {
        Assert.False(UpdateDownloader.IsReady(_folder, Pending()));
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Installer, _installer);
        Assert.False(UpdateDownloader.IsReady(_folder, Pending()));

        File.WriteAllText(Installer + ".sha256", Checksum);
        Assert.True(UpdateDownloader.IsReady(_folder, Pending()));
        Assert.False(UpdateDownloader.IsReady(_folder, Pending(size: 5)));
        using (new FileStream(Installer, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(UpdateDownloader.IsReady(_folder, Pending()));
        }

        Assert.Throws<ArgumentNullException>(() => UpdateDownloader.InstallerPath(_folder, null!));
    }

    /// <summary>A body that never arrives: reads wait until the stream is disposed.</summary>
    private sealed class BlockingStream : Stream
    {
        private readonly ManualResetEventSlim _disposed = new();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _disposed.Wait(TimeSpan.FromSeconds(10));
            throw new ObjectDisposedException(nameof(BlockingStream));
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            _disposed.Set();
            base.Dispose(disposing);
        }
    }
}
