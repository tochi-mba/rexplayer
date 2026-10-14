using System.Diagnostics;
using Rex.Media.AppCore;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.TestKit.Damage;

/// <summary>How one damaged file fared.</summary>
public enum DamageResult
{
    /// <summary>Opened and read to its end (however much of it played).</summary>
    Played,

    /// <summary>Refused cleanly with a <see cref="MediaFormatException"/> (or, in a session, a plain failure).</summary>
    Refused,

    /// <summary>Anything else escaped: a crash of the app.</summary>
    Crashed,

    /// <summary>Not finished within the time allowed: a hang, or slowness as bad as one.</summary>
    Hung,
}

/// <summary>What a run over one file measured.</summary>
/// <param name="Result">How it ended.</param>
/// <param name="Error">What escaped or refused it, when something did.</param>
/// <param name="AudioSeconds">Sound decoded (or, in a session, played).</param>
/// <param name="Pictures">Pictures decoded (or shown).</param>
/// <param name="CleanPictures">Pictures whose whole chain since a keyframe was whole.</param>
/// <param name="AllocatedBytes">Memory allocated on the reading thread (exact), or by the process (a session).</param>
/// <param name="Elapsed">How long it took.</param>
/// <param name="FileBytes">The size of the damaged file.</param>
public sealed record DamageMeasure(DamageResult Result, string? Error, double AudioSeconds, int Pictures, int CleanPictures, long AllocatedBytes, TimeSpan Elapsed, long FileBytes)
{
    /// <summary>Notices the session gave about damage (a session run only).</summary>
    public int Notices { get; init; }

    /// <summary>Seconds per megabyte of file: how slow damage makes reading.</summary>
    public double SecondsPerMegabyte => Elapsed.TotalSeconds / Math.Max(FileBytes / 1e6, 0.001);
}

/// <summary>
/// Reads damaged files the way the player does, on one thread with nothing between the parts: the
/// demuxer opens the file, every packet is read, the sound decoded by rexplayer's own decoders and
/// the pictures checked by <see cref="CheckedVideoDecoderFactory"/>, then a seek to the middle reads
/// a little more. Allocation is counted exactly on the thread; a run that does not finish in the
/// time allowed is a hang and is left behind (as a background thread) rather than waited for.
/// </summary>
public static class DamageRunner
{
    private const int PacketLimit = 400_000;

    public static DamageMeasure Demux(byte[] bytes, string extension, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        DamageMeasure? measured = null;
        using var stop = new CancellationTokenSource();
        var thread = new Thread(() => measured = Measure(bytes, extension, stop.Token)) { IsBackground = true, Name = "damage runner" };
        var clock = Stopwatch.StartNew();
        thread.Start();
        if (!thread.Join(timeout))
        {
            stop.Cancel();
            return new DamageMeasure(DamageResult.Hung, $"Not finished in {timeout.TotalSeconds:0} seconds.", 0, 0, 0, 0, clock.Elapsed, bytes.Length);
        }

        return measured!;
    }

    private static DamageMeasure Measure(byte[] bytes, string extension, CancellationToken token)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        double seconds = 0;
        var video = new CheckedVideoDecoderFactory();
        DamageMeasure Done(DamageResult result, string? error) =>
            new(result, error, seconds, video.Valid, video.Clean, GC.GetAllocatedBytesForCurrentThread() - allocated, clock.Elapsed, bytes.Length);

        try
        {
            using var source = new MemoryByteSource(bytes, "damaged" + extension);
            using var demuxer = MediaRegistries.Demuxers().Open(source, token);
            var tracks = demuxer.Info.Tracks;
            var audioTrack = tracks.Where(t => t.Audio is not null).OrderByDescending(t => t.IsDefault).FirstOrDefault();
            var videoTrack = tracks.FirstOrDefault(t => t.Kind == MediaKind.Video && t.Codec == CodecId.H264);
            using var audio = audioTrack is null ? null : MediaRegistries.Decoders().CreateAudio(audioTrack).Decoder;
            using var pictures = videoTrack is null ? null : video.CreateVideo(videoTrack);
            var frames = new List<AudioFrame>();
            var shown = new List<VideoFrame>();
            void Decode(Packet packet)
            {
                try
                {
                    if (packet.TrackId == audioTrack?.Id && audio is not null)
                    {
                        audio.Decode(packet, frames);
                    }
                    else if (packet.TrackId == videoTrack?.Id && pictures is not null)
                    {
                        pictures.Decode(packet, shown);
                    }
                }
                catch (MediaFormatException)
                {
                    // A damaged packet: the player passes over it too.
                }

                Count();
            }

            void Count()
            {
                foreach (var frame in frames)
                {
                    seconds += frame.SampleCount / (double)frame.SampleRate;
                    frame.Dispose();
                }

                frames.Clear();
                shown.ForEach(picture => picture.Dispose());
                shown.Clear();
            }

            try
            {
                for (var read = 0; read < PacketLimit && demuxer.ReadPacket(token) is { } packet; read++)
                {
                    using (packet)
                    {
                        Decode(packet);
                    }
                }
            }
            catch (MediaFormatException ex)
            {
                // Reading stopped part-way: what was read is what plays.
                audio?.Flush();
                return Done(DamageResult.Played, "Stopped part-way: " + ex.Message);
            }

            audio?.Drain(frames);
            Count();
            var counted = seconds;

            // A seek into the middle must work as well as reading through did.
            var duration = demuxer.Info.Duration.IsKnown ? demuxer.Info.Duration : MediaTime.FromSeconds(1);
            demuxer.Seek(new MediaTime(duration.Ticks / 2), token);
            audio?.Flush();
            for (var read = 0; read < 50 && demuxer.ReadPacket(token) is { } packet; read++)
            {
                using (packet)
                {
                    Decode(packet);
                }
            }

            seconds = counted;
            return Done(DamageResult.Played, null);
        }
        catch (MediaFormatException ex)
        {
            return Done(seconds > 0 ? DamageResult.Played : DamageResult.Refused, ex.Message);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Done(DamageResult.Hung, "Stopped after the time allowed.");
        }
        catch (Exception ex)
        {
            return Done(DamageResult.Crashed, ex.GetType().Name + ": " + ex.Message);
        }
    }
}
