using System.Runtime.Versioning;
using Rex.Media.Codecs.Aac;
using Rex.Media.Interop.Mac.Audio;
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Apple;

/// <summary>
/// AAC through AudioToolbox, the Mac's counterpart of Windows' Media Foundation AAC decoder
/// (ADR-0009): raw packets as MP4 and Matroska store them in, planar float frames out, at the
/// rate and channel count the stream's AudioSpecificConfig gives once SBR and PS are applied.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class AtAacDecoder : IAudioDecoder
{
    private readonly AacConverter _converter;
    private readonly int _rate;
    private readonly float[] _interleaved;
    private MediaTime _next = MediaTime.Unknown;

    public AtAacDecoder(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (track.Codec != CodecId.Aac || !AacConfig.TryParse(track.CodecPrivate, out var config))
        {
            throw new MediaFormatException("The track is not AAC with its configuration.");
        }

        var channels = config.Ps ? 2 : Math.Max(config.Channels, track.Audio?.Channels ?? 0);
        if (channels <= 0)
        {
            throw new MediaFormatException("The AAC configuration names no channel count.");
        }

        _rate = config.OutputSampleRate > 0 ? config.OutputSampleRate : config.SampleRate;
        var framesPerPacket = config.FrameLength * (_rate > config.SampleRate ? 2 : 1);
        _interleaved = new float[framesPerPacket * channels * 2];
        try
        {
            _converter = AacConverter.Open(config.SampleRate, _rate, channels, framesPerPacket, EsDescriptor.ForAac(track.CodecPrivate));
        }
        catch (IOException ex)
        {
            throw new MediaFormatException("AudioToolbox refused the AAC track: " + ex.Message, ex);
        }
    }

    public string Name => "AudioToolbox AAC";

    public DecoderSource Source => DecoderSource.OsSoftware;

    public void Decode(Packet packet, ICollection<AudioFrame> output)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(output);
        int frames;
        try
        {
            frames = _converter.Decode(packet.Data.Span, _interleaved);
        }
        catch (IOException ex)
        {
            throw new MediaFormatException(ex.Message, ex);
        }

        if (packet.Pts.IsKnown)
        {
            _next = packet.Pts;
        }

        if (frames == 0)
        {
            return;
        }

        var channels = _converter.Channels;
        var frame = AudioFrame.Rent(_rate, channels, frames);
        for (var c = 0; c < channels; c++)
        {
            var plane = frame.Channel(c);
            for (var i = 0; i < frames; i++)
            {
                plane[i] = _interleaved[(i * channels) + c];
            }
        }

        frame.Pts = _next;
        _next = frame.Pts.IsKnown ? frame.Pts + frame.Duration : MediaTime.Unknown;
        output.Add(frame);
    }

    /// <summary>AudioToolbox gives each packet's samples as it decodes it, so nothing is held back at the end.</summary>
    public void Drain(ICollection<AudioFrame> output) => ArgumentNullException.ThrowIfNull(output);

    public void Flush()
    {
        _next = MediaTime.Unknown;
        try
        {
            _converter.Reset();
        }
        catch (IOException ex)
        {
            throw new MediaFormatException(ex.Message, ex);
        }
    }

    public void Dispose() => _converter.Dispose();
}
