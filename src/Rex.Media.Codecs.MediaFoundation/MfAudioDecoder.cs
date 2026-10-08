using System.Runtime.InteropServices;
using Rex.Media.Codecs.Aac;
using Rex.Media.Interop.MediaFoundation;
using Rex.Media.Primitives;
using Windows.Win32;

namespace Rex.Media.Codecs.MediaFoundation;

/// <summary>One Media Foundation audio decoder transform, producing float frames.</summary>
public sealed class MfAudioDecoder : IAudioDecoder
{
    private readonly MfTransform _transform;
    private int _rate;
    private int _channels;
    private MediaTime _next = MediaTime.Unknown;

    public MfAudioDecoder(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var audio = track.Audio ?? throw new MediaFormatException("The track is not audio.");
        var subtype = MfDecoderFactory.AudioSubtype(track.Codec) ?? throw new MediaFormatException($"Windows is not asked to decode {track.Codec}.");
        _transform = MfTransform.Create(PInvoke.MFT_CATEGORY_AUDIO_DECODER, PInvoke.MFMediaType_Audio, subtype)
            ?? throw new MediaFormatException($"Windows has no decoder for {track.Codec}.");
        try
        {
            _transform.SetInputType(InputType(track, audio, subtype));
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

    /// <summary>
    /// The input media type. AAC gets its AudioSpecificConfig behind the 12 bytes of HEAACWAVEINFO
    /// that follow WAVEFORMATEX, raw payloads (no ADTS), as the AAC decoder documentation lays out.
    /// </summary>
    internal static Dictionary<Guid, object> InputType(TrackInfo track, AudioTrackInfo audio, Guid subtype)
    {
        var rate = audio.SampleRate;
        var channels = audio.Channels;
        var attributes = new Dictionary<Guid, object>
        {
            [PInvoke.MF_MT_MAJOR_TYPE] = PInvoke.MFMediaType_Audio,
            [PInvoke.MF_MT_SUBTYPE] = subtype,
        };
        if (track.Codec == CodecId.Aac)
        {
            if (AacConfig.TryParse(track.CodecPrivate, out var config))
            {
                rate = config.SampleRate;
                channels = config.Channels > 0 ? config.Channels : channels;
            }

            attributes[PInvoke.MF_MT_AAC_PAYLOAD_TYPE] = 0u;
            attributes[PInvoke.MF_MT_USER_DATA] = (byte[])[0, 0, 0xFE, 0, 0, 0, 0, 0, 0, 0, 0, 0, .. track.CodecPrivate];
        }
        else if (track.Codec == CodecId.Opus && track.CodecPrivate.Length > 0)
        {
            // The Opus identification header (OpusHead), as Ogg and Matroska carry it, decodes at 48 kHz.
            // Its pre-skip is given as none: the packets' times already put those samples before zero,
            // where playback leaves them out, and Windows would leave them out a second time.
            rate = 48000;
            var head = track.CodecPrivate.ToArray();
            if (head.Length >= 12)
            {
                head[10] = head[11] = 0;
            }

            attributes[PInvoke.MF_MT_USER_DATA] = head;
        }

        attributes[PInvoke.MF_MT_AUDIO_SAMPLES_PER_SECOND] = (uint)Math.Max(1, rate);
        attributes[PInvoke.MF_MT_AUDIO_NUM_CHANNELS] = (uint)Math.Max(1, channels);
        return attributes;
    }

    public void Decode(Packet packet, ICollection<AudioFrame> output)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(output);
        if (!_next.IsKnown && packet.Pts.IsKnown)
        {
            _next = packet.Pts;
        }

        var time = packet.Pts.IsKnown ? packet.Pts.Ticks : long.MinValue;
        try
        {
            while (!_transform.ProcessInput(packet.Data.Span, time, packet.Duration.Ticks, packet.IsKeyframe, discontinuity: false))
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

    public void Drain(ICollection<AudioFrame> output)
    {
        _transform.Drain();
        Collect(output);
        _transform.Begin();
    }

    public void Flush()
    {
        _transform.Flush();
        _next = MediaTime.Unknown;
    }

    public void Dispose() => _transform.Dispose();

    private void ChooseOutput()
    {
        if (!_transform.SetOutputType(type => type.Subtype == PInvoke.MFAudioFormat_Float))
        {
            throw new MediaFormatException("The Windows decoder offers no floating-point output.");
        }

        (_rate, _channels) = _transform.ReadOutputType(type => (
            (int)(type.UInt32(PInvoke.MF_MT_AUDIO_SAMPLES_PER_SECOND) ?? 0),
            (int)(type.UInt32(PInvoke.MF_MT_AUDIO_NUM_CHANNELS) ?? 0)));
        if (_rate <= 0 || _channels <= 0)
        {
            throw new MediaFormatException("The Windows decoder's output format has no rate or channel count.");
        }
    }

    /// <summary>Takes every sample the transform has ready, as planar float frames.</summary>
    private void Collect(ICollection<AudioFrame> output)
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

            var samples = sample.Data.Length / sizeof(float) / _channels;
            if (samples == 0)
            {
                continue;
            }

            var frame = AudioFrame.Rent(_rate, _channels, samples);
            var interleaved = MemoryMarshal.Cast<byte, float>(sample.Data.AsSpan(0, samples * _channels * sizeof(float)));
            for (var c = 0; c < _channels; c++)
            {
                var plane = frame.Channel(c);
                for (var i = 0; i < samples; i++)
                {
                    plane[i] = interleaved[(i * _channels) + c];
                }
            }

            frame.Pts = sample.HasTime ? new MediaTime(sample.Time) : _next;
            _next = frame.Pts.IsKnown ? frame.Pts + frame.Duration : MediaTime.Unknown;
            output.Add(frame);
        }
    }
}
