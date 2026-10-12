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
    /// <summary>
    /// The version of the features and of the detector that reads them. It is part of every
    /// signature, so a newer rexplayer analyses again instead of trusting an older cache.
    /// </summary>
    public const int FeatureVersion = 2;

    /// <summary>Content identity for invalidating a cached recording after it changes on disk.</summary>
    public static string SignatureOf(string path)
    {
        var info = new FileInfo(path);
        return string.Create(CultureInfo.InvariantCulture, $"v{FeatureVersion}:{info.Length}:{info.LastWriteTimeUtc.Ticks}");
    }

    /// <summary>
    /// How much of the start is searched for a title sequence: twelve minutes, so a long cold open
    /// still leaves the intro inside, but never more than 35 % of a short episode.
    /// </summary>
    public static TimeSpan OpeningSpan(TimeSpan duration) =>
        TimeSpan.FromTicks(Math.Min(TimeSpan.FromMinutes(12).Ticks, duration.Ticks / 100 * 35));

    /// <summary>How much of the end is searched for credits: six minutes, at most 40 % of the file.</summary>
    public static TimeSpan ClosingSpan(TimeSpan duration) =>
        TimeSpan.FromTicks(Math.Min(TimeSpan.FromMinutes(6).Ticks, duration.Ticks / 10 * 4));

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

        var opening = FingerprintWindow(path, decoders, TimeSpan.Zero, OpeningSpan(duration), cancellationToken);
        var endStart = duration - ClosingSpan(duration);
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

    /// <summary>
    /// Keyframes across the closing window, then one picture a second only where the evidence
    /// changes between keyframes (<see cref="CreditFrameEvidence.RefinementWindows"/>). Decoding every
    /// picture of six minutes of 10-bit HEVC in software took about seven minutes an episode;
    /// keyframes and a few short windows take a fraction of that.
    /// </summary>
    private static IReadOnlyList<CreditFrameSample> FramesNearEnd(string path, DecoderRegistry decoders,
        TimeSpan start, TimeSpan end, CancellationToken token)
    {
        var keyframes = Pictures(path, decoders, start, end, keyframesOnly: true, token);
        var samples = new List<CreditFrameSample>(keyframes);
        foreach (var (from, to) in CreditFrameEvidence.RefinementWindows(keyframes, start, end))
        {
            samples.AddRange(Pictures(path, decoders, from, to, keyframesOnly: false, token));
        }

        return [.. samples.GroupBy(sample => sample.At).Select(group => group.First()).OrderBy(sample => sample.At)];
    }

    private static List<CreditFrameSample> Pictures(string path, DecoderRegistry decoders,
        TimeSpan start, TimeSpan end, bool keyframesOnly, CancellationToken token)
    {
        using var source = new FileByteSource(path);
        using var demuxer = MediaRegistries.Demuxers().Open(source, token);
        var output = new List<CreditFrameSample>();
        if (demuxer.Info.FirstTrack(MediaKind.Video) is not { } track)
        {
            return output;
        }

        using var decoder = decoders.CreateVideo(track).Decoder;
        if (decoder is null)
        {
            return output;
        }

        if (start > TimeSpan.Zero && demuxer.Info.IsSeekable)
        {
            demuxer.Seek(new MediaTime(start.Ticks), token);
        }

        var frames = new List<VideoFrame>();
        var next = start;
        var done = false;
        try
        {
            while (!done && demuxer.ReadPacket(token) is { } packet)
            {
                using (packet)
                {
                    if (packet.TrackId != track.Id)
                    {
                        continue;
                    }

                    if (!keyframesOnly)
                    {
                        decoder.Decode(packet, frames);
                        done = Take(frames, output, ref next, end);
                    }
                    else if (packet.IsKeyframe && packet.Pts.IsKnown)
                    {
                        var at = packet.Pts.ToTimeSpan();
                        done = at >= end;
                        if (!done && at >= start)
                        {
                            output.AddRange(Keyframe(decoders, track, packet, frames, at));
                        }
                    }
                }

                token.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            frames.ForEach(frame => frame.Dispose());
        }

        return output;
    }

    /// <summary>
    /// Decodes one keyframe with a decoder of its own, drained at once, so the one picture that comes
    /// out is that keyframe and carries the packet's time. Measured on Windows' HEVC decoder: fed
    /// keyframes alone, one decoder held every picture to the end and stamped some with the wrong
    /// time; drained after each, it gave no picture after the first. A fresh decoder costs about
    /// 50 ms a keyframe.
    /// </summary>
    private static IEnumerable<CreditFrameSample> Keyframe(DecoderRegistry decoders, TrackInfo track, Packet packet,
        List<VideoFrame> frames, TimeSpan at)
    {
        using var decoder = decoders.CreateVideo(track).Decoder!;
        decoder.Decode(packet, frames);
        // A decoder that holds pictures back asks to be called again until it is empty.
        var drained = false;
        while (!drained)
        {
            drained = decoder.Drain(frames);
        }

        var measured = frames.FirstOrDefault(frame => frame.Surface is null) is { } picture
            ? CreditFrameEvidence.Measure(picture, at) : null;
        frames.ForEach(frame => frame.Dispose());
        frames.Clear();
        return measured is null ? [] : [measured];
    }

    /// <summary>Measures the pictures due in [next, end); true once one at or after the end shows up.</summary>
    private static bool Take(List<VideoFrame> frames, List<CreditFrameSample> output, ref TimeSpan next,
        TimeSpan end)
    {
        var done = false;
        foreach (var frame in frames)
        {
            using (frame)
            {
                if (!frame.Pts.IsKnown || frame.Surface is not null)
                {
                    continue;
                }

                var at = frame.Pts.ToTimeSpan();
                if (at >= end)
                {
                    done = true;
                }
                else if (at >= next)
                {
                    output.Add(CreditFrameEvidence.Measure(frame, at));
                    next = at + TimeSpan.FromSeconds(1);
                }
            }
        }

        frames.Clear();
        return done;
    }
}
