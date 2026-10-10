using Rex.Media.Primitives;

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

/// <summary>How a library view orders what it shows.</summary>
public enum LibrarySort
{
    /// <summary>The view's own order: by artist and album for music, by name for the rest.</summary>
    Natural,
    Title,
    Artist,
    Album,
    Year,
    Added,
    Played,
    MostPlayed,
    Length,
}

/// <summary>What a library view gathers its items under, each under a heading.</summary>
public enum LibraryGrouping
{
    None,
    Letter,
    Artist,
    Album,
    Genre,
    Year,
    Decade,
    Folder,
    Added,
    Length,
}

/// <summary>How a library view lays its items out.</summary>
public enum LibraryLook
{
    /// <summary>Compact lines, each with a small picture.</summary>
    List,

    /// <summary>Cards with their pictures, in a grid.</summary>
    Grid,

    /// <summary>Big cards: covers, posters and photos large.</summary>
    Wall,
}

/// <summary>How one library view is shown, as the user last set it: its layout, order, grouping and card size.</summary>
public sealed record LibraryViewChoice(LibraryLook Look = LibraryLook.Grid, LibrarySort Sort = LibrarySort.Natural, bool Descending = false, LibraryGrouping Grouping = LibraryGrouping.None, int CardSize = 180)
{
    /// <summary>The smallest and largest cards, in pixels across.</summary>
    public const int SmallestCard = 120;
    public const int LargestCard = 360;

    public LibraryViewChoice Normalize() => this with
    {
        Look = Enum.IsDefined(Look) ? Look : LibraryLook.Grid,
        Sort = Enum.IsDefined(Sort) ? Sort : LibrarySort.Natural,
        Grouping = Enum.IsDefined(Grouping) ? Grouping : LibraryGrouping.None,
        CardSize = Math.Clamp(CardSize, SmallestCard, LargestCard),
    };
}

/// <summary>The broad composition used for cover art drawn from an audio file.</summary>
public enum ArtworkStyle
{
    Prism,
    Orbit,
    Wave,
    Minimal,
}

/// <summary>What the picture area shows while sound without pictures plays (AU-18).</summary>
public enum VisualizerChoice
{
    Off,
    Spectrum,
    Oscilloscope,
    Meters,
    Spectrogram,

    /// <summary>A spinning record whose groove is the sound itself, the cover as its label.</summary>
    Vinyl,

    /// <summary>The spectrum radiating from a circle that swells with the bass.</summary>
    Halo,

    /// <summary>The waveform mirrored about the middle, as sound is often pictured.</summary>
    Mirror,

    /// <summary>The spectrum as a ridge of light under a soft glow.</summary>
    Aurora,

    /// <summary>Sparks the music throws up, rising and fading.</summary>
    Embers,

    /// <summary>Rings that burst outward on every beat.</summary>
    Ripples,

    /// <summary>The whole stage flashing colour with the music.</summary>
    Strobe,

    /// <summary>The listener, seen by the camera, drawn in light that moves with the music.</summary>
    Silhouette,

    /// <summary>Live camera or cover art cut, graded and moved to the music.</summary>
    BeatEdit,

    /// <summary>Continuous, spectrally driven light filaments and drum pressure waves.</summary>
    Resonance,
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

/// <summary>What turning the mouse wheel over the picture does (UI-11).</summary>
public enum WheelChoice
{
    Volume,

    /// <summary>Jumps forwards and backwards by the very short jump.</summary>
    Seek,
    None,
}

/// <summary>What Ctrl with the wheel does over the picture, which is also a touchpad's pinch (UI-11).</summary>
public enum CtrlWheelChoice
{
    Zoom,
    SubtitleSize,
}

/// <summary>What the middle mouse button does over the picture (UI-11).</summary>
public enum MiddleButtonChoice
{
    PlayPause,
    FullScreen,
    Mute,
    None,
}

/// <summary>What the mouse's back and forward buttons do over the picture (UI-11).</summary>
public enum SideButtonChoice
{
    PreviousNext,

    /// <summary>Jumps back and forward by the short jump.</summary>
    JumpBackForward,
    None,
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

    /// <summary>How long each picture shows before the next item plays, in seconds (FMT-C19).</summary>
    public int PictureSeconds { get; init; } = 5;

    /// <summary>How much one volume key press changes the volume, in percent.</summary>
    public int VolumeStepPercent { get; init; } = 5;

    public RepeatMode Repeat { get; init; } = RepeatMode.Off;

    public bool Shuffle { get; init; }

    /// <summary>The four jump sizes, in seconds: very short, short, medium and long.</summary>
    public int VeryShortJumpSeconds { get; init; } = 3;

    public int ShortJumpSeconds { get; init; } = 10;

    public int MediumJumpSeconds { get; init; } = 60;

    public int LongJumpSeconds { get; init; } = 300;

    /// <summary>Whether pointing at the timeline decodes and shows the exact video frame there.</summary>
    public bool SeekPreview { get; init; }

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

    /// <summary>Draw a distinctive cover from the sound when a music file has no embedded or folder art.</summary>
    public bool GenerateAudioArtwork { get; init; } = true;

    public ArtworkStyle AudioArtworkStyle { get; init; } = ArtworkStyle.Prism;

    /// <summary>Generated artwork colour strength, as a percentage from 0 to 200.</summary>
    public int AudioArtworkColor { get; init; } = 100;

    /// <summary>Generated artwork complexity, as a percentage from 0 to 200.</summary>
    public int AudioArtworkDetail { get; init; } = 100;

    /// <summary>Generated artwork contrast, as a percentage from 0 to 200.</summary>
    public int AudioArtworkContrast { get; init; } = 100;

    /// <summary>Let the file identity add variation beyond the decoded sound.</summary>
    public bool AudioArtworkUsesIdentity { get; init; } = true;

    /// <summary>While zoomed in, the whole picture shows small in a corner, with the view marked on it.</summary>
    public bool ShowNavigator { get; init; } = true;

    /// <summary>The GPU picture look to use for videos; Original does not alter the media.</summary>
    public VideoLook VideoLook { get; init; } = Rex.Media.Primitives.VideoLook.Original;

    /// <summary>
    /// Each visualisation's own settings, by "visualisation.setting" (AppCore's VisualizerOptions
    /// reads them, with their defaults and ranges).
    /// </summary>
    public IReadOnlyDictionary<string, string> VisualOptions { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Whether the user has agreed to the silhouette visualisation using the camera.</summary>
    public bool CameraAllowed { get; init; }

    /// <summary>Whether a song's lyrics show over its visualisation (META-09).</summary>
    public bool ShowLyrics { get; init; } = true;

    /// <summary>How each library view is shown, by the view's name (LIB-05).</summary>
    public IReadOnlyDictionary<string, LibraryViewChoice> LibraryViews { get; init; } = new Dictionary<string, LibraryViewChoice>(StringComparer.Ordinal);

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

    /// <summary>Commands whose shortcut works even while another program is in front (UI-11).</summary>
    public IReadOnlyList<string> GlobalShortcuts { get; init; } = [];

    public WheelChoice Wheel { get; init; } = WheelChoice.Volume;

    /// <summary>What a sideways wheel (or tilting the wheel) does.</summary>
    public WheelChoice SidewaysWheel { get; init; } = WheelChoice.Seek;

    public MiddleButtonChoice MiddleButton { get; init; } = MiddleButtonChoice.PlayPause;

    /// <summary>What Ctrl with the wheel, and pinching a touchpad, does: zoom the picture or size the subtitles.</summary>
    public CtrlWheelChoice CtrlWheel { get; init; } = CtrlWheelChoice.Zoom;

    /// <summary>Whether, zoomed in, the wheel and a touchpad's two-finger scroll move about the picture.</summary>
    public bool WheelPansWhenZoomed { get; init; } = true;

    public SideButtonChoice SideButtons { get; init; } = SideButtonChoice.PreviousNext;

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
            PictureSeconds = Math.Clamp(PictureSeconds, 1, 3600),
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
            GlobalShortcuts = [.. (GlobalShortcuts ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)],
            Wheel = Enum.IsDefined(Wheel) ? Wheel : WheelChoice.Volume,
            SidewaysWheel = Enum.IsDefined(SidewaysWheel) ? SidewaysWheel : WheelChoice.Seek,
            MiddleButton = Enum.IsDefined(MiddleButton) ? MiddleButton : MiddleButtonChoice.PlayPause,
            CtrlWheel = Enum.IsDefined(CtrlWheel) ? CtrlWheel : CtrlWheelChoice.Zoom,
            SideButtons = Enum.IsDefined(SideButtons) ? SideButtons : SideButtonChoice.PreviousNext,
            EqualizerPreamp = Decibels(EqualizerPreamp),
            EqualizerGains = [.. Enumerable.Range(0, 10).Select(i => Decibels(EqualizerGains is { } gains && i < gains.Count ? gains[i] : 0))],
            Stereo = Enum.IsDefined(Stereo) ? Stereo : StereoChoice.Stereo,
            Loudness = Enum.IsDefined(Loudness) ? Loudness : LoudnessChoice.Off,
            LoudnessPreamp = Decibels(LoudnessPreamp),
            SleepAction = Enum.IsDefined(SleepAction) ? SleepAction : SleepChoice.Pause,
            ResumePlayback = Enum.IsDefined(ResumePlayback) ? ResumePlayback : ResumeChoice.Ask,
            Visualizer = Enum.IsDefined(Visualizer) ? Visualizer : VisualizerChoice.Spectrum,
            VideoLook = Enum.IsDefined(VideoLook) ? VideoLook : Rex.Media.Primitives.VideoLook.Original,
            AudioArtworkStyle = Enum.IsDefined(AudioArtworkStyle) ? AudioArtworkStyle : ArtworkStyle.Prism,
            AudioArtworkColor = Math.Clamp(AudioArtworkColor, 0, 200),
            AudioArtworkDetail = Math.Clamp(AudioArtworkDetail, 0, 200),
            AudioArtworkContrast = Math.Clamp(AudioArtworkContrast, 0, 200),
            VisualOptions = VisualOptions ?? new Dictionary<string, string>(StringComparer.Ordinal),
            LibraryViews = (LibraryViews ?? new Dictionary<string, LibraryViewChoice>()).Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value.Normalize(), StringComparer.Ordinal),
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
