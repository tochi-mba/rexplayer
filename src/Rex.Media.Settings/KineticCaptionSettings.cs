namespace Rex.Media.Settings;

/// <summary>Where kinetic captions take their words from (OSD-11).</summary>
public enum CaptionWordSource
{
    /// <summary>The subtitle track being shown; failing that, the transcript, if one has been made.</summary>
    Automatic,

    /// <summary>Always the subtitle track being shown, even when there is a transcript.</summary>
    Subtitles,

    /// <summary>Always rexplayer's own transcript, even when a subtitle track is shown.</summary>
    Transcript,

    /// <summary>The subtitle track's words, at the times the transcript heard them.</summary>
    SubtitlesTimedByTranscript,
}

/// <summary>The named looks kinetic captions come in; Custom is the user's own mix.</summary>
public enum CaptionPreset
{
    Creator,
    Karaoke,
    Typewriter,
    Neon,
    Headline,
    Comic,
    Minimal,
    Rave,
    Classic,
    Glitch,
    Ticker,
    Ghost,
    Stamp,
    Pills,
    Marker,
    Stairs,
    Wire,
    Custom,
}

/// <summary>The kind of typeface, for a custom look.</summary>
public enum CaptionFontChoice
{
    Sans,
    Serif,
    Mono,
    Display,
    Rounded,
    Hand,
}

/// <summary>How words are cased, for a custom look.</summary>
public enum CaptionCaseChoice
{
    AsSaid,
    Lower,
    Upper,
}

/// <summary>How a word arrives, for a custom look.</summary>
public enum CaptionMotionChoice
{
    Pop,
    Slide,
    Typewriter,
    Bounce,
    Drop,
    Fade,
    Stamp,
}

/// <summary>Where clusters of words go.</summary>
public enum CaptionLayoutChoice
{
    Scatter,
    Stack,
    Centre,
    Left,
    Confetti,
    Ticker,
    Stairs,
}

/// <summary>The colours stressed words run through.</summary>
public enum CaptionColors
{
    Warm,
    Fire,
    Neon,
    Sunset,
    Ocean,
    Rainbow,

    /// <summary>The accent colour alone.</summary>
    Accent,
}

/// <summary>How fast and how well the speech is transcribed: a larger model hears better and takes longer.</summary>
public enum TranscriptModelChoice
{
    Tiny,
    Base,
    Small,
}

/// <summary>
/// Kinetic captions (OSD-11): words that come in as they are spoken and move the way they were
/// said, placed clear of what matters in the picture.
/// </summary>
public sealed record KineticCaptionSettings
{
    /// <summary>The least and most energy, in percent: how far the voice moves the words.</summary>
    public const int LeastEnergy = 25;

    public const int MostEnergy = 300;

    public bool On { get; init; }

    /// <summary>The named look, or Custom for the choices below.</summary>
    public CaptionPreset Preset { get; init; } = CaptionPreset.Creator;

    // The custom look

    public CaptionFontChoice Font { get; init; } = CaptionFontChoice.Sans;

    public CaptionCaseChoice Case { get; init; } = CaptionCaseChoice.AsSaid;

    public CaptionMotionChoice Motion { get; init; } = CaptionMotionChoice.Pop;

    /// <summary>Only the word being said is bright; the rest of the cluster is dimmed.</summary>
    public bool Highlight { get; init; }

    /// <summary>A shout flickers as it lands.</summary>
    public bool Flicker { get; init; }

    /// <summary>How far words are tilted, in percent of the usual (0 to 300).</summary>
    public int Tilt { get; init; } = 100;

    /// <summary>How thick the outline is, in percent of the usual (0 to 200).</summary>
    public int Outline { get; init; } = 100;

    /// <summary>A dark plate behind every word, not only where the picture needs one.</summary>
    public bool PlateAlways { get; init; }

    /// <summary>White plates with dark text, as print.</summary>
    public bool PlateLight { get; init; }

    /// <summary>The whole line shows at once, dim, and each word lights as it is said.</summary>
    public bool Prefill { get; init; }

    /// <summary>Only the word being said shows.</summary>
    public bool OneAtATime { get; init; }

    /// <summary>Every word the same size and colour, whatever the voice did.</summary>
    public bool Flat { get; init; }

    /// <summary>The red and cyan of each word drawn apart, as a broken feed.</summary>
    public bool Split { get; init; }

    /// <summary>How much every word trembles, in percent of the font size (0 to 20).</summary>
    public int Jitter { get; init; }

    /// <summary>Each word's plate in the word's own colour, the word in ink.</summary>
    public bool PlateTint { get; init; }

    /// <summary>Plain words as outlines only; stressed words fill in.</summary>
    public bool Hollow { get; init; }

    /// <summary>Seconds a word stays before it drifts up and fades on its own (0 to 10; 0 keeps it with its cluster).</summary>
    public int Dissolve { get; init; }

    public CaptionWordSource Words { get; init; } = CaptionWordSource.Automatic;

    /// <summary>How far the voice moves the words, in percent (25 to 300).</summary>
    public int Energy { get; init; } = 100;

    /// <summary>The colour of lifted words and of the accent palette, 0xRRGGBB.</summary>
    public int Accent { get; init; } = 0xFFB400;

    public CaptionColors Colors { get; init; } = CaptionColors.Warm;

    public CaptionLayoutChoice Layout { get; init; } = CaptionLayoutChoice.Scatter;

    /// <summary>A small picture beside words that have one (a muted speaker beside "mute").</summary>
    public bool Glyphs { get; init; } = true;

    /// <summary>Shouted words in capitals.</summary>
    public bool ShoutsInCapitals { get; init; } = true;

    /// <summary>Keep words off faces, text in the picture, the followed subject and movement.</summary>
    public bool AvoidPicture { get; init; } = true;

    /// <summary>Keep the ordinary subtitles showing as well, at the bottom.</summary>
    public bool KeepSubtitles { get; init; }

    public TranscriptModelChoice TranscriptModel { get; init; } = TranscriptModelChoice.Base;

    /// <summary>The language spoken, as a two-letter code, or empty to follow the audio track.</summary>
    public string TranscriptLanguage { get; init; } = "";

    /// <summary>A copy with every value in its allowed range.</summary>
    public KineticCaptionSettings Normalize() => this with
    {
        Preset = Enum.IsDefined(Preset) ? Preset : CaptionPreset.Creator,
        Font = Enum.IsDefined(Font) ? Font : CaptionFontChoice.Sans,
        Case = Enum.IsDefined(Case) ? Case : CaptionCaseChoice.AsSaid,
        Motion = Enum.IsDefined(Motion) ? Motion : CaptionMotionChoice.Pop,
        Tilt = Math.Clamp(Tilt, 0, 300),
        Jitter = Math.Clamp(Jitter, 0, 20),
        Dissolve = Math.Clamp(Dissolve, 0, 10),
        Outline = Math.Clamp(Outline, 0, 200),
        Words = Enum.IsDefined(Words) ? Words : CaptionWordSource.Automatic,
        Energy = Math.Clamp(Energy, LeastEnergy, MostEnergy),
        Accent = Accent & 0xFFFFFF,
        Colors = Enum.IsDefined(Colors) ? Colors : CaptionColors.Warm,
        Layout = Enum.IsDefined(Layout) ? Layout : CaptionLayoutChoice.Scatter,
        TranscriptModel = Enum.IsDefined(TranscriptModel) ? TranscriptModel : TranscriptModelChoice.Base,
        TranscriptLanguage = TranscriptLanguage?.Trim().ToLowerInvariant() is { Length: 2 or 3 } code && code.All(char.IsAsciiLetterLower) ? code : "",
    };
}
