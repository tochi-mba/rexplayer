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

/// <summary>What rexplayer does with a newer version once a check finds it.</summary>
public enum UpdateMode
{
    /// <summary>Download it in the background and install it when rexplayer closes or sits unused, never while playing.</summary>
    Automatic,

    /// <summary>Download it in the background, then ask when to install it.</summary>
    DownloadAndAsk,

    /// <summary>Say it is there; download nothing until asked.</summary>
    NotifyOnly,
}

/// <summary>How rexplayer behaves when a remembered position exists for a file.</summary>
public enum ResumeBehavior
{
    /// <summary>Offer an unobtrusive choice after media opens.</summary>
    Ask,
    /// <summary>Return to the saved position automatically.</summary>
    Always,
    /// <summary>Start at the beginning and do not seek to a saved position.</summary>
    Never,
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

    /// <summary>When the file last changed, newest first.</summary>
    Modified,

    /// <summary>The file's size, largest first.</summary>
    Size,

    /// <summary>The file's folder and name.</summary>
    Path,
}

/// <summary>Which items a library view keeps, by how far they have been watched or played.</summary>
public enum LibraryFilter
{
    All,

    /// <summary>Never played and not left part-way.</summary>
    Unwatched,

    /// <summary>Left part-way, with somewhere to go back to.</summary>
    InProgress,

    /// <summary>Played at least once and not left part-way.</summary>
    Finished,
}

/// <summary>Which kinds of media a library view keeps.</summary>
public enum LibraryKindFilter
{
    Any,
    Music,
    Video,
    Picture,
}

/// <summary>Which items a library view keeps, by whether their files have finished downloading.</summary>
public enum LibraryDownloadFilter
{
    Any,

    /// <summary>Files that are whole.</summary>
    Complete,

    /// <summary>Files still being downloaded into their folder.</summary>
    Downloading,
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

    /// <summary>Dense, justified rows of differently shaped artwork, without empty grid columns.</summary>
    Collage,

    /// <summary>A streaming-style browse: a featured billboard over horizontal rows by category.</summary>
    Browse,
}

/// <summary>
/// How one library view is shown, as the user last set it: its layout, order, grouping and card
/// size, and which of its items it keeps (by watched state, by kind, by folder, and by whether
/// the file has finished downloading).
/// <see cref="LookChosen"/> says the layout itself was picked, not merely saved alongside another change.
/// </summary>
public sealed record LibraryViewChoice(
    LibraryLook Look = LibraryLook.Grid,
    LibrarySort Sort = LibrarySort.Natural,
    bool Descending = false,
    LibraryGrouping Grouping = LibraryGrouping.None,
    int CardSize = 180,
    LibraryFilter Filter = LibraryFilter.All,
    LibraryKindFilter Kinds = LibraryKindFilter.Any,
    string? Folder = null,
    LibraryDownloadFilter Download = LibraryDownloadFilter.Any,
    bool LookChosen = false)
{
    /// <summary>The smallest and largest cards, in pixels across.</summary>
    public const int SmallestCard = 120;
    public const int LargestCard = 360;

    /// <summary>Whether any of the quick filters narrows the view.</summary>
    public bool IsFiltered => Filter != LibraryFilter.All || Kinds != LibraryKindFilter.Any || Folder is not null || Download != LibraryDownloadFilter.Any;

    /// <summary>The same view with every quick filter off.</summary>
    public LibraryViewChoice Unfiltered() => this with { Filter = LibraryFilter.All, Kinds = LibraryKindFilter.Any, Folder = null, Download = LibraryDownloadFilter.Any };

    public LibraryViewChoice Normalize() => this with
    {
        Look = Enum.IsDefined(Look) ? Look : LibraryLook.Grid,
        Sort = Enum.IsDefined(Sort) ? Sort : LibrarySort.Natural,
        Grouping = Enum.IsDefined(Grouping) ? Grouping : LibraryGrouping.None,
        CardSize = Math.Clamp(CardSize, SmallestCard, LargestCard),
        Filter = Enum.IsDefined(Filter) ? Filter : LibraryFilter.All,
        Kinds = Enum.IsDefined(Kinds) ? Kinds : LibraryKindFilter.Any,
        Folder = string.IsNullOrWhiteSpace(Folder) ? null : Folder.Trim(),
        Download = Enum.IsDefined(Download) ? Download : LibraryDownloadFilter.Any,
    };
}

/// <summary>
/// Where the user was in one library view: the line at the top of the list and the line chosen
/// (each named by its identity), and for the surfaces without lines, how far Home's page and each
/// of its shelves, or the collage, were scrolled, in pixels.
/// </summary>
public sealed record LibraryPlace(string? Anchor = null, string? Selected = null, double Offset = 0, IReadOnlyList<double>? Shelves = null)
{
    /// <summary>The most places kept; older ones are forgotten first.</summary>
    public const int Most = 200;

    /// <summary>Whether the place says nothing: the top of the view with nothing chosen.</summary>
    public bool IsEmpty => Anchor is null && Selected is null && Offset <= 0 && (Shelves is null || Shelves.All(shelf => shelf <= 0));

    public LibraryPlace Normalize() => new(
        string.IsNullOrWhiteSpace(Anchor) ? null : Anchor,
        string.IsNullOrWhiteSpace(Selected) ? null : Selected,
        Pixels(Offset),
        Shelves is null || Shelves.Count == 0 ? null : [.. Shelves.Select(Pixels)]);

    private static double Pixels(double value) => double.IsFinite(value) && value > 0 ? value : 0;
}

/// <summary>The route the library was last at: its view and, when one was opened in it, a group (an album, an artist, a show) and the group's page (a season, an artist's album).</summary>
public sealed record LibraryRoute(string View, string? Group = null, string? GroupDetail = null, string? Page = null);

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

    /// <summary>The last seconds of the music scrolling by: its spectrum, the beat grid and every drum hit.</summary>
    Groove,

    /// <summary>A flight down a tunnel of light rings that pass on the beat.</summary>
    Flight,

    /// <summary>Sand gathering into the figures a vibrating plate sings, the mode set by the pitch.</summary>
    Cymatics,

    /// <summary>The spectrum laid down as ridges, each new one at the front, the old climbing away.</summary>
    Ridgelines,

    /// <summary>A drive over a neon grid toward a striped sun, the spectrum raising the mountains.</summary>
    Synthwave,

    /// <summary>Shells launched by the kick and burst by the snare.</summary>
    Fireworks,

    /// <summary>A storm whose bolts strike on the kick and crack on the snare.</summary>
    Lightning,

    /// <summary>Blobs that swell with their part of the spectrum and melt together on the kick.</summary>
    Lava,

    /// <summary>Each beat lights the star of its pitch, drawing the tune as a constellation.</summary>
    Constellation,

    /// <summary>A spiral galaxy turning on the beat, its arms lit by the spectrum.</summary>
    Galaxy,

    /// <summary>Shards of the spectrum in turning mirrors that click round on the beat.</summary>
    Kaleidoscope,

    /// <summary>A flock wheeling as one, led by the melody and drawn tight by the kick.</summary>
    Murmuration,

    /// <summary>Drops of ink let fall by the drums and carried in a current the bass stirs.</summary>
    Ink,

    /// <summary>A night city whose towers are the spectrum and whose searchlights cross on the beat.</summary>
    Skyline,

    /// <summary>Rings of petals the spectrum opens, turning a step on each beat.</summary>
    Mandala,

    /// <summary>The listener's outline frozen on every beat, the clones drifting away behind them.</summary>
    Clones,

    /// <summary>The camera as a slit-scan mirror, each part of the picture a different moment, deepened by the bass.</summary>
    TimeWarp,

    /// <summary>A glow round the listener that breathes with the bass, with shockwaves on each kick.</summary>
    Aura,

    /// <summary>The camera's picture on a wall of lights, each column gated by its part of the spectrum.</summary>
    LedWall,

    /// <summary>The listener made of motes that blast apart on each kick and re-form between.</summary>
    Stardust,

    /// <summary>The camera's picture folded into mirrors that turn on the beat.</summary>
    HallOfMirrors,

    /// <summary>Strokes of light wherever the listener's outline moves, flaring on the kick.</summary>
    LightPainting,

    /// <summary>A bar of light that sweeps the picture once a beat, the picture fading behind it.</summary>
    Scanner,

    /// <summary>The picture in tiles that the kick blasts apart and that mend between beats.</summary>
    Shatter,

    /// <summary>Spotlights that follow the listener and change colour on the kick.</summary>
    Spotlight,

    /// <summary>The picture inside itself, level by level, each a beat further back.</summary>
    InfinityMirror,

    /// <summary>The picture bent through ripples each kick drops at the listener.</summary>
    Water,
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

/// <summary>Which side of the picture a panel over it sits on.</summary>
public enum PanelSide
{
    Right,
    Left,
}

/// <summary>How a Subject Lock selection is made on the picture.</summary>
public enum SubjectSelectionMode
{
    /// <summary>One click locks onto the best box around the point; a drag still draws a box.</summary>
    Click,

    /// <summary>A drag draws the box to lock onto.</summary>
    Draw,
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
    public const int CurrentSchema = 3;

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

    /// <summary>Whether and how to return to remembered playback positions.</summary>
    public ResumeBehavior ResumeMode { get; init; } = ResumeBehavior.Always;

    /// <summary>The four jump sizes, in seconds: very short, short, medium and long.</summary>
    public int VeryShortJumpSeconds { get; init; } = 3;

    public int ShortJumpSeconds { get; init; } = 10;

    public int MediumJumpSeconds { get; init; } = 60;

    public int LongJumpSeconds { get; init; } = 300;

    /// <summary>Whether pointing at the timeline decodes and shows the exact video frame there.</summary>
    public bool SeekPreview { get; init; }

    /// <summary>Allow hardware video decoding when the native platform supports it. Reopening a video applies changes.</summary>
    public bool HardwareVideoDecoding { get; init; } = true;

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

    /// <summary>The visualisation used for recognised audiobooks, independently of other audio.</summary>
    public VisualizerChoice AudiobookVisualizer { get; init; } = VisualizerChoice.Spectrum;

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

    // Subject Lock's panel

    /// <summary>Which side of the picture the Subject Lock panel sits on.</summary>
    public PanelSide SubjectPanelSide { get; init; } = PanelSide.Right;

    /// <summary>Whether a click or a drawn box selects the subject.</summary>
    public SubjectSelectionMode SubjectSelection { get; init; } = SubjectSelectionMode.Click;

    /// <summary>Whether the panel's Following section is open.</summary>
    public bool SubjectFollowingOpen { get; init; } = true;

    /// <summary>Whether the panel's Removal preview section is open.</summary>
    public bool SubjectRemovalOpen { get; init; } = true;

    /// <summary>How the removal preview hides the selection: show what is behind it, or blend it into the surface around it.</summary>
    public RemovalMode SubjectRemoval { get; init; } = RemovalMode.Remove;

    /// <summary>Whether the panel's Help section is open.</summary>
    public bool SubjectHelpOpen { get; init; }

    /// <summary>The GPU picture look to use for videos; Original does not alter the media.</summary>
    public VideoLook VideoLook { get; init; } = Rex.Media.Primitives.VideoLook.Original;

    /// <summary>Optional real-time pixel effects, disabled by default.</summary>
    public VideoEffect VideoEffect { get; init; } = Rex.Media.Primitives.VideoEffect.Off;

    /// <summary>How strongly the current pixel effect changes the frame, from 0 to 100.</summary>
    public int VideoEffectStrength { get; init; } = 65;

    /// <summary>Independent controls for every GPU picture look and effect, keyed by style and option.</summary>
    public IReadOnlyDictionary<string, int> VideoStyleValues { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

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

    /// <summary>Representative track locations of albums deliberately pinned to Music Home, newest pin first. A missing file does not fabricate a card.</summary>
    public IReadOnlyList<string> PinnedAlbumPaths { get; init; } = [];

    /// <summary>Where the user was in each library view and page, by the route's key, newest last (LIB-05).</summary>
    public IReadOnlyDictionary<string, LibraryPlace> LibraryPlaces { get; init; } = new Dictionary<string, LibraryPlace>(StringComparer.Ordinal);

    /// <summary>The route the library was last at, so the next launch returns to it; null for Home.</summary>
    public LibraryRoute? LibraryRoute { get; init; }

    /// <summary>Opening the library pauses what is playing, so the picture is not missed while browsing (LIB-05).</summary>
    public bool PauseWhenLibraryOpens { get; init; } = true;

    /// <summary>Whether a library item opens on one click or two.</summary>
    public MediaOpenClick MediaOpenClickMode { get; init; } = Rex.Media.Settings.MediaOpenClick.DoubleClick;

    /// <summary>
    /// Opening the library reveals the playing item; otherwise it returns to where the library was
    /// left: the same view or page, scrolled as it was, with the same line chosen. Off by default:
    /// what was opened was most often opened from there, and a page left open stays open.
    /// </summary>
    public bool OpenLibraryAtPlayingItem { get; init; }

    /// <summary>Edited song details go into the files when their format can be written; otherwise rexplayer keeps them.</summary>
    public bool WriteDetailsIntoFiles { get; init; } = true;

    /// <summary>Opening the library leaves full screen so browsing has the normal window chrome.</summary>
    public bool LeaveFullScreenWhenLibraryOpens { get; init; } = true;

    /// <summary>Starting media from the library closes it and returns to the player.</summary>
    public bool ShowPlayerWhenLibraryMediaStarts { get; init; } = true;

    /// <summary>With <see cref="PauseWhenLibraryOpens"/>, music and other sound-only items keep playing while the library is open.</summary>
    public bool KeepAudioPlayingInLibrary { get; init; } = true;

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

    /// <summary>Words that come in as they are spoken and move the way they were said (OSD-11).</summary>
    public KineticCaptionSettings KineticCaptions { get; init; } = new();

    /// <summary>The library's speech heard ahead of time, and what plays heard from where it plays (SUB-20, SUB-21).</summary>
    public TranscriptionSettings Transcription { get; init; } = new();

    /// <summary>Online metadata enrichment: strictly off until the user turns a kind on.</summary>
    public MetadataSettings Metadata { get; init; } = new();

    /// <summary>How much the player asks of the computer: decided from what the computer is, or chosen.</summary>
    public PerformanceChoice Performance { get; init; }

    /// <summary>While Windows' energy saver is on, a chosen mode gives way to Automatic; the choice itself is kept.</summary>
    public bool AutomaticWhenEnergySaver { get; init; } = true;

    // Window

    public WindowPlacement? Window { get; init; }

    public AlwaysOnTop AlwaysOnTop { get; init; } = AlwaysOnTop.Never;

    public bool PlaylistVisible { get; init; }

    /// <summary>Whether the library uses its compact icon navigation rail when space permits either form.</summary>
    public bool LibraryNavigationCollapsed { get; init; }

    /// <summary>Allow taskbar transport buttons, media keys, the lock-screen media card and the jump list.</summary>
    public bool ShellMediaControls { get; init; } = true;

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

    /// <summary>Only skip an offered intro without a click when the viewer opts in.</summary>
    public bool SkipIntrosAutomatically { get; init; }

    /// <summary>Only skip offered credits without a click when the viewer opts in.</summary>
    public bool SkipCreditsAutomatically { get; init; }

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

    /// <summary>
    /// The old single switch for looking up artwork and details online (§6.15), saved but never
    /// read. <see cref="Metadata"/> replaced it with a switch per kind; a file that says true turns
    /// them all on once, in <see cref="Normalize"/>, and the old switch is cleared.
    /// </summary>
    public bool OnlineLookups { get; init; }

    public UpdateCadence UpdateChecks { get; init; } = UpdateCadence.Daily;

    public DateTimeOffset? LastUpdateCheck { get; init; }

    /// <summary>The newest version whose "what's new" the user has seen.</summary>
    public string? LastSeenVersion { get; init; }

    /// <summary>What happens when a check finds a newer version (TOOL-09).</summary>
    public UpdateMode UpdateMode { get; init; } = UpdateMode.DownloadAndAsk;

    /// <summary>The version the user chose to skip; a newer one is still offered.</summary>
    public string? SkippedUpdateVersion { get; init; }

    /// <summary>The newer version being downloaded, held or installed; null when there is none.</summary>
    public PendingUpdate? PendingUpdate { get; init; }

    /// <summary>The version an update last brought, and when, for Preferences to say what happened.</summary>
    public string? LastUpdatedVersion { get; init; }

    public DateTimeOffset? LastUpdatedAt { get; init; }

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
        // The old single online-lookups switch becomes the per-kind metadata switches, once.
        var metadata = (Metadata ?? new MetadataSettings()).Normalize();
        if (OnlineLookups && metadata is { TvShows: false, Movies: false, Music: false })
        {
            metadata = metadata with { TvShows = true, Movies = true, Music = true };
        }
        return this with
        {
            Schema = CurrentSchema,
            Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, maxVolume / 100.0) : 1,
            MaxVolumePercent = maxVolume,
            VolumeStepPercent = Math.Clamp(VolumeStepPercent, 1, 25),
            PictureSeconds = Math.Clamp(PictureSeconds, 1, 3600),
            Repeat = Enum.IsDefined(Repeat) ? Repeat : RepeatMode.Off,
            ResumeMode = Enum.IsDefined(ResumeMode) ? ResumeMode : ResumeBehavior.Always,
            VeryShortJumpSeconds = Math.Clamp(VeryShortJumpSeconds, 1, 3600),
            ShortJumpSeconds = Math.Clamp(ShortJumpSeconds, 1, 3600),
            MediumJumpSeconds = Math.Clamp(MediumJumpSeconds, 1, 3600),
            LongJumpSeconds = Math.Clamp(LongJumpSeconds, 1, 3600),
            Window = Window is { Width: >= 200, Height: >= 150 } ? Window : null,
            AlwaysOnTop = Enum.IsDefined(AlwaysOnTop) ? AlwaysOnTop : AlwaysOnTop.Never,
            ControlsHideSeconds = double.IsFinite(ControlsHideSeconds) ? Math.Clamp(ControlsHideSeconds, 0.5, 10) : 1.5,
            TitleSeconds = double.IsFinite(TitleSeconds) ? Math.Clamp(TitleSeconds, 0, 30) : 3,
            Theme = Enum.IsDefined(Theme) ? Theme : ThemeChoice.System,
            // Before schema 3 the default was weekly; a user who never changed it gets the new daily default.
            UpdateChecks = !Enum.IsDefined(UpdateChecks) ? UpdateCadence.Daily
                : Schema < 3 && UpdateChecks == UpdateCadence.Weekly ? UpdateCadence.Daily : UpdateChecks,
            UpdateMode = Enum.IsDefined(UpdateMode) ? UpdateMode : UpdateMode.DownloadAndAsk,
            PendingUpdate = PendingUpdate?.Normalize(),
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
            Visualizer = Enum.IsDefined(Visualizer) ? Visualizer : VisualizerChoice.Spectrum,
            AudiobookVisualizer = Enum.IsDefined(AudiobookVisualizer) ? AudiobookVisualizer : VisualizerChoice.Spectrum,
            SubjectPanelSide = Enum.IsDefined(SubjectPanelSide) ? SubjectPanelSide : PanelSide.Right,
            SubjectSelection = Enum.IsDefined(SubjectSelection) ? SubjectSelection : SubjectSelectionMode.Click,
            SubjectRemoval = Enum.IsDefined(SubjectRemoval) ? SubjectRemoval : RemovalMode.Remove,
            MediaOpenClickMode = Enum.IsDefined(MediaOpenClickMode) ? MediaOpenClickMode : Rex.Media.Settings.MediaOpenClick.DoubleClick,
            VideoLook = Enum.IsDefined(VideoLook) ? VideoLook : Rex.Media.Primitives.VideoLook.Original,
            VideoEffect = Enum.IsDefined(VideoEffect) ? VideoEffect : Rex.Media.Primitives.VideoEffect.Off,
            VideoEffectStrength = VideoEffects.Strength(VideoEffectStrength),
            VideoStyleValues = (VideoStyleValues ?? new Dictionary<string, int>())
                .Where(pair => pair.Key is { Length: > 0 and <= 100 })
                .ToDictionary(pair => pair.Key, pair => Math.Clamp(pair.Value, 0, 200), StringComparer.Ordinal),
            AudioArtworkStyle = Enum.IsDefined(AudioArtworkStyle) ? AudioArtworkStyle : ArtworkStyle.Prism,
            AudioArtworkColor = Math.Clamp(AudioArtworkColor, 0, 200),
            AudioArtworkDetail = Math.Clamp(AudioArtworkDetail, 0, 200),
            AudioArtworkContrast = Math.Clamp(AudioArtworkContrast, 0, 200),
            VisualOptions = VisualOptions ?? new Dictionary<string, string>(StringComparer.Ordinal),
            LibraryViews = (LibraryViews ?? new Dictionary<string, LibraryViewChoice>()).Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value.Normalize(), StringComparer.Ordinal),
            LibraryPlaces = (LibraryPlaces ?? new Dictionary<string, LibraryPlace>())
                .Where(pair => pair.Key.Length > 0 && pair.Value is not null)
                .Select(pair => (pair.Key, Place: pair.Value.Normalize()))
                .Where(pair => !pair.Place.IsEmpty)
                .TakeLast(LibraryPlace.Most)
                .ToDictionary(pair => pair.Key, pair => pair.Place, StringComparer.Ordinal),
            PinnedAlbumPaths = [.. (PinnedAlbumPaths ?? []).Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(40)],
            LibraryRoute = string.IsNullOrWhiteSpace(LibraryRoute?.View) ? null : LibraryRoute,
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
            KineticCaptions = (KineticCaptions ?? new KineticCaptionSettings()).Normalize(),
            Transcription = (Transcription ?? new TranscriptionSettings()).Normalize(),
            Metadata = metadata,
            OnlineLookups = false,
            Performance = Enum.IsDefined(Performance) ? Performance : PerformanceChoice.Automatic,
        };
    }
}

/// <summary>How much the player asks of the computer (Preferences, Performance): Automatic decides from the computer itself.</summary>
public enum PerformanceChoice
{
    Automatic,
    BestQuality,
    Balanced,
    Fastest,
}

/// <summary>How many clicks open a media item from the library or playlist.</summary>
public enum MediaOpenClick
{
    SingleClick,
    DoubleClick,
}
