using Rex.Media.Codecs;
using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.TestKit;

/// <summary>
/// Decodes H.264 tracks into small grey pictures whose every byte is the packet's first byte, so a
/// test can tell which packet a picture came from. Options make it hold pictures, fail, or go quiet.
/// </summary>
public sealed class FakeVideoDecoderFactory : IDecoderFactory
{
    private int _decoded;

    public int Decoded => Volatile.Read(ref _decoded);

    /// <summary>Pictures carry no time.</summary>
    public bool Untimed { get; init; }

    /// <summary>Pictures are only given at the drain, as a reordering decoder may.</summary>
    public bool Hold { get; init; }

    /// <summary>No pictures at all.</summary>
    public bool Silent { get; init; }

    /// <summary>Throws on the packet after this many.</summary>
    public int FailAfter { get; init; } = int.MaxValue;

    /// <summary>Gives the failing packet's picture before throwing, as a decoder failing part-way may.</summary>
    public bool FailAfterGiving { get; init; }

    /// <summary>The drain gives held pictures one per call, asking to be called again.</summary>
    public bool DrainOneAtATime { get; init; }

    /// <summary>Packets whose first byte is this fail to decode.</summary>
    public int? BrokenByte { get; init; }

    public string Name => "grey";

    public DecoderSource Source => DecoderSource.Own;

    public int Rank => 100;

    public bool CanDecode(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.Codec == CodecId.H264;
    }

    public IAudioDecoder CreateAudio(TrackInfo track) => throw new NotSupportedException("The fake decodes pictures only.");

    public IVideoDecoder CreateVideo(TrackInfo track) => new Decoder(this);

    private sealed class Decoder(FakeVideoDecoderFactory factory) : IVideoDecoder
    {
        private readonly List<VideoFrame> _held = [];

        public string Name => "grey";

        public DecoderSource Source => DecoderSource.Own;

        public void Decode(Packet packet, ICollection<VideoFrame> output)
        {
            if (factory.Decoded >= factory.FailAfter && !factory.FailAfterGiving)
            {
                throw new MediaFormatException("broken");
            }

            if (packet.Data.Span[0] == factory.BrokenByte)
            {
                throw new MediaFormatException("a broken picture");
            }

            var decoded = Interlocked.Increment(ref factory._decoded);
            if (factory.Silent)
            {
                return;
            }

            var frame = VideoFrame.Rent(PixelFormat.Gray8, 16, 8);
            frame.Plane(0).Fill(packet.Data.Span[0]);
            frame.Pts = factory.Untimed ? MediaTime.Unknown : packet.Pts;
            frame.Duration = packet.Duration;
            (factory.Hold ? _held : output).Add(frame);
            if (decoded > factory.FailAfter)
            {
                throw new MediaFormatException("broken part-way");
            }
        }

        /// <summary>Gives held pictures one at a time when asked to, as a decoder short of surfaces does.</summary>
        public bool Drain(ICollection<VideoFrame> output)
        {
            if (factory.DrainOneAtATime && _held.Count > 1)
            {
                output.Add(_held[0]);
                _held.RemoveAt(0);
                return false;
            }

            _held.ForEach(output.Add);
            _held.Clear();
            return true;
        }

        public void Flush()
        {
            _held.ForEach(frame => frame.Dispose());
            _held.Clear();
        }

        public void Dispose() => Flush();
    }
}

/// <summary>A presenter that records each picture shown: its time and its first byte.</summary>
public sealed class RecordingVideoPresenter : IVideoPresenter
{
    private readonly List<(MediaTime Pts, byte First)> _shown = [];

    public string Name => "recording";

    public bool Disposed { get; private set; }

    public IReadOnlyList<(MediaTime Pts, byte First)> Shown
    {
        get
        {
            lock (_shown)
            {
                return [.. _shown];
            }
        }
    }

    public void Present(VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (_shown)
        {
            _shown.Add((frame.Pts, frame.Row(0, 0)[0]));
        }
    }

    public void Dispose() => Disposed = true;
}
