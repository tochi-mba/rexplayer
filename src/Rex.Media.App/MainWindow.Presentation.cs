using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Rex.Media.AppCore.Player;
using Rex.Media.Primitives;
using Windows.Storage.Streams;

namespace Rex.Media.App;

/// <summary>
/// Music without pictures (AU-19): its cover beside its title, artist and album, and its lyrics
/// below with the line being sung marked (META-09). AppCore finds them; this shows them.
/// </summary>
public sealed partial class MainWindow
{
    private byte[]? _shownCover;
    private Lyrics? _shownLyrics;
    private int _shownLine = -1;
    private readonly List<TextBlock> _lyricTexts = [];

    /// <summary>Brings the picture, the album and the lyrics on screen in line with what plays.</summary>
    private void ShowPresentation()
    {
        var music = _player.Item is not null && !HasVideo && _player.Info is not null;
        var album = music ? _player.Info!.Metadata.GetValueOrDefault(MetadataKeys.Album) : null;
        IdleAlbum.Text = album ?? "";
        IdleAlbum.Visibility = string.IsNullOrEmpty(album) ? Visibility.Collapsed : Visibility.Visible;

        var cover = music ? _player.CoverArt : null;
        if (!ReferenceEquals(cover, _shownCover))
        {
            _shownCover = cover;
            _ = ShowCoverAsync(cover);
        }

        var lyrics = music ? _player.Lyrics : null;
        if (!ReferenceEquals(lyrics, _shownLyrics))
        {
            _shownLyrics = lyrics;
            _shownLine = -1;
            LyricsLines.Children.Clear();
            _lyricTexts.Clear();
            foreach (var (index, line) in (lyrics?.Lines ?? []).Index())
            {
                var text = new TextBlock
                {
                    Text = line.Text.Length == 0 ? " " : line.Text,
                    FontSize = 20,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                _lyricTexts.Add(text);
                if (line.At is not { } at)
                {
                    LyricsLines.Children.Add(text);
                    continue;
                }

                var when = TimeText.Format(at < TimeSpan.Zero ? TimeSpan.Zero : at, _player.Duration);
                var button = new Button
                {
                    Content = text,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 7, 12, 7),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, $"LyricLine-{index}");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Go to {when}: {line.Text}");
                ToolTipService.SetToolTip(button, $"Go to {when}");
                button.Click += (_, _) =>
                {
                    _player.Seek(at);
                    Say(when);
                };
                LyricsLines.Children.Add(button);
            }

            LyricsBox.Visibility = lyrics is null ? Visibility.Collapsed : Visibility.Visible;
            LyricsBox.Height = Math.Max(120, Stage.ActualHeight * 0.45);
            ShowLyricLine();
        }
    }

    /// <summary>The cover beside the details, or the details alone, centred, when there is none (or it will not load).</summary>
    private async Task ShowCoverAsync(byte[]? cover)
    {
        var shown = false;
        if (cover is not null)
        {
            try
            {
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(cover.AsBuffer());
                stream.Seek(0);
                var image = new BitmapImage { DecodePixelWidth = 400 };
                await image.SetSourceAsync(stream);
                if (!ReferenceEquals(cover, _shownCover))
                {
                    return;
                }

                CoverImage.Source = image;
                shown = true;
            }
            catch (Exception ex) when (ex is COMException or ArgumentException)
            {
                App.Log.Info(LogSource, "The cover picture could not be shown: " + ex.Message);
            }
        }

        CoverBox.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown)
        {
            CoverImage.Source = null;
        }

        // With a cover the details sit beside it, left-aligned; without, they are centred.
        var alignment = shown ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        var textAlignment = shown ? TextAlignment.Left : TextAlignment.Center;
        foreach (var text in new[] { IdleTitle, IdleHint, IdleAlbum })
        {
            text.HorizontalAlignment = alignment;
            text.TextAlignment = textAlignment;
        }
    }

    /// <summary>Marks the line being sung and brings it to the middle of the lyrics.</summary>
    private void ShowLyricLine()
    {
        if (_shownLyrics is null)
        {
            return;
        }

        var line = _player.LyricIndex;
        if (line == _shownLine)
        {
            return;
        }

        if (_shownLine >= 0 && _shownLine < _lyricTexts.Count)
        {
            var previous = _lyricTexts[_shownLine];
            previous.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
            previous.FontWeight = FontWeights.Normal;
        }

        _shownLine = line;
        if (line >= 0 && line < _lyricTexts.Count)
        {
            var current = _lyricTexts[line];
            current.Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            current.FontWeight = FontWeights.SemiBold;
            LyricsLines.Children[line].StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.5, AnimationDesired = true });
        }
    }
}
