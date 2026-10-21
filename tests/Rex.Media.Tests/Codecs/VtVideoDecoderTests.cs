using System.Security.Cryptography;
using Rex.Media.AppCore;
using Rex.Media.Codecs.Apple;
using Rex.Media.Containers;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Codecs;

/// <summary>
/// The Mac's H.264 and HEVC decoders, reached through VideoToolbox (ADR-0021). Decoding these
/// codecs is exact, so every picture must hash as the independent decode of the same file
/// hashes: the very hashes Windows' decoders are held to, which is what makes the two platforms
/// show the same pictures (ADR-0020). They run only on a Mac and skip elsewhere.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public sealed class VtVideoDecoderTests
{
    private static readonly AppleDecoderFactory Factory = new();

    private static IDemuxer Open(string fixture)
    {
        var source = new MemoryByteSource(File.ReadAllBytes(RepoPaths.Combine($"tests/fixtures/{fixture}")), fixture);
        return MediaRegistries.Demuxers().Probe(source, CancellationToken.None)!.Open(source, CancellationToken.None);
    }

    /// <summary>The MD5 of each picture the independent decoder made, in presentation order.</summary>
    private static List<string> ReferenceHashes(string fixture, PixelFormat format) =>
        [.. File.ReadAllLines(RepoPaths.Combine($"tests/fixtures/{Path.ChangeExtension(fixture, format == PixelFormat.P010 ? ".p010.framemd5" : ".nv12.framemd5")}"))
            .Where(line => !line.StartsWith('#'))
            .Select(line => line.Split(',')[^1].Trim())];

    /// <summary>The MD5 of a picture's planes, rows without padding, as framemd5 hashes them.</summary>
    private static string Hash(VideoFrame frame)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        for (var plane = 0; plane < frame.Format.PlaneCount(); plane++)
        {
            var (_, rows) = frame.Format.PlaneSize(plane, frame.Width, frame.Height);
            for (var row = 0; row < rows; row++)
            {
                md5.AppendData(frame.Row(plane, row));
            }
        }

        return Convert.ToHexStringLower(md5.GetHashAndReset());
    }

    private static (List<string> Hashes, List<MediaTime> Times, PixelFormat Format) DecodeAll(string fixture, MediaTime? stopAt = null, bool flushThenRestart = false)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "VideoToolbox exists only on a Mac.");
        using var demuxer = Open(fixture);
        var track = demuxer.Info.FirstTrack(MediaKind.Video)!;
        Assert.True(Factory.CanDecode(track), $"VideoToolbox should take {fixture}'s {track.Codec}.");
        using var decoder = Factory.CreateVideo(track);
        var frames = new List<VideoFrame>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            if (packet.TrackId == track.Id && (stopAt is null || packet.Pts < stopAt))
            {
                decoder.Decode(packet, frames);
            }

            packet.Dispose();
        }

        if (flushThenRestart)
        {
            decoder.Flush();
            frames.ForEach(frame => frame.Dispose());
            frames.Clear();
            demuxer.Seek(MediaTime.Zero, CancellationToken.None);
            while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
            {
                if (packet.TrackId == track.Id)
                {
                    decoder.Decode(packet, frames);
                }

                packet.Dispose();
            }
        }

        Assert.True(decoder.Drain(frames));
        Assert.NotEmpty(frames);
        var result = (frames.Select(Hash).ToList(), frames.Select(frame => frame.Pts).ToList(), frames[0].Format);
        frames.ForEach(frame => frame.Dispose());
        return result;
    }

    [Theory]
    [InlineData("mp4/h264-aac.mp4")]
    [InlineData("mkv/h264-aac-subtitles.mkv")]
    [InlineData("video/hevc-in-mp4.mp4")]
    [Capability("FMT-V01", CapabilityPlatform.MacOS)]
    [Capability("FMT-V02", CapabilityPlatform.MacOS)]
    public void EveryPictureMatchesTheSameIndependentDecodeWindowsIsHeldTo(string fixture)
    {
        var (hashes, times, format) = DecodeAll(fixture);

        Assert.Equal(PixelFormat.Nv12, format);
        Assert.Equal(ReferenceHashes(fixture, format), hashes);
        Assert.Equal(times.Order(), times);
    }

    [Fact]
    [Capability("FMT-V02", CapabilityPlatform.MacOS)]
    public void Main10HevcComesOutAsP010()
    {
        var (hashes, times, format) = DecodeAll("video/hevc-main10-hdr.mp4");

        Assert.Equal(PixelFormat.P010, format);
        Assert.Equal(ReferenceHashes("video/hevc-main10-hdr.mp4", PixelFormat.P010), hashes);
        Assert.Equal(times.Order(), times);
    }

    [Fact]
    public void PicturesComeOutInPresentationOrderAtTheirTimes()
    {
        var (_, times, _) = DecodeAll("mp4/h264-aac.mp4");

        Assert.Equal(Enumerable.Range(0, 10).Select(i => MediaTime.FromSeconds(i * 0.04)), times);
    }

    [Fact]
    public void AFlushedDecoderStartsCleanAndDecodesTheWholeStreamAgain()
    {
        var (hashes, _, format) = DecodeAll("mp4/h264-aac.mp4", stopAt: MediaTime.FromSeconds(0.1), flushThenRestart: true);

        Assert.Equal(ReferenceHashes("mp4/h264-aac.mp4", format), hashes);
    }

    [Fact]
    public void OnlyMacsWithVideoThatCarriesItsParameterSetsAreTaken()
    {
        var h264 = new TrackInfo { Id = 1, Codec = CodecId.H264, CodecPrivate = [1], Video = new VideoTrackInfo { Width = 2, Height = 2 } };

        Assert.Equal(OperatingSystem.IsMacOS(), Factory.CanDecode(h264));
        Assert.False(Factory.CanDecode(h264 with { CodecPrivate = [] }));
        Assert.False(Factory.CanDecode(h264 with { Codec = CodecId.Vp9 }));
        Assert.Throws<NotSupportedException>(() => Factory.CreateAudio(h264));
        Assert.Equal(("macOS VideoToolbox", 50), (Factory.Name, Factory.Rank));
    }
}
