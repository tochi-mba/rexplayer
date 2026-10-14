using Rex.Media.AppCore.Commands;
using Rex.Media.Engine;
using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Player;

/// <summary>What is loaded, as far as the transport buttons and Windows' media controls care.</summary>
public enum TransportMedia
{
    /// <summary>Nothing is loaded.</summary>
    None,

    /// <summary>An item is opening and its details are not known yet.</summary>
    Unknown,

    Audio,

    Video,

    /// <summary>A picture, shown for a while as part of a slideshow.</summary>
    Picture,

    /// <summary>Media with no known length that cannot be sought in: a live stream.</summary>
    Stream,
}

/// <summary>How a transport button shows.</summary>
public enum TransportButtonState
{
    Hidden,
    Disabled,
    Enabled,
}

/// <summary>The playback status Windows' media controls show (its order is theirs).</summary>
public enum TransportStatus
{
    Closed,
    Changing,
    Stopped,
    Playing,
    Paused,
}

/// <summary>The small picture over rexplayer's taskbar button.</summary>
public enum TransportBadge
{
    None,
    Paused,
    Failed,
}

/// <summary>What Windows' media controls, a media key or the taskbar asks of the transport.</summary>
public enum TransportRequest
{
    Play,
    Pause,
    PlayPause,
    Stop,
    Previous,
    Next,
}

/// <summary>
/// Everything about the player the transport depends on, read once. Two equal inputs always give the
/// same <see cref="TransportView"/>, so the window compares inputs, which allocates nothing, and works
/// out a view only when something changed.
/// </summary>
/// <param name="State">The player's state.</param>
/// <param name="Playing">Whether it is playing or about to (opening, buffering, seeking): the button offers Pause.</param>
/// <param name="HasQueue">Whether the playlist holds anything.</param>
/// <param name="Media">What the current item is.</param>
/// <param name="Title">The current item's title, or empty.</param>
/// <param name="EarlierTitle">The title of the item Previous goes back to, or null when there is none before this one.</param>
/// <param name="FollowingTitle">The title of the playlist item Next plays, or null when none follows.</param>
/// <param name="NextEpisode">Where the next episode is, found beside this one, when no playlist item follows; null otherwise.</param>
/// <param name="RestartsFirst">Whether Previous would go back to the start of this item first, as it does a few seconds in.</param>
public readonly record struct TransportInput(
    SessionState State,
    bool Playing,
    bool HasQueue,
    TransportMedia Media,
    string Title,
    string? EarlierTitle,
    string? FollowingTitle,
    string? NextEpisode,
    bool RestartsFirst);

/// <summary>One transport button: whether it shows, what its tooltip says and the command a click runs.</summary>
public readonly record struct TransportButton(TransportButtonState State, string Tooltip, string Command)
{
    public bool IsVisible => State != TransportButtonState.Hidden;

    public bool IsEnabled => State == TransportButtonState.Enabled;
}

/// <summary>What the taskbar's buttons, Windows' media controls and the window's own transport show.</summary>
/// <param name="Previous">Shows only when there is an earlier item to go back to.</param>
/// <param name="PlayPause">Shows whenever there is something to play.</param>
/// <param name="Next">Shows only when something follows: a playlist item, or the next episode after the last one.</param>
/// <param name="Stop">Enabled while there is something to stop.</param>
/// <param name="ShowsPause">Whether the play button offers Pause (it is playing) rather than Play.</param>
/// <param name="Status">The playback status for Windows' media controls.</param>
/// <param name="Badge">The picture over the taskbar button.</param>
public readonly record struct TransportView(
    TransportButton Previous,
    TransportButton PlayPause,
    TransportButton Next,
    TransportButton Stop,
    bool ShowsPause,
    TransportStatus Status,
    TransportBadge Badge);

/// <summary>
/// Which of Previous, Play or Pause, Next and Stop are offered, and what they say, for the taskbar's
/// thumbnail buttons, Windows' media controls (media keys, the volume flyout and the lock screen) and
/// the window's transport, so all of them agree. Previous shows only when there is an earlier item:
/// in the play order, so shuffle's order counts, and repeat-all wraps from the first item to the last;
/// repeat-one does not change what Previous and Next do when asked. Next shows only when something
/// follows, including items added while this one plays and the library's Play next; after the last
/// item, the next episode found beside this one. A click runs the same command the window's button
/// does, so Previous a few seconds into an item goes back to its start first, and its tooltip says so.
/// </summary>
public static class TransportAvailability
{
    /// <summary>The longest tooltip the taskbar holds (its buffer is 260 characters with the terminator).</summary>
    public const int MaxTooltip = 259;

    /// <summary>Nothing loaded and nothing queued: every button hidden, the media controls closed.</summary>
    public static TransportView Nothing { get; } = new(
        new TransportButton(TransportButtonState.Hidden, "Previous", CommandCatalog.Previous),
        new TransportButton(TransportButtonState.Hidden, "Play", CommandCatalog.PlayPause),
        new TransportButton(TransportButtonState.Hidden, "Next", CommandCatalog.Next),
        new TransportButton(TransportButtonState.Hidden, "Stop", CommandCatalog.Stop),
        ShowsPause: false,
        TransportStatus.Closed,
        TransportBadge.None);

    /// <summary>Reads what the transport depends on from <paramref name="player"/>.</summary>
    public static TransportInput Read(PlayerController player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var playlist = player.Playlist;
        var item = player.Item;
        string? earlier = null;
        string? following = null;
        string? episode = null;
        if (item is not null)
        {
            // An entry that would only start this one again is not an earlier or a following item.
            if (playlist.PeekPrevious() is { } before && !IsCurrent(before, item, playlist))
            {
                earlier = before.Title;
            }

            if (playlist.PeekNext(automatic: false) is { } after && !IsCurrent(after, item, playlist))
            {
                following = after.Title;
            }
            else
            {
                episode = player.NextEpisode;
            }
        }

        return new TransportInput(
            player.State,
            player.IsPlaying,
            playlist.Items.Count > 0,
            MediaOf(player),
            item is null ? "" : player.Title,
            earlier,
            following,
            episode,
            player.PreviousRestarts);
    }

    /// <summary>What <paramref name="player"/>'s current item is.</summary>
    public static TransportMedia MediaOf(PlayerController player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.Item is not { } item ? TransportMedia.None
            : MediaFiles.IsPicture(item.Location) ? TransportMedia.Picture
            : player.Info is not { } info ? TransportMedia.Unknown
            : !player.CanSeek ? TransportMedia.Stream
            : info.FirstTrack(MediaKind.Video) is not null ? TransportMedia.Video
            : TransportMedia.Audio;
    }

    /// <summary>The buttons, status and badge for <paramref name="input"/>.</summary>
    public static TransportView Decide(in TransportInput input)
    {
        if (!input.HasQueue && input.Media == TransportMedia.None)
        {
            return Nothing;
        }

        var picture = input.Media == TransportMedia.Picture;
        var playTip = input.Playing ? (picture ? "Pause the slideshow" : "Pause")
            : input.State switch
            {
                SessionState.Ended => "Play again",
                SessionState.Faulted => "Try again",
                _ => picture ? "Play the slideshow" : "Play",
            };

        var previous = input.EarlierTitle is not { } earlier
            ? Nothing.Previous
            : new TransportButton(TransportButtonState.Enabled, Fit(input.RestartsFirst ? Named("Back to the start of ", "Back to the start", input.Title) : Named("Previous: ", "Previous", earlier)), CommandCatalog.Previous);
        var next = input.FollowingTitle is { } following
            ? new TransportButton(TransportButtonState.Enabled, Fit(Named("Next: ", "Next", following)), CommandCatalog.Next)
            : input.NextEpisode is { } episode
                ? new TransportButton(TransportButtonState.Enabled, Fit(Named("Next episode: ", "Next episode", PlaylistItem.TitleOf(episode))), CommandCatalog.NextEpisode)
                : Nothing.Next;

        return new TransportView(
            previous,
            new TransportButton(TransportButtonState.Enabled, playTip, CommandCatalog.PlayPause),
            next,
            Nothing.Stop with { State = input.State == SessionState.Idle ? TransportButtonState.Disabled : TransportButtonState.Enabled },
            input.Playing,
            StatusOf(input),
            input.State switch
            {
                SessionState.Faulted => TransportBadge.Failed,
                SessionState.Paused or SessionState.Ready => TransportBadge.Paused,
                _ => TransportBadge.None,
            });
    }

    /// <summary>
    /// The command <paramref name="request"/> runs while <paramref name="view"/> shows, or null when
    /// it asks for something not on offer: a click on a button that has just gone, Play while playing.
    /// </summary>
    public static string? CommandFor(TransportRequest request, in TransportView view) => request switch
    {
        TransportRequest.Play => view.PlayPause.IsEnabled && !view.ShowsPause ? view.PlayPause.Command : null,
        TransportRequest.Pause => view.PlayPause.IsEnabled && view.ShowsPause ? view.PlayPause.Command : null,
        TransportRequest.PlayPause => view.PlayPause.IsEnabled ? view.PlayPause.Command : null,
        TransportRequest.Stop => view.Stop.IsEnabled ? view.Stop.Command : null,
        TransportRequest.Previous => view.Previous.IsEnabled ? view.Previous.Command : null,
        TransportRequest.Next => view.Next.IsEnabled ? view.Next.Command : null,
        _ => null,
    };

    /// <summary><paramref name="text"/> cut to <see cref="MaxTooltip"/> characters, never through a surrogate pair.</summary>
    public static string Fit(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length <= MaxTooltip)
        {
            return text;
        }

        var cut = MaxTooltip - 3;
        if (char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return string.Concat(text.AsSpan(0, cut), "...");
    }

    /// <summary>
    /// The prefix and the title, or the word alone when there is no title to show. A title that is a
    /// whole path (a file named only by its extension is titled by its location) is never shown: a
    /// tooltip stays readable, and a path is private.
    /// </summary>
    private static string Named(string prefix, string alone, string title) =>
        title.Length > 0 && !Path.IsPathFullyQualified(title) ? prefix + title : alone;

    private static bool IsCurrent(PlaylistItem candidate, PlaylistItem item, Playlist playlist) =>
        ReferenceEquals(candidate, item) || ReferenceEquals(candidate, playlist.Current);

    private static TransportStatus StatusOf(in TransportInput input) => input.State switch
    {
        SessionState.Opening => TransportStatus.Changing,
        SessionState.Playing or SessionState.Buffering or SessionState.Seeking => TransportStatus.Playing,
        SessionState.Paused or SessionState.Ready => TransportStatus.Paused,
        _ => TransportStatus.Stopped,
    };
}
