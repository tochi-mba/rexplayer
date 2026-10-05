using Rex.Media.Codecs.Flac;
using Rex.Media.Codecs.Software.Flac;
using Rex.Media.Containers.Flac;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Codecs;

/// <summary>
/// FLAC files from an independent encoder (see docs/fixtures.md), demuxed and decoded by rexplayer.
/// The oracle is inside each file: STREAMINFO carries the encoder's MD5 of its source samples, so a
/// lossless decode must reproduce that checksum exactly.
/// </summary>
public sealed class FlacFixtureTests
{
    [Theory]
    [InlineData("stereo-16bit-44k-lpc.flac", 2, 16, 44_100)]
    [InlineData("mono-24bit-96k-sweep.flac", 1, 24, 96_000)]
    [InlineData("surround-51-16bit-48k.flac", 6, 16, 48_000)]
    [InlineData("stereo-16bit-22k-midside-fixed.flac", 2, 16, 22_050)]
    [Capability("FMT-A04")]
    [Capability("FMT-C04")]
    public void DecodingReproducesTheEncodersChecksum(string name, int channels, int bits, int rate)
    {
        var file = File.ReadAllBytes(RepoPaths.Combine("tests/fixtures/flac/" + name));
        using var demuxer = new FlacDemuxer(new MemoryByteSource(file, name), CancellationToken.None);
        var track = demuxer.Info.Tracks[0];
        var streamInfo = FlacStreamInfo.Parse(track.CodecPrivate);
        Assert.Equal((channels, bits, rate), (track.Audio!.Channels, track.Audio.BitsPerSample, track.Audio.SampleRate));
        using var decoder = new FlacDecoder(track);

        var planes = Enumerable.Range(0, channels).Select(_ => new List<int>()).ToArray();
        var scale = (double)(1L << (bits - 1));
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            var frames = new List<AudioFrame>();
            decoder.Decode(packet, frames);
            packet.Dispose();
            foreach (var frame in frames)
            {
                for (var c = 0; c < channels; c++)
                {
                    foreach (var sample in frame.Channel(c))
                    {
                        planes[c].Add((int)Math.Round(sample * scale));
                    }
                }

                frame.Dispose();
            }
        }

        Assert.Equal(0, decoder.CorruptFrames);
        Assert.Equal(streamInfo.TotalSamples, planes[0].Count);
        Assert.Equal(Convert.ToHexString(streamInfo.Md5), Convert.ToHexString(FlacBuilder.Md5([.. planes.Select(p => p.ToArray())], bits)));
    }
}
