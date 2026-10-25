using Microsoft.Win32.SafeHandles;
using Rex.Media.Containers.Mp4;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Tests.Engine;

namespace Rex.Media.Tests.IO;

/// <summary>
/// A sparse download (a torrent's file, laid out at full size and filled in any order) read and
/// played while parts of it are missing: reads stop where a missing part begins, the MP4 reader
/// waits there instead of passing over it, a seek into a missing part waits too, and playback
/// buffers, then carries on once the part arrives.
/// </summary>
public sealed class SparseDownloadTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "rexplayer-sparse-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => File.Delete(_path);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A file whose listed ranges are not on the disk yet, until <see cref="Fill"/>.</summary>
    private sealed class Holes(params (long Start, long End)[] holes) : IFileAllocation
    {
        private readonly object _gate = new();
        private readonly List<(long Start, long End)> _missing = [.. holes];

        public void Fill()
        {
            lock (_gate)
            {
                _missing.Clear();
            }
        }

        public long Allocated(SafeFileHandle file, long length)
        {
            lock (_gate)
            {
                return length - _missing.Sum(hole => Math.Max(0, Math.Min(hole.End, length) - hole.Start));
            }
        }

        public long AllocatedEnd(SafeFileHandle file, long position, long length)
        {
            lock (_gate)
            {
                var end = position + length;
                foreach (var (start, stop) in _missing)
                {
                    if (position >= start && position < stop)
                    {
                        return position;
                    }

                    if (start > position && start < end)
                    {
                        end = start;
                    }
                }

                return end;
            }
        }
    }

    [Fact]
    [Capability("LIB-05")]
    public void ReadsStopAtAMissingPartAndItArrivingCountsAsGrowth()
    {
        File.WriteAllBytes(_path, [.. Enumerable.Range(0, 100).Select(i => (byte)i)]);
        var holes = new Holes((40, 60));
        using var source = new FileByteSource(_path, holes);
        var buffer = new byte[30];

        Assert.Equal(30, source.Read(0, buffer, Token));
        Assert.Equal(10, source.Read(30, buffer, Token));
        Assert.Equal(0, source.Read(45, buffer, Token));
        Assert.Equal(30, source.Read(60, buffer, Token));
        Assert.True(source.IsPending(30, 20));
        Assert.False(source.IsPending(0, 30));
        Assert.False(source.IsPending(90, 20));
        Assert.False(source.IsPending(0, 0));

        // Nothing new yet, then the part arrives: a wait sees growth, and the read goes through.
        var extent = source.Extent;
        Assert.Equal(ByteSourceChange.TimedOut, source.WaitForChange(extent, TimeSpan.Zero, Token));
        holes.Fill();
        Assert.Equal(ByteSourceChange.Grew, source.WaitForChange(extent, TimeSpan.FromSeconds(5), Token));
        Assert.True(source.Extent.Revision > extent.Revision);
        Assert.Equal(30, source.Read(30, buffer, Token));
        Assert.Equal(40, buffer[10]);
        Assert.False(source.IsPending(30, 20));

        // Whole from the start, a sparse file reads as any other and is never asked again.
        using var whole = new FileByteSource(_path, new Holes());
        Assert.Equal(30, whole.Read(30, buffer, Token));
        Assert.False(whole.IsPending(30, 20));
        Assert.Equal(ByteSourceChange.TimedOut, whole.WaitForChange(whole.Extent, TimeSpan.Zero, Token));
    }

    [Fact]
    [Capability("LIB-05")]
    public void TheMp4ReaderWaitsAtAMissingSampleAndOnASeekIntoOne()
    {
        var fixture = File.ReadAllBytes(RepoPaths.Combine("tests", "fixtures", "mp4", "h264-aac.mp4"));
        File.WriteAllBytes(_path, fixture);
        var all = Packets(new Mp4Demuxer(new MemoryByteSource(fixture, "whole.mp4"), Token));

        // The media data (after its 8-byte header at 2415) is missing from 6000 on.
        var holes = new Holes((6000, fixture.Length));
        using (var source = new FileByteSource(_path, holes))
        {
            using var demuxer = new Mp4Demuxer(source, Token);
            var before = Packets(demuxer);
            Assert.InRange(before.Count, 1, all.Count - 1);
            Assert.Null(demuxer.ReadPacket(Token));

            // The rest arrives: reading carries on with the sample it stopped at, none passed over.
            holes.Fill();
            Assert.Equal(all, [.. before, .. Packets(demuxer)]);
        }

        var missing = new Holes((2423, fixture.Length));
        using (var source = new FileByteSource(_path, missing))
        {
            using var demuxer = new Mp4Demuxer(source, Token);
            demuxer.Seek(MediaTime.FromSeconds(0.2), Token);
            Assert.Null(demuxer.ReadPacket(Token));
            missing.Fill();
            Assert.NotNull(demuxer.ReadPacket(Token));
        }
    }

    private static List<(int Track, long Pts, int Length)> Packets(Mp4Demuxer demuxer)
    {
        var packets = new List<(int, long, int)>();
        while (demuxer.ReadPacket(Token) is { } packet)
        {
            using (packet)
            {
                packets.Add((packet.TrackId, packet.Pts.Ticks, packet.Data.Length));
            }
        }

        return packets;
    }

    [Fact]
    [Capability("LIB-05")]
    public async Task PlaybackBuffersAtAMissingPartAndCarriesOnWhenItArrives()
    {
        var wav = Pcm.RampWav(8000, 80_000);
        File.WriteAllBytes(_path, wav);
        var holes = new Holes((44 + 40_000, 44 + 120_000));
        using var source = new FileByteSource(_path, holes);
        using var harness = new SessionHarness(growingFileStallTimeout: TimeSpan.FromSeconds(20));

        await harness.Session.OpenAsync(source);
        harness.WaitForState(SessionState.Buffering);
        holes.Fill();
        await harness.FinishAsync();

        // Every sample played, in order: nothing passed over, no zeros from the missing part.
        var played = harness.Recording.Channel(0);
        Assert.Equal(80_000, played.Length);
        Assert.Equal(played[29_999] + (played[30_001] - played[29_999]) / 2, played[30_000], 3);
        Assert.NotEqual(0f, played[30_000]);
        Assert.Contains(SessionState.Buffering, harness.States);
        Assert.Equal(SessionState.Ended, harness.Session.State);
    }
}
