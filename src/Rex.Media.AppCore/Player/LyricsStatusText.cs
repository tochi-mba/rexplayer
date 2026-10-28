namespace Rex.Media.AppCore.Player;

/// <summary>
/// The line under a song's lyrics (META-09): in plain words, how the lyrics follow the song, or that
/// it has none. Only a song says anything. Sounds, podcasts, recordings and audiobooks have no
/// lyrics to miss, and nothing is said while a file opens or once it has failed, so an old message
/// never stays behind.
/// </summary>
public static class LyricsStatusText
{
    /// <summary>A song without lyrics.</summary>
    public const string None = "No lyrics for this song.";

    /// <summary>Lyrics without any times.</summary>
    public const string Untimed = "These lyrics have no timings, so they do not follow the song.";

    /// <summary>Lyrics timed by the line.</summary>
    public const string LineTimed = "The lyrics follow the song line by line.";

    /// <summary>Lyrics timed by the word.</summary>
    public const string WordTimed = "The lyrics follow the song word by word.";

    /// <summary>Lyrics timed by the line, some of them by the word too.</summary>
    public const string PartlyWordTimed = "The lyrics follow the song, some lines word by word.";

    /// <summary>What the online search says when it found the lyrics shown.</summary>
    public const string FromLrclib = "Lyrics from LRCLIB.";

    /// <summary>The status for the stage, or null when none shows.</summary>
    /// <param name="stage">What the stage shows.</param>
    /// <param name="wanted">Lyrics are switched on.</param>
    /// <param name="mode">How the lyrics shown are timed; null when there are none.</param>
    /// <param name="online">What the online search last said about this song, if it was asked.</param>
    /// <param name="problem">Why the lyrics beside the song could not be read, if they could not.</param>
    /// <param name="minimal">The minimal interface is on, where a song without lyrics says nothing.</param>
    public static string? For(in MediaStageView stage, bool wanted, LyricsDisplayMode? mode,
        string? online = null, string? problem = null, bool minimal = false)
    {
        if (!wanted || !stage.ShowAudioPresentation || stage.Profile is not (MediaStageProfile.Song or MediaStageProfile.Audio))
        {
            return null;
        }

        var timing = mode switch
        {
            LyricsDisplayMode.Plain => Untimed,
            LyricsDisplayMode.LineTimed => LineTimed,
            LyricsDisplayMode.WordTimed => WordTimed,
            LyricsDisplayMode.MixedTimed => PartlyWordTimed,
            _ => null,
        };
        if (timing is not null)
        {
            return online == FromLrclib ? FromLrclib + " " + timing : timing;
        }

        return online ?? problem ?? (minimal ? null : None);
    }
}
