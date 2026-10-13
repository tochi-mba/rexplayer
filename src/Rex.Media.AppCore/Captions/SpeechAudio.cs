using Rex.Media.Codecs;
using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Captions;

/// <summary>
/// Sound as a speech model wants it (SUB-19): the first audio track decoded, every channel
/// averaged, at 16 kHz. The rate is changed by averaging the samples each output sample covers
/// (a box filter, which also keeps the sound's highs from folding down) and reading between them.
/// </summary>
public static class SpeechAudio
{
    /// <summary>The rate speech models hear at.</summary>
    public const int Rate = 16000;

    /// <summary>The first audio track of <paramref name="path"/>, every channel averaged, at its own rate; empty when there is none.</summary>
    public static (float[] Mono, int Rate) DecodeMono(string path, DecoderRegistry decoders, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(decoders);
        using var source = new FileByteSource(path);
        using var demuxer = MediaRegistries.Demuxers().Open(source, cancellationToken);
        if (demuxer.Info.FirstTrack(MediaKind.Audio) is not { } track || decoders.CreateAudio(track).Decoder is not { } decoder)
        {
            return ([], 0);
        }

        using (decoder)
        {
            var mono = new List<float>();
            var rate = 0;
            var frames = new List<AudioFrame>();
            void Take()
            {
                foreach (var frame in frames)
                {
                    using (frame)
                    {
                        rate = rate == 0 ? frame.SampleRate : rate;
                        for (var sample = 0; sample < frame.SampleCount; sample++)
                        {
                            var total = 0f;
                            for (var channel = 0; channel < frame.Channels; channel++)
                            {
                                total += frame.Channel(channel)[sample];
                            }

                            mono.Add(total / Math.Max(1, frame.Channels));
                        }
                    }
                }

                frames.Clear();
            }

            while (demuxer.ReadPacket(cancellationToken) is { } packet)
            {
                using (packet)
                {
                    if (packet.TrackId == track.Id)
                    {
                        decoder.Decode(packet, frames);
                    }
                }

                Take();
            }

            decoder.Drain(frames);
            Take();
            return ([.. mono], rate);
        }
    }

    /// <summary>
    /// <paramref name="mono"/> at <paramref name="rate"/> brought to <paramref name="target"/> samples
    /// a second: each output sample the mean of the input it spans, read between the means.
    /// </summary>
    public static float[] Resample(ReadOnlySpan<float> mono, int rate, int target = Rate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(target, 1);
        if (rate == target || mono.Length == 0)
        {
            return mono.ToArray();
        }

        var count = (int)Math.Round((double)mono.Length * target / rate);
        var output = new float[count];
        var span = (double)rate / target;
        for (var i = 0; i < count; i++)
        {
            var from = i * span;
            var to = from + span;
            var first = (int)Math.Floor(from);
            var last = Math.Min(mono.Length - 1, (int)Math.Ceiling(to) - 1);
            if (span <= 1)
            {
                // Going up: read between the two nearest samples.
                var at = Math.Min(mono.Length - 1, first);
                var next = Math.Min(mono.Length - 1, at + 1);
                var t = (float)(from - at);
                output[i] = (mono[at] * (1 - t)) + (mono[next] * t);
                continue;
            }

            double sum = 0;
            var n = 0;
            for (var j = Math.Max(0, first); j <= last; j++)
            {
                sum += mono[j];
                n++;
            }

            output[i] = n == 0 ? 0 : (float)(sum / n);
        }

        return output;
    }
}
