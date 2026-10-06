using System.Security.Cryptography;
using Rex.Media.AppCore;
using Rex.Media.Codecs;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.Containers;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Windows.Tests;

/// <summary>
/// Windows' H.264 and HEVC decoders, reached through Media Foundation. Decoding these codecs is
/// exact, so every picture must hash as FFmpeg's decode of the same file hashes.
/// </summary>
public sealed class MfVideoDecoderTests
{
    private static readonly MfDecoderFactory Factory = new();

    private static IDemuxer Open(string fixture)
    {
        var source = new MemoryByteSource(File.ReadAllBytes(RepoPaths.Combine($"tests/fixtures/{fixture}")), fixture);
        return MediaRegistries.Demuxers().Probe(source, CancellationToken.None)!.Open(source, CancellationToken.None);
    }

    /// <summary>The MD5 of each picture FFmpeg decoded, in presentation order.</summary>
    private static List<string> ReferenceHashes(string fixture) => File.ReadAllLines(RepoPaths.Combine($"tests/fixtures/{Path.ChangeExtension(fixture, ".nv12.framemd5")}"))
        .Where(line => !line.StartsWith('#'))
        .Select(line => line.Split(',')[^1].Trim())
        .ToList();

    /// <summary>The MD5 of a picture's planes, rows without padding, as framemd5 hashes raw NV12.</summary>
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

    private static (List<string> Hashes, List<MediaTime> Times, TrackInfo Track) DecodeAll(string fixture)
    {
        using var demuxer = Open(fixture);
        var track = demuxer.Info.FirstTrack(MediaKind.Video)!;
        if (!Factory.CanDecode(track))
        {
            Assert.Skip($"This Windows has no Media Foundation {track.Codec} decoder (HEVC needs the HEVC Video Extensions from the Microsoft Store).");
        }

        using var decoder = MediaRegistries.Decoders(Factory).CreateVideo(track).Decoder!;
        var frames = new List<VideoFrame>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            if (packet.TrackId == track.Id)
            {
                decoder.Decode(packet, frames);
            }

            packet.Dispose();
        }

        while (!decoder.Drain(frames))
        {
        }
        var result = (frames.Select(Hash).ToList(), frames.Select(f => f.Pts).ToList(), track);
        frames.ForEach(f => f.Dispose());
        return result;
    }

    [Theory]
    [InlineData("mp4/h264-aac.mp4")]
    [InlineData("mkv/h264-aac-subtitles.mkv")]
    [InlineData("video/hevc-in-mp4.mp4")]
    [Capability("FMT-V01")]
    [Capability("FMT-V02")]
    public void EveryPictureMatchesAnIndependentDecoderExactly(string fixture)
    {
        var (hashes, times, _) = DecodeAll(fixture);

        Assert.Equal(ReferenceHashes(fixture), hashes);
        Assert.Equal(times.Order(), times);
    }

    [Fact]
    public void PicturesComeOutInPresentationOrderAtTheirTimes()
    {
        var (_, times, track) = DecodeAll("mp4/h264-aac.mp4");

        Assert.Equal(Enumerable.Range(0, 10).Select(i => MediaTime.FromSeconds(i * 0.04)), times);
        Assert.Equal(CodecId.H264, track.Codec);
    }

    [Fact]
    public void AFlushedDecoderStartsAgainAtTheNextKeyframe()
    {
        using var demuxer = Open("mp4/h264-aac.mp4");
        var track = demuxer.Info.FirstTrack(MediaKind.Video)!;
        if (!Factory.CanDecode(track))
        {
            Assert.Skip("This Windows has no Media Foundation H.264 decoder.");
        }

        using var decoder = new MfVideoDecoder(track);
        var frames = new List<VideoFrame>();
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            if (packet.TrackId == track.Id && packet.Pts < MediaTime.FromSeconds(0.1))
            {
                decoder.Decode(packet, frames);
            }

            packet.Dispose();
        }

        decoder.Flush();
        frames.ForEach(f => f.Dispose());
        frames.Clear();
        demuxer.Seek(MediaTime.FromSeconds(0.2), CancellationToken.None);
        while (demuxer.ReadPacket(CancellationToken.None) is { } packet)
        {
            if (packet.TrackId == track.Id)
            {
                decoder.Decode(packet, frames);
            }

            packet.Dispose();
        }

        while (!decoder.Drain(frames))
        {
        }
        var hashes = frames.Select(Hash).ToList();
        frames.ForEach(f => f.Dispose());

        Assert.Equal(ReferenceHashes("mp4/h264-aac.mp4")[5..], hashes);
        Assert.Equal(DecoderSource.OsSoftware, decoder.Source);
        Assert.False(string.IsNullOrEmpty(decoder.Name));
    }

    [Fact]
    public void TracksWindowsIsNotAskedAboutAreRefused()
    {
        var vp8 = new TrackInfo { Id = 1, Codec = CodecId.Vp8, Video = new VideoTrackInfo { Width = 16, Height = 16 } };
        var audio = new TrackInfo { Id = 1, Codec = CodecId.H264 };

        Assert.False(Factory.CanDecode(vp8));
        Assert.Throws<MediaFormatException>(() => new MfVideoDecoder(vp8));
        Assert.Throws<MediaFormatException>(() => new MfVideoDecoder(audio));
        Assert.Throws<NotSupportedException>(() => ((IDecoderFactory)new AudioOnlyFactory()).CreateVideo(vp8));
    }

    private sealed class AudioOnlyFactory : IDecoderFactory
    {
        public string Name => "audio only";

        public DecoderSource Source => DecoderSource.Own;

        public int Rank => 1;

        public bool CanDecode(TrackInfo track) => false;

        public IAudioDecoder CreateAudio(TrackInfo track) => throw new NotSupportedException();
    }
}
