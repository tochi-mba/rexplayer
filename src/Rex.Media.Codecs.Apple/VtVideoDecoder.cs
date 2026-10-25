using System.Runtime.Versioning;
using Rex.Media.Codecs.H264;
using Rex.Media.Codecs.Hevc;
using Rex.Media.Codecs.Video;
using Rex.Media.Interop.Mac.Media;
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Apple;

/// <summary>
/// One VideoToolbox decoder for an H.264 or HEVC track, producing NV12 pictures, or P010 for
/// streams deeper than 8 bits, in memory, in the stream's own sample range. VideoToolbox takes samples with their length prefixes
/// as MP4 and Matroska store them, so packets go in unchanged; pictures are copied out while the
/// decoder holds them, cropped to the display size, and put in presentation order.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class VtVideoDecoder : IVideoDecoder
{
    /// <summary>The deepest reordering H.264 and HEVC streams use in practice; VideoToolbox's own ordering usually leaves nothing for it to do.</summary>
    private const int ReorderDepth = 4;

    private readonly VtCodec _codec;
    private readonly bool _allowHardware;
    private readonly IReadOnlyList<byte[]> _parameterSets;
    private readonly int _lengthSize;
    private readonly PixelFormat _format;
    private readonly int _displayWidth;
    private readonly int _displayHeight;
    private readonly ColorInfo _color;
    private readonly Rational _aspect;
    private readonly PresentationOrder<VideoFrame> _order = new(ReorderDepth, frame => frame.Pts.IsKnown ? frame.Pts.Ticks : long.MinValue);
    private VtDecodeSession _session;

    public VtVideoDecoder(TrackInfo track, bool allowHardware = true)
    {
        ArgumentNullException.ThrowIfNull(track);
        _allowHardware = allowHardware;
        var video = track.Video ?? throw new MediaFormatException("The track is not video.");
        SequenceInfo? sequence;
        switch (track.Codec)
        {
            case CodecId.H264:
                var avc = AvcConfig.Parse(track.CodecPrivate);
                (_codec, _lengthSize, _parameterSets, sequence) = (VtCodec.H264, avc.LengthSize, [.. avc.SequenceParameterSets, .. avc.PictureParameterSets], avc.Sequence());
                break;
            case CodecId.Hevc:
                var hevc = HevcConfig.Parse(track.CodecPrivate);
                (_codec, _lengthSize, _parameterSets, sequence) = (VtCodec.Hevc, hevc.LengthSize, hevc.ParameterSets(), hevc.Sequence());
                break;
            default:
                throw new MediaFormatException($"VideoToolbox is not asked to decode {track.Codec}.");
        }

        _format = (sequence?.BitDepth ?? 8) > 8 ? PixelFormat.P010 : PixelFormat.Nv12;
        _displayWidth = video.Width;
        _displayHeight = video.Height;
        _color = video.Color != ColorInfo.Unspecified || sequence is null ? video.Color : sequence.Color;
        _aspect = video.PixelAspect;
        _session = Open();
    }

    public string Name => _session.OnHardware ? "VideoToolbox (hardware)" : "VideoToolbox";

    public DecoderSource Source => _session.OnHardware ? DecoderSource.OsHardware : DecoderSource.OsSoftware;

    public void Decode(Packet packet, ICollection<VideoFrame> output)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(output);
        Guard(() => _session.Decode(packet.Data.Span, packet.Pts.IsKnown ? packet.Pts.Ticks : 0, packet.Duration.Ticks, packet.Pts.IsKnown));
        _order.Release(output);
    }

    public bool Drain(ICollection<VideoFrame> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        Guard(_session.Finish);
        _order.Drain(output);
        return true;
    }

    public void Flush()
    {
        // VideoToolbox has no reset: a new session starts clean at the next keyframe.
        _order.Clear(frame => frame.Dispose());
        _session.Dispose();
        _session = Open();
    }

    public void Dispose()
    {
        _order.Clear(frame => frame.Dispose());
        _session.Dispose();
    }

    private VtDecodeSession Open()
    {
        try
        {
            return VtDecodeSession.Open(_codec, _parameterSets, _lengthSize,
                _format == PixelFormat.P010, _color.FullRange, Take, _allowHardware);
        }
        catch (IOException ex)
        {
            throw new MediaFormatException($"VideoToolbox refused the track's format: {ex.Message}", ex);
        }
    }

    private static void Guard(Action decode)
    {
        try
        {
            decode();
        }
        catch (IOException ex)
        {
            throw new MediaFormatException("VideoToolbox could not decode a packet: " + ex.Message, ex);
        }
    }

    /// <summary>Copies one decoded picture out of VideoToolbox, cropped to the display size.</summary>
    private void Take(VtPicture picture)
    {
        var coded = (Width: picture.PlaneWidth(0), Height: picture.PlaneHeight(0));
        var width = Math.Min(_displayWidth > 0 ? _displayWidth : coded.Width, coded.Width);
        var height = Math.Min(_displayHeight > 0 ? _displayHeight : coded.Height, coded.Height);
        var frame = VideoFrame.Rent(_format, width, height);
        for (var plane = 0; plane < 2; plane++)
        {
            var (rowBytes, rows) = _format.PlaneSize(plane, width, height);
            for (var row = 0; row < rows; row++)
            {
                picture.Row(plane, row, rowBytes).CopyTo(frame.Row(plane, row));
            }
        }

        frame.Pts = picture.HasTime ? new MediaTime(picture.PresentationTicks) : MediaTime.Unknown;
        frame.Duration = new MediaTime(picture.DurationTicks);
        frame.Color = _color;
        frame.PixelAspect = _aspect;
        _order.Add(frame);
    }
}
