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
/// Media-adaptive Windows presentation (AU-19): the AppCore policy chooses the
/// supported stage, while this window arranges art, labels, lyrics and chapter actions.
/// </summary>
public sealed partial class MainWindow
{
    private byte[]? _shownCover;
    private Lyrics? _shownLyrics;
    private PlaylistItem? _shownLyricsItem;
    private MediaInfo? _shownLyricsInfo;
    private TimeSpan _shownLyricsDuration;
    private bool _shownLyricsCanSeek;
    private int _shownLine = -1;
    private readonly List<TextBlock> _lyricTexts = [];
    private PlaylistItem? _shownChapterItem;
    private MediaInfo? _shownChapterInfo;
    private TimeSpan _shownChapterDuration;
    private MediaStageView _stageView;

    /// <summary>Synchronizes the stage, its accessible profile and available media-specific actions.</summary>
    private void ShowPresentation()
    {
        var item = _player.Item;
        var info = _player.Info;
        var entry = item?.Location is { } location ? _library?.Entry(location) : null;
        var episode = entry is null ? null : LibraryViews.EpisodeOf(entry);
        // Direct files may be tagged with a media type or a genre, but they
        // have no library shelf. Resolve those hints in the pure stage policy.
        AudioShelf? audioShelf = entry?.Kind == LibraryKind.Music ? ShelfOf(entry)
            : MediaStagePolicy.ShelfFromTags(
                info?.Metadata.GetValueOrDefault(MetadataKeys.MediaType),
                info?.Metadata.GetValueOrDefault(MetadataKeys.Genre));
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
            HasLyrics: _settings.ShowLyrics && _player.Lyrics is not null,
            // A lone zero-time chapter is just Start Over, not a useful chapter picker.
            HasChapters: MediaStagePolicy.HasChapterJumps(info, _player.Duration),
            HasCaptions: _player.Subtitles is not null || _player.SecondarySubtitles is not null,
            HasFailure: _player.State == Rex.Media.Engine.SessionState.Faulted));
        _stageView = stage;
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
        if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(Stage) != stage.AutomationName)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Stage, stage.AutomationName);
        }

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(Stage, stage.AutomationHelpText);

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
        // The visual album art is decorative only when absent. If present, give
        // screen readers the current item's identity rather than a generic "Cover".
        var coverName = cover is null ? "Cover" : "Cover for " + _player.Title;
        if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(CoverImage) != coverName)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CoverImage, coverName);
        }

        if (!ReferenceEquals(cover, _shownCover))
        {
            _shownCover = cover;
            // Clear immediately, before async decoding, so the previous item never flashes.
            CoverImage.Source = null;
            CoverBox.Visibility = Visibility.Collapsed;
            _visualCover = null;
            Forget(ShowCoverAsync(cover));
        }

        var lyrics = stage.ShowLyrics ? _player.Lyrics : null;
        // Growing media can report a slightly longer duration on each refresh.
        // Rebuilding timed controls every time would repeatedly steal keyboard
        // focus; only rebuild when some lyric becomes (un)seekable.
        var lyricSeekEligibilityChanged = _shownLyricsDuration != _player.Duration
            && lyrics?.Lines.Any(line => line.At is { } at
                && MediaStagePolicy.IsPlayableTimeJump(at, _shownLyricsDuration)
                != MediaStagePolicy.IsPlayableTimeJump(at, _player.Duration)) == true;
        if (!ReferenceEquals(lyrics, _shownLyrics) || !ReferenceEquals(item, _shownLyricsItem)
            || !ReferenceEquals(info, _shownLyricsInfo) || lyricSeekEligibilityChanged
            || (lyrics is not null && _shownLyricsCanSeek != stage.CanSeek))
        {
            // Timed lyrics are focusable buttons. Removing the old lines must not
            // strand keyboard/screen-reader focus on an element outside the tree.
            if (_shownLyrics is not null && Content.XamlRoot is { } root
                && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root) is DependencyObject focused
                && Rex.Media.AppCore.Shell.FocusWithin.IsInside<DependencyObject>(
                    focused, LyricsLines, Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent))
            {
                PlayPauseButton.Focus(FocusState.Programmatic);
            }

            _shownLyrics = lyrics;
            _shownLyricsItem = item;
            _shownLyricsInfo = info;
            _shownLyricsDuration = _player.Duration;
            _shownLyricsCanSeek = stage.CanSeek;
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
                if (line.At is not { } at || !stage.CanSeek
                    || !MediaStagePolicy.IsPlayableTimeJump(at, _player.Duration))
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

                var when = TimeText.Format(at, _player.Duration);
                var button = new Button
                {
                    Content = text,
                    // Keep contrast just behind the words, not across the entire light background.
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x98, 0x0B, 0x10, 0x19)),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(12, 7, 12, 7),
                    MinWidth = 44,
                    MinHeight = 44,
                    MaxWidth = 640,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, $"LyricLine-{index}");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Go to {when}: {line.Text}");
                ToolTipService.SetToolTip(button, $"Go to {when}");
                button.Click += (_, _) =>
                {
                    // A queued click from an old lyrics control must never seek a
                    // replacement stream (or a stream that has lost seek support).
                    if (!ReferenceEquals(_player.Item, item)
                        || !ReferenceEquals(_player.Info, info)
                        || !ReferenceEquals(_player.Lyrics, lyrics)
                        || !MediaStagePolicy.IsPlayableTimeJump(at, _player.Duration)
                        || !_stageView.ShowLyrics || !_stageView.CanSeek
                        || !_settings.ShowLyrics || !_player.CanSeek)
                    {
                        return;
                    }

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
        var compactLyrics = compact && _shownLyrics is not null && LyricsBox.Visibility == Visibility.Visible;

        // On small stages the old fixed top/bottom insets could leave the visualiser
        // with no drawable height at all. Keep a meaningful canvas at every size.
        Visualizer.Margin = tiny ? new Thickness(8)
            : compact ? new Thickness(20, 36, 20, 12)
            : new Thickness(48, 120, 48, 48);

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
            && !tiny && !compactLyrics && artSize >= 80 ? Visibility.Visible : Visibility.Collapsed;
        // Compact lyrics need their own bottom region, not overlap the album artwork.
        Idle.VerticalAlignment = compactLyrics ? VerticalAlignment.Top : VerticalAlignment.Center;

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
        var minimalControls = _settings.MinimalInterface && !IsFullScreen;
        Controls.Padding = minimalControls ? new Thickness(12, 3, 12, 5)
            : narrowControls ? new Thickness(12, 8, 12, 9) : new Thickness(20, 12, 20, 14);
        Controls.ColumnSpacing = narrowControls ? 6 : 12;
        Controls.RowSpacing = minimalControls ? 1 : narrowControls ? 4 : 8;
    }

    /// <summary>
    /// Build chapter jumps only from the active, parsed audio stream. The menu belongs to the
    /// same transport as Play/Pause; switching files clears actions that would seek an old item.
    /// </summary>
    private void ShowStageChapters(MediaStageView stage, PlaylistItem? item, MediaInfo? info)
    {
        if (!stage.ShowChapters || item is null || info is null)
        {
            // A hidden chapter button cannot keep keyboard focus. Return to the
            // permanent playback action rather than dropping focus into the window.
            if (StageChaptersButton.Visibility == Visibility.Visible && Content.XamlRoot is { } xamlRoot)
            {
                var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot);
                if (ReferenceEquals(focused, StageChaptersButton)
                    || (focused is MenuFlyoutItemBase menuItem && StageChapterMenu.Items.Contains(menuItem)))
                {
                    PlayPauseButton.Focus(FocusState.Programmatic);
                }
            }

            StageChaptersButton.Visibility = Visibility.Collapsed;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(StageChaptersButton, "");
            if (_shownChapterItem is not null)
            {
                StageChapterMenu.Items.Clear();
                _shownChapterItem = null;
                _shownChapterInfo = null;
                _shownChapterDuration = default;
            }

            return;
        }

        // A growing spoken stream may update its duration without changing
        // which chapters are actionable. Do not destroy a focused flyout on
        // every such update; rebuild only when eligibility actually changes.
        var chapterEligibilityChanged = _shownChapterDuration != _player.Duration
            && info.Chapters.Any(chapter =>
                MediaStagePolicy.IsPlayableChapter(chapter.Start, _shownChapterDuration)
                != MediaStagePolicy.IsPlayableChapter(chapter.Start, _player.Duration));
        if (!ReferenceEquals(item, _shownChapterItem) || !ReferenceEquals(info, _shownChapterInfo)
            || chapterEligibilityChanged)
        {
            // Replacing a chaptered stream with another chaptered stream also
            // replaces the flyout's items. Restore focus before removing the
            // focused menu item, just as when chapters become unavailable.
            if (Content.XamlRoot is { } xamlRoot
                && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot) is MenuFlyoutItemBase focusedChapter
                && StageChapterMenu.Items.Contains(focusedChapter))
            {
                PlayPauseButton.Focus(FocusState.Programmatic);
            }

            StageChapterMenu.Items.Clear();
            foreach (var (index, chapter) in info.Chapters.Index())
            {
                if (!MediaStagePolicy.IsPlayableChapter(chapter.Start, _player.Duration))
                {
                    continue;
                }

                var at = chapter.Start.ToTimeSpan();

                var label = string.IsNullOrWhiteSpace(chapter.Title)
                    ? $"Chapter {index + 1}" : chapter.Title.Trim();
                var name = $"Go to {label} at {TimeText.Format(at, _player.Duration)}";
                var button = new MenuFlyoutItem
                {
                    Text = $"{TimeText.Format(at, _player.Duration)} · {label}",
                    MinWidth = 44,
                    MinHeight = 44,
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, $"StageChapter-{index}");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
                button.Click += (_, _) =>
                {
                    if (ReferenceEquals(_player.Item, item)
                        && ReferenceEquals(_player.Info, info)
                        && _stageView.ShowChapters && _stageView.CanSeek && _player.CanSeek
                        && MediaStagePolicy.IsPlayableChapter(chapter.Start, _player.Duration)
                        && (!_settings.MinimalInterface || IsFullScreen))
                    {
                        _player.Seek(at);
                    }
                };
                StageChapterMenu.Items.Add(button);
            }

            _shownChapterItem = item;
            _shownChapterInfo = info;
            _shownChapterDuration = _player.Duration;
        }

        StageChaptersButton.Visibility = StageChapterMenu.Items.Count > 0
            && (!_settings.MinimalInterface || IsFullScreen)
            ? Visibility.Visible : Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(StageChaptersButton,
            $"{StageChapterMenu.Items.Count} chapter jumps are available for this audio stream.");
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
                var header = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                var limit = PictureDecodeLimit.For(header.OrientedPixelWidth, header.OrientedPixelHeight, 400);
                stream.Seek(0);
                var image = new BitmapImage { DecodePixelWidth = limit.Width, DecodePixelHeight = limit.Height };
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
