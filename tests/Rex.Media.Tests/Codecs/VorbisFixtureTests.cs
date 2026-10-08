using Rex.Media.Codecs.Software.Vorbis;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Tests.Containers;

namespace Rex.Media.Tests.Codecs;

/// <summary>rexplayer's Vorbis decoder against FFmpeg's, on files libvorbis made (FMT-A09).</summary>
public sealed class VorbisFixtureTests
{
    private static (float[][] Decoded, float[][] Reference, TrackInfo Track) Decode(string name)
    {
        var reference = Mp3FixtureTests.ReadReference(RepoPaths.Combine($"tests/fixtures/mkv/{Path.GetFileNameWithoutExtension(name)}.audio0.reference.wav"));
        using var demuxer = MatroskaDemuxerTests.Open(File.ReadAllBytes(RepoPaths.Combine($"tests/fixtures/mkv/{name}")));
        var track = demuxer.Info.Tracks.First(t => t.Kind == MediaKind.Audio);
        return (MatroskaDemuxerTests.DecodeTrack(demuxer, track), reference, track);
    }

    [Theory]
    [Capability("FMT-A09")]
    [InlineData("vorbis-stereo.webm", 2)]
    [InlineData("vorbis-clicks.webm", 1)]
    [InlineData("vorbis-51.mkv", 6)]
    public void VorbisDecodesLikeAnIndependentDecoder(string name, int channels)
    {
        var (decoded, reference, track) = Decode(name);

        Assert.Equal(CodecId.Vorbis, track.Codec);
        Assert.Equal(channels, decoded.Length);
        Assert.Equal(reference.Length, decoded.Length);
        for (var c = 0; c < reference.Length; c++)
        {
            // FFmpeg keeps a few more samples of the first block before time zero; the streams line
            // up from the end. Its reference is 24-bit, so it clips where the decoded signal overshoots.
            var lead = reference[c].Length - decoded[c].Length;
            Assert.True(lead is >= 0 and <= 16, $"{name}: decoded {decoded[c].Length} samples, reference {reference[c].Length}");
            var clipped = decoded[c].Select(sample => Math.Clamp(sample, -1f, 1f)).ToArray();
            var difference = Mp3FixtureTests.Difference(clipped, reference[c][lead..]);
            Assert.True(difference.Peak < 2e-4, $"{name} channel {c}: peak difference {difference.Peak}");
        }
    }

    [Fact]
    public void TheInverseTransformMatchesItsDefinition()
    {
        var random = new Random(7);
        foreach (var size in new[] { 16, 64, 256, 2048 })
        {
            var spectrum = Enumerable.Range(0, size / 2).Select(_ => (float)((random.NextDouble() * 2) - 1)).ToArray();
            var fast = new float[size];
            var direct = new float[size];

            new VorbisMdct(size).Inverse(spectrum, fast);
            VorbisMdct.Direct(spectrum, direct);

            var peak = fast.Zip(direct, (a, b) => Math.Abs(a - b)).Max();
            Assert.True(peak < 1e-3 * Math.Sqrt(size), $"size {size}: peak difference {peak}");
        }
    }
}
