using Rex.Media.Audio;
using Rex.Media.Primitives;

namespace Rex.Media.Engine;

/// <summary>
/// The user's sound settings for a session: the equaliser, the stereo mode and how loudness tags
/// are used. The loudness gain differs from item to item, so it is worked out from each item's tags
/// as it starts (<see cref="EffectsFor"/>).
/// </summary>
public sealed record SoundSettings(EqualizerSettings Equalizer, StereoMode StereoMode, ReplayGainSettings ReplayGain)
{
    public static SoundSettings Plain { get; } = new(EqualizerSettings.Off, StereoMode.Stereo, ReplayGainSettings.Off);

    /// <summary>The effects for media described by <paramref name="info"/>.</summary>
    public AudioEffects EffectsFor(MediaInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new AudioEffects(Equalizer, StereoMode, Rex.Media.Audio.ReplayGain.Factor(info.Metadata, ReplayGain));
    }
}
