using Rex.Media.AppCore.Library;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Player;

/// <summary>The media-specific presentation, never a second playback session.</summary>
public enum MediaStageProfile
{
    Welcome,
    Loading,
    Unsupported,
    Error,
    Picture,
    Video,
    Movie,
    Television,
    Song,
    Audiobook,
    Podcast,
    Recording,
    Sound,
    Audio,
}

/// <summary>
/// A read-only snapshot of proven streams and optional library evidence.
/// Stream information, when available, outranks stored media classifications.
/// </summary>
public readonly record struct MediaStageContext(
    bool HasItem,
    bool IsPicture = false,
    bool StreamsKnown = false,
    bool HasVideoTrack = false,
    bool HasAudioTrack = false,
    bool CanSeek = false,
    LibraryKind? Kind = null,
    AudioShelf? Shelf = null,
    bool IsEpisode = false,
    bool IsMovie = false,
    bool HasLyrics = false,
    bool HasChapters = false,
    bool HasCaptions = false,
    bool HasFailure = false);

/// <summary>Stage semantics derived without creating a decoder, player, queue or window.</summary>
public readonly record struct MediaStageView(
    MediaStageProfile Profile,
    string Label,
    bool ShowAudioPresentation,
    bool ShowLyrics,
    bool ShowChapters,
    bool ShowCaptions,
    bool SupportsKineticCaptions,
    bool ShowEpisodeContext,
    bool ShowPictureNavigation,
    bool CanSeek)
{
    /// <summary>A stable UI Automation label for the current stage.</summary>
    public string AutomationName => Profile == MediaStageProfile.Welcome
        ? "Playback stage" : Label + " playback stage";

    /// <summary>Screen-reader help derived from proven capabilities, never the previous media.</summary>
    public string AutomationHelpText => Profile switch
    {
        MediaStageProfile.Loading => "Media is opening. Playback capabilities are not known yet.",
        MediaStageProfile.Error => "The active media could not be played.",
        MediaStageProfile.Unsupported => "The active file has no playable audio or video track.",
        MediaStageProfile.Picture => "Picture view; the seek timeline is unavailable.",
        _ => (CanSeek
            ? "Playback position can be changed using the seek slider."
            : "Playback position cannot be changed for this stream.")
            + (ShowChapters ? " Chapter jumps are in the full playback controls." : "")
            + (ShowLyrics ? " Lyrics are available for this stream." : "")
            + (ShowCaptions ? " Captions are available for this stream." : ""),
    };
}

/// <summary>Resolves media facts to an accessible stage; capabilities are independent of labels.</summary>
public static class MediaStagePolicy
{
    public static MediaStageView Resolve(in MediaStageContext context)
    {
        if (!context.HasItem)
        {
            return Present(MediaStageProfile.Welcome, "WELCOME", context);
        }

        if (context.HasFailure)
        {
            return Present(MediaStageProfile.Error, "PLAYBACK ERROR", context);
        }

        // Image playback has its own navigation rather than an audio/video transport.
        if (context.IsPicture)
        {
            return Present(MediaStageProfile.Picture, "PICTURE", context);
        }

        if (context.StreamsKnown)
        {
            if (context.HasVideoTrack)
            {
                return Video(context);
            }

            if (context.HasAudioTrack)
            {
                return Audio(context);
            }

            // Never invent movie/audio controls when a parsed file has neither stream.
            return Present(MediaStageProfile.Unsupported, "UNSUPPORTED MEDIA", context);
        }

        // While a file opens, known library classification is useful but not authoritative.
        return context.Kind switch
        {
            LibraryKind.Picture => Present(MediaStageProfile.Picture, "PICTURE", context),
            LibraryKind.Video => Video(context),
            LibraryKind.Music => Audio(context),
            _ => Present(MediaStageProfile.Loading, "OPENING", context),
        };
    }

    private static MediaStageView Video(in MediaStageContext context) =>
        context.IsEpisode ? Present(MediaStageProfile.Television, "TV EPISODE", context)
        : context.IsMovie ? Present(MediaStageProfile.Movie, "MOVIE", context)
        : Present(MediaStageProfile.Video, "VIDEO", context);

    private static MediaStageView Audio(in MediaStageContext context) =>
        context.Kind != LibraryKind.Music && context.Shelf is null ? Present(MediaStageProfile.Audio, "AUDIO", context)
        : context.Shelf switch
        {
            AudioShelf.Audiobooks => Present(MediaStageProfile.Audiobook, "AUDIOBOOK", context),
            AudioShelf.Podcasts => Present(MediaStageProfile.Podcast, "PODCAST", context),
            AudioShelf.Recordings => Present(MediaStageProfile.Recording, "RECORDING", context),
            AudioShelf.Sounds => Present(MediaStageProfile.Sound, "AUDIO", context),
            _ => Present(MediaStageProfile.Song, "NOW PLAYING", context),
        };

    private static MediaStageView Present(MediaStageProfile profile, string label, in MediaStageContext context)
    {
        var audio = profile is MediaStageProfile.Song or MediaStageProfile.Audiobook
            or MediaStageProfile.Podcast or MediaStageProfile.Recording or MediaStageProfile.Sound or MediaStageProfile.Audio;
        var video = profile is MediaStageProfile.Video or MediaStageProfile.Movie or MediaStageProfile.Television;
        var readyAudio = audio && context.StreamsKnown && context.HasAudioTrack;
        return new MediaStageView(
            profile,
            label,
            readyAudio,
            (profile is MediaStageProfile.Song or MediaStageProfile.Audio) && readyAudio && context.HasLyrics,
            (profile is MediaStageProfile.Audiobook or MediaStageProfile.Podcast) && readyAudio && context.HasChapters && context.CanSeek,
            context.HasCaptions && context.StreamsKnown && (audio || video) && (context.HasAudioTrack || context.HasVideoTrack),
            video && context.StreamsKnown && context.HasVideoTrack,
            profile == MediaStageProfile.Television,
            profile == MediaStageProfile.Picture,
            context.StreamsKnown && context.CanSeek && (audio || video));
    }
}
