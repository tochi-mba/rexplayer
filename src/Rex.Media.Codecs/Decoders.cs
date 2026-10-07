using Rex.Media.Primitives;

namespace Rex.Media.Codecs;

/// <summary>Where a decoder's code comes from, shown in Media Information and the stats overlay.</summary>
public enum DecoderSource
{
    /// <summary>rexplayer's own implementation.</summary>
    Own,

    /// <summary>A software decoder supplied by Windows.</summary>
    OsSoftware,

    /// <summary>A hardware decoder on the graphics card, reached through Windows.</summary>
    OsHardware,
}

/// <summary>
/// Turns packets of one audio track into float frames. A decoder may hold samples back (a codec with
/// overlapping windows needs the next packet to finish the current one), so <see cref="Drain"/>
/// collects the rest at the end of the stream and <see cref="Flush"/> forgets them after a seek.
/// </summary>
public interface IAudioDecoder : IDisposable
{
    string Name { get; }

    DecoderSource Source { get; }

    /// <summary>Decodes one packet, adding zero or more frames. The caller keeps ownership of the packet.</summary>
    void Decode(Packet packet, ICollection<AudioFrame> output);

    void Drain(ICollection<AudioFrame> output);

    void Flush();
}

/// <summary>
/// Turns packets of one video track into pictures. Decoders hold pictures back to reorder them
/// (B-frames), so <see cref="Drain"/> collects the rest at the end and <see cref="Flush"/> forgets
/// them after a seek. Pictures come out in presentation order. A decoder on the graphics card has a
/// fixed pool of surfaces, and a picture's surface returns to it only when the picture is disposed,
/// so callers show and dispose the pictures each call gives before the next call.
/// </summary>
public interface IVideoDecoder : IDisposable
{
    string Name { get; }

    DecoderSource Source { get; }

    /// <summary>Decodes one packet, adding zero or more pictures. The caller keeps ownership of the packet.</summary>
    void Decode(Packet packet, ICollection<VideoFrame> output);

    /// <summary>
    /// Adds the pictures still held at the end of the stream. False means more remain: show and
    /// dispose the pictures given, then call again; true means the decoder is empty.
    /// </summary>
    bool Drain(ICollection<VideoFrame> output);

    void Flush();
}

/// <summary>Creates decoders for the codecs it knows.</summary>
public interface IDecoderFactory
{
    string Name { get; }

    DecoderSource Source { get; }

    /// <summary>Higher runs first. Own decoders rank above Windows ones for the codecs they cover.</summary>
    int Rank { get; }

    bool CanDecode(TrackInfo track);

    IAudioDecoder CreateAudio(TrackInfo track);

    /// <summary>A video decoder; factories that decode only audio keep this default.</summary>
    IVideoDecoder CreateVideo(TrackInfo track) => throw new NotSupportedException($"{Name} decodes audio only.");

    /// <summary>
    /// A video decoder that may put its pictures straight onto <paramref name="gpu"/>, the graphics
    /// device the presenter draws with (opaque here); factories that cannot keep this default.
    /// </summary>
    IVideoDecoder CreateVideo(TrackInfo track, object? gpu) => CreateVideo(track);
}

/// <summary>
/// The decode ladder (ADR-009): every factory that can decode a track, best first. When the first
/// rung fails to open, the next is tried, and the reasons are kept so the user can be told why a
/// track does not play.
/// </summary>
public sealed class DecoderRegistry
{
    private readonly List<IDecoderFactory> _factories = [];

    public IReadOnlyList<IDecoderFactory> Factories => _factories;

    public DecoderRegistry Add(IDecoderFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factories.Add(factory);
        return this;
    }

    /// <summary>Factories able to decode <paramref name="track"/>, highest rank first.</summary>
    public IReadOnlyList<IDecoderFactory> Ladder(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return _factories.Where(factory => factory.CanDecode(track)).OrderByDescending(factory => factory.Rank).ToList();
    }

    /// <summary>The first audio decoder on the ladder that opens, or a reason none did.</summary>
    public DecoderResult<IAudioDecoder> CreateAudio(TrackInfo track) => Create(track, factory => factory.CreateAudio(track));

    /// <summary>The first video decoder on the ladder that opens, or a reason none did; <paramref name="gpu"/> is the presenter's graphics device, if any.</summary>
    public DecoderResult<IVideoDecoder> CreateVideo(TrackInfo track, object? gpu = null) => Create(track, factory => factory.CreateVideo(track, gpu));

    private DecoderResult<T> Create<T>(TrackInfo track, Func<IDecoderFactory, T> create)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(track);
        var failures = new List<string>();
        foreach (var factory in Ladder(track))
        {
            try
            {
                return DecoderResult.Opened(create(factory));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Decoders are often the operating system's, which can fail in any way it likes (COM
                // errors, refused casts): any failure moves down the ladder rather than ending playback.
                failures.Add($"{factory.Name}: {ex.Message}");
            }
        }

        return DecoderResult.Failed<T>(failures.Count == 0
            ? $"No decoder for {track.Codec.DisplayName()} is available."
            : $"No decoder for {track.Codec.DisplayName()} could open the track. " + string.Join(" ", failures));
    }
}

/// <summary>A decoder, or the plain-words reason there is none.</summary>
public sealed record DecoderResult<T>(T? Decoder, string? Reason)
    where T : class;

public static class DecoderResult
{
    public static DecoderResult<T> Opened<T>(T decoder)
        where T : class => new(decoder, null);

    public static DecoderResult<T> Failed<T>(string reason)
        where T : class => new(null, reason);
}
