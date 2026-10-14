using System.Buffers.Binary;
using System.Text;

namespace Rex.Media.TestKit.Damage;

/// <summary>
/// Damage aimed at each format's structure, the way real files break: an MP4 whose boxes are emptied
/// or claim impossible sizes, a Matroska file with a cluster overwritten or a block of a size no block
/// has, an Ogg file with pages lost or misdated (CRC put right, so only the date is wrong), a FLAC file
/// whose metadata or stream information lies, an MP3 behind a tag of the wrong size, a WAV whose
/// chunks are wrong. Each case says the least share of the sound it should still play.
/// </summary>
public static class StructuralDamage
{
    public static IReadOnlyList<DamagedMedia> For(HealthyFile healthy, Random random)
    {
        ArgumentNullException.ThrowIfNull(healthy);
        ArgumentNullException.ThrowIfNull(random);
        if (healthy.Landmarks.Count == 0 && healthy.Format is not ("mp4" or "ogg"))
        {
            // A file made elsewhere (a real recording) has no landmarks: only the damage that finds its own way in.
            return [];
        }

        return healthy.Format switch
        {
            "mp4" => Mp4(healthy, random),
            "mkv" => Matroska(healthy, random),
            "ogg" => Ogg(healthy, random),
            "flac" => Flac(healthy, random),
            "mp3" => Mp3(healthy, random),
            "wav" => Wav(healthy),
            _ => [],
        };
    }

    private static DamagedMedia Case(HealthyFile healthy, string kind, byte[] bytes, double minimumShare) =>
        new(healthy.Format, kind, bytes, healthy.Extension) { MinimumShare = minimumShare };

    private static List<DamagedMedia> Mp4(HealthyFile healthy, Random random)
    {
        var cases = new List<DamagedMedia>();
        var file = healthy.Bytes;

        // Header boxes emptied: each shrinks to its 8-byte header and a free box takes the rest, so
        // every box around it keeps its size. A track without its media header cannot be read; the
        // movie header and edit-free tracks can do without theirs.
        foreach (var (path, share) in new (string[] Path, double Share)[]
        {
            (["moov", "mvhd"], 0.95),
            (["moov", "trak", "tkhd"], 0.95),
            (["moov", "trak", "mdia", "mdhd"], 0.0),
            (["moov", "trak", "mdia", "minf", "stbl", "stsd"], 0.0),
        })
        {
            if (Mp4Box(file, path) is { } box)
            {
                var emptied = (byte[])file.Clone();
                BinaryPrimitives.WriteUInt32BigEndian(emptied.AsSpan(box.Start), 8);
                BinaryPrimitives.WriteUInt32BigEndian(emptied.AsSpan(box.Start + 8), (uint)(box.Size - 8));
                Encoding.ASCII.GetBytes("free").CopyTo(emptied, box.Start + 12);
                cases.Add(Case(healthy, "empty-" + path[^1], emptied, share));
            }
        }

        // The video track's (second) sample size table claims fifty million fixed-size samples.
        if (Mp4Boxes(file, ["moov", "trak", "mdia", "minf", "stbl", "stsz"]).Skip(1).FirstOrDefault() is { Size: > 0 } stsz)
        {
            var huge = (byte[])file.Clone();
            BinaryPrimitives.WriteUInt32BigEndian(huge.AsSpan(stsz.Start + 12), 4);
            BinaryPrimitives.WriteUInt32BigEndian(huge.AsSpan(stsz.Start + 16), 50_000_000);
            cases.Add(Case(healthy, "stsz-fifty-million", huge, 0.95));

            var oversize = (byte[])file.Clone();
            for (var i = 0; i < 40; i++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(oversize.AsSpan(stsz.Start + 20 + (4 * random.Next(200))), 0x7FFFFFF0);
            }

            cases.Add(Case(healthy, "samples-oversize", oversize, 0.9));
        }

        if (Mp4Box(file, ["moov"]) is { } moov)
        {
            var claims = (byte[])file.Clone();
            BinaryPrimitives.WriteUInt32BigEndian(claims.AsSpan(moov.Start), 0x7FFFFFF0);
            cases.Add(Case(healthy, "moov-claims-2gb", claims, 0.9));

            var lost = (byte[])file.Clone();
            Encoding.ASCII.GetBytes("free").CopyTo(lost, moov.Start + 4);
            cases.Add(Case(healthy, "moov-lost", lost, 0));
        }

        // The video track's chunk offsets point past the end of the file.
        if (Mp4Boxes(file, ["moov", "trak", "mdia", "minf", "stbl", "stco"]).Skip(1).FirstOrDefault() is { Size: > 0 } stco)
        {
            var beyond = (byte[])file.Clone();
            var count = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(stco.Start + 12));
            for (var i = count / 2; i < count; i++)
            {
                var at = stco.Start + 16 + (4 * i);
                BinaryPrimitives.WriteUInt32BigEndian(beyond.AsSpan(at), BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at)) + (uint)file.Length);
            }

            cases.Add(Case(healthy, "chunks-beyond-end", beyond, 0.95));
        }

        return cases;
    }

    private static List<DamagedMedia> Matroska(HealthyFile healthy, Random random)
    {
        var cases = new List<DamagedMedia>();
        var file = healthy.Bytes;
        var clusters = healthy.Landmarks["cluster"];

        // Four kilobytes of a cluster in the middle overwritten: its header and first blocks are gone.
        var (start, _) = clusters[clusters.Count / 2];
        var overwritten = (byte[])file.Clone();
        random.NextBytes(overwritten.AsSpan(start, Math.Min(4096, file.Length - start)));
        cases.Add(Case(healthy, "cluster-overwritten", overwritten, 0.85));

        // A block whose size is the largest its size field holds, then one of unknown size.
        var block = FirstBlock(file, clusters[clusters.Count / 3]);
        var oversized = (byte[])file.Clone();
        var sizeLength = 8 - (int)Math.Log2(file[block.SizeAt]);
        oversized[block.SizeAt] = (byte)((0x100 >> sizeLength) | ((0x100 >> sizeLength) - 1));
        oversized.AsSpan(block.SizeAt + 1, sizeLength - 1).Fill(0xFF);
        oversized[block.SizeAt + sizeLength - 1] = 0xFE;
        cases.Add(Case(healthy, "block-oversized", oversized, 0.85));
        var unknown = (byte[])oversized.Clone();
        unknown[block.SizeAt + sizeLength - 1] = 0xFF;
        cases.Add(Case(healthy, "block-unknown-size", unknown, 0.85));

        // The file cut inside its cues, which the seek head points to; and cues pointing past the end.
        var (cuesAt, cuesLength) = healthy.Landmarks["cues"][0];
        cases.Add(Case(healthy, "cues-cut", file[..(cuesAt + (cuesLength / 2))], 0.95));
        var far = (byte[])file.Clone();
        for (var at = cuesAt; at + 10 <= cuesAt + cuesLength; at++)
        {
            if (far[at] == 0xF1 && far[at + 1] == 0x88)
            {
                BinaryPrimitives.WriteUInt64BigEndian(far.AsSpan(at + 2), (ulong)file.Length * 4);
            }
        }

        cases.Add(Case(healthy, "cues-beyond-end", far, 0.95));

        // Garbage where the tags begin, and garbage between the headers and the first cluster.
        var (tagsAt, tagsLength) = healthy.Landmarks["tags"][0];
        var tags = (byte[])file.Clone();
        random.NextBytes(tags.AsSpan(tagsAt + 4, Math.Min(8, tagsLength - 4)));
        cases.Add(Case(healthy, "tags-size-damaged", tags, 0.95));
        var beforeClusters = (byte[])file.Clone();
        random.NextBytes(beforeClusters.AsSpan(clusters[0].Start - 64, 64));
        cases.Add(Case(healthy, "garbage-before-clusters", beforeClusters, 0.0));
        return cases;
    }

    private static List<DamagedMedia> Ogg(HealthyFile healthy, Random random)
    {
        var cases = new List<DamagedMedia>();
        var pages = OggPages.Split(healthy.Bytes);
        var headers = pages.TakeWhile(page => page.Granule == 0).Count();
        var middle = headers + ((pages.Count - headers) / 2);

        cases.Add(Case(healthy, "comment-page-lost", OggPages.Join(pages.Where((_, i) => i != 1)), 0));
        var tenth = Math.Max(1, (pages.Count - headers) / 10);
        cases.Add(Case(healthy, "pages-lost", OggPages.Join(pages.Where((_, i) => i < middle || i >= middle + tenth)), 0.8));

        var misdated = pages.ToList();
        misdated[middle] = misdated[middle] with { Granule = long.MaxValue / 4 };
        cases.Add(Case(healthy, "granule-damaged", OggPages.Join(misdated), 0.9));

        var bad = OggPages.Join(pages);
        var offsets = OggPages.Offsets(pages);
        for (var i = middle; i < middle + tenth && i < offsets.Count; i++)
        {
            bad[offsets[i].Start + offsets[i].Length - 1 - random.Next(Math.Max(1, offsets[i].Length - 40))] ^= 0x5A;
        }

        cases.Add(Case(healthy, "crc-failures", bad, 0.8));
        return cases;
    }

    private static List<DamagedMedia> Flac(HealthyFile healthy, Random random)
    {
        var cases = new List<DamagedMedia>();
        var file = healthy.Bytes;

        // The picture block's length says 100 bytes less than it holds.
        for (var at = 4; at + 4 <= file.Length;)
        {
            var type = file[at] & 0x7F;
            var length = (file[at + 1] << 16) | (file[at + 2] << 8) | file[at + 3];
            if (type == 6)
            {
                var wrong = (byte[])file.Clone();
                var claimed = length - 100;
                (wrong[at + 1], wrong[at + 2], wrong[at + 3]) = ((byte)(claimed >> 16), (byte)(claimed >> 8), (byte)claimed);
                cases.Add(Case(healthy, "picture-length-wrong", wrong, 0.95));
                break;
            }

            if ((file[at] & 0x80) != 0)
            {
                break;
            }

            at += 4 + length;
        }

        // STREAMINFO claims six channels (its channel count is bits 1 to 3 of byte 12).
        var channels = (byte[])file.Clone();
        channels[8 + 12] = (byte)((channels[8 + 12] & 0xF1) | (5 << 1));
        cases.Add(Case(healthy, "streaminfo-channels", channels, 0.95));

        var frames = healthy.Landmarks["frames"];
        var lostFrom = frames[frames.Count / 2].Start;
        var lostTo = frames[(frames.Count / 2) + (frames.Count / 10)].Start;
        cases.Add(Case(healthy, "frames-lost", [.. file[..lostFrom], .. file[lostTo..]], 0.8));

        var damaged = (byte[])file.Clone();
        for (var i = 0; i < 20; i++)
        {
            var (start, length) = frames[random.Next(frames.Count)];
            damaged[start + 16 + random.Next(Math.Max(1, length - 32))] ^= (byte)random.Next(1, 256);
        }

        cases.Add(Case(healthy, "frames-damaged", damaged, 0.9));
        return cases;
    }

    private static List<DamagedMedia> Mp3(HealthyFile healthy, Random random)
    {
        var cases = new List<DamagedMedia>();
        var tag = Id3Builder.V23().Frame("TIT2", [0, .. "Damage"u8]).Padding(64).Build();

        // A tag claiming 4000 bytes more than it has swallows the first frames; one claiming less
        // leaves its own end in front of the audio.
        var longer = (byte[])tag.Clone();
        WriteSyncSafe(longer, SyncSafe(tag) + 4000);
        cases.Add(Case(healthy, "id3-size-too-big", [.. longer, .. healthy.Bytes], 0.9));
        var shorter = (byte[])tag.Clone();
        WriteSyncSafe(shorter, SyncSafe(tag) - 40);
        cases.Add(Case(healthy, "id3-size-too-small", [.. shorter, .. healthy.Bytes], 0.95));

        var headers = (byte[])healthy.Bytes.Clone();
        var frames = healthy.Landmarks["frames"];
        for (var i = 0; i < frames.Count / 20; i++)
        {
            headers[frames[random.Next(frames.Count)].Start + 1] ^= 0x1E;
        }

        cases.Add(Case(healthy, "frame-headers-damaged", headers, 0.85));
        return cases;
    }

    private static List<DamagedMedia> Wav(HealthyFile healthy)
    {
        var cases = new List<DamagedMedia>();
        var (dataAt, _) = healthy.Landmarks["data"][0];
        var huge = (byte[])healthy.Bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(huge.AsSpan(dataAt + 4), 0xFFFFFFFF);
        cases.Add(Case(healthy, "data-size-huge", huge, 0.99));
        var zero = (byte[])healthy.Bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(zero.AsSpan(dataAt + 4), 0);
        cases.Add(Case(healthy, "data-size-zero", zero, 0));
        var (fmtAt, fmtLength) = healthy.Landmarks["fmt"][0];
        var fmt = (byte[])healthy.Bytes.Clone();
        fmt.AsSpan(fmtAt + 8, fmtLength - 8).Clear();
        cases.Add(Case(healthy, "fmt-zeroed", fmt, 0));
        return cases;
    }

    private static int SyncSafe(byte[] tag) => (tag[6] << 21) | (tag[7] << 14) | (tag[8] << 7) | tag[9];

    private static void WriteSyncSafe(byte[] tag, int size) =>
        (tag[6], tag[7], tag[8], tag[9]) = ((byte)((size >> 21) & 0x7F), (byte)((size >> 14) & 0x7F), (byte)((size >> 7) & 0x7F), (byte)(size & 0x7F));

    /// <summary>The first SimpleBlock in a cluster: where its size field is.</summary>
    private static (int SizeAt, int Start) FirstBlock(byte[] file, (int Start, int Length) cluster)
    {
        for (var at = cluster.Start + 8; at < cluster.Start + cluster.Length; at++)
        {
            if (file[at] == 0xA3)
            {
                return (at + 1, at);
            }
        }

        throw new InvalidOperationException("The cluster holds no block.");
    }

    /// <summary>The first box at <paramref name="path"/> in an MP4 file.</summary>
    public static (int Start, int Size)? Mp4Box(byte[] file, string[] path) => Mp4Boxes(file, path).Cast<(int, int)?>().FirstOrDefault();

    /// <summary>Every box at <paramref name="path"/> (a type per level), in file order.</summary>
    public static IEnumerable<(int Start, int Size)> Mp4Boxes(byte[] file, string[] path)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(path);
        return Walk(file, 0, file.Length, path, 0);

        static IEnumerable<(int, int)> Walk(byte[] file, int from, int to, string[] path, int depth)
        {
            for (var at = from; at + 8 <= to;)
            {
                var size = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at));
                if (size < 8 || at + size > to)
                {
                    yield break;
                }

                if (Encoding.ASCII.GetString(file, at + 4, 4) == path[depth])
                {
                    if (depth == path.Length - 1)
                    {
                        yield return (at, size);
                    }
                    else
                    {
                        foreach (var found in Walk(file, at + 8, at + size, path, depth + 1))
                        {
                            yield return found;
                        }
                    }
                }

                at += size;
            }
        }
    }
}
