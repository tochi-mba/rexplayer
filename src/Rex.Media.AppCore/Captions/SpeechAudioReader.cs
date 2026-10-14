using Rex.Media.AppCore.Transcription;
using Rex.Media.Codecs;
using Rex.Media.Containers;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Captions;

/// <summary>
/// A file's sound read a stretch at a time for a speech model (SUB-21), on a demuxer and decoder of
/// its own, never playback's. The first audio track is decoded, every channel averaged, and brought
/// to 16 kHz. Reading on from where the last stretch ended (the usual case, pieces overlapping by a
/// few seconds) keeps decoding without a seek; any other stretch seeks to it. Only the last few
/// seconds decoded are kept, so a film of any length needs the memory of one piece.
/// </summary>
public sealed class SpeechAudioReader : ISpeechSource
{
    /// <summary>A stretch starting this far past what was decoded is reached by a seek rather than by reading on.</summary>
    private static readonly TimeSpan ReadOnRather = TimeSpan.FromSeconds(10);

    private readonly FileByteSource _source;
    private readonly IDemuxer _demuxer;
    private readonly IAudioDecoder? _decoder;
    private readonly int _trackId;
    private readonly List<AudioFrame> _frames = [];
    private float[] _carry = new float[1 << 16];
    private int _carried;
    private TimeSpan _carryStart;
    private TimeSpan _soughtTo;
    private int _rate;

    /// <summary>Opens <paramref name="path"/>; a file with no sound, or none that can be decoded, opens with <see cref="HasSound"/> false.</summary>
    public SpeechAudioReader(string path, DecoderRegistry decoders, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(decoders);
        _source = new FileByteSource(path);
        try
        {
            _demuxer = MediaRegistries.Demuxers().Open(_source, cancellationToken);
        }
        catch
        {
            _source.Dispose();
            throw;
        }

        if (_demuxer.Info.FirstTrack(MediaKind.Audio) is { } track && decoders.CreateAudio(track).Decoder is { } decoder)
        {
            (_trackId, _decoder) = (track.Id, decoder);
        }

        Duration = _demuxer.Info.Duration.IsKnown ? _demuxer.Info.Duration.ToTimeSpan() : null;
    }

    /// <summary>Whether there is sound to hear.</summary>
    public bool HasSound => _decoder is not null;

    /// <summary>The file's length, when its container says.</summary>
    public TimeSpan? Duration { get; }

    /// <summary>Whether the sound ended before the end of the last stretch asked for.</summary>
    public bool Ended { get; private set; }

    /// <summary>The time just after the last sample decoded.</summary>
    private TimeSpan DecodedTo => _carryStart + TimeSpan.FromSeconds((double)_carried / Math.Max(1, _rate));

    /// <summary>The sound of <paramref name="range"/>, mono at 16 kHz; shorter when the sound ends first, empty without sound.</summary>
    public float[] Read(TimeRange range, CancellationToken cancellationToken)
    {
        if (_decoder is null || range.End <= range.Start)
        {
            return [];
        }

        if (_rate == 0 || range.Start < _carryStart || range.Start > DecodedTo + ReadOnRather)
        {
            _demuxer.Seek(new MediaTime(range.Start.Ticks), cancellationToken);
            _decoder.Flush();
            (_carried, _rate, Ended, _soughtTo) = (0, 0, false, range.Start);
        }

        while (!Ended && (_rate == 0 || DecodedTo < range.End))
        {
            if (_demuxer.ReadPacket(cancellationToken) is not { } packet)
            {
                _decoder.Drain(_frames);
                Ended = true;
            }
            else
            {
                using (packet)
                {
                    if (packet.TrackId == _trackId)
                    {
                        _decoder.Decode(packet, _frames);
                    }
                }
            }

            Take();
            if (_rate > 0)
            {
                // What comes before the stretch asked for (from the keyframe a seek lands on) is not kept.
                Drop(Index(range.Start));
            }
        }

        var from = Math.Clamp(Index(range.Start), 0, _carried);
        var to = Math.Clamp(Index(range.End), from, _carried);
        var heard = _rate == 0 ? [] : SpeechAudio.Resample(_carry.AsSpan(from, to - from), _rate);

        // The next piece starts a little before this one ended: keep that much.
        Drop(Index(range.End - (ChunkPlanner.Context * 2)));
        return heard;
    }

    public void Dispose()
    {
        _decoder?.Dispose();
        _demuxer.Dispose();
        _source.Dispose();
    }

    /// <summary>The sample of the kept sound at <paramref name="time"/>.</summary>
    private int Index(TimeSpan time) => (int)Math.Round((time - _carryStart).TotalSeconds * _rate);

    /// <summary>Forgets the first <paramref name="count"/> samples kept.</summary>
    private void Drop(int count)
    {
        count = Math.Clamp(count, 0, _carried);
        if (count == 0)
        {
            return;
        }

        Array.Copy(_carry, count, _carry, 0, _carried - count);
        _carried -= count;
        _carryStart += TimeSpan.FromSeconds((double)count / _rate);
    }

    /// <summary>Adds the frames decoded to the kept sound, every channel averaged.</summary>
    private void Take()
    {
        foreach (var frame in _frames)
        {
            using (frame)
            {
                if (_rate == 0)
                {
                    // A frame without a time is taken to start where the seek went.
                    (_rate, _carryStart) = (frame.SampleRate, frame.Pts.IsKnown ? frame.Pts.ToTimeSpan() : _soughtTo);
                }

                var count = frame.SampleCount;
                if (_carried + count > _carry.Length)
                {
                    Array.Resize(ref _carry, Math.Max(_carry.Length * 2, _carried + count));
                }

                // Channel by channel, so each is read as one span.
                var mono = _carry.AsSpan(_carried, count);
                frame.Channel(0)[..count].CopyTo(mono);
                for (var channel = 1; channel < frame.Channels; channel++)
                {
                    var samples = frame.Channel(channel);
                    for (var i = 0; i < count; i++)
                    {
                        mono[i] += samples[i];
                    }
                }

                if (frame.Channels > 1)
                {
                    var scale = 1f / frame.Channels;
                    for (var i = 0; i < count; i++)
                    {
                        mono[i] *= scale;
                    }
                }

                _carried += count;
            }
        }

        _frames.Clear();
    }
}
