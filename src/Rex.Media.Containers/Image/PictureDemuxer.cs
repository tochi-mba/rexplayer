// Spec: none of its own; the formats are recognised as PictureFormats.cs describes and GIF is read as GifDemuxer.cs describes. This file turns a still picture into a stream of one frame.
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Image;

/// <summary>
/// Recognises pictures (FMT-C19) by their first bytes and opens them as video: a still as one frame
/// shown for <see cref="ShowFor"/>, an animated GIF as its frames, looping until then.
/// </summary>
public sealed class PictureDemuxerFactory : IDemuxerFactory
{
    /// <summary>How long a picture shows when nothing says otherwise.</summary>
    public static readonly TimeSpan DefaultShowFor = TimeSpan.FromSeconds(5);

    public PictureDemuxerFactory(TimeSpan? showFor = null)
    {
        ShowFor = showFor ?? DefaultShowFor;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ShowFor, TimeSpan.Zero, nameof(showFor));
    }

    /// <summary>How long each picture shows before the next item plays.</summary>
    public TimeSpan ShowFor { get; }

    public string Name => "picture";

    public int Probe(ReadOnlySpan<byte> head, string? extension) => PictureFormats.Detect(head) is null ? 0 : 100;

    public IDemuxer Open(IByteSource source, CancellationToken cancellationToken)
    {
        var data = PictureDemuxer.ReadAll(source, cancellationToken);
        var format = PictureFormats.Detect(data) ?? throw new MediaFormatException($"{source.Name} is not a picture rexplayer knows.");
        return format == PictureFormat.Gif ? new GifDemuxer(data, ShowFor) : new PictureDemuxer(format, data, ShowFor);
    }
}

/// <summary>
/// A still picture as a video of one frame: the whole file is the frame's packet, decoded by
/// Windows' imaging, shown for as long as pictures show. A seek sends the frame again from there.
/// </summary>
public sealed class PictureDemuxer : IDemuxer
{
    /// <summary>The largest picture file rexplayer reads into memory.</summary>
    public const int MaxBytes = 256 * 1024 * 1024;

    private const int TrackId = 1;
    private readonly byte[] _data;
    private readonly MediaTime _length;
    private MediaTime _from = MediaTime.Zero;
    private bool _sent;

    public PictureDemuxer(PictureFormat format, byte[] data, TimeSpan showFor)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _length = new MediaTime(showFor.Ticks);
        var (width, height) = PictureFormats.SizeOf(format, data);
        Info = new MediaInfo
        {
            FormatName = PictureFormats.Name(format),
            Duration = _length,
            Tracks = [new TrackInfo { Id = TrackId, Codec = CodecId.Picture, Duration = _length, Video = new VideoTrackInfo { Width = width, Height = height } }],
        };
    }

    public MediaInfo Info { get; }

    public Packet? ReadPacket(CancellationToken cancellationToken)
    {
        if (_sent)
        {
            return null;
        }

        _sent = true;
        return Packet.Create(TrackId, MediaBuffer.CopyOf(_data), _from, _from, _length - _from, isKeyframe: true);
    }

    public void Seek(MediaTime target, CancellationToken cancellationToken)
    {
        // The frame starts where the seek lands, so a precise seek does not pass it over.
        _from = target < MediaTime.Zero ? MediaTime.Zero : target < _length ? target : _length - new MediaTime(1);
        _sent = false;
    }

    public void Dispose()
    {
    }

    /// <summary>The whole of <paramref name="source"/>, which must be a file of a picture's size.</summary>
    public static byte[] ReadAll(IByteSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length is not { } length || length > MaxBytes)
        {
            throw new MediaFormatException(source.Length is null ? $"{source.Name} does not say how big it is, so it cannot be shown as a picture." : $"{source.Name} is larger than the {MaxBytes / (1024 * 1024)} MB rexplayer reads as a picture.");
        }

        var data = new byte[length];
        var read = 0;
        while (read < data.Length && source.Read(read, data.AsSpan(read), cancellationToken) is var count and > 0)
        {
            read += count;
        }

        return read == data.Length ? data : data[..read];
    }
}
