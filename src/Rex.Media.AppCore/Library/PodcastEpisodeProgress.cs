using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// A local episode's honest state and progress, without fetching its feed or probing the disk.
/// Missing or impossible durations never produce fabricated percentages or remaining times.
/// </summary>
public sealed record PodcastEpisodeProgress(string Status, TimeSpan? Remaining, double Fraction)
{
    public static PodcastEpisodeProgress Of(LibraryEntry episode, TimeSpan? leftAt)
    {
        ArgumentNullException.ThrowIfNull(episode);
        if (episode.Problem is not null)
        {
            return new PodcastEpisodeProgress("File unavailable", null, 0);
        }

        if (episode.Downloading)
        {
            return new PodcastEpisodeProgress("File incomplete", null, 0);
        }

        var length = episode.Duration is { Ticks: > 0 } known ? known : (TimeSpan?)null;
        if (leftAt is { } at)
        {
            var position = at < TimeSpan.Zero ? TimeSpan.Zero : at;
            if (length is { } duration)
            {
                if (position > duration)
                {
                    position = duration;
                }

                return new PodcastEpisodeProgress("Stopped at " + TimeText.Format(position, duration),
                    duration - position, Math.Clamp(position.Ticks / (double)duration.Ticks, 0, 1));
            }

            return new PodcastEpisodeProgress("Stopped at " + TimeText.Format(position), null, 0);
        }

        return new PodcastEpisodeProgress(episode.Plays > 0 ? "Played" : "New",
            episode.Plays > 0 ? TimeSpan.Zero : length, episode.Plays > 0 ? 1 : 0);
    }
}
