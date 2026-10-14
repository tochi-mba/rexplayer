using System.Buffers.Binary;
using Rex.Media.Codecs.Mpeg;
using static Rex.Media.TestKit.Mp4Writer;

namespace Rex.Media.TestKit.Damage;

/// <summary>A healthy file the corpus damages, with what a full read of it gives.</summary>
/// <param name="Format">The container, as the corpus names it ("mp4", "mkv", "ogg", "flac", "mp3", "wav").</param>
/// <param name="Extension">The file extension it carries, with the dot.</param>
/// <param name="Bytes">The whole file.</param>
/// <param name="Seconds">How long its sound lasts.</param>
/// <param name="VideoFrames">How many pictures it holds (0 for sound only).</param>
public sealed record HealthyFile(string Format, string Extension, byte[] Bytes, double Seconds, int VideoFrames)
{
    /// <summary>Byte ranges of the structure the format damages aim at, by name (for example "moov" or "cluster").</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<(int Start, int Length)>> Landmarks { get; init; } = new Dictionary<string, IReadOnlyList<(int, int)>>();
}

/// <summary>
/// Builds the healthy files the damaged-file corpus starts from: long enough that a damaged stretch
/// leaves most of the file to salvage, short enough that the whole corpus runs in seconds. Every
/// format carries sound rexplayer's own decoders play; MP4 and Matroska carry pictures too, whose
/// packets <see cref="CheckedVideoDecoderFactory"/> can tell apart from damaged ones.
/// </summary>
public static class HealthyMedia
{
    /// <summary>The audio rate of the MP4 and Matroska files: 8 kHz mono, 16-bit.</summary>
    public const int ContainerAudioRate = 8000;

    /// <summary>Pictures per second in the MP4 and Matroska files.</summary>
    public const int FramesPerSecond = 25;

    /// <summary>A keyframe every this many pictures.</summary>
    public const int KeyframeInterval = 50;

    public static IReadOnlyList<HealthyFile> All(double seconds = 20) =>
    [
        Wav(seconds),
        Flac(seconds),
        Mp3(seconds),
        Mp4(seconds),
        Matroska(seconds),
        Ogg(seconds),
    ];

    public static HealthyFile Wav(double seconds)
    {
        var bytes = Pcm.SineWav(440, 16000, 2, seconds);
        return new HealthyFile("wav", ".wav", bytes, seconds, 0)
        {
            Landmarks = new Dictionary<string, IReadOnlyList<(int, int)>>
            {
                ["fmt"] = [(12, 24)],
                ["data"] = [(36, bytes.Length - 36)],
            },
        };
    }

    public static HealthyFile Flac(double seconds)
    {
        const int Rate = 16000;
        var samples = (int)(Rate * seconds);
        var picture = new byte[16 * 1024];
        new Random(7).NextBytes(picture);
        var builder = new FlacBuilder
        {
            SampleRate = Rate,
            BlockSize = 1152,
            Picture = (picture, "image/jpeg", 3),
            Comments = ["TITLE=Damage", "ARTIST=rexplayer tests"],
            SeekPointInterval = 20,
        };
        var bytes = builder.Build([FlacBuilder.Tone(samples, 16, 440, Rate, seed: 1), FlacBuilder.Tone(samples, 16, 660, Rate, seed: 2)]);
        var first = (int)builder.FirstFrameOffset;
        return new HealthyFile("flac", ".flac", bytes, seconds, 0)
        {
            Landmarks = new Dictionary<string, IReadOnlyList<(int, int)>>
            {
                ["streaminfo"] = [(8, 34)],
                ["frames"] = [.. builder.FrameOffsets.Select((offset, i) => (first + (int)offset, (int)((i + 1 < builder.FrameOffsets.Count ? builder.FrameOffsets[i + 1] : bytes.Length - first) - offset)))],
                ["metadata"] = [(4, first - 4)],
            },
        };
    }

    /// <summary>The committed constant-bitrate MP3 fixture's frames repeated (its encoder-tag frame left out) to the length asked for.</summary>
    public static HealthyFile Mp3(double seconds)
    {
        var fixture = System.IO.File.ReadAllBytes(RepoPaths.Combine("tests", "fixtures", "mp3", "stereo-44k-128k-cbr.mp3"));
        var frames = new List<(int Start, int Length)>();
        for (var at = 0; at + MpegAudioHeader.Size <= fixture.Length;)
        {
            if (fixture[at] == 0xFF && MpegAudioHeader.TryParse(fixture.AsSpan(at), out var header) && header.FrameLength > 0 && at + header.FrameLength <= fixture.Length)
            {
                frames.Add((at, header.FrameLength));
                at += header.FrameLength;
            }
            else
            {
                at++;
            }
        }

        // The first frame of an encoded file holds the encoder's tag; repeated, it would claim the wrong length.
        var tagged = fixture.AsSpan(frames[0].Start, frames[0].Length);
        if (tagged.IndexOf("Xing"u8) >= 0 || tagged.IndexOf("Info"u8) >= 0)
        {
            frames.RemoveAt(0);
        }

        var perFrame = 1152.0 / 44100;
        var wanted = (int)Math.Ceiling(seconds / perFrame);
        var output = new List<byte>();
        var landmarks = new List<(int, int)>();
        for (var i = 0; i < wanted; i++)
        {
            var (start, length) = frames[i % frames.Count];
            landmarks.Add((output.Count, length));
            output.AddRange(fixture.AsSpan(start, length));
        }

        return new HealthyFile("mp3", ".mp3", [.. output], wanted * perFrame, 0)
        {
            Landmarks = new Dictionary<string, IReadOnlyList<(int, int)>> { ["frames"] = landmarks },
        };
    }

    /// <summary>
    /// An MP4 with a 16-bit PCM track and a picture track, interleaved half a second at a time the
    /// way a recorder writes them, its index (moov) in front of the media.
    /// </summary>
    public static HealthyFile Mp4(double seconds)
    {
        var audio = AudioBytes(seconds);
        var video = VideoSamples((int)(seconds * FramesPerSecond));
        const int AudioChunk = ContainerAudioRate / 2;
        const int VideoChunk = FramesPerSecond / 2;
        var audioFrames = audio.Length / 2;
        var chunkCount = (audioFrames + AudioChunk - 1) / AudioChunk;

        byte[] Moov(long mdatData)
        {
            var audioOffsets = new List<long>();
            var videoOffsets = new List<long>();
            var videoPerChunk = new List<int>();
            var at = mdatData;
            var videoIndex = 0;
            for (var chunk = 0; chunk < chunkCount; chunk++)
            {
                audioOffsets.Add(at);
                at += Math.Min(AudioChunk, audioFrames - (chunk * AudioChunk)) * 2;
                var count = Math.Min(VideoChunk, video.Count - videoIndex);
                if (count > 0)
                {
                    videoOffsets.Add(at);
                    videoPerChunk.Add(count);
                    at += video.Skip(videoIndex).Take(count).Sum(s => s.Length);
                    videoIndex += count;
                }
            }

            var durationMs = (long)(seconds * 1000);
            var mvhd = Full("mvhd", 0, 0, U32(0), U32(0), U32(1000), U32(durationMs), new byte[80]);
            var audioTrak = Trak(1, "soun", ContainerAudioRate, audioFrames, AudioEntry("sowt", 1, 16, ContainerAudioRate), Concat(
                Full("stts", 0, 0, U32(1), U32(audioFrames), U32(1)),
                Full("stsc", 0, 0, U32(2), U32(1), U32(AudioChunk), U32(1), U32(chunkCount), U32(audioFrames - ((chunkCount - 1) * AudioChunk)), U32(1)),
                Full("stsz", 0, 0, U32(2), U32(audioFrames)),
                Full("stco", 0, 0, U32(audioOffsets.Count), Concat([.. audioOffsets.Select(U32)]))));
            var keyframes = Enumerable.Range(0, video.Count).Where(i => i % KeyframeInterval == 0).Select(i => U32(i + 1)).ToArray();
            var runs = new List<byte[]>();
            for (var i = 0; i < videoPerChunk.Count; i++)
            {
                if (i == 0 || videoPerChunk[i] != videoPerChunk[i - 1])
                {
                    runs.Add(Concat(U32(i + 1), U32(videoPerChunk[i]), U32(1)));
                }
            }

            var videoTrak = Trak(2, "vide", 1000, video.Count * 40, VideoEntry("avc1", 64, 36), Concat(
                Full("stts", 0, 0, U32(1), U32(video.Count), U32(40)),
                Full("stss", 0, 0, U32(keyframes.Length), Concat(keyframes)),
                Full("stsc", 0, 0, U32(runs.Count), Concat([.. runs])),
                Full("stsz", 0, 0, U32(0), U32(video.Count), Concat([.. video.Select(s => U32(s.Length))])),
                Full("stco", 0, 0, U32(videoOffsets.Count), Concat([.. videoOffsets.Select(U32)]))));
            return Box("moov", mvhd, audioTrak, videoTrak);
        }

        var ftyp = Box("ftyp", Ascii("isom"), U32(0x200), Ascii("isom"), Ascii("mp41"));
        var moovLength = Moov(0).Length;
        var moov = Moov(ftyp.Length + moovLength + 8);
        var payload = new List<byte>();
        var landmarks = new List<(int, int)>();
        var start = ftyp.Length + moov.Length + 8;
        for (int chunk = 0, v = 0; chunk < chunkCount; chunk++)
        {
            var bytes = Math.Min(AudioChunk, audioFrames - (chunk * AudioChunk)) * 2;
            payload.AddRange(audio.AsSpan(chunk * AudioChunk * 2, bytes));
            for (var i = 0; i < VideoChunk && v < video.Count; i++, v++)
            {
                landmarks.Add((start + payload.Count, video[v].Length));
                payload.AddRange(video[v]);
            }
        }

        var file = Concat(ftyp, moov, U32(8 + payload.Count), Ascii("mdat"), [.. payload]);
        return new HealthyFile("mp4", ".mp4", file, audioFrames / (double)ContainerAudioRate, video.Count)
        {
            Landmarks = new Dictionary<string, IReadOnlyList<(int, int)>>
            {
                ["moov"] = [(ftyp.Length, moov.Length)],
                ["mdat"] = [(ftyp.Length + moov.Length, payload.Count + 8)],
                ["video"] = landmarks,
            },
        };

        static byte[] Trak(int id, string handler, uint timescale, long duration, byte[] entry, byte[] tables)
        {
            var matrix = Concat(U32(0x10000), U32(0), U32(0), U32(0), U32(0x10000), U32(0), U32(0), U32(0), U32(0x40000000));
            var tkhd = Full("tkhd", 0, 3, U32(0), U32(0), U32(id), U32(0), U32(0), new byte[16], matrix, U32(0), U32(0));
            var mdhd = Full("mdhd", 0, 0, U32(0), U32(0), U32(timescale), U32(duration), U16(0x15C7), U16(0));
            var hdlr = Full("hdlr", 0, 0, U32(0), Ascii(handler), new byte[12], [0]);
            var stbl = Box("stbl", Full("stsd", 0, 0, U32(1), entry), tables);
            return Box("trak", tkhd, Box("mdia", mdhd, hdlr, Box("minf", stbl)));
        }
    }

    /// <summary>A Matroska file with the same sound and pictures in one-second clusters, cues and tags after them, found through a seek head.</summary>
    public static HealthyFile Matroska(double seconds)
    {
        const uint Info = 0x1549A966, Tracks = 0x1654AE6B, TrackEntry = 0xAE, Cluster = 0x1F43B675, Cues = 0x1C53BB6B, Tags = 0x1254C367, SeekHead = 0x114D9B74;
        var audio = AudioBytes(seconds);
        var video = VideoSamples((int)(seconds * FramesPerSecond));
        const int AudioBlock = ContainerAudioRate / 10 * 2;
        var info = EbmlWriter.Element(Info, EbmlWriter.UInt(0x2AD7B1, 1_000_000), EbmlWriter.Float(0x4489, seconds * 1000), EbmlWriter.Text(0x4D80, "rexplayer tests"));
        var tracks = EbmlWriter.Element(
            Tracks,
            EbmlWriter.Element(TrackEntry, EbmlWriter.UInt(0xD7, 1), EbmlWriter.UInt(0x73C5, 1), EbmlWriter.UInt(0x83, 2), EbmlWriter.Text(0x86, "A_PCM/INT/LIT"),
                EbmlWriter.Element(0xE1, EbmlWriter.Float(0xB5, ContainerAudioRate), EbmlWriter.UInt(0x9F, 1), EbmlWriter.UInt(0x6264, 16))),
            EbmlWriter.Element(TrackEntry, EbmlWriter.UInt(0xD7, 2), EbmlWriter.UInt(0x73C5, 2), EbmlWriter.UInt(0x83, 1), EbmlWriter.Text(0x86, "V_MPEG4/ISO/AVC"),
                EbmlWriter.UInt(0x23E383, 40_000_000), EbmlWriter.Element(0xE0, EbmlWriter.UInt(0xB0, 64), EbmlWriter.UInt(0xBA, 36))));
        var clusters = new List<byte[]>();
        var clusterCount = (int)Math.Ceiling(seconds);
        for (var c = 0; c < clusterCount; c++)
        {
            var children = new List<byte[]> { EbmlWriter.UInt(0xE7, (ulong)(c * 1000)) };
            // Sound every 100 ms and a picture every 40 ms, written in time order.
            for (var ms = 0; ms < 1000; ms += 20)
            {
                var at = ((c * 1000) + ms) / 100 * AudioBlock;
                if (ms % 100 == 0 && at < audio.Length)
                {
                    children.Add(EbmlWriter.Element(0xA3, EbmlWriter.Block(1, (short)ms, 0x80, 0, audio[at..Math.Min(audio.Length, at + AudioBlock)])));
                }

                var frame = ((c * 1000) + ms) / 40;
                if (ms % 40 == 0 && frame < video.Count)
                {
                    children.Add(EbmlWriter.Element(0xA3, EbmlWriter.Block(2, (short)ms, frame % KeyframeInterval == 0 ? (byte)0x80 : (byte)0, 0, video[frame])));
                }
            }

            clusters.Add(EbmlWriter.Element(Cluster, [.. children]));
        }

        var tags = EbmlWriter.Element(Tags, EbmlWriter.Element(0x7373, EbmlWriter.Element(0x63C0), EbmlWriter.Element(0x67C8, EbmlWriter.Text(0x45A3, "TITLE"), EbmlWriter.Text(0x4487, "Damage"))));
        byte[] Head(long cues, long tagsAt) => EbmlWriter.Element(
            SeekHead,
            EbmlWriter.Element(0x4DBB, EbmlWriter.Raw(0x53AB, 0x1C, 0x53, 0xBB, 0x6B), EbmlWriter.UInt64(0x53AC, (ulong)cues)),
            EbmlWriter.Element(0x4DBB, EbmlWriter.Raw(0x53AB, 0x12, 0x54, 0xC3, 0x67), EbmlWriter.UInt64(0x53AC, (ulong)tagsAt)));
        var headLength = Head(0, 0).Length;
        var positions = new List<long>();
        var offset = (long)headLength + info.Length + tracks.Length;
        foreach (var cluster in clusters)
        {
            positions.Add(offset);
            offset += cluster.Length;
        }

        var cues = EbmlWriter.Element(Cues, [.. positions.Select((position, c) => EbmlWriter.Element(0xBB, EbmlWriter.UInt(0xB3, (ulong)(c * 1000)),
            EbmlWriter.Element(0xB7, EbmlWriter.UInt(0xF7, 2), EbmlWriter.UInt64(0xF1, (ulong)position))))]);
        var segment = new List<byte[]> { Head(offset, offset + cues.Length), info, tracks };
        segment.AddRange(clusters);
        segment.Add(cues);
        segment.Add(tags);
        var file = EbmlWriter.File("matroska", segment);
        var segmentData = file.Length - (int)segment.Sum(part => (long)part.Length);
        return new HealthyFile("mkv", ".mkv", file, audio.Length / 2.0 / ContainerAudioRate, video.Count)
        {
            Landmarks = new Dictionary<string, IReadOnlyList<(int, int)>>
            {
                ["cluster"] = [.. positions.Select((position, c) => (segmentData + (int)position, clusters[c].Length))],
                ["cues"] = [(segmentData + (int)offset, cues.Length)],
                ["tags"] = [(segmentData + (int)offset + cues.Length, tags.Length)],
                ["segment-data"] = [(segmentData, file.Length - segmentData)],
            },
        };
    }

    /// <summary>The committed Vorbis fixture's sound pages repeated (positions and sequence numbers carried on, CRCs recomputed) to the length asked for.</summary>
    public static HealthyFile Ogg(double seconds)
    {
        var fixture = System.IO.File.ReadAllBytes(RepoPaths.Combine("tests", "fixtures", "ogg", "vorbis.ogg"));
        var pages = OggPages.Split(fixture);
        var headers = pages.TakeWhile(page => page.Granule == 0).ToList();
        var sound = pages.Skip(headers.Count).ToList();
        // The identification header: "\x01vorbis", the version (4 bytes), channels (1), then the rate.
        var rate = BinaryPrimitives.ReadInt32LittleEndian(headers[0].Body.AsSpan(12));
        var lastGranule = sound[^1].Granule;
        var copies = Math.Max(1, (int)Math.Ceiling(seconds * rate / lastGranule));
        var output = new List<OggPageData>(headers);
        var sequence = headers.Count;
        for (var copy = 0; copy < copies; copy++)
        {
            foreach (var page in sound)
            {
                var flags = (byte)(page.Flags & ~0x04);
                if (copy == copies - 1 && page == sound[^1])
                {
                    flags |= 0x04;
                }

                output.Add(page with { Flags = flags, Granule = page.Granule < 0 ? page.Granule : page.Granule + (copy * lastGranule), Sequence = sequence++ });
            }
        }

        var file = OggPages.Join(output);
        return new HealthyFile("ogg", ".ogg", file, copies * lastGranule / (double)rate, 0)
        {
            Landmarks = new Dictionary<string, IReadOnlyList<(int, int)>> { ["pages"] = OggPages.Offsets(output) },
        };
    }

    /// <summary>16-bit mono PCM at <see cref="ContainerAudioRate"/>: a tone whose pitch rises each second, so a lost second is audible.</summary>
    private static byte[] AudioBytes(double seconds)
    {
        var count = (int)(ContainerAudioRate * seconds);
        var bytes = new byte[count * 2];
        for (var i = 0; i < count; i++)
        {
            var frequency = 220 + (20 * (i / ContainerAudioRate));
            var value = (short)(Math.Sin(2 * Math.PI * frequency * i / ContainerAudioRate) * 12000);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), value);
        }

        return bytes;
    }

    /// <summary>The picture samples <see cref="CheckedVideoDecoderFactory"/> checks.</summary>
    public static List<byte[]> VideoSamples(int count)
    {
        var random = new Random(11);
        var samples = new List<byte[]>(count);
        for (var i = 0; i < count; i++)
        {
            samples.Add(CheckedVideoDecoderFactory.Sample(i % KeyframeInterval == 0, (byte)i, 300 + random.Next(500), random));
        }

        return samples;
    }
}
