using Rex.Media.Containers.Riff;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Containers;

/// <summary>WAV arriving from a source that does not know its own length, like an HTTP stream.</summary>
public sealed class StreamingWavTests
{
    private sealed class LiveSource(byte[] data) : IByteSource
    {
        private readonly MemoryByteSource _inner = new(data);

        public string Name => "live.wav";

        public long? Length => null;

        public bool CanSeek => false;

        public int Read(long position, Span<byte> destination, CancellationToken cancellationToken) => _inner.Read(position, destination, cancellationToken);

        public void Dispose() => _inner.Dispose();
    }

    private static int TotalBytes(WavDemuxer demuxer)
    {
        var total = 0;
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            total += packet.Data.Length;
            packet.Dispose();
        }

        return total;
    }

    [Fact]
    public void AnUnboundedStreamHasNoDurationAndPlaysWholeBlocksUntilItRunsDry()
    {
        var file = WavBuilder.Pcm(8000, 2, 16, new byte[402]).DataSizeField(0).Build();
        using var demuxer = new WavDemuxer(new LiveSource(file), CancellationToken.None);

        Assert.False(demuxer.Info.Duration.IsKnown);
        Assert.False(demuxer.Info.IsSeekable);
        Assert.Equal(400, TotalBytes(demuxer));
        Assert.Null(demuxer.ReadPacket(CancellationToken.None));
    }

    [Fact]
    public void ADeclaredSizeIsTrustedWhenTheSourceCannotSayOtherwise()
    {
        var file = WavBuilder.Pcm(8000, 1, 16, new byte[800]).Build();
        using var demuxer = new WavDemuxer(new LiveSource(file), CancellationToken.None);

        Assert.Equal(MediaTime.FromMilliseconds(50), demuxer.Info.Duration);
        Assert.Equal(800, TotalBytes(demuxer));
    }

    [Fact]
    public void AFormatWithEmptyBlocksIsRejected()
    {
        var file = new WavBuilder().Format(1, 1, 8000, 0, 16).Data(new byte[4]).Build();

        Assert.Throws<MediaFormatException>(() => new WavDemuxer(new MemoryByteSource(file), CancellationToken.None));
    }

    [Fact]
    public void ABigEndianExtensibleFormatIsRead()
    {
        var file = new WavBuilder().Riff("RIFX").ExtensibleFormat(1, 2, 48_000, 24, 20, (uint)ChannelLayout.Stereo).Data(new byte[12]).Build();

        using var demuxer = new WavDemuxer(new MemoryByteSource(file), CancellationToken.None);
        var audio = demuxer.Info.Tracks[0].Audio!;

        Assert.Equal(20, audio.BitsPerSample);
        Assert.Equal(ChannelLayout.Stereo, audio.Layout);
        Assert.Equal(SampleFormat.S24, audio.PcmFormat);
        Assert.True(audio.BigEndian);
    }
}
