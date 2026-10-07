using Rex.Media.Codecs;
using Rex.Media.Primitives;

namespace Rex.Media.Engine;

/// <summary>
/// Sound for media that has none. The engine keeps time by the audio output, so a video with no
/// sound plays against a track of silence: the demux thread writes short silent packets alongside
/// the pictures, and <see cref="SilenceDecoder"/> turns each into a frame of zeros. Pausing,
/// seeking, the position and the end then work exactly as they do for any other media.
/// </summary>
internal static class SilentAudio
{
    /// <summary>No container numbers a track below one, so this never meets a real track.</summary>
    public const int TrackId = -1;

    public const int SampleRate = 48_000;

    /// <summary>
    /// The length of each silent packet. Silence is queued up to the end of each picture as it is
    /// read, and reading runs ahead of what is shown, so the pictures never wait for sound.
    /// </summary>
    public static readonly MediaTime Chunk = MediaTime.FromMilliseconds(20);

    public static TrackInfo Track(MediaTime duration) => new()
    {
        Id = TrackId,
        Codec = CodecId.Pcm,
        Duration = duration,
        Title = "Silence",
        Audio = new AudioTrackInfo { SampleRate = SampleRate, Channels = 2, Layout = ChannelLayout.Stereo },
    };

    public static bool Is(TrackInfo track) => track.Id == TrackId;

    /// <summary>A silent packet covering <paramref name="start"/> up to <paramref name="end"/>.</summary>
    public static Packet Between(MediaTime start, MediaTime end) =>
        Packet.Create(TrackId, MediaBuffer.Rent(0), start, start, end - start, isKeyframe: true);
}

/// <summary>Decodes the silent packets of <see cref="SilentAudio"/> into frames of zeros as long as each packet.</summary>
internal sealed class SilenceDecoder : IAudioDecoder
{
    public string Name => "silence";

    public DecoderSource Source => DecoderSource.Own;

    public void Decode(Packet packet, ICollection<AudioFrame> output)
    {
        var samples = (int)packet.Duration.ToSamples(SilentAudio.SampleRate);
        var frame = AudioFrame.Rent(SilentAudio.SampleRate, 2, samples, ChannelLayout.Stereo);
        frame.Channel(0).Clear();
        frame.Channel(1).Clear();
        frame.Pts = packet.Pts;
        output.Add(frame);
    }

    public void Drain(ICollection<AudioFrame> output)
    {
    }

    public void Flush()
    {
    }

    public void Dispose()
    {
    }
}
