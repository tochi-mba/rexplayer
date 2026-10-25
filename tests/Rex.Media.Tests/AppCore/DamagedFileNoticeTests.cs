using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Diagnostics;
using Rex.Media.Engine;
using Rex.Media.Library;
using Rex.Media.Primitives;
using Rex.Media.Tests.Containers;
using static Rex.Media.TestKit.EbmlWriter;
using Id = Rex.Media.Containers.Matroska.MatroskaId;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// What the window is told about a damaged file: once that it is damaged and plays what can be
/// read, then each stretch passed over; a file that cannot play says why in a sentence; and the
/// library marks a file it could not read, or that took too long, with the reason.
/// </summary>
public sealed class DamagedFileNoticeTests
{
    /// <summary>Sound with a block whose lacing is broken, then, five seconds on, more sound.</summary>
    private static byte[] DamagedSound()
    {
        var tenth = new byte[1600];
        byte[] broken = [0x81, 0, 50, 0x80 | (1 << 1)];
        return MatroskaCraftedTests.Mkv(
            MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack()),
            MatroskaCraftedTests.Cluster(0, MatroskaCraftedTests.Simple(1, 0, true, tenth), Element(Id.SimpleBlock, broken), MatroskaCraftedTests.Simple(1, 100, true, tenth)),
            MatroskaCraftedTests.Cluster(5200, MatroskaCraftedTests.Simple(1, 0, true, tenth)));
    }

    [Fact]
    public void ADamagedFileIsToldOnceAndThenWhatWasSkipped()
    {
        using var harness = new ControllerHarness();
        harness.Files["damaged.mkv"] = DamagedSound();

        harness.Controller.Open(["damaged.mkv"]);
        harness.WaitForEngine<EndedEvent>();
        harness.PumpUntil(controller => controller.State == SessionState.Ended);

        Assert.Single(harness.Messages, message => message == DamageNotices.Damaged);
        Assert.Contains("Skipped 5 seconds that could not be read.", harness.Messages);
    }

    [Fact]
    public void AFileThatCannotPlayOnItsOwnSaysSoWithoutItsName()
    {
        using var harness = new ControllerHarness();
        harness.Files["broken.wav"] = [1, 2, 3, 4];

        harness.Controller.Open(["broken.wav"]);
        harness.PumpUntil(controller => controller.State == SessionState.Faulted);

        Assert.StartsWith("This file could not be played: ", Assert.Single(harness.Messages), StringComparison.Ordinal);
    }

    private static readonly string Music = Path.Combine(Path.GetTempPath(), "rexplayer-library-damage", "Music");

    [Fact]
    public void TheLibraryMarksAFileItCouldNotReadWithTheReason()
    {
        var library = new MediaLibrary(RexStore.InMemory());
        library.AddFolder(Music);
        var damaged = Path.Combine(Music, "damaged.mkv");
        var slow = Path.Combine(Music, "slow.mkv");
        using var release = new ManualResetEventSlim();
        using var scanner = new LibraryScanner(library, _ => [new LibraryFile(damaged, 10, DateTime.UnixEpoch), new LibraryFile(slow, 10, DateTime.UnixEpoch)], (path, token) =>
        {
            if (path == slow)
            {
                // A reader stuck on a damaged file: it is told to stop once the file is passed over.
                release.Wait(TimeSpan.FromMinutes(1), token);
            }

            throw new MediaFormatException("A Matroska element runs past the end of the file. The rest is for the log.");
        }, RexLog.InMemory(), background: false, probeTimeout: TimeSpan.FromSeconds(3));

        scanner.Request();
        scanner.RunPending(TestContext.Current.CancellationToken);

        Assert.Equal("A Matroska element runs past the end of the file.", library.Entry(damaged)!.Problem);
        Assert.Equal("Reading it took too long, so it may be damaged.", library.Entry(slow)!.Problem);
        Assert.Empty(library.Unprobed);
    }

    [Fact]
    public void AFileThatReadsOnceItChangesLosesItsProblem()
    {
        var library = new MediaLibrary(RexStore.InMemory());
        library.AddFolder(Music);
        var path = Path.Combine(Music, "song.wav");
        library.Scan(Music, [new LibraryFile(path, 10, DateTime.UnixEpoch)], _ => LibraryKind.Music);

        library.SetReadDetails([(path, null, "damaged")]);
        Assert.Equal("damaged", library.Entry(path)!.Problem);

        library.SetDetails(path, new MediaInfo { FormatName = "WAVE", Tracks = [] });
        Assert.Null(library.Entry(path)!.Problem);
        Assert.Equal(LibraryScanner.DefaultProbeTimeout, TimeSpan.FromSeconds(20));
    }
}
