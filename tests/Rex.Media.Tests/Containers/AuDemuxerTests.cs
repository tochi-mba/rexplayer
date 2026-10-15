using System.Buffers.Binary;
using System.Text;
using Rex.Media.Containers.Riff;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Containers;

public sealed class AuDemuxerTests
{
    [Fact]
    [Capability("FMT-C15")]
    public void AuDescribesBigEndianPcmAndItsAnnotation()
    {
        using var demuxer = Open(Build(encoding: 3, sampleRate: 8000, channels: 2, data: new byte[320], annotation: "recorded live"));
        var track = Assert.Single(demuxer.Info.Tracks);

        Assert.Equal("AU", demuxer.Info.FormatName);
        Assert.True(demuxer.Info.IsSeekable);
        Assert.Equal(CodecId.Pcm, track.Codec);
        Assert.Equal(SampleFormat.S16, track.Audio!.PcmFormat);
        Assert.True(track.Audio.BigEndian);
        Assert.Equal(8000, track.Audio.SampleRate);
        Assert.Equal(2, track.Audio.Channels);
        Assert.Equal(4, track.Audio.BlockAlign);
        Assert.Equal(16, track.Audio.BitsPerSample);
        Assert.Equal(256_000, track.BitRate);
        Assert.Equal(MediaTime.FromSamples(80, 8000), demuxer.Info.Duration);
        Assert.Equal("recorded live", demuxer.Info.Metadata[MetadataKeys.Comment]);
    }

    [Theory]
    [InlineData(1, CodecId.Mulaw, SampleFormat.Unknown, 8, 1)]
    [InlineData(2, CodecId.Pcm, SampleFormat.S8, 8, 1)]
    [InlineData(3, CodecId.Pcm, SampleFormat.S16, 16, 2)]
    [InlineData(4, CodecId.Pcm, SampleFormat.S24, 24, 3)]
    [InlineData(5, CodecId.Pcm, SampleFormat.S32, 32, 4)]
    [InlineData(6, CodecId.Pcm, SampleFormat.F32, 32, 4)]
    [InlineData(7, CodecId.Pcm, SampleFormat.F64, 64, 8)]
    [InlineData(27, CodecId.Alaw, SampleFormat.Unknown, 8, 1)]
    [InlineData(99, CodecId.Unknown, SampleFormat.Unknown, 0, 1)]
    public void EncodingsDescribeTheirStoredSamples(uint encoding, CodecId codec, SampleFormat format, int bits, int bytes)
    {
        using var demuxer = Open(Build(encoding, 48_000, 1, new byte[bytes * 2]));
        var track = demuxer.Info.Tracks[0];

        Assert.Equal(codec, track.Codec);
        Assert.Equal(format, track.Audio!.PcmFormat);
        Assert.Equal(bits, track.Audio.BitsPerSample);
        Assert.Equal(bytes, track.Audio.BlockAlign);
    }

    [Fact]
    public void PacketsAndSeeksStartAtTheDeclaredDataOffset()
    {
        var samples = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();
        using var demuxer = Open(Build(2, 1000, 1, samples, "note"));

        using (var first = demuxer.ReadPacket(CancellationToken.None)!)
        {
            Assert.Equal(samples[..50], first.Data.Span.ToArray());
        }

        demuxer.Seek(MediaTime.FromSamples(125, 1000), CancellationToken.None);
        using var seeked = demuxer.ReadPacket(CancellationToken.None)!;
        Assert.Equal(samples[125..175], seeked.Data.Span.ToArray());
        demuxer.Seek(MediaTime.FromSeconds(2), CancellationToken.None);
        Assert.Null(demuxer.ReadPacket(CancellationToken.None));
    }

    [Fact]
    public void AnUnknownDataSizeUsesTheAvailableFileLength()
    {
        using var demuxer = Open(Build(3, 8000, 1, new byte[32], declaredSize: uint.MaxValue));

        Assert.Equal(MediaTime.FromSamples(16, 8000), demuxer.Info.Duration);
    }

    [Fact]
    public void InvalidHeadersAreFormatErrors()
    {
        Assert.Throws<ArgumentNullException>(() => new AuDemuxer(null!, CancellationToken.None));
        Assert.Throws<MediaFormatException>(() => Open([]));
        Assert.Throws<MediaFormatException>(() => Open(Build(3, 8000, 1, [], magic: "nope")));
        Assert.Throws<MediaFormatException>(() => Open(Build(3, 8000, 1, [], dataOffset: 23)));
        Assert.Throws<MediaFormatException>(() => Open(Build(3, 0, 1, [])));
        Assert.Throws<MediaFormatException>(() => Open(Build(3, 768_001, 1, [])));
        Assert.Throws<MediaFormatException>(() => Open(Build(3, 8000, 0, [])));
        Assert.Throws<MediaFormatException>(() => Open(Build(3, 8000, (uint)AudioFormat.MaxChannels + 1, [])));
        Assert.Throws<MediaFormatException>(() => Open(Build(3, 8000, 1, [], dataOffset: 32)));
    }

    [Fact]
    public void FactoryRecognisesOnlyCompleteAuHeaders()
    {
        var factory = new AuDemuxerFactory();
        var file = Build(3, 8000, 1, new byte[2]);

        Assert.Equal("au", factory.Name);
        Assert.Equal(100, factory.Probe(file, null));
        Assert.Equal(0, factory.Probe(file.AsSpan(0, 23), ".au"));
        Assert.Equal(0, factory.Probe("RIFF0000WAVE"u8, ".au"));
        using var opened = factory.Open(new MemoryByteSource(file), CancellationToken.None);
        Assert.IsType<AuDemuxer>(opened);
    }

    private static AuDemuxer Open(byte[] bytes) => new(new MemoryByteSource(bytes, "test.au"), CancellationToken.None);

    private static byte[] Build(uint encoding, uint sampleRate, uint channels, byte[] data, string? annotation = null,
        uint? declaredSize = null, uint? dataOffset = null, string magic = ".snd")
    {
        var note = annotation is null ? [] : Encoding.UTF8.GetBytes(annotation + "\0");
        var offset = dataOffset ?? checked((uint)(24 + note.Length));
        var bytes = new byte[24 + note.Length + data.Length];
        Encoding.ASCII.GetBytes(magic).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), offset);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), declaredSize ?? (uint)data.Length);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), encoding);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), sampleRate);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), channels);
        note.CopyTo(bytes, 24);
        data.CopyTo(bytes, 24 + note.Length);
        return bytes;
    }
}
