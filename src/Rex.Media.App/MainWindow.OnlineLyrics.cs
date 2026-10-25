using System.Net;
using System.Text.Json;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    private static readonly HttpClient LyricsClient = new() { Timeout = TimeSpan.FromSeconds(12) };
    private PlaylistItem? _onlineLyricsItem;
    private CancellationTokenSource? _onlineLyricsCancellation;
    private string? _onlineLyricsStatus;
    private DateTimeOffset _onlineLyricsRetryAt;
    private bool _findingLyrics;

    private async void OnFindLyrics(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_findingLyrics || _player.Item is not { } item || _player.Lyrics is not null
            || string.IsNullOrWhiteSpace(_player.Artist) || _player.Duration is not { TotalSeconds: >= 1 and <= 3600 })
        {
            return;
        }

        if (DateTimeOffset.UtcNow < _onlineLyricsRetryAt)
        {
            _onlineLyricsStatus = "The lyrics service asked us to wait before trying again.";
            ShowPresentation();
            return;
        }

        _onlineLyricsItem = item;
        var title = _player.Title;
        var artist = _player.Artist;
        var duration = _player.Duration;
        using var cancellation = new CancellationTokenSource();
        _onlineLyricsCancellation = cancellation;
        _findingLyrics = true;
        _onlineLyricsStatus = "Looking for lyrics online...";
        ShowPresentation();
        try
        {
            var uri = new Uri("https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(title)
                + "&artist_name=" + Uri.EscapeDataString(artist!)
                + "&duration=" + Math.Round(duration.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd($"rexplayer/{Version} (https://github.com/tochi-mba/rexplayer)");
            using var response = await LyricsClient.SendAsync(request, cancellation.Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _onlineLyricsRetryAt = response.Headers.RetryAfter?.Date
                    ?? DateTimeOffset.UtcNow + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
                _onlineLyricsStatus = "The lyrics service asked us to wait before trying again.";
            }
            else if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _onlineLyricsStatus = "No matching lyrics were found online.";
            }
            else
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > 262_144)
                {
                    throw new HttpRequestException("The lyrics response was too large.");
                }

                var json = await response.Content.ReadAsStringAsync(cancellation.Token);
                if (json.Length > 262_144)
                {
                    throw new HttpRequestException("The lyrics response was too large.");
                }

                var result = JsonSerializer.Deserialize<OnlineLyricsResult>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var lyrics = result?.Match(title, artist!, duration);
                _onlineLyricsStatus = lyrics is null ? "No reliable lyrics match was found online."
                    : _player.UseOnlineLyrics(item, lyrics) ? "Lyrics from LRCLIB."
                    : null;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A new song replaced the request, or the window is closing.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _onlineLyricsStatus = "The lyrics service is unavailable right now.";
            App.Log.Warning("lyrics", "Online lookup failed: " + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_onlineLyricsCancellation, cancellation))
            {
                _onlineLyricsCancellation = null;
                _findingLyrics = false;
                if (!_closing && ReferenceEquals(_player.Item, item))
                {
                    ShowPresentation();
                }
            }
        }
    }
}
