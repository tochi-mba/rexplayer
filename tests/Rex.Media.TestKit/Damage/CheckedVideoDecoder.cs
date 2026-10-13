using System.Buffers.Binary;
using Rex.Media.Codecs;
using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.TestKit.Damage;

/// <summary>
/// Stands in for an H.264 decoder in the damaged-file corpus. Its samples are NAL units with 4-byte
/// lengths, as MP4 and Matroska store H.264, each ending in a checksum of its bytes; a sample whose
/// lengths do not add up, whose NAL headers are impossible or whose checksums fail is refused as a
/// real decoder refuses a damaged picture. A picture is "clean" when it and every picture since its
/// keyframe checked out, which is what a viewer would see without smearing.
/// </summary>
public sealed class CheckedVideoDecoderFactory : IDecoderFactory
{
    private int _valid;
    private int _clean;
    private int _refused;

    public int Valid => Volatile.Read(ref _valid);

    public int Clean => Volatile.Read(ref _clean);

    public int Refused => Volatile.Read(ref _refused);

    public string Name => "checked";

    public DecoderSource Source => DecoderSource.Own;

    public int Rank => 100;

    public bool CanDecode(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.Codec == CodecId.H264;
    }

    public IAudioDecoder CreateAudio(TrackInfo track) => throw new NotSupportedException("Pictures only.");

    public IVideoDecoder CreateVideo(TrackInfo track) => new Decoder(this);

    /// <summary>A sample of about <paramref name="size"/> bytes: two NAL units, IDR slices for a keyframe.</summary>
    public static byte[] Sample(bool keyframe, byte tag, int size, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var first = Math.Max(4, size / 3);
        var second = Math.Max(4, size - first);
        return [.. Nal(keyframe ? (byte)0x65 : (byte)0x41, tag, first, random), .. Nal(keyframe ? (byte)0x65 : (byte)0x41, tag, second, random)];
    }

    private static byte[] Nal(byte header, byte tag, int length, Random random)
    {
        var nal = new byte[4 + length];
        BinaryPrimitives.WriteInt32BigEndian(nal, length);
        nal[4] = header;
        nal[5] = tag;
        random.NextBytes(nal.AsSpan(6, length - 3));
        nal[^1] = Checksum(nal.AsSpan(4, length - 1));
        return nal;
    }

    private static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        byte sum = 0x5A;
        foreach (var b in bytes)
        {
            sum = (byte)((sum << 1 | sum >> 7) ^ b);
        }

        return sum;
    }

    /// <summary>Whether a sample is whole: its NAL lengths fill it exactly and every NAL checks out.</summary>
    public static bool Check(ReadOnlySpan<byte> sample, out bool keyframe)
    {
        keyframe = false;
        var at = 0;
        var count = 0;
        while (at < sample.Length)
        {
            if (at + 4 > sample.Length)
            {
                return false;
            }

            var length = BinaryPrimitives.ReadInt32BigEndian(sample[at..]);
            at += 4;
            if (length < 3 || length > sample.Length - at)
            {
                return false;
            }

            var nal = sample.Slice(at, length);
            if (nal[0] is not (0x65 or 0x41) || Checksum(nal[..^1]) != nal[^1])
            {
                return false;
            }

            keyframe = nal[0] == 0x65;
            at += length;
            count++;
        }

        return count > 0;
    }

    private sealed class Decoder(CheckedVideoDecoderFactory factory) : IVideoDecoder
    {
        private bool _chainIntact;

        public string Name => "checked";

        public DecoderSource Source => DecoderSource.Own;

        public void Decode(Packet packet, ICollection<VideoFrame> output)
        {
            if (!Check(packet.Data.Span, out var keyframe))
            {
                Interlocked.Increment(ref factory._refused);
                _chainIntact = false;
                throw new MediaFormatException("A damaged picture.");
            }

            Interlocked.Increment(ref factory._valid);
            _chainIntact = keyframe || _chainIntact;
            if (_chainIntact)
            {
                Interlocked.Increment(ref factory._clean);
            }

            var frame = VideoFrame.Rent(PixelFormat.Gray8, 16, 8);
            frame.Pts = packet.Pts;
            frame.Duration = packet.Duration;
            output.Add(frame);
        }

        public bool Drain(ICollection<VideoFrame> output) => true;

        public void Flush() => _chainIntact = false;

        public void Dispose()
        {
        }
    }
}
