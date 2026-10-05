// Spec: none. Content sniffing across the registered demuxers.
using System.Globalization;
using System.Text;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Containers;

/// <summary>
/// Picks a demuxer by looking at the bytes, never trusting the file extension alone. Every factory
/// scores the first <see cref="ProbeSize"/> bytes after any ID3v2 tags (which can hold megabytes of
/// cover art in front of MP3 and FLAC streams); the highest score wins, and the extension only
/// separates equal scores.
/// </summary>
public sealed class DemuxerRegistry
{
    public const int ProbeSize = 64 * 1024;

    private readonly List<IDemuxerFactory> _factories = [];

    public IReadOnlyList<IDemuxerFactory> Factories => _factories;

    public DemuxerRegistry Add(IDemuxerFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factories.Add(factory);
        return this;
    }

    /// <summary>The factory that recognises the source, or null when none does.</summary>
    public IDemuxerFactory? Probe(IByteSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var head = new byte[ProbeSize];
        var length = source.Read(Tags.Id3v2.LeadingTagsLength(source, cancellationToken), head, cancellationToken);
        var extension = Path.GetExtension(source.Name);
        return Probe(head.AsSpan(0, length), extension);
    }

    public IDemuxerFactory? Probe(ReadOnlySpan<byte> head, string? extension)
    {
        IDemuxerFactory? best = null;
        var bestScore = 0;
        foreach (var factory in _factories)
        {
            var score = factory.Probe(head, extension);
            if (score > bestScore)
            {
                best = factory;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>Opens the source with the best demuxer, or explains in plain words why none fits.</summary>
    public IDemuxer Open(IByteSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var factory = Probe(source, cancellationToken) ?? throw new MediaFormatException(DescribeUnknown(source, cancellationToken));
        return factory.Open(source, cancellationToken);
    }

    private static string DescribeUnknown(IByteSource source, CancellationToken cancellationToken)
    {
        Span<byte> head = stackalloc byte[16];
        var length = source.Read(0, head, cancellationToken);
        if (length == 0)
        {
            return $"{source.Name} is empty.";
        }

        var hex = new StringBuilder();
        for (var i = 0; i < length; i++)
        {
            hex.Append(head[i].ToString("X2", CultureInfo.InvariantCulture)).Append(i < length - 1 ? " " : string.Empty);
        }

        return $"rexplayer does not recognise the format of {source.Name}. It starts with {hex}.";
    }
}
