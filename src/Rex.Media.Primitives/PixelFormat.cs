namespace Rex.Media.Primitives;

/// <summary>How a decoded picture's samples are laid out in memory.</summary>
public enum PixelFormat
{
    Unknown = 0,

    /// <summary>8-bit 4:2:0: a luma plane, then one plane of interleaved Cb and Cr. What Windows' decoders give.</summary>
    Nv12,

    /// <summary>NV12's 10-bit form: 16-bit little-endian samples with the value in the top 10 bits.</summary>
    P010,

    /// <summary>8-bit 4:2:0 with three separate planes (Y, Cb, Cr).</summary>
    I420,

    /// <summary>10-bit 4:2:0 with three planes of 16-bit little-endian samples, the value in the low 10 bits.</summary>
    I420P10,

    /// <summary>8-bit 4:2:2 with three planes.</summary>
    I422,

    /// <summary>8-bit 4:4:4 with three planes.</summary>
    I444,

    /// <summary>8-bit luma only.</summary>
    Gray8,

    /// <summary>8-bit blue, green, red and alpha, packed: what a screen and a snapshot hold.</summary>
    Bgra32,
}

/// <summary>The geometry of each pixel format's planes.</summary>
public static class PixelFormats
{
    /// <summary>The largest width or height rexplayer accepts (SEC-01).</summary>
    public const int MaxDimension = 16384;

    public static int PlaneCount(this PixelFormat format) => format switch
    {
        PixelFormat.Nv12 or PixelFormat.P010 => 2,
        PixelFormat.I420 or PixelFormat.I420P10 or PixelFormat.I422 or PixelFormat.I444 => 3,
        PixelFormat.Gray8 or PixelFormat.Bgra32 => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The pixel format has no layout."),
    };

    /// <summary>Bits per sample as stored: 8, or 16 for the 10-bit formats.</summary>
    public static int StorageBits(this PixelFormat format) => format is PixelFormat.P010 or PixelFormat.I420P10 ? 16 : 8;

    /// <summary>The bytes one row of a plane holds, and how many rows the plane has.</summary>
    public static (int RowBytes, int Rows) PlaneSize(this PixelFormat format, int plane, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(plane);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(plane, format.PlaneCount());
        var bytes = format.StorageBits() / 8;
        var halfWidth = (width + 1) / 2;
        var halfHeight = (height + 1) / 2;
        return (format, plane) switch
        {
            (PixelFormat.Bgra32, _) => (width * 4, height),
            (_, 0) => (width * bytes, height),
            (PixelFormat.Nv12 or PixelFormat.P010, _) => (halfWidth * 2 * bytes, halfHeight),
            (PixelFormat.I420 or PixelFormat.I420P10, _) => (halfWidth * bytes, halfHeight),
            (PixelFormat.I422, _) => (halfWidth, height),
            _ => (width, height),
        };
    }
}
