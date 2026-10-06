using System.Buffers;

namespace Rex.Media.Primitives;

/// <summary>
/// A decoded picture: in system memory, its planes in one pooled array with each row padded to a
/// 64-byte stride so converters can read whole vectors; or on the graphics card, as a surface a
/// hardware decoder left there (then it has no planes). The frame object is pooled like
/// <see cref="AudioFrame"/>; disposing returns the planes or releases the surface.
/// </summary>
public sealed class VideoFrame : IDisposable
{
    private const int Alignment = 64;
    private static readonly SlotPool<VideoFrame> Pool = new(32);

    private readonly int[] _offsets = new int[3];
    private readonly int[] _strides = new int[3];
    private byte[]? _data;
    private IDisposable? _surface;
    private int _live;
    private SlotPool<VideoFrame>? _home;

    private VideoFrame()
    {
    }

    public PixelFormat Format { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>When the picture should be shown.</summary>
    public MediaTime Pts { get; set; }

    public MediaTime Duration { get; set; }

    public ColorInfo Color { get; set; } = ColorInfo.Unspecified;

    public Rational PixelAspect { get; set; } = new(1, 1);

    public long Generation { get; set; }

    public bool IsDisposed => Volatile.Read(ref _live) == 0;

    /// <summary>The surface on the graphics card holding the picture, or null for a picture in memory.</summary>
    public IDisposable? Surface => _surface;

    public static VideoFrame Rent(PixelFormat format, int width, int height) => Rent(Pool, format, width, height);

    /// <summary>A picture held on the graphics card by <paramref name="surface"/>, which the frame then owns.</summary>
    public static VideoFrame OnGraphicsCard(PixelFormat format, int width, int height, IDisposable surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var frame = Rent(Pool, format, width, height, planes: false);
        frame._surface = surface;
        return frame;
    }

    /// <summary>Rents from <paramref name="pool"/>; with no pool the frame is dropped when disposed.</summary>
    internal static VideoFrame Rent(SlotPool<VideoFrame>? pool, PixelFormat format, int width, int height, bool planes = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, PixelFormats.MaxDimension);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, PixelFormats.MaxDimension);
        var frame = pool?.Take() ?? new VideoFrame();
        var size = 0;
        for (var plane = 0; plane < format.PlaneCount(); plane++)
        {
            var (rowBytes, rows) = format.PlaneSize(plane, width, height);
            frame._offsets[plane] = size;
            frame._strides[plane] = (rowBytes + Alignment - 1) / Alignment * Alignment;
            size += frame._strides[plane] * rows;
        }

        frame._home = pool;
        frame._data = planes ? ArrayPool<byte>.Shared.Rent(size) : null;
        frame._surface = null;
        frame._live = 1;
        frame.Format = format;
        frame.Width = width;
        frame.Height = height;
        frame.Pts = MediaTime.Unknown;
        frame.Duration = MediaTime.Zero;
        frame.Color = ColorInfo.Unspecified;
        frame.PixelAspect = new Rational(1, 1);
        frame.Generation = 0;
        return frame;
    }

    /// <summary>The distance in bytes from one row of a plane to the next.</summary>
    public int Stride(int plane)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(plane, Format.PlaneCount());
        return _strides[plane];
    }

    /// <summary>A plane's bytes, rows <see cref="Stride"/> apart.</summary>
    public Span<byte> Plane(int plane)
    {
        var (_, rows) = Format.PlaneSize(plane, Width, Height);
        return Data.AsSpan(_offsets[plane], _strides[plane] * rows);
    }

    /// <summary>One row of a plane, without its padding.</summary>
    public Span<byte> Row(int plane, int row)
    {
        var (rowBytes, rows) = Format.PlaneSize(plane, Width, Height);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, rows);
        return Data.AsSpan(_offsets[plane] + (row * _strides[plane]), rowBytes);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _live, 0) == 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref _data, null) is { } data)
        {
            ArrayPool<byte>.Shared.Return(data);
        }

        Interlocked.Exchange(ref _surface, null)?.Dispose();
        _home?.Return(this);
    }

    private byte[] Data => _data ?? (IsDisposed
        ? throw new ObjectDisposedException(nameof(VideoFrame))
        : throw new InvalidOperationException("The picture is on the graphics card; it has no planes in memory."));
}
