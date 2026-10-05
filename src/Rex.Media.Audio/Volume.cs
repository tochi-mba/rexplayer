using Rex.Media.Primitives;

namespace Rex.Media.Audio;

/// <summary>
/// The user's volume, applied in the float domain. The slider position maps to amplitude on a square
/// curve: 50 % is -12 dB and 200 % is +12 dB. Squared amplitude feels even across the slider, where a
/// straight line puts most of the audible change in the bottom quarter. Gain changes ramp across one
/// frame so moving the slider never clicks.
/// </summary>
public sealed class VolumeProcessor
{
    public const double Maximum = 2.0;

    private float _currentGain = 1f;
    private double _volume = 1.0;

    /// <summary>The slider value: 0 is silent, 1 is unity, 2 is the top of the boost range.</summary>
    public double Volume
    {
        get => _volume;
        set => _volume = double.IsFinite(value) ? Math.Clamp(value, 0, Maximum) : 1.0;
    }

    public bool Muted { get; set; }

    /// <summary>The amplitude multiplier for a slider value.</summary>
    public static float Gain(double volume) => (float)(Math.Clamp(volume, 0, Maximum) * Math.Clamp(volume, 0, Maximum));

    /// <summary>The level in decibels for a slider value, for the OSD ("-12.0 dB").</summary>
    public static double Decibels(double volume) => volume <= 0 ? double.NegativeInfinity : 20 * Math.Log10(Gain(volume));

    public void Process(AudioFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var target = Muted ? 0f : Gain(_volume);
        var start = _currentGain;
        if (start == 1f && target == 1f)
        {
            return;
        }

        var count = frame.SampleCount;
        for (var channel = 0; channel < frame.Channels; channel++)
        {
            var plane = frame.Channel(channel);
            if (start == target)
            {
                for (var i = 0; i < count; i++)
                {
                    plane[i] *= target;
                }

                continue;
            }

            var step = (target - start) / Math.Max(1, count);
            for (var i = 0; i < count; i++)
            {
                plane[i] *= start + (step * (i + 1));
            }
        }

        _currentGain = target;
    }
}

/// <summary>
/// Keeps boosted audio from hard-clipping: below 0.9 nothing changes, above it the signal bends
/// smoothly towards full scale along a tanh knee instead of being cut off.
/// </summary>
public static class SoftClipper
{
    public const float Knee = 0.9f;

    public static float Apply(float sample)
    {
        var magnitude = Math.Abs(sample);
        if (magnitude <= Knee)
        {
            return sample;
        }

        var bent = Knee + ((1f - Knee) * MathF.Tanh((magnitude - Knee) / (1f - Knee)));
        return sample < 0 ? -bent : bent;
    }

    public static void Process(AudioFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        for (var channel = 0; channel < frame.Channels; channel++)
        {
            var plane = frame.Channel(channel);
            for (var i = 0; i < plane.Length; i++)
            {
                plane[i] = Apply(plane[i]);
            }
        }
    }
}
