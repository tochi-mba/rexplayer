using System.Globalization;
using Rex.Media.Audio;
using Rex.Media.Codecs;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Player;

/// <summary>
/// Bounded, cancellable independent decoding for episode fingerprinting. The playback session is
/// never touched. The caller supplies the same decoder ladder the host uses (including Windows
/// decoders when available) and runs this work off the playback thread.
/// </summary>
public static class SectionAnalysis
{
    /// <summary>Content identity for invalidating a cached recording after it changes on disk.</summary>
    public static string SignatureOf(string path)
    {
        var info = new FileInfo(path);
        return string.Create(CultureInfo.InvariantCulture, $"{info.Length}:{info.LastWriteTimeUtc.Ticks}");
    }

    public static EpisodeAnalysis Analyze(string path, DecoderRegistry decoders, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(decoders);
        cancellationToken.ThrowIfCancellationRequested();
        using var source = new FileByteSource(path);
        using var demuxer = MediaRegistries.Demuxers().Open(source, cancellationToken);
        var duration = demuxer.Info.Duration.IsKnown ? demuxer.Info.Duration.ToTimeSpan() : TimeSpan.Zero;
        var signature = SignatureOf(path);
        if (duration < TimeSpan.FromMinutes(6))
        {
            return new EpisodeAnalysis(path, signature, duration, [], [], []);
        }

        var opening = FingerprintWindow(path, decoders, TimeSpan.Zero,
            TimeSpan.FromMinutes(8), cancellationToken);
        var endStart = duration > TimeSpan.FromMinutes(6) ? duration - TimeSpan.FromMinutes(6) : TimeSpan.Zero;
        var closing = FingerprintWindow(path, decoders, endStart, duration, cancellationToken);
        var frames = FramesNearEnd(path, decoders, endStart, duration, cancellationToken);
        return new EpisodeAnalysis(path, signature, duration, opening, closing, frames);
    }

    private static IReadOnlyList<AudioLandmark> FingerprintWindow(string path, DecoderRegistry decoders,
        TimeSpan start, TimeSpan end, CancellationToken token)
    {
        using var source = new FileByteSource(path);
        using var demuxer = MediaRegistries.Demuxers().Open(source, token);
        if (demuxer.Info.FirstTrack(MediaKind.Audio) is not { } track)
        {
            return [];
        }

        using var decoder = decoders.CreateAudio(track).Decoder;
        if (decoder is null)
        {
            return [];
        }

        if (start > TimeSpan.Zero && demuxer.Info.IsSeekable)
        {
            demuxer.Seek(new MediaTime(start.Ticks), token);
        }

        var mono = new List<float>(Math.Min(1_000_000, (int)Math.Ceiling((end - start).TotalSeconds * AudioLandmarks.Rate)));
        var frames = new List<AudioFrame>();
        Resampler? resampler = null;
        var done = false;
        try
        {
            while (!done && demuxer.ReadPacket(token) is { } packet)
            {
                using (packet)
                {
                    if (packet.TrackId == track.Id)
                    {
                        decoder.Decode(packet, frames);
                    }
                }

                done = ConsumeAudio(frames, mono, ref resampler, start, end);
                token.ThrowIfCancellationRequested();
            }

            if (!done)
            {
                decoder.Drain(frames);
                ConsumeAudio(frames, mono, ref resampler, start, end);
            }
        }
        finally
        {
            frames.ForEach(frame => frame.Dispose());
        }

        var offset = (int)Math.Round(start.TotalSeconds / AudioLandmarks.SecondsPerFrame);
        return AudioLandmarks.Fingerprint([.. mono], offset);
    }

    private static bool ConsumeAudio(List<AudioFrame> frames, List<float> output, ref Resampler? resampler,
        TimeSpan start, TimeSpan end)
    {
        var wanted = (int)Math.Ceiling((end - start).TotalSeconds * AudioLandmarks.Rate);
        var done = false;
        foreach (var frame in frames)
        {
            using (frame)
            {
                if (done || frame.Pts.IsKnown && frame.Pts.ToTimeSpan() >= end)
                {
                    done = true;
                    continue;
                }

                if (frame.Pts.IsKnown && frame.Pts.ToTimeSpan() + frame.Duration.ToTimeSpan() <= start)
                {
                    continue;
                }

                var cut = frame.Pts.IsKnown ? (int)Math.Clamp(
                    Math.Ceiling((start - frame.Pts.ToTimeSpan()).TotalSeconds * frame.SampleRate), 0, frame.SampleCount) : 0;
                if (cut >= frame.SampleCount)
                {
                    continue;
                }

                using var single = AudioFrame.Rent(frame.SampleRate, 1, frame.SampleCount - cut);
                single.Pts = frame.Pts.IsKnown ? frame.Pts + MediaTime.FromSamples(cut, frame.SampleRate) : MediaTime.Unknown;
                var dest = single.Channel(0);
                for (var sample = 0; sample < dest.Length; sample++)
                {
                    var total = 0f;
                    for (var channel = 0; channel < frame.Channels; channel++)
                    {
                        total += frame.Channel(channel)[sample + cut];
                    }

                    dest[sample] = total / frame.Channels;
                }

                resampler ??= new Resampler(frame.SampleRate, AudioLandmarks.Rate, 1, ResamplerQuality.Fast);
                using var small = resampler.Process(single);
                if (small is not null)
                {
                    var take = Math.Min(small.SampleCount, wanted - output.Count);
                    for (var i = 0; i < take; i++)
                    {
                        output.Add(small.Channel(0)[i]);
                    }
                }

                done = output.Count >= wanted;
            }
        }

        frames.Clear();
        return done;
    }

    private static IReadOnlyList<CreditFrameSample> FramesNearEnd(string path, DecoderRegistry decoders,
        TimeSpan start, TimeSpan end, CancellationToken token)
    {
        using var source = new FileByteSource(path);
        using var demuxer = MediaRegistries.Demuxers().Open(source, token);
        if (demuxer.Info.FirstTrack(MediaKind.Video) is not { } track)
        {
            return [];
        }

        using var decoder = decoders.CreateVideo(track).Decoder;
        if (decoder is null)
        {
            return [];
        }

        if (start > TimeSpan.Zero && demuxer.Info.IsSeekable)
        {
            demuxer.Seek(new MediaTime(start.Ticks), token);
        }

        var output = new List<CreditFrameSample>();
        var frames = new List<VideoFrame>();
        var next = start;
        try
        {
            while (next < end && demuxer.ReadPacket(token) is { } packet)
            {
                using (packet)
                {
                    if (packet.TrackId == track.Id)
                    {
                        decoder.Decode(packet, frames);
                    }
                }

                foreach (var frame in frames)
                {
                    using (frame)
                    {
                        if (!frame.Pts.IsKnown || frame.Pts.ToTimeSpan() < next || frame.Surface is not null)
                        {
                            continue;
                        }

                        var at = frame.Pts.ToTimeSpan();
                        if (at < end)
                        {
                            output.Add(CreditFrameEvidence.Measure(frame, at));
                            next = at + TimeSpan.FromSeconds(1);
                        }
                    }
                }

                frames.Clear();
                token.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            frames.ForEach(frame => frame.Dispose());
        }

        return output;
    }
}
