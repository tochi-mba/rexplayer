using System.Globalization;
using Rex.Media.Primitives;

namespace Rex.Media.Audio;

/// <summary>How the two front channels are heard (AU-11).</summary>
public enum StereoMode
{
    Stereo,

    /// <summary>Both channels carry their average.</summary>
    Mono,

    /// <summary>Both channels carry the left one.</summary>
    Left,

    /// <summary>Both channels carry the right one.</summary>
    Right,

    /// <summary>Left and right swap.</summary>
    Reverse,
}

/// <summary>Which loudness tags evens out loudness (AU-13).</summary>
public enum ReplayGainMode
{
    Off,

    /// <summary>Each track to the same loudness.</summary>
    Track,

    /// <summary>Each album to the same loudness, keeping the tracks' differences within it.</summary>
    Album,
}

/// <summary>
/// How ReplayGain is applied: the mode, an extra gain on top (decibels), the gain for media that
/// carries no tags, and whether a track's peak may be pushed past full scale.
/// </summary>
public sealed record ReplayGainSettings(ReplayGainMode Mode, double Preamp = 0, double UntaggedGain = 0, bool PreventClipping = true)
{
    public static ReplayGainSettings Off { get; } = new(ReplayGainMode.Off);
}

/// <summary>
/// Everything the user changes about the sound besides the volume, applied in one pass after the
/// sound is in the output's format: the loudness gain, the stereo mode, then the equaliser.
/// Immutable, so the window can replace it while the audio thread plays.
/// </summary>
public sealed record AudioEffects(EqualizerSettings Equalizer, StereoMode StereoMode, float Gain)
{
    public static AudioEffects None { get; } = new(EqualizerSettings.Off, StereoMode.Stereo, 1);
}

/// <summary>The stereo modes, applied to the first two channels.</summary>
public static class StereoModes
{
    public static void Apply(AudioFrame frame, StereoMode mode)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (mode == StereoMode.Stereo || frame.Channels < 2)
        {
            return;
        }

        var left = frame.Channel(0)[..frame.SampleCount];
        var right = frame.Channel(1)[..frame.SampleCount];
        for (var i = 0; i < left.Length; i++)
        {
            (left[i], right[i]) = mode switch
            {
                StereoMode.Mono => ((left[i] + right[i]) / 2, (left[i] + right[i]) / 2),
                StereoMode.Left => (left[i], left[i]),
                StereoMode.Right => (right[i], right[i]),
                _ => (right[i], left[i]),
            };
        }
    }
}

/// <summary>
/// The gain ReplayGain gives media from its tags ("-6.48 dB" and a peak such as "0.988"): album
/// values in album mode when there are any, else the track's, else the untagged gain; the preamp on
/// top; and, when asked, no more than lets the peak reach full scale.
/// </summary>
public static class ReplayGain
{
    public static float Factor(IReadOnlyDictionary<string, string> metadata, ReplayGainSettings settings)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Mode == ReplayGainMode.Off)
        {
            return 1;
        }

        var (gainKey, peakKey) = settings.Mode == ReplayGainMode.Album && metadata.ContainsKey(MetadataKeys.ReplayGainAlbumGain)
            ? (MetadataKeys.ReplayGainAlbumGain, MetadataKeys.ReplayGainAlbumPeak)
            : (MetadataKeys.ReplayGainTrackGain, MetadataKeys.ReplayGainTrackPeak);
        var decibels = (Number(metadata, gainKey) ?? settings.UntaggedGain) + settings.Preamp;
        var factor = Math.Pow(10, Math.Clamp(decibels, -60, 30) / 20);
        if (settings.PreventClipping && Number(metadata, peakKey) is > 0 and var peak)
        {
            factor = Math.Min(factor, 1 / peak);
        }

        return (float)factor;
    }

    /// <summary>The tag's number, without a trailing "dB", or null when it has none.</summary>
    private static double? Number(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var text)
        && double.TryParse(text.Replace("dB", "", StringComparison.OrdinalIgnoreCase).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value)
            ? value
            : null;
}
