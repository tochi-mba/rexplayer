using System.Buffers.Binary;
using Rex.Media.Primitives;

namespace Rex.Media.TestKit.Damage;

/// <summary>One Ogg page taken apart (RFC 3533 section 6): the fields the corpus changes, its lacing values and body.</summary>
public sealed record OggPageData(byte Flags, long Granule, int Serial, int Sequence, byte[] Lacing, byte[] Body);

/// <summary>Splits an Ogg file into pages and writes pages back, each with its CRC worked out again.</summary>
public static class OggPages
{
    public static List<OggPageData> Split(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var pages = new List<OggPageData>();
        var at = 0;
        while (at + 27 <= file.Length && file.AsSpan(at).StartsWith("OggS"u8))
        {
            var segments = file[at + 26];
            var lacing = file.AsSpan(at + 27, segments).ToArray();
            var length = lacing.Sum(lace => lace);
            var body = file.AsSpan(at + 27 + segments, length).ToArray();
            pages.Add(new OggPageData(
                file[at + 5],
                BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(at + 6)),
                BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at + 14)),
                BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at + 18)),
                lacing,
                body));
            at += 27 + segments + length;
        }

        return pages;
    }

    public static byte[] Write(OggPageData page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var bytes = new byte[27 + page.Lacing.Length + page.Body.Length];
        "OggS"u8.CopyTo(bytes);
        bytes[5] = page.Flags;
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(6), page.Granule);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), page.Serial);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), page.Sequence);
        bytes[26] = (byte)page.Lacing.Length;
        page.Lacing.CopyTo(bytes, 27);
        page.Body.CopyTo(bytes, 27 + page.Lacing.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(22), Crc.Crc32Ogg.Compute(bytes));
        return bytes;
    }

    public static byte[] Join(IEnumerable<OggPageData> pages) => [.. pages.SelectMany(Write)];

    /// <summary>Where each page starts in the file <see cref="Join"/> writes, and its length.</summary>
    public static IReadOnlyList<(int Start, int Length)> Offsets(IEnumerable<OggPageData> pages)
    {
        var offsets = new List<(int, int)>();
        var at = 0;
        foreach (var page in pages)
        {
            var length = 27 + page.Lacing.Length + page.Body.Length;
            offsets.Add((at, length));
            at += length;
        }

        return offsets;
    }
}
