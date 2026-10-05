using Rex.Media.Primitives;

namespace Rex.Media.Audio;

/// <summary>
/// Maps one speaker layout onto another with a fixed matrix. Speakers the output has are copied;
/// a missing speaker falls back to its nearest neighbour (side to back, back to side, rear to front,
/// height to ear level) at -3 dB per step, the centre splits into left and right at -3 dB, and the
/// low-frequency channel is left out of a downmix. When a row would sum above unity every row is
/// scaled by the same amount, so the balance survives and full-scale input cannot clip.
/// </summary>
public sealed class ChannelMixer
{
    private const float Minus3dB = 0.70710677f;

    private readonly float[,] _matrix;

    public ChannelMixer(ChannelLayout input, ChannelLayout output)
    {
        if (input == ChannelLayout.None || output == ChannelLayout.None)
        {
            throw new ArgumentException("Both layouts need at least one speaker.");
        }

        Input = input;
        Output = output;
        _matrix = BuildMatrix(input, output);
    }

    public ChannelLayout Input { get; }

    public ChannelLayout Output { get; }

    /// <summary>Coefficient of input channel <paramref name="inputIndex"/> in output channel <paramref name="outputIndex"/>.</summary>
    public float Coefficient(int outputIndex, int inputIndex) => _matrix[outputIndex, inputIndex];

    public AudioFrame Process(AudioFrame input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Channels != Input.ChannelCount())
        {
            throw new InvalidOperationException("The frame's channel count does not match the mixer's input layout.");
        }

        var outputs = Output.ChannelCount();
        var result = AudioFrame.Rent(input.SampleRate, outputs, input.SampleCount, Output);
        result.Pts = input.Pts;
        result.Generation = input.Generation;
        for (var o = 0; o < outputs; o++)
        {
            var target = result.Channel(o);
            target.Clear();
            for (var i = 0; i < input.Channels; i++)
            {
                var coefficient = _matrix[o, i];
                if (coefficient == 0)
                {
                    continue;
                }

                var source = input.Channel(i);
                for (var s = 0; s < target.Length; s++)
                {
                    target[s] += source[s] * coefficient;
                }
            }
        }

        return result;
    }

    internal static float[,] BuildMatrix(ChannelLayout input, ChannelLayout output)
    {
        var inSpeakers = input.Speakers();
        var outSpeakers = output.Speakers();
        var matrix = new float[outSpeakers.Length, inSpeakers.Length];
        for (var i = 0; i < inSpeakers.Length; i++)
        {
            foreach (var (speaker, gain) in Route(inSpeakers[i], output, 1f))
            {
                matrix[Array.IndexOf(outSpeakers, speaker), i] += gain;
            }
        }

        var largest = 0f;
        for (var o = 0; o < outSpeakers.Length; o++)
        {
            var sum = 0f;
            for (var i = 0; i < inSpeakers.Length; i++)
            {
                sum += matrix[o, i];
            }

            largest = Math.Max(largest, sum);
        }

        if (largest > 1f)
        {
            for (var o = 0; o < outSpeakers.Length; o++)
            {
                for (var i = 0; i < inSpeakers.Length; i++)
                {
                    matrix[o, i] /= largest;
                }
            }
        }

        return matrix;
    }

    /// <summary>Where one input speaker's signal goes in the output layout, and at what gain.</summary>
    internal static IEnumerable<(ChannelLayout Speaker, float Gain)> Route(ChannelLayout speaker, ChannelLayout output, float gain)
    {
        if ((output & speaker) != 0)
        {
            return [(speaker, gain)];
        }

        if (speaker == ChannelLayout.LowFrequency)
        {
            return [];
        }

        if (output == ChannelLayout.Mono)
        {
            return [(ChannelLayout.FrontCenter, gain)];
        }

        return speaker switch
        {
            ChannelLayout.FrontCenter => Split(output, ChannelLayout.FrontLeft, ChannelLayout.FrontRight, gain * Minus3dB),
            ChannelLayout.FrontLeftOfCenter => Route(ChannelLayout.FrontLeft, output, gain),
            ChannelLayout.FrontRightOfCenter => Route(ChannelLayout.FrontRight, output, gain),
            ChannelLayout.SideLeft => Either(output, ChannelLayout.BackLeft, ChannelLayout.FrontLeft, gain),
            ChannelLayout.SideRight => Either(output, ChannelLayout.BackRight, ChannelLayout.FrontRight, gain),
            ChannelLayout.BackLeft => Either(output, ChannelLayout.SideLeft, ChannelLayout.FrontLeft, gain),
            ChannelLayout.BackRight => Either(output, ChannelLayout.SideRight, ChannelLayout.FrontRight, gain),
            ChannelLayout.BackCenter => Split(output, ChannelLayout.BackLeft, ChannelLayout.BackRight, gain * Minus3dB),
            ChannelLayout.TopFrontLeft => Route(ChannelLayout.FrontLeft, output, gain * Minus3dB),
            ChannelLayout.TopFrontRight => Route(ChannelLayout.FrontRight, output, gain * Minus3dB),
            ChannelLayout.TopFrontCenter or ChannelLayout.TopCenter => Route(ChannelLayout.FrontCenter, output, gain * Minus3dB),
            ChannelLayout.TopBackLeft => Route(ChannelLayout.BackLeft, output, gain * Minus3dB),
            ChannelLayout.TopBackRight => Route(ChannelLayout.BackRight, output, gain * Minus3dB),
            ChannelLayout.TopBackCenter => Route(ChannelLayout.BackCenter, output, gain * Minus3dB),
            _ => (output & ChannelLayout.FrontCenter) != 0 ? [(ChannelLayout.FrontCenter, gain * Minus3dB)] : [],
        };
    }

    /// <summary>A pair present in the output takes the signal in halves; otherwise each side routes alone.</summary>
    private static IEnumerable<(ChannelLayout, float)> Split(ChannelLayout output, ChannelLayout left, ChannelLayout right, float gain) =>
        Route(left, output, gain).Concat(Route(right, output, gain));

    /// <summary>The equivalent speaker at full gain if the output has it, else the front fallback at -3 dB.</summary>
    private static IEnumerable<(ChannelLayout, float)> Either(ChannelLayout output, ChannelLayout equivalent, ChannelLayout front, float gain) =>
        (output & equivalent) != 0 ? [(equivalent, gain)] : Route(front, output, gain * Minus3dB);
}
