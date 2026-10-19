// Spec: none. This file writes edited tags into a file safely, using the tag writers and demuxers beside it.
using Rex.Media.Containers.Flac;
using Rex.Media.Containers.Mpeg;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Containers.Tags;

/// <summary>How writing a song's details into its file went.</summary>
public enum TagWriteStatus
{
    /// <summary>The file holds the new details.</summary>
    Written,

    /// <summary>rexplayer cannot write this kind of file; keep the edit in the library only.</summary>
    Unsupported,

    /// <summary>The file's tags are damaged or the edit cannot be stored safely; the file is untouched.</summary>
    Refused,

    /// <summary>Another program (or playback) has the file; try again once it is free.</summary>
    Busy,
}

/// <summary>The outcome of one write, with a sentence for the person.</summary>
public sealed record TagWriteResult(TagWriteStatus Status, string Message);

/// <summary>
/// Writes a <see cref="TagEdit"/> into an MP3 (ID3v2.3/2.4, and ID3v1 when present) or FLAC file.
/// The new file is written beside the original under a temporary name, opened again with rexplayer's
/// own demuxer to prove it reads back with the new details, compared byte for byte over the audio,
/// and only then swapped in with one rename that keeps the original's creation time. Any doubt leaves
/// the original exactly as it was.
/// </summary>
public static class TagFileWriter
{
    /// <summary>What the dialog says when a file cannot be written to.</summary>
    public const string LibraryOnlyMessage = "Saved in rexplayer only; this file type can't be written to.";

    private const int CopyBuffer = 1 << 20;

    /// <summary>Whether rexplayer can write details into files with this name's extension.</summary>
    public static bool CanWrite(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Format(path) is not null;
    }

    /// <summary>Writes the edit into <paramref name="path"/>; the file is either fully updated or untouched.</summary>
    public static TagWriteResult Write(string path, TagEdit edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(edit);
        if (Format(path) is not { } format)
        {
            return new TagWriteResult(TagWriteStatus.Unsupported, LibraryOnlyMessage);
        }

        if (edit.IsEmpty)
        {
            return new TagWriteResult(TagWriteStatus.Written, "Nothing changed.");
        }

        var full = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(full)!, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.rex-tags.tmp");
        try
        {
            if ((File.GetAttributes(full) & FileAttributes.ReadOnly) != 0)
            {
                return new TagWriteResult(TagWriteStatus.Refused, "The file is read-only.");
            }

            var before = (File.GetLastWriteTimeUtc(full), new FileInfo(full).Length);
            using (var original = new FileByteSource(full))
            {
                Open(format, original, cancellationToken).Dispose();
                var plan = Plan(format, original, edit, cancellationToken);
                WriteCopy(temporary, original, plan, cancellationToken);
                if (!Verify(temporary, format, original, plan, edit, cancellationToken))
                {
                    File.Delete(temporary);
                    return new TagWriteResult(TagWriteStatus.Refused, "The rewritten file did not read back exactly; the original is untouched.");
                }
            }

            if ((File.GetLastWriteTimeUtc(full), new FileInfo(full).Length) != before)
            {
                File.Delete(temporary);
                return new TagWriteResult(TagWriteStatus.Busy, "The file changed while its details were written; try again.");
            }

            File.SetCreationTimeUtc(temporary, File.GetCreationTimeUtc(full));
            File.Replace(temporary, full, destinationBackupFileName: null, ignoreMetadataErrors: true);
            return new TagWriteResult(TagWriteStatus.Written, "Saved into the file.");
        }
        catch (TagWriteException exception)
        {
            File.Delete(temporary);
            return new TagWriteResult(TagWriteStatus.Refused, exception.Message);
        }
        catch (MediaFormatException exception)
        {
            File.Delete(temporary);
            return new TagWriteResult(TagWriteStatus.Refused, "The file is damaged: " + exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            File.Delete(temporary);
            return new TagWriteResult(TagWriteStatus.Busy, "The file could not be written now: " + exception.Message);
        }
    }

    private static string? Format(string path) => Path.GetExtension(path).ToUpperInvariant() switch
    {
        ".MP3" => "mp3",
        ".FLAC" => "flac",
        _ => null,
    };

    private static IDemuxer Open(string format, IByteSource source, CancellationToken cancellationToken) =>
        format == "mp3" ? new MpegAudioDemuxer(source, cancellationToken) : new FlacDemuxer(source, cancellationToken);

    /// <summary>The new head, where the audio starts and ends in the original, and the new tail.</summary>
    private static WritePlan Plan(string format, IByteSource source, TagEdit edit, CancellationToken cancellationToken)
    {
        var length = source.Length!.Value;
        if (format == "flac")
        {
            var metadataLength = FlacTagWriter.MetadataLength(source, cancellationToken);
            var rewrite = FlacTagWriter.Rewrite(Read(source, 0, metadataLength, cancellationToken), edit);
            return new WritePlan(rewrite, metadataLength, length, []);
        }

        var leading = Id3v2.LeadingTagsLength(source, cancellationToken);
        if (leading > Id3v2Writer.MaxTagBytes)
        {
            throw new TagWriteException("The file's tag is too large to rewrite.");
        }

        var tag = Id3v2Writer.Rewrite(Read(source, 0, leading, cancellationToken), edit);
        var tail = length - leading >= Id3v1.Size ? Read(source, length - Id3v1.Size, Id3v1.Size, cancellationToken) : [];
        if (Id3v1.IsTag(tail))
        {
            return new WritePlan(tag, leading, length - Id3v1.Size, Id3v1Writer.Update(tail, edit));
        }

        return new WritePlan(tag, leading, length, []);
    }

    private static byte[] Read(IByteSource source, long offset, long count, CancellationToken cancellationToken)
    {
        var bytes = new byte[count];
        if (source.Read(offset, bytes, cancellationToken) != count)
        {
            throw new TagWriteException("The file ended while it was read.");
        }

        return bytes;
    }

    private static void WriteCopy(string temporary, IByteSource original, WritePlan plan, CancellationToken cancellationToken)
    {
        using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBuffer);
        output.Write(plan.Head.Bytes);
        var buffer = new byte[CopyBuffer];
        for (var position = plan.AudioStart; position < plan.AudioEnd;)
        {
            var read = original.Read(position, buffer.AsSpan(0, (int)Math.Min(buffer.Length, plan.AudioEnd - position)), cancellationToken);
            if (read == 0)
            {
                throw new TagWriteException("The file ended while it was copied.");
            }

            output.Write(buffer, 0, read);
            position += read;
        }

        output.Write(plan.Tail);
        output.Flush(flushToDisk: true);
    }

    /// <summary>Whether the copy opens, shows the edit, and holds the original's audio byte for byte.</summary>
    private static bool Verify(string temporary, string format, IByteSource original, WritePlan plan, TagEdit edit, CancellationToken cancellationToken)
    {
        using var copy = new FileByteSource(temporary);
        var audioLength = plan.AudioEnd - plan.AudioStart;
        if (copy.Length != plan.Head.Bytes.Length + audioLength + plan.Tail.Length)
        {
            return false;
        }

        using (var demuxer = Open(format, copy, cancellationToken))
        {
            foreach (var (key, value) in plan.Head.Expected)
            {
                var found = demuxer.Info.Metadata.TryGetValue(key, out var shown) ? shown : null;
                if (found != value)
                {
                    return false;
                }
            }

            if (edit.ChangesCover && !(demuxer.Info.CoverArt ?? []).AsSpan().SequenceEqual(edit.Cover?.Data ?? []))
            {
                return false;
            }
        }

        var left = new byte[CopyBuffer];
        var right = new byte[CopyBuffer];
        for (long done = 0; done < audioLength;)
        {
            var count = (int)Math.Min(CopyBuffer, audioLength - done);
            var a = original.Read(plan.AudioStart + done, left.AsSpan(0, count), cancellationToken);
            var b = copy.Read(plan.Head.Bytes.Length + done, right.AsSpan(0, count), cancellationToken);
            if (a != count || b != count || !left.AsSpan(0, count).SequenceEqual(right.AsSpan(0, count)))
            {
                return false;
            }

            done += count;
        }

        return true;
    }

    private sealed record WritePlan(TagRewrite Head, long AudioStart, long AudioEnd, byte[] Tail);
}
