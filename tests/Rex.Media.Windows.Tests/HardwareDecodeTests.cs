using System.Runtime.Versioning;
using Rex.Media.AppCore;
using Rex.Media.Codecs;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.Containers;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Video.D3D11;

namespace Rex.Media.Windows.Tests;

/// <summary>
/// Windows' decoder on the graphics card, sharing the presenter's device so pictures go from decoder
/// to screen without leaving the card. Skipped where there is no graphics card (CI's runners).
/// </summary>
[SupportedOSPlatform("windows8.0")]
public sealed class HardwareDecodeTests
{
    private static IDemuxer Open(string fixture)
    {
        var source = new MemoryByteSource(File.ReadAllBytes(RepoPaths.Combine($"tests/fixtures/{fixture}")), fixture);
        return MediaRegistries.Demuxers().Probe(source, CancellationToken.None)!.Open(source, CancellationToken.None);
    }

    [Fact(Timeout = 60_000)]
    [Capability("VID-01")]
    public async Task PicturesDecodedOnTheCardAreDrawnAsTheSoftwareDecoderDrawsThem()
    {
        await Task.Yield();
        using var hardware = D3D11Presenter.Offscreen(128, 72, software: false);
        if (hardware.Name.Contains("software", StringComparison.Ordinal))
        {
            Assert.Skip("There is no graphics card here.");
        }

        using var demuxer = Open("mp4/h264-aac.mp4");
        var track = demuxer.Info.FirstTrack(MediaKind.Video)!;
        using var decoder = new MfVideoDecoder(track, hardware.Gpu);
        if (decoder.Source != DecoderSource.OsHardware)
        {
            Assert.Skip("Windows' decoder would not decode on this graphics card.");
        }

        using var software = D3D11Presenter.Offscreen(128, 72, software: true);
        using var reference = new MfVideoDecoder(track);
        var frames = new List<VideoFrame>();
        var references = new List<VideoFrame>();
        var compared = 0;
        while (demuxer.ReadPacket(TestContext.Current.CancellationToken) is { } packet)
        {
            if (packet.TrackId == track.Id)
            {
                decoder.Decode(packet, frames);
                reference.Decode(packet, references);
            }

            packet.Dispose();
            compared += Compare(frames, references, hardware, software);
        }

        while (!decoder.Drain(frames))
        {
        }
        while (!reference.Drain(references))
        {
        }
        compared += Compare(frames, references, hardware, software);

        Assert.Equal(10, compared);
    }

    /// <summary>Draws each pair (card and software) and checks they match; returns how many pairs were compared.</summary>
    private static int Compare(List<VideoFrame> frames, List<VideoFrame> references, D3D11Presenter hardware, D3D11Presenter software)
    {
        var count = 0;
        while (frames.Count > 0 && references.Count > 0)
        {
            using var frame = frames[0];
            using var expected = references[0];
            frames.RemoveAt(0);
            references.RemoveAt(0);
            Assert.NotNull(frame.Surface);
            Assert.Equal(expected.Pts, frame.Pts);
            hardware.SmoothChroma = software.SmoothChroma = false;
            hardware.Present(frame);
            software.Present(expected);
            using var drawn = hardware.ReadBack();
            using var wanted = software.ReadBack();
            for (var y = 0; y < drawn.Height; y++)
            {
                var (row, other) = (drawn.Row(0, y).ToArray(), wanted.Row(0, y).ToArray());
                Assert.True(row.Zip(other).All(pair => Math.Abs(pair.First - pair.Second) <= 2), $"picture at {frame.Pts}, row {y} differs");
            }

            count++;
        }

        return count;
    }
}
