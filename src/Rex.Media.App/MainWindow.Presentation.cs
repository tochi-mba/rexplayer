using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private readonly List<TextBlock> _lyricTexts = [];
    private PlaylistItem? _shownChapterItem;
    private MediaInfo? _shownChapterInfo;

    /// <summary>Brings the picture, the album and the lyrics on screen in line with what plays.</summary>
    private void ShowPresentation()
    {
        var item = _player.Item;
        var info = _player.Info;
        var entry = item?.Location is { } location ? _library?.Entry(location) : null;
        var episode = entry is null ? null : LibraryViews.EpisodeOf(entry);
        // Files opened directly have no library shelf; their own media-type tag still
        // identifies audiobooks and podcasts once the stream has been parsed.
        AudioShelf? audioShelf = entry?.Kind == LibraryKind.Music ? ShelfOf(entry)
            : info?.Metadata.GetValueOrDefault(MetadataKeys.MediaType) switch
            {
                MetadataKeys.Audiobook => AudioShelf.Audiobooks,
                MetadataKeys.Podcast => AudioShelf.Podcasts,
                _ => null,
            };
        var stage = MediaStagePolicy.Resolve(new MediaStageContext(
            HasItem: item is not null,
            IsPicture: item is not null && MediaFiles.IsPicture(item.Location),
            StreamsKnown: info is not null,
            HasVideoTrack: info?.FirstTrack(MediaKind.Video) is not null,
            HasAudioTrack: info?.FirstTrack(MediaKind.Audio) is not null,
            CanSeek: _player.CanSeek,
            Kind: entry?.Kind,
            Shelf: audioShelf,
            IsEpisode: episode is not null,
            IsMovie: entry is { Kind: LibraryKind.Video } && episode is null && MovieRules.IsMovie(entry, MovieLookupOf),
            HasLyrics: _player.Lyrics is not null,
            HasChapters: info?.Chapters.Any(chapter => chapter.Start.IsKnown && chapter.Start >= MediaTime.Zero) == true,
            HasCaptions: _player.Subtitles is not null || _player.SecondarySubtitles is not null,
            HasFailure: _player.State == Rex.Media.Engine.SessionState.Faulted));
        var music = stage.ShowAudioPresentation;
        // A picture or unsupported stream must not leave the previous item's seek controls active.
        SubtitleLayer.Visibility = stage.ShowCaptions ? Visibility.Visible : Visibility.Collapsed;
        SeekBar.IsEnabled = stage.CanSeek;
        StartOverButton.IsEnabled = stage.CanSeek;
        if (stage.Profile is MediaStageProfile.Error or MediaStageProfile.Unsupported)
        {
            VideoBlankLayer.Visibility = Visibility.Visible;
            Idle.Visibility = Visibility.Visible;
            IdleScroller.Visibility = Visibility.Visible;
        }

        IdleKicker.Text = stage.Label;
        // The video itself may hide the Idle panel; the always-present stage must still
        // identify what is playing to keyboard and screen-reader users.
        var stageName = stage.Profile == MediaStageProfile.Welcome
            ? "Playback stage" : stage.Label + " playback stage";
        if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(Stage) != stageName)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Stage, stageName);
        }

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(Stage, stage.Profile switch
        {
            MediaStageProfile.Loading => "Media is opening. Playback capabilities are not known yet.",
            MediaStageProfile.Error => "The active media could not be played.",
            MediaStageProfile.Unsupported => "The active file has no playable audio or video track.",
            MediaStageProfile.Picture => "Picture view; the seek timeline is unavailable.",
            _ when stage.CanSeek => "Playback position can be changed using the seek slider.",
            _ => "Playback position cannot be changed for this stream.",
        });

        var album = music ? _player.Info!.Metadata.GetValueOrDefault(MetadataKeys.Album) : null;
        IdleAlbum.Text = album ?? "";
        IdleAlbum.Visibility = string.IsNullOrEmpty(album) ? Visibility.Collapsed : Visibility.Visible;

        var context = stage.ShowEpisodeContext && episode is not null
            ? string.Join(" · ", new[] { episode.Series, episode.Spoken }.Where(part => !string.IsNullOrWhiteSpace(part)))
            : music ? string.Join(" · ", new[] { _player.Artist, album }.Where(part => !string.IsNullOrWhiteSpace(part))) : "";
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
            // Clear immediately, before async decoding, so the previous item never flashes.
            CoverImage.Source = null;
            CoverBox.Visibility = Visibility.Collapsed;
            _visualCover = null;
            Forget(ShowCoverAsync(cover));
        }

        var lyrics = stage.ShowLyrics && _settings.ShowLyrics ? _player.Lyrics : null;
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
                if (line.At is not { } at || !stage.CanSeek)
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
                    _player.Seek(at);
                    Say(when);
                };
                LyricsLines.Children.Add(button);
            }

            LyricsBox.Visibility = lyrics is null ? Visibility.Collapsed : Visibility.Visible;
            LyricsBox.Height = Math.Min(360, Math.Max(0, Stage.ActualHeight * 0.45));
            ShowLyricLine();
        }

        ShowStageChapters(stage, item, info);
        LayOutMediaStage();
    }

    /// <summary>
    /// Recompose the existing stage when the window narrows: no second player, no duplicated
    /// controls and no new AutomationIDs. Moving elements keeps keyboard focus and transport state.
    /// </summary>
    private void LayOutMediaStage()
    {
        var width = Math.Max(0, Stage.ActualWidth);
        var height = Math.Max(0, Stage.ActualHeight);
        var compact = width < 760 || height < 450;
        var tiny = width < 480 || height < 280;

        Grid.SetColumn(IdleText, compact ? 0 : 1);
        Grid.SetRow(IdleText, compact ? 1 : 0);
        Idle.ColumnDefinitions[1].Width = compact ? new GridLength(0) : GridLength.Auto;
        Idle.ColumnSpacing = compact ? 0 : 32;
        Idle.RowSpacing = compact ? 10 : 0;
        Idle.Margin = new Thickness(tiny ? 12 : compact ? 20 : 32);
        IdleText.MaxWidth = compact ? Math.Max(160, width - 40) : 560;
        IdleTitle.FontSize = tiny ? 24 : compact ? 32 : 44;

        var artSize = Math.Min(240, Math.Max(0, Math.Min(width - 48,
            compact ? height - 180 : height - 48)));
        CoverBox.Width = artSize;
        CoverBox.Height = artSize;
        // Decoding is asynchronous. Never resurrect a stale cover after a stream switch.
        CoverBox.Visibility = _shownCover is not null && CoverImage.Source is not null
            && !tiny && artSize >= 80 ? Visibility.Visible : Visibility.Collapsed;

        LyricsBox.Height = Math.Min(360, Math.Max(0, height * (tiny ? 0.35 : 0.45)));
        LyricsBox.Margin = compact ? new Thickness(12, 0, 12, 12) : new Thickness(48, 0, 48, 24);

        // The transport itself scrolls, preserving all existing buttons and their focus order.
        // On narrow windows the secondary tools move to their own row rather than overlap it.
        var narrowControls = Root.ActualWidth < 820;
        Grid.SetRow(AudioControlStrip, narrowControls ? 2 : 1);
        Grid.SetColumn(AudioControlStrip, narrowControls ? 0 : 2);
        Grid.SetColumnSpan(AudioControlStrip, narrowControls ? 3 : 1);
        AudioControlStrip.HorizontalAlignment = narrowControls
            ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        Controls.Padding = narrowControls ? new Thickness(12, 8, 12, 9) : new Thickness(20, 12, 20, 14);
        Controls.ColumnSpacing = narrowControls ? 6 : 12;
        Controls.RowSpacing = narrowControls ? 4 : 8;
    }

    /// <summary>
    /// Build chapter jumps only from the active, parsed audio stream. The menu belongs to the
    /// same transport as Play/Pause; switching files clears actions that would seek an old item.
    /// </summary>
    private void ShowStageChapters(MediaStageView stage, PlaylistItem? item, MediaInfo? info)
    {
        if (!stage.ShowChapters || item is null || info is null)
        {
            StageChaptersButton.Visibility = Visibility.Collapsed;
            if (_shownChapterItem is not null)
            {
                StageChapterMenu.Items.Clear();
                _shownChapterItem = null;
                _shownChapterInfo = null;
            }

            return;
        }

        if (!ReferenceEquals(item, _shownChapterItem) || !ReferenceEquals(info, _shownChapterInfo))
        {
            StageChapterMenu.Items.Clear();
            foreach (var (index, chapter) in info.Chapters.Index())
            {
                if (!chapter.Start.IsKnown || chapter.Start < MediaTime.Zero)
                {
                    continue;
                }

                var at = chapter.Start.ToTimeSpan();
                if (_player.Duration > TimeSpan.Zero && at > _player.Duration)
                {
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(chapter.Title)
                    ? $"Chapter {index + 1}" : chapter.Title.Trim();
                var name = $"Go to {label} at {TimeText.Format(at, _player.Duration)}";
                var button = new MenuFlyoutItem { Text = $"{TimeText.Format(at, _player.Duration)} · {label}" };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, $"StageChapter-{index}");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
                button.Click += (_, _) =>
                {
                    if (ReferenceEquals(_player.Item, item)
                        && ReferenceEquals(_player.Info, info) && _player.CanSeek)
                    {
                        _player.Seek(at);
                    }
                };
                StageChapterMenu.Items.Add(button);
            }

            _shownChapterItem = item;
            _shownChapterInfo = info;
        }

        StageChaptersButton.Visibility = StageChapterMenu.Items.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
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

        LayOutMediaStage();
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
            LyricsLines.Children[line].StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.5, AnimationDesired = _budget.Animations });
        }
    }
}
