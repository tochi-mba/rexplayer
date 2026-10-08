// Spec: Graphics Interchange Format Version 89a (CompuServe, 1990): header, logical screen descriptor, color tables, graphic control extension, image descriptor, data sub-blocks, trailer.
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Image;

/// <summary>
/// A GIF as video (FMT-C19). Each picture in the file is a packet: its graphic control extension
/// (made up when it has none), image descriptor, local color table and image data, exactly as
/// stored; the header, screen descriptor and global color table are the codec's private data. An
/// animation plays its frames at their own delays and loops until the time pictures show is up,
/// at least once through; a single picture is a still. A file cut short keeps the whole pictures
/// before the cut, as a download stopped part-way does.
/// </summary>
public sealed class GifDemuxer : IDemuxer
{
    /// <summary>Delays this short are read as 100 ms, as browsers do, since such GIFs were made to play at that pace.</summary>
    public static readonly MediaTime ShortestDelay = MediaTime.FromMilliseconds(20);

    private static readonly MediaTime UsualDelay = MediaTime.FromMilliseconds(100);

    private const int TrackId = 1;
    private readonly List<(byte[] Data, MediaTime Start, MediaTime Length)> _frames = [];
    private readonly MediaTime _cycle;
    private readonly int _packets;
    private int _next;

    public GifDemuxer(byte[] data, TimeSpan showFor)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 13)
        {
            throw new MediaFormatException("The GIF file ends inside its header.");
        }

        var screenFlags = data[10];
        var header = 13 + ((screenFlags & 0x80) != 0 ? 3 << ((screenFlags & 7) + 1) : 0);
        if (header > data.Length)
        {
            throw new MediaFormatException("The GIF file ends inside its color table.");
        }

        var showLength = new MediaTime(showFor.Ticks);
        ReadFrames(data, header);
        if (_frames.Count == 0)
        {
            throw new MediaFormatException("The GIF file holds no whole picture.");
        }

        // A still lasts as long as any picture; an animation loops to fill that time.
        if (_frames.Count == 1)
        {
            _frames[0] = (_frames[0].Data, MediaTime.Zero, showLength);
        }

        _cycle = _frames[^1].Start + _frames[^1].Length;
        var loops = Math.Max(1, (int)Math.Ceiling(showLength.Ticks / (double)_cycle.Ticks));
        _packets = loops * _frames.Count;

        // A screen of no size takes the first picture's (after its 8-byte control and the descriptor's position).
        var width = data[6] | (data[7] << 8);
        var height = data[8] | (data[9] << 8);
        if (width == 0 || height == 0)
        {
            var first = _frames[0].Data;
            (width, height) = (first[13] | (first[14] << 8), first[15] | (first[16] << 8));
        }
        Info = new MediaInfo
        {
            FormatName = _frames.Count > 1 ? "GIF animation" : "GIF",
            Duration = new MediaTime(_cycle.Ticks * loops),
            Tracks =
            [
                new TrackInfo
                {
                    Id = TrackId,
                    Codec = CodecId.Gif,
                    CodecPrivate = data[..header],
                    Duration = new MediaTime(_cycle.Ticks * loops),
                    Video = new VideoTrackInfo { Width = width, Height = height },
                },
            ],
        };
    }

    public MediaInfo Info { get; }

    /// <summary>The pictures in the file, once through.</summary>
    public int FrameCount => _frames.Count;

    public Packet? ReadPacket(CancellationToken cancellationToken)
    {
        if (_next >= _packets)
        {
            return null;
        }

        var loop = _next / _frames.Count;
        var (data, start, length) = _frames[_next % _frames.Count];
        var pts = new MediaTime((_cycle.Ticks * loop) + start.Ticks);

        // Each loop starts afresh, so its first picture is where decoding can begin.
        var packet = Packet.Create(TrackId, MediaBuffer.CopyOf(data), pts, pts, length, isKeyframe: _next % _frames.Count == 0);
        _next++;
        return packet;
    }

    /// <summary>Goes back to the start of the loop holding <paramref name="target"/>: later pictures are drawn over earlier ones.</summary>
    public void Seek(MediaTime target, CancellationToken cancellationToken)
    {
        var loop = target <= MediaTime.Zero ? 0 : Math.Min(target.Ticks / _cycle.Ticks, (_packets / _frames.Count) - 1);
        _next = (int)loop * _frames.Count;
    }

    public void Dispose()
    {
    }

    private void ReadFrames(byte[] data, int at)
    {
        var start = MediaTime.Zero;
        byte[]? control = null;
        while (at < data.Length)
        {
            switch (data[at])
            {
                case 0x21 when at + 1 < data.Length:
                    var end = SkipSubBlocks(data, at + 2);
                    if (end < 0)
                    {
                        return;
                    }

                    // The graphic control extension applies to the next picture; others are passed over.
                    if (data[at + 1] == 0xF9 && end - at >= 8)
                    {
                        control = data[at..(at + 8)];
                    }

                    at = end;
                    break;
                case 0x2C:
                    var frameEnd = PictureEnd(data, at);
                    if (frameEnd < 0)
                    {
                        return;
                    }

                    control ??= [0x21, 0xF9, 4, 0, 0, 0, 0, 0];
                    var delay = MediaTime.FromMilliseconds(10 * (control[4] | (control[5] << 8)));
                    var length = delay < ShortestDelay ? UsualDelay : delay;
                    _frames.Add(([.. control, .. data[at..frameEnd]], start, length));
                    start += length;
                    control = null;
                    at = frameEnd;
                    break;
                default:
                    // The trailer, or bytes that are no block: the pictures so far are the file.
                    return;
            }
        }
    }

    /// <summary>The end of the picture whose descriptor is at <paramref name="at"/>; -1 when the file ends first.</summary>
    private static int PictureEnd(byte[] data, int at)
    {
        if (at + 10 > data.Length)
        {
            return -1;
        }

        var flags = data[at + 9];
        var image = at + 10 + ((flags & 0x80) != 0 ? 3 << ((flags & 7) + 1) : 0);

        // The LZW code size, then the image data.
        return image + 1 > data.Length ? -1 : SkipSubBlocks(data, image + 1);
    }

    /// <summary>The position after the sub-blocks starting at <paramref name="at"/> and their terminator; -1 when the file ends first.</summary>
    private static int SkipSubBlocks(byte[] data, int at)
    {
        while (at < data.Length)
        {
            var size = data[at];
            at += 1 + size;
            if (size == 0)
            {
                return at;
            }
        }

        return -1;
    }
}
