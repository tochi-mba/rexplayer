// Spec: ISO/IEC 15948 (W3C PNG, second edition) sections 5.2 (signature), 5.3 (chunk layout and CRC), 11.2.2 (IHDR), 11.2.4 (IDAT), 11.2.5 (IEND), 9.2 (filter type 1, Sub), 10 (zlib-compressed data stream); RFC 1950 for the zlib wrapper.
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Rex.Media.Primitives;

namespace Rex.Media.Video;

/// <summary>
/// Writes a picture as an 8-bit RGB PNG, with no other library: the snapshot format. Rows use the
/// Sub filter, which suits the smooth gradients of video frames.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Writes <paramref name="frame"/>, converting it to RGB first if it is not BGRA.</summary>
    public static void Write(Stream output, VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Write(output, frame, new PictureArea(0, 0, frame.Width, frame.Height));
    }

    /// <summary>Writes the part <paramref name="area"/> of <paramref name="frame"/>, converting it to RGB first if it is not BGRA.</summary>
    public static void Write(Stream output, VideoFrame frame, PictureArea area)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(frame);
        if (area.X < 0 || area.Y < 0 || area.Width <= 0 || area.Height <= 0 || area.X + area.Width > frame.Width || area.Y + area.Height > frame.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(area), "The area must lie inside the picture.");
        }

        if (frame.Format != PixelFormat.Bgra32)
        {
            using var converted = ColorConverter.ToBgra(frame);
            Write(output, converted, area);
            return;
        }

        output.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, area.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), area.Height);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(output, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var line = new byte[1 + (area.Width * 3)];
            line[0] = 1;
            for (var y = 0; y < area.Height; y++)
            {
                var row = frame.Row(0, area.Y + y)[(area.X * 4)..];
                for (var x = 0; x < area.Width; x++)
                {
                    for (var c = 0; c < 3; c++)
                    {
                        // RGB from BGRA, each byte minus the byte one pixel to its left (filter Sub).
                        var value = row[(x * 4) + 2 - c];
                        var left = x > 0 ? row[((x - 1) * 4) + 2 - c] : 0;
                        line[1 + (x * 3) + c] = (byte)(value - left);
                    }
                }

                zlib.Write(line);
            }
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        var typed = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, typed);
        data.CopyTo(typed, 4);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc.Crc32.Compute(typed));
        output.Write(length);
        output.Write(typed);
        output.Write(crc);
    }
}
