using Rex.Media.Containers.Matroska;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit.Damage;

namespace Rex.Media.Tests.Containers;

/// <summary>Malformed EBML blocks must not hide later independently timed clusters.</summary>
public sealed class MatroskaDamageRecoveryTests
{
    [Fact]
    public void AFalseClusterSignatureIsSkippedBeforeRecoveringARealCluster()
    {
        var file = MatroskaCraftedTests.Mkv(
            MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack()),
            MatroskaCraftedTests.Cluster(0, MatroskaCraftedTests.Simple(1, 0, true, 1)),
            [0, 0x1F, 0x43, 0xB6, 0x75, 0, 0],
            MatroskaCraftedTests.Cluster(5, MatroskaCraftedTests.Simple(1, 0, true, 2)));
        using var demuxer = new MatroskaDemuxer(new MemoryByteSource(file, "broken.mkv"), CancellationToken.None);
        var values = new List<byte>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            using (packet)
            {
                values.Add(packet.Data.Span[0]);
            }
        }

        Assert.Equal([1, 2], values);
    }

    [Fact]
    public void ATruncatedBlockOnAnUnknownLengthSourceIsRefusedCleanly()
    {
        var file = MatroskaCraftedTests.Mkv(
            MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack()),
            MatroskaCraftedTests.Cluster(0, MatroskaCraftedTests.Simple(1, 0, true, 1, 2, 3, 4)));
        using var demuxer = new MatroskaDemuxer(new UnknownLengthSource(file[..^2]), CancellationToken.None);
        var failure = Assert.Throws<MediaFormatException>(() => demuxer.ReadPacket(CancellationToken.None));
        Assert.Contains("past the end", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownLengthTruncationBoundsTheClusterSearch()
    {
        var second = MatroskaCraftedTests.Cluster(5, MatroskaCraftedTests.Simple(1, 0, true, 2));
        var file = MatroskaCraftedTests.Mkv(
            MatroskaCraftedTests.Tracks(MatroskaCraftedTests.PcmTrack()),
            MatroskaCraftedTests.Cluster(0, MatroskaCraftedTests.Simple(1, 0, true, 1)),
            [0, 0, 0, 0, 0], second);
        // Keep just the first two bytes of the second cluster. The EBML segment still
        // declares the original size, while this source has no reported length.
        using var demuxer = new MatroskaDemuxer(new UnknownLengthSource(file[..^(second.Length - 2)]), CancellationToken.None);
        using var first = demuxer.ReadPacket(CancellationToken.None);
        Assert.NotNull(first);
        Assert.Null(demuxer.ReadPacket(CancellationToken.None));
    }

    private sealed class UnknownLengthSource(byte[] bytes) : IByteSource
    {
        private readonly MemoryByteSource _inner = new(bytes, "damaged.mkv");

        public string Name => _inner.Name;
        public long? Length => null;
        public bool CanSeek => true;

        public int Read(long position, Span<byte> destination, CancellationToken cancellationToken) =>
            _inner.Read(position, destination, cancellationToken);

        public void Dispose() => _inner.Dispose();
    }

    [Fact]
    public void PlaybackFindsTheNextClusterAfterAChildClaimsAnUnknownSize()
    {
        var healthy = HealthyMedia.Matroska(3);
        var bytes = (byte[])healthy.Bytes.Clone();
        var (start, length) = healthy.Landmarks["cluster"][0];
        var searchAt = start + 8;
        var tag = Array.IndexOf(bytes, (byte)0xA3, searchAt, Math.Min(128, start + length - searchAt));
        Assert.True(tag >= 0, "The first cluster must carry a SimpleBlock.");
        // A block is never allowed an EBML unknown size. The next cluster remains intact.
        bytes[tag + 1] = 0xFF;

        using var demuxer = new MatroskaDemuxer(new MemoryByteSource(bytes, "damaged.mkv"), CancellationToken.None);
        var seenLater = false;
        for (var i = 0; i < 200 && demuxer.ReadPacket(CancellationToken.None) is { } packet; i++)
        {
            using (packet)
            {
                seenLater |= packet.Pts >= MediaTime.FromSeconds(1);
            }
        }

        Assert.True(seenLater, "The decoder must still receive packets from later clusters.");
    }
}
