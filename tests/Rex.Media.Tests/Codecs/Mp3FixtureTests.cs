using System.Buffers.Binary;
using Rex.Media.Codecs.Software.Mpeg;
using Rex.Media.Containers.Mpeg;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Codecs;

/// <summary>
/// MP3 files from an independent encoder (see docs/fixtures.md), demuxed and decoded by rexplayer
/// and compared with an independent decoder's output. Two compliant decoders may differ only by
/// rounding, so the bar is the conformance test's "full accuracy": RMS difference under
/// 2^-15 / sqrt(12) and no sample more than 2^-14 apart, with full scale at 1.0.
/// </summary>
public sealed class Mp3FixtureTests
{
    private static readonly double RmsLimit = Math.Pow(2, -15) / Math.Sqrt(12);
    private static readonly double PeakLimit = Math.Pow(2, -14);

    public static TheoryData<string, int, int> Fixtures => new()
    {
        { "stereo-44k-128k-cbr", 2, 44_100 },
        { "stereo-44k-vbr-bursts", 2, 44_100 },
        { "mono-48k-vbr", 1, 48_000 },
        { "stereo-22k-64k-mpeg2", 2, 22_050 },
        { "mono-8k-16k-mpeg25", 1, 8000 },
    };

    /// <summary>Reads a 24-bit PCM WAV into float planes.</summary>
    internal static float[][] ReadReference(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var span = bytes.AsSpan();
        var fmt = span.IndexOf("fmt "u8);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(span[(fmt + 10)..]);
        var data = span.IndexOf("data"u8);
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(data + 4)..]);
        var samples = length / 3 / channels;
        var planes = new float[channels][];
        for (var c = 0; c < channels; c++)
        {
            planes[c] = new float[samples];
        }

        var at = data + 8;
        for (var i = 0; i < samples; i++)
        {
            for (var c = 0; c < channels; c++, at += 3)
            {
                var value = (bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16)) << 8 >> 8;
                planes[c][i] = value / 8_388_608f;
            }
        }

        return planes;
    }

    /// <summary>Decodes a file the way playback does: gapless delay and padding cut by timestamp and duration.</summary>
    internal static (float[][] Planes, Mp3Decoder Decoder, MediaInfo Info) Decode(byte[] file)
    {
        using var demuxer = new MpegAudioDemuxer(new MemoryByteSource(file, "fixture.mp3"), CancellationToken.None);
        var track = demuxer.Info.Tracks[0];
        var decoder = new Mp3Decoder(track);
        var rate = track.Audio!.SampleRate;
        var end = track.Duration.IsKnown ? track.Duration.ToSamples(rate) : long.MaxValue;
        var planes = Enumerable.Range(0, track.Audio.Channels).Select(_ => new List<float>()).ToArray();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            var frames = new List<AudioFrame>();
            decoder.Decode(packet, frames);
            packet.Dispose();
            foreach (var frame in frames)
            {
                var first = frame.Pts.ToSamples(rate);
                for (var c = 0; c < planes.Length; c++)
                {
                    var samples = frame.Channel(c);
                    for (var i = 0; i < samples.Length; i++)
                    {
                        if (first + i >= 0 && first + i < end)
                        {
                            planes[c].Add(samples[i]);
                        }
                    }
                }

                frame.Dispose();
            }
        }

        return ([.. planes.Select(p => p.ToArray())], decoder, demuxer.Info);
    }

    internal static (double Rms, double Peak) Difference(float[] actual, float[] expected)
    {
        double sum = 0;
        double peak = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            var difference = Math.Abs((double)actual[i] - expected[i]);
            sum += difference * difference;
            peak = Math.Max(peak, difference);
        }

        return (Math.Sqrt(sum / expected.Length), peak);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    [Capability("FMT-A02")]
    [Capability("FMT-C03")]
    public void DecodingMatchesAnIndependentDecoderToFullAccuracy(string name, int channels, int rate)
    {
        var file = File.ReadAllBytes(RepoPaths.Combine($"tests/fixtures/mp3/{name}.mp3"));
        var reference = ReadReference(RepoPaths.Combine($"tests/fixtures/mp3/{name}.reference.wav"));

        var (planes, decoder, info) = Decode(file);

        Assert.Equal((channels, rate), (info.Tracks[0].Audio!.Channels, info.Tracks[0].Audio!.SampleRate));
        Assert.Equal(0, decoder.CorruptFrames);
        for (var c = 0; c < channels; c++)
        {
            Assert.Equal(reference[c].Length, planes[c].Length);
            var (rms, peak) = Difference(planes[c], reference[c]);
            Assert.True(rms < RmsLimit && peak <= PeakLimit, $"{name} channel {c}: RMS {rms:E3} (limit {RmsLimit:E3}), peak {peak:E3} (limit {PeakLimit:E3}).");
        }
    }
}
