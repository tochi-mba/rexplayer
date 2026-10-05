// Spec: none. This file defines the engine's demuxer contract, not a published format.
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Containers;

/// <summary>
/// Splits a container into packets. One demuxer is owned by one thread (the engine's demux thread),
/// so implementations need no locking. Bad bytes surface as <see cref="MediaFormatException"/>.
/// </summary>
public interface IDemuxer : IDisposable
{
    MediaInfo Info { get; }

    /// <summary>The next packet in file order, or null at the end of the stream.</summary>
    Packet? ReadPacket(CancellationToken cancellationToken);

    /// <summary>
    /// Positions the demuxer so the next packets start at a keyframe at or before
    /// <paramref name="target"/>. Decoders then discard up to the exact target for a precise seek.
    /// </summary>
    void Seek(MediaTime target, CancellationToken cancellationToken);
}

/// <summary>Recognises one container format and opens it.</summary>
public interface IDemuxerFactory
{
    /// <summary>The format's name, as used in logs and the --demux option.</summary>
    string Name { get; }

    /// <summary>
    /// How sure the factory is that the bytes are its format: 0 for "not mine", 100 for a signature
    /// match. The extension only breaks ties; a renamed file still opens.
    /// </summary>
    int Probe(ReadOnlySpan<byte> head, string? extension);

    IDemuxer Open(IByteSource source, CancellationToken cancellationToken);
}
