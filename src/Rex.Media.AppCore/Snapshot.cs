using Rex.Media.Codecs;
using Rex.Media.Containers;
using Rex.Media.Primitives;

namespace Rex.Media.AppCore;

/// <summary>
/// Takes the picture a file shows at a moment (PB-16): the last one presented at or before it, or
/// the first picture when the moment comes before any. The demuxer seeks to the keyframe before
/// the moment and the decoder runs forward from there, so the picture is exact, not the keyframe.
/// </summary>
public static class Snapshot
{
    /// <summary>The picture at <paramref name="at"/>, and the decoder that made it. The caller disposes the picture.</summary>
    public static (VideoFrame Picture, string Decoder) Take(IDemuxer demuxer, DecoderRegistry decoders, MediaTime at, CancellationToken cancellationToken)
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

                done = Choose(frames, at, ref chosen);
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
