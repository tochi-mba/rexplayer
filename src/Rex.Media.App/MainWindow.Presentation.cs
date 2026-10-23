using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Visuals;
using Rex.Media.Library;
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
    private bool _followCurrentLyric = true;
    private readonly List<TextBlock> _lyricTexts = [];
    private readonly Dictionary<int, List<Run>> _lyricWordRuns = [];
    private int _shownWordLine = -1;
    private int _shownWordIndex = -2;

    /// <summary>
    /// A manual scroll should not be immediately undone by the next playback tick.
    /// The button is available by pointer, Tab and screen-reader navigation.
    /// </summary>
    private void WireLyricNavigation()
    {
        // The ScrollViewer and its scrollbars can handle these events internally.
        // Observe them even when handled, so dragging the thumb, wheel, touch, or
        // keyboard scrolling never gets immediately undone by the playback timer.
        LyricsBox.AddHandler(UIElement.PointerWheelChangedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => SuspendLyricFollow()), handledEventsToo: true);
        LyricsBox.AddHandler(UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => SuspendLyricFollow()), handledEventsToo: true);
        LyricsBox.AddHandler(UIElement.KeyDownEvent,
            new Microsoft.UI.Xaml.Input.KeyEventHandler((_, args) =>
            {
                if (args.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down
                    or Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown
                    or Windows.System.VirtualKey.Home or Windows.System.VirtualKey.End)
                {
                    SuspendLyricFollow();
                }
            }), handledEventsToo: true);
        LyricsFollowButton.Click += (_, _) =>
        {
            _followCurrentLyric = true;
            LyricsFollowButton.Visibility = Visibility.Collapsed;
            BringActiveLyricIntoView();
        };

        Stage.SizeChanged += (_, _) =>
        {
            if (_shownLyrics is null)
            {
                return;
            }

            // A narrower window changes wrapping and the vertical anchor.
            // Do not reset the user's manually suspended follow state.
            LyricsBox.Height = Math.Max(120, Stage.ActualHeight * 0.45);
            if (_followCurrentLyric)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_closed && _followCurrentLyric)
                    {
                        BringActiveLyricIntoView();
                    }
                });
            }
        };
    }

    private void SuspendLyricFollow()
    {
        if (_shownLyrics is not { IsTimed: true })
        {
            return;
        }

        _followCurrentLyric = false;
        LyricsFollowButton.Visibility = Visibility.Visible;
    }

    private void BringActiveLyricIntoView()
    {
        var current = _player.LyricIndex;
        if (current >= 0 && current < LyricsLines.Children.Count)
        {
            LyricsLines.Children[current].StartBringIntoView(new BringIntoViewOptions
            {
                VerticalAlignmentRatio = 0.5,
                AnimationDesired = _budget.Animations,
            });
        }
    }

    /// <summary>Brings the picture, the album and the lyrics on screen in line with what plays.</summary>
    private void ShowPresentation()
    {
        var music = _player.Item is not null && !HasVideo && _player.Info is not null;
        var entry = _player.Item?.Location is { } location ? _library?.Entry(location) : null;
        var episode = entry is null ? null : LibraryViews.EpisodeOf(entry);
        var kind = entry?.Kind switch
        {
            LibraryKind.Picture => "PICTURE",
            LibraryKind.Video when episode is not null => "TV EPISODE",
            LibraryKind.Video => "VIDEO",
            LibraryKind.Music => ShelfOf(entry) switch
            {
                AudioShelf.Audiobooks => "AUDIOBOOK",
                AudioShelf.Podcasts => "PODCAST",
                AudioShelf.Recordings => "RECORDING",
                AudioShelf.Sounds => "AUDIO",
                _ => "NOW PLAYING",
            },
            _ when _player.Item is not null && HasVideo => "VIDEO",
            _ when music => "NOW PLAYING",
            _ => "WELCOME",
        };
        IdleKicker.Text = kind;

        var album = music ? _player.Info!.Metadata.GetValueOrDefault(MetadataKeys.Album) : null;
        IdleAlbum.Text = album ?? "";
        IdleAlbum.Visibility = string.IsNullOrEmpty(album) ? Visibility.Collapsed : Visibility.Visible;

        var context = episode is not null
            ? string.Join(" · ", new[] { episode.Series, episode.Spoken }.Where(part => !string.IsNullOrWhiteSpace(part)))
            : string.Join(" · ", new[] { _player.Artist, album }.Where(part => !string.IsNullOrWhiteSpace(part)));
        NowPlayingContext.Text = context;
        NowPlayingContext.Visibility = string.IsNullOrWhiteSpace(context) ? Visibility.Collapsed : Visibility.Visible;

        string[] facts = entry is null ? [] : new string?[]
            {
                entry.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                entry.Genre,
                entry.Duration is { } duration && duration > TimeSpan.Zero ? TimeText.Format(duration, duration) : null,
            }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!)
            .ToArray();
        IdleMeta.Text = string.Join(" · ", facts);
        IdleMeta.Visibility = facts.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        var cover = music ? _player.CoverArt : null;
        if (!ReferenceEquals(cover, _shownCover))
        {
            _shownCover = cover;
            Forget(ShowCoverAsync(cover));
        }

        var lyrics = music && _settings.ShowLyrics ? _player.Lyrics : null;
        var lyricStatus = !_settings.ShowLyrics || _player.Item is null || HasVideo ? null
            : _player.Info is null ? "Loading lyrics from local media..."
            : lyrics?.DisplayMode switch
            {
                LyricsDisplayMode.Plain => "Untimed lyrics; no line or word times were supplied.",
                LyricsDisplayMode.LineTimed => "Timed lyrics; only line starts were supplied.",
                LyricsDisplayMode.WordTimed => "Word-timed lyrics; highlights follow exact source timestamps.",
                LyricsDisplayMode.MixedTimed => "Partly word-timed lyrics; untimed words are shown without invented timing.",
                _ => _player.LyricsProblem ?? "No lyrics found in this song or beside it.",
            };
        if (LyricsStatus.Text != lyricStatus)
        {
            LyricsStatus.Text = lyricStatus ?? "";
        }

        LyricsStatus.Visibility = lyricStatus is null ? Visibility.Collapsed : Visibility.Visible;
        if (!ReferenceEquals(lyrics, _shownLyrics))
        {
            // Refreshing playback content can remove the currently focused
            // lyric button. Restore keyboard focus only when it was already
            // inside the lyrics; never steal focus from the user's other tasks.
            var focusedLyrics = HasFocusIn(LyricsBox);
            _shownLyrics = lyrics;
            _shownLine = -1;
            _shownWordLine = -1;
            _shownWordIndex = -2;
            _lyricWordRuns.Clear();
            _followCurrentLyric = true;
            LyricsFollowButton.Visibility = Visibility.Collapsed;
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
                // Render word timing only when the parser has independently
                // verified every segment. A line-only LRC line stays line-only.
                if (line.Words.Count > 0)
                {
                    text.Text = "";
                    var runs = new List<Run>();
                    foreach (var word in line.Words)
                    {
                        var run = new Run { Text = word.Text };
                        text.Inlines.Add(run);
                        runs.Add(run);
                    }

                    _lyricWordRuns[index] = runs;
                }

                _lyricTexts.Add(text);
                if (line.At is not { } at)
                {
                    // Untimed lyric lines need the same close-fitting contrast as timed lines;
                    // never paint a broad translucent rectangle behind the whole stage.
                    LyricsLines.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x98, 0x0B, 0x10, 0x19)),
                        CornerRadius = new CornerRadius(9),
                        Padding = new Thickness(12, 7, 12, 7),
                        MaxWidth = 640,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Child = text,
                    });
                    continue;
                }

                var when = TimeText.Format(at < TimeSpan.Zero ? TimeSpan.Zero : at, _player.Duration);
                var button = new Button
                {
                    Content = text,
                    // Keep contrast just behind the words, not across the entire light background.
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x98, 0x0B, 0x10, 0x19)),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(12, 7, 12, 7),
                    MaxWidth = 640,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, $"LyricLine-{index}");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Go to {when}: {line.Text}");
                ToolTipService.SetToolTip(button, $"Go to {when}");
                button.Click += (_, _) =>
                {
                    _followCurrentLyric = true;
                    LyricsFollowButton.Visibility = Visibility.Collapsed;
                    _player.Seek(at);
                    Say(when);
                };
                LyricsLines.Children.Add(button);
            }

            LyricsBox.Visibility = lyrics is null ? Visibility.Collapsed : Visibility.Visible;
            LyricsBox.Height = Math.Max(120, Stage.ActualHeight * 0.45);
            ShowLyricLine();
            if (focusedLyrics && lyrics is not null)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_closed || !ReferenceEquals(lyrics, _shownLyrics))
                    {
                        return;
                    }

                    if (LyricsLines.Children.OfType<Button>().FirstOrDefault() is { } nextLine)
                    {
                        nextLine.Focus(FocusState.Keyboard);
                    }
                    else
                    {
                        LyricsBox.Focus(FocusState.Keyboard);
                    }
                });
            }
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

                var visualCover = await DecodeVisualPictureAsync(cover);
                if (_closed || !ReferenceEquals(cover, _shownCover))
                {
                    return;
                }

                _visualCover = visualCover;
                CoverImage.Source = image;
                shown = true;
            }
            catch (Exception ex) when (ex is COMException or ArgumentException)
            {
                App.Log.Info(LogSource, "The cover picture could not be shown: " + ex.Message);
            }
        }

        if (_closed || !ReferenceEquals(cover, _shownCover))
        {
            return;
        }

        CoverBox.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown)
        {
            _visualCover = null;
            CoverImage.Source = null;
        }

        // With a cover the details sit beside it, left-aligned; without, they are centred.
        var alignment = shown ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        var textAlignment = shown ? TextAlignment.Left : TextAlignment.Center;
        foreach (var text in new[] { IdleKicker, IdleTitle, IdleHint, IdleAlbum, IdleMeta })
        {
            text.HorizontalAlignment = alignment;
            text.TextAlignment = textAlignment;
        }
    }

    /// <summary>A small BGRA copy of cover art for software-drawn visualisations.</summary>
    private static async Task<VisualPicture> DecodeVisualPictureAsync(byte[] encoded)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(encoded.AsBuffer());
        stream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        var scale = Math.Min(1, 400.0 / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var width = Math.Max(1u, (uint)Math.Round(decoder.PixelWidth * scale));
        var height = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * scale));
        var transform = new Windows.Graphics.Imaging.BitmapTransform { ScaledWidth = width, ScaledHeight = height };
        var data = await decoder.GetPixelDataAsync(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            transform,
            Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation,
            Windows.Graphics.Imaging.ColorManagementMode.ColorManageToSRgb);
        var rotated = decoder.OrientedPixelWidth != decoder.PixelWidth;
        return new VisualPicture(data.DetachPixelData(), (int)(rotated ? height : width), (int)(rotated ? width : height));
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
            UpdateLyricWords(line);
            return;
        }

        if (_shownLine >= 0 && _shownLine < _lyricTexts.Count)
        {
            var previous = _lyricTexts[_shownLine];
            previous.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
            previous.FontWeight = FontWeights.Normal;
            ClearLyricWords(_shownLine);
        }

        _shownLine = line;
        if (line >= 0 && line < _lyricTexts.Count)
        {
            var current = _lyricTexts[line];
            // With precise timings only the words already sung take the
            // accent, not the entire line ahead of the singer.
            current.Foreground = _lyricWordRuns.ContainsKey(line)
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF))
                : (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            current.FontWeight = FontWeights.SemiBold;
            UpdateLyricWords(line);
            if (_followCurrentLyric)
            {
                BringActiveLyricIntoView();
            }
        }
    }

    /// <summary>Only source-verified word starts control the karaoke highlight.</summary>
    private void UpdateLyricWords(int line)
    {
        if (_shownLyrics is null || !_lyricWordRuns.TryGetValue(line, out var runs))
        {
            return;
        }

        var index = Lyrics.WordIndexAt(_shownLyrics.Lines[line], _player.Position);
        if (line == _shownWordLine && index == _shownWordIndex)
        {
            return;
        }

        _shownWordLine = line;
        _shownWordIndex = index;
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var neutral = new SolidColorBrush(Windows.UI.Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
        for (var i = 0; i < runs.Count; i++)
        {
            runs[i].Foreground = i <= index ? accent : neutral;
        }
    }

    /// <summary>Last line's karaoke segments no longer inherit the active accent.</summary>
    private void ClearLyricWords(int line)
    {
        if (!_lyricWordRuns.TryGetValue(line, out var runs))
        {
            return;
        }

        var neutral = new SolidColorBrush(Windows.UI.Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
        foreach (var run in runs)
        {
            run.Foreground = neutral;
        }

        _shownWordLine = -1;
        _shownWordIndex = -2;
    }
}