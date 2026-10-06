using System.Runtime.InteropServices;
using Rex.Media.Codecs.H264;
using Rex.Media.Codecs.Hevc;
using Rex.Media.Codecs.Video;
using Rex.Media.Interop.MediaFoundation;
using Rex.Media.Primitives;
using Windows.Win32;

namespace Rex.Media.Codecs.MediaFoundation;

/// <summary>
/// One Media Foundation video decoder transform (H.264 or HEVC), producing NV12 pictures, or P010
/// for streams deeper than 8 bits. Windows' decoders take Annex B, so samples stored with length
/// prefixes (MP4, Matroska) are rewritten, with the parameter sets put in front of each keyframe.
/// </summary>
public sealed class MfVideoDecoder : IVideoDecoder
{
    private readonly MfTransform _transform;
    private readonly int _lengthSize;
    private readonly byte[] _parameterSets;
    private readonly int _displayWidth;
    private readonly int _displayHeight;
    private readonly PixelFormat _format;
    private readonly Guid _outputSubtype;
    private readonly ColorInfo _color;
    private readonly Rational _aspect;
    private int _codedWidth;
    private int _codedHeight;
    private int _stride;

    public MfVideoDecoder(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var video = track.Video ?? throw new MediaFormatException("The track is not video.");
        var subtype = MfDecoderFactory.VideoSubtype(track.Codec) ?? throw new MediaFormatException($"Windows is not asked to decode {track.Codec}.");
        SequenceInfo? sequence = null;
        if (track.CodecPrivate.Length > 0)
        {
            if (track.Codec == CodecId.H264)
            {
                var config = AvcConfig.Parse(track.CodecPrivate);
                (_lengthSize, _parameterSets, sequence) = (config.LengthSize, config.ParameterSetsAnnexB(), config.Sequence());
            }
            else
            {
                var config = HevcConfig.Parse(track.CodecPrivate);
                (_lengthSize, _parameterSets, sequence) = (config.LengthSize, config.ParameterSetsAnnexB(), config.Sequence());
            }
        }
        else
        {
            _parameterSets = [];
        }

        _displayWidth = video.Width;
        _displayHeight = video.Height;
        _color = video.Color != ColorInfo.Unspecified || sequence is null ? video.Color : sequence.Color;
        _aspect = video.PixelAspect;
        (_format, _outputSubtype) = (sequence?.BitDepth ?? 8) > 8 ? (PixelFormat.P010, PInvoke.MFVideoFormat_P010) : (PixelFormat.Nv12, PInvoke.MFVideoFormat_NV12);
        _transform = MfTransform.Create(PInvoke.MFT_CATEGORY_VIDEO_DECODER, PInvoke.MFMediaType_Video, subtype)
            ?? throw new MediaFormatException($"Windows has no decoder for {track.Codec}.");
        try
        {
            _transform.SetInputType(new Dictionary<Guid, object>
            {
                [PInvoke.MF_MT_MAJOR_TYPE] = PInvoke.MFMediaType_Video,
                [PInvoke.MF_MT_SUBTYPE] = subtype,
                [PInvoke.MF_MT_FRAME_SIZE] = ((ulong)(uint)video.Width << 32) | (uint)video.Height,
            });
            ChooseOutput();
            _transform.Begin();
        }
        catch (COMException ex)
        {
            _transform.Dispose();
            throw new MediaFormatException($"The Windows {track.Codec} decoder refused the track's format (0x{ex.HResult:X8}).", ex);
        }
    }

    public string Name => _transform.Name;

    public DecoderSource Source => DecoderSource.OsSoftware;

    public void Decode(Packet packet, ICollection<VideoFrame> output)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(output);
        var data = _lengthSize > 0
            ? NalUnits.ToAnnexB(packet.Data.Span, _lengthSize, packet.IsKeyframe ? _parameterSets : default)
            : packet.Data.Span.ToArray();
        var time = packet.Pts.IsKnown ? packet.Pts.Ticks : long.MinValue;
        try
        {
            while (!_transform.ProcessInput(data, time, packet.Duration.Ticks, packet.IsKeyframe, discontinuity: false))
            {
                Collect(output);
            }

            Collect(output);
        }
        catch (COMException ex)
        {
            throw new MediaFormatException($"The Windows decoder could not decode a packet (0x{ex.HResult:X8}).", ex);
        }
    }

    public void Drain(ICollection<VideoFrame> output)
    {
        _transform.Drain();
        Collect(output);
        _transform.Begin();
    }

    public void Flush() => _transform.Flush();

    public void Dispose() => _transform.Dispose();

    private void ChooseOutput()
    {
        if (!_transform.SetOutputType(type => type.Subtype == _outputSubtype))
        {
            throw new MediaFormatException($"The Windows decoder offers no {_format} output.");
        }

        (_codedWidth, _codedHeight, _stride) = _transform.ReadOutputType(type =>
        {
            var size = type.UInt64(PInvoke.MF_MT_FRAME_SIZE) ?? 0;
            var width = (int)(size >> 32);
            var stride = (int)(type.UInt32(PInvoke.MF_MT_DEFAULT_STRIDE) ?? 0);
            return (width, (int)(uint)size, stride > 0 ? stride : width * (_format.StorageBits() / 8));
        });
    }

    /// <summary>Takes every picture the transform has ready, cropped to the display size.</summary>
    private void Collect(ICollection<VideoFrame> output)
    {
        while (true)
        {
            switch (_transform.ProcessOutput(out var sample))
            {
                case MfOutputStatus.NeedMoreInput:
                    return;
                case MfOutputStatus.FormatChanged:
                    ChooseOutput();
                    continue;
            }

            var width = Math.Min(_displayWidth > 0 ? _displayWidth : _codedWidth, _codedWidth);
            var height = Math.Min(_displayHeight > 0 ? _displayHeight : _codedHeight, _codedHeight);
            if (sample.Data.Length < _stride * _codedHeight * 3 / 2)
            {
                throw new MediaFormatException("The Windows decoder returned a picture smaller than its format.");
            }

            var frame = VideoFrame.Rent(_format, width, height);
            for (var plane = 0; plane < 2; plane++)
            {
                var (rowBytes, rows) = _format.PlaneSize(plane, width, height);
                var start = plane == 0 ? 0 : _stride * _codedHeight;
                for (var row = 0; row < rows; row++)
                {
                    sample.Data.AsSpan(start + (row * _stride), rowBytes).CopyTo(frame.Row(plane, row));
                }
            }

            frame.Pts = sample.HasTime ? new MediaTime(sample.Time) : MediaTime.Unknown;
            frame.Duration = new MediaTime(sample.Duration);
            frame.Color = _color;
            frame.PixelAspect = _aspect;
            output.Add(frame);
        }
    }
}
