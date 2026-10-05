namespace Rex.Media.Primitives;

/// <summary>
/// One compressed access unit on its way from a demuxer to a decoder. The packet owns its payload;
/// disposing it returns the payload and the packet to their pools. The generation number ties the
/// packet to a seek: after a seek every older generation is stale and is dropped unread.
/// </summary>
public sealed class Packet : IDisposable
{
    private static readonly SlotPool<Packet> Pool = new(1024);

    private MediaBuffer? _data;

    private Packet()
    {
    }

    public int TrackId { get; private set; }

    /// <summary>Presentation time, or <see cref="MediaTime.Unknown"/> if the container did not say.</summary>
    public MediaTime Pts { get; set; }

    /// <summary>Decode time, or <see cref="MediaTime.Unknown"/>.</summary>
    public MediaTime Dts { get; set; }

    public MediaTime Duration { get; set; }

    public bool IsKeyframe { get; set; }

    /// <summary>The container signalled a break in the timeline before this packet.</summary>
    public bool IsDiscontinuity { get; set; }

    public long Generation { get; set; }

    public MediaBuffer Data => _data ?? throw new ObjectDisposedException(nameof(Packet));

    public bool IsDisposed => _data is null;

    /// <summary>A packet that takes ownership of <paramref name="data"/>.</summary>
    public static Packet Create(int trackId, MediaBuffer data, MediaTime pts, MediaTime dts, MediaTime duration, bool isKeyframe)
    {
        ArgumentNullException.ThrowIfNull(data);
        var packet = Pool.Take() ?? new Packet();
        packet._data = data;
        packet.TrackId = trackId;
        packet.Pts = pts;
        packet.Dts = dts;
        packet.Duration = duration;
        packet.IsKeyframe = isKeyframe;
        packet.IsDiscontinuity = false;
        packet.Generation = 0;
        return packet;
    }

    /// <summary>The best timestamp to schedule by: presentation time when known, else decode time.</summary>
    public MediaTime Timestamp => Pts.IsKnown ? Pts : Dts;

    public void Dispose()
    {
        var data = Interlocked.Exchange(ref _data, null);
        if (data is null)
        {
            return;
        }

        data.Dispose();
        Pool.Return(this);
    }
}
