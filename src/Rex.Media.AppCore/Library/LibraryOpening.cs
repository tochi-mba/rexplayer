namespace Rex.Media.AppCore.Library;

/// <summary>What playback does when the library opens over the picture.</summary>
public enum LibraryOpenAction
{
    /// <summary>What plays carries on behind the library.</summary>
    Continue,

    /// <summary>What plays pauses where it is, to go on from there when the user asks.</summary>
    Pause,
}

/// <summary>
/// Whether opening the library pauses what is playing (LIB-05). A video is paused so none of it is
/// missed while browsing; a song keeps playing when the user asks for that, since there is nothing
/// to miss. Closing the library never changes playback: a paused item stays paused where it is,
/// and the user plays on when ready.
/// </summary>
public static class LibraryOpening
{
    /// <param name="pauseWhenLibraryOpens">The setting: opening the library pauses playback.</param>
    /// <param name="keepAudioPlaying">The setting: sound-only items keep playing all the same.</param>
    /// <param name="itemHasVideo">Whether what plays has a picture.</param>
    /// <param name="isPlaying">Whether anything is playing now; nothing is paused that is not.</param>
    public static LibraryOpenAction Decide(bool pauseWhenLibraryOpens, bool keepAudioPlaying, bool itemHasVideo, bool isPlaying)
    {
        if (!pauseWhenLibraryOpens || !isPlaying)
        {
            return LibraryOpenAction.Continue;
        }

        return itemHasVideo || !keepAudioPlaying ? LibraryOpenAction.Pause : LibraryOpenAction.Continue;
    }
}
