using System.Globalization;
using Rex.Media.Codecs;
using Rex.Media.Containers;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.Video;

namespace Rex.Media.AppCore;

/// <summary>
/// Takes the picture a file shows at a moment (PB-16): the last one presented at or before it, or
/// the first picture when the moment comes before any. The demuxer seeks to the keyframe before
/// the moment and the decoder runs forward from there, so the picture is exact, not the keyframe.
/// </summary>
public static class Snapshot
{
    /// <summary>
    /// Saves the picture <paramref name="source"/> shows at <paramref name="at"/> as a PNG at
    /// <paramref name="output"/>, making its folder if needed.
    /// </summary>
    public static SavedSnapshot SaveAsPng(IByteSource source, DecoderRegistry decoders, MediaTime at, string output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var demuxer = MediaRegistries.Demuxers().Open(source, cancellationToken);
        var (picture, decoder) = Take(demuxer, decoders, at, cancellationToken);
        using (picture)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            using var file = File.Create(output);
            PngWriter.Write(file, picture);
            return new SavedSnapshot(output, picture.Width, picture.Height, picture.Pts, decoder);
        }
    }

    /// <summary>
    /// A file name for a snapshot of <paramref name="title"/> at <paramref name="at"/> in
    /// <paramref name="folder"/>, such as "Sungba 0-01-23.png", numbered when that name is taken.
    /// Characters Windows does not allow in file names become underscores.
    /// </summary>
    public static string FileFor(string folder, string title, TimeSpan at, Func<string, bool>? exists = null,
        string? pattern = null, bool sequential = true)
    {
        exists ??= File.Exists;
        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        var safe = new string([.. title.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c)]).Trim().TrimEnd('.');
        safe = safe.Length > 0 ? safe : "snapshot";
        var timestamp = string.Create(CultureInfo.InvariantCulture, $"{(int)at.TotalHours}-{at.Minutes:00}-{at.Seconds:00}");
        var template = string.IsNullOrWhiteSpace(pattern) ? "{title} {time}" : pattern.Trim();
        var numberedToken = template.Contains("{n}", StringComparison.Ordinal);

        string FileAt(int sequence)
        {
            var name = template.Replace("{title}", safe, StringComparison.Ordinal)
                .Replace("{time}", timestamp, StringComparison.Ordinal)
                .Replace("{n}", sequence.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            var cleaned = new string([.. name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c)]).Trim().TrimEnd('.');
            if (cleaned.Length == 0) cleaned = safe + " " + timestamp;
            if (sequential && !numberedToken && sequence > 1)
                cleaned += " (" + sequence.ToString(CultureInfo.InvariantCulture) + ")";
            return Path.Combine(folder, cleaned + ".png");
        }

        var result = FileAt(1);
        if (sequential)
        {
            for (var n = 2; exists(result); n++)
                result = FileAt(n);
        }

        return result;
    }

    /// <summary>
    /// The picture at <paramref name="at"/>, and the decoder that made it. The caller disposes the
    /// picture. With <paramref name="nearest"/> it is the first picture decoded after the seek (the
    /// key picture before the moment), which a thumbnail takes because it needs no exact moment and
    /// is spared decoding up to it.
    /// </summary>
    public static (VideoFrame Picture, string Decoder) Take(IDemuxer demuxer, DecoderRegistry decoders, MediaTime at, CancellationToken cancellationToken, bool nearest = false)
    {
        ArgumentNullException.ThrowIfNull(demuxer);
        ArgumentNullException.ThrowIfNull(decoders);
        var track = demuxer.Info.FirstTrack(MediaKind.Video) ?? throw new NotSupportedException("The file has no video to take a picture of.");
        var opened = decoders.CreateVideo(track);
        using var decoder = opened.Decoder ?? throw new NotSupportedException(opened.Reason);
        if (at > MediaTime.Zero && demuxer.Info.IsSeekable)
        {
            demuxer.Seek(at, cancellationToken);
        }

        VideoFrame? chosen = null;
        var frames = new List<VideoFrame>();
        var done = false;
        try
        {
            while (!done && demuxer.ReadPacket(cancellationToken) is { } packet)
            {
                using (packet)
                {
                    if (packet.TrackId == track.Id)
                    {
                        decoder.Decode(packet, frames);
                    }
                }

                done = Choose(frames, at, ref chosen) || (nearest && chosen is not null);
            }

            while (!done)
            {
                var finished = decoder.Drain(frames);
                done = Choose(frames, at, ref chosen) || finished;
            }
        }
        catch
        {
            chosen?.Dispose();
            frames.ForEach(frame => frame.Dispose());
            throw;
        }

        return (chosen ?? throw new MediaFormatException("The video track gave no picture."), decoder.Name);
    }

    /// <summary>Keeps the latest picture at or before the moment; true once a later one shows the search is over.</summary>
    private static bool Choose(List<VideoFrame> frames, MediaTime at, ref VideoFrame? chosen)
    {
        var done = false;
        foreach (var frame in frames)
        {
            if (!done && (chosen is null || !frame.Pts.IsKnown || frame.Pts <= at))
            {
                chosen?.Dispose();
                chosen = frame;
                done = frame.Pts.IsKnown && frame.Pts > at;
                continue;
            }

            done |= frame.Pts > at;
            frame.Dispose();
        }

        frames.Clear();
        return done;
    }
}

/// <summary>Where a snapshot went, its size, the moment of the picture and the decoder that made it.</summary>
public sealed record SavedSnapshot(string Path, int Width, int Height, MediaTime At, string Decoder);
