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

/// <summary>How thick the line drawn round subtitle letters is.</summary>
public enum OutlineChoice
{
    None,
    Thin,
    Normal,
    Thick,
}

/// <summary>Whose look subtitles take: the colours and weights the subtitle file asks for, or only the user's.</summary>
public enum SubtitleStyleChoice
{
    Respect,
    Override,
}

/// <summary>What the picture area shows while sound without pictures plays (AU-18).</summary>
public enum VisualizerChoice
{
    Off,
    Spectrum,
    Oscilloscope,
    Meters,
    Spectrogram,
}

/// <summary>What happens when something is opened that was left part-way through (PB-11).</summary>
public enum ResumeChoice
{
    /// <summary>Offer to go back there, in a bar over the picture.</summary>
    Ask,
    Always,
    Never,
}

/// <summary>What the sleep timer does when its time comes (PB-21).</summary>
public enum SleepChoice
{
    Pause,
    Stop,
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
    public const int CurrentSchema = 2;

    public int Schema { get; init; } = CurrentSchema;

    // Playback

    /// <summary>The volume, 1 being the media's own level; up to 2 (200 %) when the user allows it.</summary>
    public double Volume { get; init; } = 1;

    public bool Muted { get; init; }

    /// <summary>The highest volume the slider offers, in percent (100 to 200).</summary>
    public int MaxVolumePercent { get; init; } = 200;

    /// <summary>What the loudest volume was before version 2 of the settings made it 200 %.</summary>
    private const int OldMaxVolumeDefault = 125;

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

    public VisualizerChoice Visualizer { get; init; } = VisualizerChoice.Spectrum;

    /// <summary>While zoomed in, the whole picture shows small in a corner, with the view marked on it.</summary>
    public bool ShowNavigator { get; init; } = true;

    /// <summary>The languages to play audio in, best first, such as "ja, original"; empty for the media's own choice.</summary>
    public string AudioLanguages { get; init; } = "";

    // Subtitles

    /// <summary>The font subtitles are drawn in; Windows picks a fallback for scripts it lacks.</summary>
    public string SubtitleFont { get; init; } = "Segoe UI";

    /// <summary>The size of subtitle text, in percent of the normal size (50 to 400).</summary>
    public int SubtitleSize { get; init; } = 100;

    /// <summary>The text colour as 0xRRGGBB.</summary>
    public int SubtitleColor { get; init; } = 0xFFFFFF;

    /// <summary>The text's opacity, in percent.</summary>
    public int SubtitleOpacity { get; init; } = 100;

    public bool SubtitleBold { get; init; }

    public OutlineChoice SubtitleOutline { get; init; } = OutlineChoice.Normal;

    public int SubtitleOutlineColor { get; init; }

    public int SubtitleShadowColor { get; init; }

    /// <summary>The shadow's opacity, in percent; 0 draws no shadow.</summary>
    public int SubtitleShadowOpacity { get; init; } = 60;

    /// <summary>How far the shadow falls below and right of the text, in pixels (0 to 10).</summary>
    public int SubtitleShadowOffset { get; init; } = 2;

    public int SubtitleBoxColor { get; init; }

    /// <summary>The opacity of the box behind subtitle text, in percent; 0 draws no box.</summary>
    public int SubtitleBoxOpacity { get; init; }

    /// <summary>The gap below bottom subtitles and above top ones, in percent of the picture's height (0 to 40).</summary>
    public int SubtitleMargin { get; init; } = 5;

    /// <summary>Subtitles sit in the black bars below a wide picture rather than over it.</summary>
    public bool SubtitlesInBars { get; init; }

    /// <summary>Every cue at the bottom, wherever its file placed it.</summary>
    public bool SubtitlesAtBottom { get; init; }

    public SubtitleStyleChoice SubtitleStyles { get; init; } = SubtitleStyleChoice.Respect;

    /// <summary>The code page subtitle files without a byte-order mark and not in UTF-8 are read in.</summary>
    public int SubtitleCodePage { get; init; } = 1252;

    /// <summary>The languages to show subtitles in, best first, such as "en, fr"; empty to show files beside the media first.</summary>
    public string SubtitleLanguages { get; init; } = "";

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

    // Memory

    public ResumeChoice ResumePlayback { get; init; } = ResumeChoice.Ask;

    /// <summary>Remember where things were left and what played recently (PRIV-03).</summary>
    public bool KeepHistory { get; init; } = true;

    /// <summary>Put the queue back as it was when rexplayer starts without files to open (LIB-03).</summary>
    public bool RestoreQueue { get; init; } = true;

    public SleepChoice SleepAction { get; init; } = SleepChoice.Pause;

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

    private static int Rgb(int value) => value & 0xFFFFFF;

    private static double Decibels(double value) => double.IsFinite(value) ? Math.Clamp(value, -20, 20) : 0;

    /// <summary>A copy with every value in its allowed range; values from a damaged file are reset or clamped.</summary>
    public PlayerSettings Normalize()
    {
        // Settings from before schema 2 that kept the old default loudest volume get the new one; a
        // loudest volume the user chose stays.
        var maxVolume = Math.Clamp(Schema < 2 && MaxVolumePercent == OldMaxVolumeDefault ? 200 : MaxVolumePercent, 100, 200);
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
            SleepAction = Enum.IsDefined(SleepAction) ? SleepAction : SleepChoice.Pause,
            ResumePlayback = Enum.IsDefined(ResumePlayback) ? ResumePlayback : ResumeChoice.Ask,
            Visualizer = Enum.IsDefined(Visualizer) ? Visualizer : VisualizerChoice.Spectrum,
            AudioLanguages = AudioLanguages?.Trim() ?? "",
            SubtitleLanguages = SubtitleLanguages?.Trim() ?? "",
            SubtitleFont = string.IsNullOrWhiteSpace(SubtitleFont) ? "Segoe UI" : SubtitleFont.Trim(),
            SubtitleSize = Math.Clamp(SubtitleSize, 50, 400),
            SubtitleColor = Rgb(SubtitleColor),
            SubtitleOpacity = Math.Clamp(SubtitleOpacity, 0, 100),
            SubtitleOutline = Enum.IsDefined(SubtitleOutline) ? SubtitleOutline : OutlineChoice.Normal,
            SubtitleOutlineColor = Rgb(SubtitleOutlineColor),
            SubtitleShadowColor = Rgb(SubtitleShadowColor),
            SubtitleShadowOpacity = Math.Clamp(SubtitleShadowOpacity, 0, 100),
            SubtitleShadowOffset = Math.Clamp(SubtitleShadowOffset, 0, 10),
            SubtitleBoxColor = Rgb(SubtitleBoxColor),
            SubtitleBoxOpacity = Math.Clamp(SubtitleBoxOpacity, 0, 100),
            SubtitleMargin = Math.Clamp(SubtitleMargin, 0, 40),
            SubtitleStyles = Enum.IsDefined(SubtitleStyles) ? SubtitleStyles : SubtitleStyleChoice.Respect,
        };
    }
}
