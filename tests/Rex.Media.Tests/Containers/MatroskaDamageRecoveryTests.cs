using Rex.Media.Containers.Matroska;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit.Damage;

namespace Rex.Media.Tests.Containers;

/// <summary>Malformed EBML blocks must not hide later independently timed clusters.</summary>
public sealed class MatroskaDamageRecoveryTests
{
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
