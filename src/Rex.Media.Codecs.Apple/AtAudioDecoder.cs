using System.Runtime.Versioning;
using Rex.Media.Interop.Mac.Audio;
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Apple;

/// <summary>
/// AAC and Opus through AudioToolbox, the Mac's counterpart of Windows' Media Foundation audio
/// decoders (ADR-0009): raw packets as MP4, Matroska and Ogg store them in, planar float frames
/// out, at the rate and channel count <see cref="AudioToolboxSetup"/> decides for the track.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class AtAudioDecoder : IAudioDecoder
{
    private readonly AudioToolboxSetup _setup;
    private readonly CompressedAudioConverter _converter;
    private readonly float[] _interleaved;
    private MediaTime _next = MediaTime.Unknown;

    public AtAudioDecoder(TrackInfo track)
    {
        _setup = AudioToolboxSetup.For(track);
        _interleaved = new float[_setup.LongestPacket * _setup.Channels * 2];
        var format = _setup.Format == AudioToolboxFormat.Opus ? CompressedAudioConverter.FormatOpus : CompressedAudioConverter.FormatAac;
        try
        {
            _converter = CompressedAudioConverter.Open(format, _setup.CodedRate, _setup.OutputRate, _setup.Channels, _setup.FramesPerPacket, _setup.Cookie);
        }
        catch (IOException ex)
        {
            throw new MediaFormatException($"AudioToolbox refused the {track.Codec} track: {ex.Message}", ex);
        }
    }

    public string Name => "AudioToolbox " + _setup.Format;

    public DecoderSource Source => DecoderSource.OsSoftware;

    public void Decode(Packet packet, ICollection<AudioFrame> output)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(output);
        int frames;
        try
        {
            frames = _converter.Decode(packet.Data.Span, _interleaved, _setup.SamplesIn(packet.Data.Span));
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
        var frame = AudioFrame.Rent(_setup.OutputRate, channels, frames);
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
