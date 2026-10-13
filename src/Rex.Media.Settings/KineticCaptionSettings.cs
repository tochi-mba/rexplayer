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

/// <summary>Where clusters of words go.</summary>
public enum CaptionLayoutChoice
{
    Scatter,
    Stack,
    Centre,
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
        Words = Enum.IsDefined(Words) ? Words : CaptionWordSource.Automatic,
        Energy = Math.Clamp(Energy, LeastEnergy, MostEnergy),
        Accent = Accent & 0xFFFFFF,
        Colors = Enum.IsDefined(Colors) ? Colors : CaptionColors.Warm,
        Layout = Enum.IsDefined(Layout) ? Layout : CaptionLayoutChoice.Scatter,
        TranscriptModel = Enum.IsDefined(TranscriptModel) ? TranscriptModel : TranscriptModelChoice.Base,
        TranscriptLanguage = TranscriptLanguage?.Trim().ToLowerInvariant() is { Length: 2 or 3 } code && code.All(char.IsAsciiLetterLower) ? code : "",
    };
}
