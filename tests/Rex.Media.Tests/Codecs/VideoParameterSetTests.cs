using System.Text.Json;
using Rex.Media.AppCore;
using Rex.Media.Codecs.H264;
using Rex.Media.Codecs.Hevc;
using Rex.Media.Codecs.Video;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Codecs;

/// <summary>H.264 and HEVC parameter sets from independent encoders, read as FFmpeg's own parser reads them.</summary>
public sealed class VideoParameterSetTests
{
    private static string FixturePath(string name) => RepoPaths.Combine($"tests/fixtures/video/{name}");

    /// <summary>The stream FFmpeg's probe described, saved beside each fixture.</summary>
    private static JsonElement Probe(string name)
    {
        var path = FixturePath(Path.GetFileNameWithoutExtension(name) + ".probe.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("streams")[0].Clone();
    }

    private static string? Text(JsonElement stream, string property) => stream.TryGetProperty(property, out var value) ? value.GetString() : null;

    private static int ProfileNumber(string name) => name switch
    {
        "Constrained Baseline" => 66,
        "High" => 100,
        "High 4:2:2" => 122,
        "High 4:4:4 Predictive" => 244,
        "Main" => 1,
        "Main 10" => 2,
        "Rext" => 4,
        _ => throw new ArgumentException($"Unknown profile {name}.", nameof(name)),
    };

    private static (int Chroma, int Bits) Layout(string pixelFormat) => pixelFormat switch
    {
        "gray" => (0, 8),
        "yuv420p" or "yuvj420p" => (1, 8),
        "yuv420p10le" => (1, 10),
        "yuv422p" => (2, 8),
        "yuv422p10le" => (2, 10),
        "yuv444p" => (3, 8),
        _ => throw new ArgumentException($"Unknown pixel format {pixelFormat}.", nameof(pixelFormat)),
    };

    /// <summary>
    /// Checks a parsed sequence against everything the probe reported. The probe gives the format
    /// FFmpeg decodes to, which is 4:2:0 for a monochrome stream, so those pass <paramref name="chroma"/>.
    /// </summary>
    private static void AssertMatchesProbe(SequenceInfo info, JsonElement probe, bool interlacedByFields = true, int? chroma = null)
    {
        Assert.Equal(ProfileNumber(Text(probe, "profile")!), info.Profile);
        Assert.Equal(probe.GetProperty("level").GetInt32(), info.Level);
        Assert.Equal((probe.GetProperty("width").GetInt32(), probe.GetProperty("height").GetInt32()), (info.Width, info.Height));
        var (probedChroma, bits) = Layout(Text(probe, "pix_fmt")!);
        Assert.Equal((chroma ?? probedChroma, bits), (info.ChromaFormat, info.BitDepth));
        var aspect = Text(probe, "sample_aspect_ratio")!.Split(':');
        Assert.Equal(new Rational(long.Parse(aspect[0], System.Globalization.CultureInfo.InvariantCulture), long.Parse(aspect[1], System.Globalization.CultureInfo.InvariantCulture)), info.PixelAspect);
        Assert.Equal(Text(probe, "color_range") == "pc", info.Color.FullRange);
        Assert.Equal(Text(probe, "color_primaries") switch { "bt709" => ColorPrimaries.Bt709, "bt2020" => ColorPrimaries.Bt2020, _ => ColorPrimaries.Unspecified }, info.Color.Primaries);
        Assert.Equal(Text(probe, "color_transfer") switch { "bt709" => ColorTransfer.Bt709, "smpte2084" => ColorTransfer.Pq, _ => ColorTransfer.Unspecified }, info.Color.Transfer);
        Assert.Equal(Text(probe, "color_space") switch { "bt709" => ColorMatrix.Bt709, "bt2020nc" => ColorMatrix.Bt2020NonConstant, _ => ColorMatrix.Unspecified }, info.Color.Matrix);
        if (interlacedByFields)
        {
            Assert.Equal(Text(probe, "field_order") is not (null or "progressive"), info.Interlaced);
        }
    }

    /// <summary>Each stream, its frame rate, and for the monochrome one the chroma_format_idc FFmpeg's header trace shows.</summary>
    [Theory]
    [InlineData("h264-high-420.h264", 25, 1, null)]
    [InlineData("h264-high422-10bit-interlaced.h264", 25, 1, null)]
    [InlineData("h264-high444-scaling.h264", 30000, 1001, null)]
    [InlineData("h264-baseline.h264", 24, 1, null)]
    [InlineData("h264-gray.h264", 25, 1, 0)]
    public void H264SequenceParameterSetsReadAsAnIndependentParserReadsThem(string name, int rateNumerator, int rateDenominator, int? chroma)
    {
        var stream = File.ReadAllBytes(FixturePath(name));
        var units = NalUnits.SplitAnnexB(stream).Select(range => stream[range]).ToList();
        var sps = units.First(unit => (unit[0] & 0x1F) == H264Sps.NalType);

        Assert.True(H264Sps.TryParse(sps, out var info));

        AssertMatchesProbe(info, Probe(name), chroma: chroma);
        Assert.Equal(new Rational(rateNumerator, rateDenominator), info.FrameRate);
        Assert.Contains(units, unit => (unit[0] & 0x1F) == 5);
    }

    [Theory]
    [InlineData("hevc-main.hevc", 25, false)]
    [InlineData("hevc-main10-hdr.hevc", 50, false)]
    [InlineData("hevc-444-layers.hevc", 30, false)]
    [InlineData("hevc-field.hevc", 25, true)]
    public void HevcSequenceParameterSetsReadAsAnIndependentParserReadsThem(string name, int rate, bool fields)
    {
        var stream = File.ReadAllBytes(FixturePath(name));
        var sps = NalUnits.SplitAnnexB(stream).Select(range => stream[range]).First(unit => HevcSps.TypeOf(unit[0]) == HevcSps.NalType);

        Assert.True(HevcSps.TryParse(sps, out var info));

        AssertMatchesProbe(info, Probe(name), interlacedByFields: false);
        Assert.Equal(new Rational(rate, 1), info.FrameRate);
        Assert.Equal(fields, info.Interlaced);
    }

    private static TrackInfo VideoTrack(string path)
    {
        using var source = new MemoryByteSource(File.ReadAllBytes(path), path);
        using var demuxer = MediaRegistries.Demuxers().Probe(source, CancellationToken.None)!.Open(source, CancellationToken.None);
        return demuxer.Info.FirstTrack(MediaKind.Video)!;
    }

    [Fact]
    public void AnAvcConfigurationRecordFromAMuxerHoldsTheParameterSets()
    {
        var track = VideoTrack(RepoPaths.Combine("tests/fixtures/mp4/h264-aac.mp4"));

        var config = AvcConfig.Parse(track.CodecPrivate);
        var info = config.Sequence()!;

        Assert.Equal((4, 100), (config.LengthSize, config.Profile));
        Assert.Single(config.SequenceParameterSets);
        Assert.Single(config.PictureParameterSets);
        Assert.Equal((128, 72, 25.0), (info.Width, info.Height, info.FrameRate!.Value.Value));
        Assert.Equal(config.Level, info.Level);
        var annexB = config.ParameterSetsAnnexB();
        Assert.Equal([0, 0, 0, 1, 0x67], annexB[..5]);
        Assert.Equal(2, NalUnits.SplitAnnexB(annexB).Count);
    }

    [Fact]
    public void AnHevcConfigurationRecordFromAMuxerHoldsTheParameterSets()
    {
        var track = VideoTrack(FixturePath("hevc-in-mp4.mp4"));

        var config = HevcConfig.Parse(track.CodecPrivate);
        var info = config.Sequence()!;

        Assert.Equal(CodecId.Hevc, track.Codec);
        Assert.Equal((4, 1), (config.LengthSize, config.Profile));
        Assert.Equal([32, 33, 34], config.Units.Select(u => HevcSps.TypeOf(u[0])).Where(t => t < 35).Order());
        AssertMatchesProbe(info, Probe("hevc-in-mp4.mp4"), interlacedByFields: false);
        Assert.Equal(config.Level, info.Level);
        Assert.Equal(config.Units.Count, NalUnits.SplitAnnexB(config.ParameterSetsAnnexB()).Count);
    }
}
