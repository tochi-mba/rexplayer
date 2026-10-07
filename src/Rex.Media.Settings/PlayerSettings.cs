namespace Rex.Media.Settings;

/// <summary>What happens when the end of the play queue is reached.</summary>
public enum RepeatMode
{
    /// <summary>Stop after the last item.</summary>
    Off,

    /// <summary>Start the queue again from the top.</summary>
    All,

    /// <summary>Play the current item again and again.</summary>
    One,
}

/// <summary>When the window stays above other windows.</summary>
public enum AlwaysOnTop
{
    Never,
    Always,
    WhilePlaying,
}

/// <summary>The colour scheme of the window.</summary>
public enum ThemeChoice
{
    /// <summary>Follow Windows' light or dark setting, live.</summary>
    System,
    Light,
    Dark,
}

/// <summary>How often rexplayer looks for a new version (only ever with the user's consent, §6.15).</summary>
public enum UpdateCadence
{
    Off,
    Daily,
    Weekly,
}

/// <summary>How the two front channels are heard.</summary>
public enum StereoChoice
{
    Stereo,
    Mono,
    Left,
    Right,
    Reverse,
}

/// <summary>Which loudness tags even out loudness.</summary>
public enum LoudnessChoice
{
    Off,
    Track,
    Album,
}

/// <summary>Where the window was, so it opens there again.</summary>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized);

/// <summary>
/// Every setting the player keeps between runs, with the defaults a new user gets. A record, so a
/// change is a copy (<c>settings with { Volume = 0.5 }</c>) that is saved whole.
/// Values read from disk may be anything, so <see cref="Normalize"/> brings each back into range
/// before it is used; <see cref="SettingsStore"/> does that on every load.
/// </summary>
public sealed record PlayerSettings
{
    /// <summary>The version of this shape; older files are migrated when they are read.</summary>
    public const int CurrentSchema = 1;

    public int Schema { get; init; } = CurrentSchema;

    // Playback

    /// <summary>The volume, 1 being the media's own level; up to 2 (200 %) when the user allows it.</summary>
    public double Volume { get; init; } = 1;

    public bool Muted { get; init; }

    /// <summary>The highest volume the slider offers, in percent (100 to 200).</summary>
    public int MaxVolumePercent { get; init; } = 125;

    /// <summary>How much one volume key press changes the volume, in percent.</summary>
    public int VolumeStepPercent { get; init; } = 5;

    public RepeatMode Repeat { get; init; } = RepeatMode.Off;

    public bool Shuffle { get; init; }

    /// <summary>The four jump sizes, in seconds: very short, short, medium and long.</summary>
    public int VeryShortJumpSeconds { get; init; } = 3;

    public int ShortJumpSeconds { get; init; } = 10;

    public int MediumJumpSeconds { get; init; } = 60;

    public int LongJumpSeconds { get; init; } = 300;

    // Sound

    public bool EqualizerEnabled { get; init; }

    /// <summary>The equaliser's preamp, in decibels from -20 to +20.</summary>
    public double EqualizerPreamp { get; init; }

    /// <summary>The ten bands' gains, in decibels from -20 to +20, lowest band first.</summary>
    public IReadOnlyList<double> EqualizerGains { get; init; } = new double[10];

    /// <summary>The name of the preset the gains came from, or null once they were changed by hand.</summary>
    public string? EqualizerPreset { get; init; }

    public StereoChoice Stereo { get; init; } = StereoChoice.Stereo;

    public LoudnessChoice Loudness { get; init; } = LoudnessChoice.Off;

    /// <summary>Extra gain on top of the loudness tags, in decibels from -20 to +20.</summary>
    public double LoudnessPreamp { get; init; }

    // Window

    public WindowPlacement? Window { get; init; }

    public AlwaysOnTop AlwaysOnTop { get; init; } = AlwaysOnTop.Never;

    public bool PlaylistVisible { get; init; }

    /// <summary>Only the picture and the menus, without the controls (Ctrl+H).</summary>
    public bool MinimalInterface { get; init; }

    /// <summary>Seconds without the mouse moving before the full-screen controls and the pointer hide.</summary>
    public double ControlsHideSeconds { get; init; } = 1.5;

    /// <summary>Short messages over the picture (volume, position, speed) when something changes.</summary>
    public bool OnScreenMessages { get; init; } = true;

    /// <summary>Seconds the title shows over the picture when an item starts; 0 turns it off.</summary>
    public double TitleSeconds { get; init; } = 3;

    public bool StatsOverlay { get; init; }

    public ThemeChoice Theme { get; init; } = ThemeChoice.System;

    // The application

    /// <summary>A second launch hands its files to the running player instead of opening another window.</summary>
    public bool SingleInstance { get; init; } = true;

    /// <summary>Files handed over by a second launch join the queue instead of playing straight away.</summary>
    public bool EnqueueFromSecondLaunch { get; init; }

    public bool FirstRunDone { get; init; }

    /// <summary>Looking up artwork and details online: off until the user turns it on (§6.15).</summary>
    public bool OnlineLookups { get; init; }

    public UpdateCadence UpdateChecks { get; init; } = UpdateCadence.Weekly;

    public DateTimeOffset? LastUpdateCheck { get; init; }

    /// <summary>The newest version whose "what's new" the user has seen.</summary>
    public string? LastSeenVersion { get; init; }

    /// <summary>
    /// Shortcuts the user changed, by command id: the chord's text, or an empty string for a
    /// shortcut they removed. Commands not listed keep their default.
    /// </summary>
    public IReadOnlyDictionary<string, string> Shortcuts { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    private static double Decibels(double value) => double.IsFinite(value) ? Math.Clamp(value, -20, 20) : 0;

    /// <summary>A copy with every value in its allowed range; values from a damaged file are reset or clamped.</summary>
    public PlayerSettings Normalize()
    {
        var maxVolume = Math.Clamp(MaxVolumePercent, 100, 200);
        return this with
        {
            Schema = CurrentSchema,
            Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, maxVolume / 100.0) : 1,
            MaxVolumePercent = maxVolume,
            VolumeStepPercent = Math.Clamp(VolumeStepPercent, 1, 25),
            Repeat = Enum.IsDefined(Repeat) ? Repeat : RepeatMode.Off,
            VeryShortJumpSeconds = Math.Clamp(VeryShortJumpSeconds, 1, 3600),
            ShortJumpSeconds = Math.Clamp(ShortJumpSeconds, 1, 3600),
            MediumJumpSeconds = Math.Clamp(MediumJumpSeconds, 1, 3600),
            LongJumpSeconds = Math.Clamp(LongJumpSeconds, 1, 3600),
            Window = Window is { Width: >= 200, Height: >= 150 } ? Window : null,
            AlwaysOnTop = Enum.IsDefined(AlwaysOnTop) ? AlwaysOnTop : AlwaysOnTop.Never,
            ControlsHideSeconds = double.IsFinite(ControlsHideSeconds) ? Math.Clamp(ControlsHideSeconds, 0.5, 10) : 1.5,
            TitleSeconds = double.IsFinite(TitleSeconds) ? Math.Clamp(TitleSeconds, 0, 30) : 3,
            Theme = Enum.IsDefined(Theme) ? Theme : ThemeChoice.System,
            UpdateChecks = Enum.IsDefined(UpdateChecks) ? UpdateChecks : UpdateCadence.Weekly,
            Shortcuts = Shortcuts ?? new Dictionary<string, string>(StringComparer.Ordinal),
            EqualizerPreamp = Decibels(EqualizerPreamp),
            EqualizerGains = [.. Enumerable.Range(0, 10).Select(i => Decibels(EqualizerGains is { } gains && i < gains.Count ? gains[i] : 0))],
            Stereo = Enum.IsDefined(Stereo) ? Stereo : StereoChoice.Stereo,
            Loudness = Enum.IsDefined(Loudness) ? Loudness : LoudnessChoice.Off,
            LoudnessPreamp = Decibels(LoudnessPreamp),
        };
    }
}
