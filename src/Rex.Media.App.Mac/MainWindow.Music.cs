using Rex.Media.Primitives;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Visuals;
using Rex.Media.Audio;
using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>
/// The stage while music plays (AU-18, AU-19, META-09), laid out as the Windows stage is by the shared
/// <see cref="StageLayout"/>: the title block with the cover, the visualisation in a band of its own
/// under it (a scene drawn in software spans the whole stage behind the title), and the lyrics dock
/// at the foot, with the lyrics status and the lyrics window's button. The classic visualisations are
/// drawn as shapes from the same analysers the Windows stage uses; the scenes are drawn by AppCore's
/// <see cref="VisualStage"/> away from the window, a frame at a time, at the band's own size in the
/// screen's own pixels, and shown as they are.
/// </summary>
public sealed partial class MainWindow
{
    private MusicStageHost? _musicStage;
    private VisualStageView? _musicVisual;
    private MusicBarsView? _musicBars;
    private RexButton? _musicRecalibrate;
    private MusicCoverView? _musicArtwork;
    private RexLabel? _musicKicker;
    private RexLabel? _musicTitle;
    private RexLabel? _musicArtist;
    private RexLabel? _musicAlbum;
    private RexLabel? _musicMeta;
    private readonly RexPanel[] _musicLyricChips = new RexPanel[3];
    private readonly RexLabel[] _musicLyricRows = new RexLabel[3];
    private RexPanel? _musicLyricStatusPill;
    private RexLabel? _musicLyricStatus;
    private string _musicHeadingText = "";
    private string _musicLyricsText = "";
    private int _musicLyricRowsShown;
    private int _musicLyricCurrentRow = -1;
    private double _musicTitleHeight;
    private NSTimer? _musicTimer;
    private NSTimer? _musicFrameTimer;
    private NSImage? _musicImage;
    private byte[]? _musicImageSource;
    private VisualPicture? _musicVisualCover;
    private PlaylistItem? _musicShownItem;
    private int _musicLyricLine = -2;
    private bool _musicShowLyrics = true;

    // The visualisation: what is wanted, what was built, and whether it is being drawn.
    private VisualizerChoice? _musicBuiltFor;
    private bool _musicVisualWanted;
    private bool _musicVisualFullBleed;
    private bool _musicVisualizing;
    private VisualStage? _musicScene;
    private Task? _musicStageWork;
    private CancellationTokenSource _musicFrameCancellation = new();
    private bool _musicStageReset;
    private SpectrumAnalyzer _musicBands = new(48);
    private readonly SpectrumAnalyzer _musicSpectrogramBands = new(MusicSpectrogramRows);
    private readonly LevelMeter _musicMeterLeft = new();
    private readonly LevelMeter _musicMeterRight = new();
    private MacCameraFeed? _musicCamera;

    private void AddMusicPresentation(NSView content)
    {
        var stage = new MusicStageHost(LayOutMusicStage)
        {
            Hidden = true,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        ((NSView)stage).AccessibilityLabel = "Now playing music, cover art, audio visualisation and lyrics";
        // Just above the picture, so captions, the statistics and every page over the stage stay on top.
        content.AddSubview(stage, NSWindowOrderingMode.Above, _picture);
        stage.TopAnchor.ConstraintEqualTo(_picture!.TopAnchor).Active = true;
        stage.BottomAnchor.ConstraintEqualTo(_picture.BottomAnchor).Active = true;
        stage.LeadingAnchor.ConstraintEqualTo(_picture.LeadingAnchor).Active = true;
        stage.TrailingAnchor.ConstraintEqualTo(_picture.TrailingAnchor).Active = true;
        _musicStage = stage;

        _musicVisual = Placed(new VisualStageView { Hidden = true });
        ((NSView)_musicVisual).AccessibilityLabel = "Sound-reactive music visualisation";
        _musicBars = Placed(new MusicBarsView { Hidden = true, Baseline = MacTheme.OverlayHairline.CGColor });
        ((NSView)_musicBars).AccessibilityLabel = "Audio meter";
        _musicRecalibrate = Placed(MacTheme.TextButton("Recalibrate silhouette", () =>
        {
            _musicCamera?.Relearn();
            Say("Step completely out of the camera view while the background recalibrates.");
        }, RexButtonLook.Overlay, label: "Recalibrate the empty background after stepping out of camera view"));
        _musicRecalibrate.Hidden = true;

        _musicArtwork = Placed(new MusicCoverView { Hidden = true });
        _musicKicker = Placed(MacTheme.Kicker("Now playing", MacTheme.Accent));
        _musicTitle = Placed(MacTheme.WrappingLabel("", 5, MacTheme.OverlayText, semibold: true));
        _musicTitle.MaximumNumberOfLines = 2;
        _musicTitle.Cell.TruncatesLastVisibleLine = true;
        ((NSView)_musicTitle).AccessibilityLabel = "Song";
        _musicArtist = Placed(MacTheme.WrappingLabel("", 2, MacTheme.OverlaySecondaryText));
        _musicAlbum = Placed(MacTheme.WrappingLabel("", 2, MacTheme.OverlayTertiaryText));
        _musicMeta = Placed(MacTheme.WrappingLabel("", 1, MacTheme.OverlayTertiaryText));

        for (var row = 0; row < _musicLyricRows.Length; row++)
        {
            // Each line on its own chip, just behind the words, so it reads over a moving picture
            // without a broad shade across the stage.
            var chip = Placed(MacTheme.Panel(MacTheme.Overlay, 1));
            chip.Hidden = true;
            var text = MacTheme.WrappingLabel("", 2, MacTheme.OverlaySecondaryText);
            text.Font = NSFont.SystemFontOfSize(20)!;
            text.Alignment = NSTextAlignment.Center;
            text.TranslatesAutoresizingMaskIntoConstraints = true;
            chip.AddSubview(text);
            (_musicLyricChips[row], _musicLyricRows[row]) = (chip, text);
        }

        _musicLyricStatusPill = Placed(MacTheme.Panel(MacTheme.Overlay, 1));
        _musicLyricStatusPill.Hidden = true;
        _musicLyricStatus = MacTheme.WrappingLabel("", 2, MacTheme.OverlaySecondaryText);
        _musicLyricStatus.Alignment = NSTextAlignment.Center;
        _musicLyricStatus.TranslatesAutoresizingMaskIntoConstraints = true;
        ((NSView)_musicLyricStatus).AccessibilityLabel = "Lyrics status";
        _musicLyricStatusPill.AddSubview(_musicLyricStatus);
        Placed(CreateLyricsWindowButton());

        foreach (var label in new[] { _musicTitle, _musicArtist, _musicAlbum, _musicMeta, _musicKicker })
        {
            label.Alignment = NSTextAlignment.Center;
        }

        _musicShowLyrics = _settings.ShowLyrics;
        _musicTimer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(120), _ =>
        {
            RefreshMusicPosition();
            RefreshLyricsCompanion();
        });

        // Placed by frame in LayOutMusicStage, so each part leaves its own constraints out of it.
        T Placed<T>(T view) where T : NSView
        {
            view.TranslatesAutoresizingMaskIntoConstraints = true;
            stage.AddSubview(view);
            return view;
        }
    }

    private bool IsMusicPlaying() =>
        _player.Item is not null && _player.Info?.FirstTrack(MediaKind.Video) is null
        && _player.Info?.FirstTrack(MediaKind.Audio) is not null;

    /// <summary>The stage for what plays now: its words, cover and lyrics status, and whether a visualisation is wanted.</summary>
    private void RefreshMusicPresentation()
    {
        if (_musicStage is null || _musicTitle is null || _musicArtwork is null)
        {
            return;
        }

        var visible = IsMusicPlaying();
        _musicStage.Hidden = !visible;
        // The detached window follows every track change, including to a video or to nothing.
        RefreshLyricsCompanion();
        if (!visible)
        {
            _musicLyricLine = -2;
            _musicShownItem = null;
            ShowMusicVisualizer(false);
            return;
        }

        if (!ReferenceEquals(_musicShownItem, _player.Item))
        {
            // A new song starts its picture again from silence.
            _musicShownItem = _player.Item;
            _musicStageReset = true;
            _musicBands.Reset();
            _musicSpectrogramBands.Reset();
            _musicMeterLeft.Reset();
            _musicMeterRight.Reset();
        }

        var info = _player.Info!;
        var artist = _player.Artist;
        _musicHeadingText = _player.Title + (string.IsNullOrWhiteSpace(artist) ? "" : "\n" + artist);
        SetMusicText(_musicTitle, _player.Title);
        SetMusicText(_musicArtist!, artist ?? "");
        var album = info.Metadata.GetValueOrDefault(MetadataKeys.Album);
        SetMusicText(_musicAlbum!, album ?? "");
        var year = info.Metadata.GetValueOrDefault(MetadataKeys.Date) is { Length: >= 4 } date && date[..4].All(char.IsAsciiDigit) ? date[..4] : null;
        var duration = _player.Duration > TimeSpan.Zero ? TimeText.Format(_player.Duration, _player.Duration) : null;
        SetMusicText(_musicMeta!, string.Join(" · ", new[] { year, info.Metadata.GetValueOrDefault(MetadataKeys.Genre), duration }
            .Where(part => !string.IsNullOrWhiteSpace(part))));

        var lyrics = _musicShowLyrics ? _player.Lyrics : null;
        var view = MediaStagePolicy.Resolve(new MediaStageContext(
            HasItem: true,
            StreamsKnown: true,
            HasAudioTrack: true,
            CanSeek: _player.CanSeek,
            Kind: LibraryKind.Music,
            Shelf: MediaStagePolicy.ShelfFromTags(info.Metadata.GetValueOrDefault(MetadataKeys.MediaType), info.Metadata.GetValueOrDefault(MetadataKeys.Genre)),
            HasLyrics: lyrics is not null));
        if (_musicKicker!.Text != view.Label)
        {
            _musicKicker.Text = view.Label;
        }

        var status = LyricsStatusText.For(view, _musicShowLyrics, lyrics?.DisplayMode, problem: _player.LyricsProblem, minimal: _settings.MinimalInterface);
        SetMusicText(_musicLyricStatus!, status ?? "");
        _musicLyricStatusPill!.Hidden = status is null;

        // The controller owns the bytes. Only decode again when a different media item supplies art.
        if (!ReferenceEquals(_musicImageSource, _player.CoverArt))
        {
            _musicArtwork.Image = null;
            _musicImage?.Dispose();
            _musicImage = null;
            _musicVisualCover = null;
            _musicImageSource = _player.CoverArt;
            if (_musicImageSource is { Length: > 0 } art)
            {
                try
                {
                    using var data = NSData.FromArray(art);
                    _musicImage = new NSImage(data);
                    _musicArtwork.Image = _musicImage;
                    // The scenes that show the cover (the vinyl's label) take it at up to 400 pixels.
                    _musicVisualCover = MusicVisualPicture(_musicImage);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    _log.Warning(LogSource, "Cover artwork could not be displayed: " + ex.Message);
                }
            }
        }

        _musicArtwork.CoverDescription = _musicImage is null ? "Cover" : "Cover for " + _player.Title;
        ApplyMusicVisualizer();
        _musicLyricLine = -2;
        RefreshMusicPosition();
        _musicStage.NeedsLayout = true;
    }

    /// <summary>Sets a label's text and hides it while empty, so an empty line takes no room.</summary>
    private static void SetMusicText(RexLabel label, string text)
    {
        if (label.StringValue != text)
        {
            label.StringValue = text;
        }

        label.Hidden = text.Length == 0;
    }

    /// <summary>The lyric lines about the one being sung, and the visualisation woken when the music moves again.</summary>
    private void RefreshMusicPosition()
    {
        if (_musicStage?.Hidden != false)
        {
            return;
        }

        var lyrics = _musicShowLyrics ? _player.Lyrics : null;
        var line = lyrics?.IndexAt(_player.Position) ?? -1;
        if (line != _musicLyricLine)
        {
            _musicLyricLine = line;
            ShowMusicLyrics(lyrics, line);
        }

        // Paused, hidden or covered, the frames stop; whatever moves the music on starts them again.
        if (_musicVisualizing && _musicFrameTimer is null
            && (_player.IsPlaying || (_musicBuiltFor is { } built && MacVisualizerNeedsCamera(built))))
        {
            WakeMusicVisualizer();
        }
    }

    /// <summary>
    /// The line being sung with the lines either side of it, the sung one in the stage's own white
    /// and semibold, the others quieter; untimed lyrics show their first lines.
    /// </summary>
    private void ShowMusicLyrics(Lyrics? lyrics, int line)
    {
        var shown = new List<(string Text, bool Current)>(3);
        if (lyrics is not null)
        {
            if (line < 0)
            {
                if (!lyrics.IsTimed)
                {
                    shown.AddRange(lyrics.Lines.Take(3).Select(entry => (entry.Text, false)));
                }
            }
            else
            {
                var first = Math.Max(0, line - 1);
                shown.AddRange(lyrics.Lines.Skip(first).Take(3).Select((entry, at) => (entry.Text, first + at == line)));
            }
        }

        _musicLyricsText = string.Join("\n", shown.Select(entry => entry.Text));
        if (_musicLyricsText.Trim().Length == 0)
        {
            shown.Clear();
            _musicLyricsText = "";
        }

        for (var row = 0; row < _musicLyricRows.Length; row++)
        {
            var label = _musicLyricRows[row];
            if (row < shown.Count)
            {
                var (text, current) = shown[row];
                label.Font = NSFont.SystemFontOfSize(20, current ? NSFontWeight.Semibold : NSFontWeight.Regular)!;
                label.FixedColor = current ? MacTheme.OverlayText : MacTheme.OverlaySecondaryText;
                label.StringValue = text.Length == 0 ? " " : text;
            }

            _musicLyricChips[row].Hidden = row >= shown.Count;
        }

        _musicLyricRowsShown = shown.Count;
        _musicLyricCurrentRow = shown.FindIndex(entry => entry.Current);
        if (_musicStage is not null)
        {
            _musicStage.NeedsLayout = true;
        }
    }

    /// <summary>
    /// Arranges the stage as <see cref="StageLayout"/> decides, from what it measures now: it runs
    /// whenever the stage changes size or what it holds changes, never per frame.
    /// </summary>
    private void LayOutMusicStage(MusicStageHost host)
    {
        if (_musicTitle is null || _musicArtwork is null || _musicVisual is null || _musicBars is null)
        {
            return;
        }

        var (width, height) = ((double)host.Bounds.Width, (double)host.Bounds.Height);
        var footerHeight = LayOutMusicFooter(width, 0, measureOnly: true);
        StageLayoutView layout = default;
        // The title block's height depends on the layout's title size, so it is measured and laid out again once.
        for (var pass = 0; pass < 2; pass++)
        {
            layout = StageLayout.Resolve(new StageLayoutInput(
                width, height,
                HasCover: _musicImage is not null,
                LyricsShown: _musicLyricRowsShown > 0,
                VisualizerWanted: _musicVisualWanted,
                FullBleed: _musicVisualFullBleed,
                TitleHeight: _musicTitleHeight,
                FooterHeight: footerHeight));
            var measured = LayOutMusicTitle(layout, width, height, measureOnly: true);
            if (Math.Abs(measured - _musicTitleHeight) < 0.5)
            {
                break;
            }

            _musicTitleHeight = measured;
        }

        LayOutMusicTitle(layout, width, height, measureOnly: false);

        // The lyrics dock at the foot: the lines above the status and the lyrics window's button.
        var dockWidth = Math.Max(0, width - (2 * layout.DockSide));
        var footerTop = height - layout.DockBottom - footerHeight;
        if (layout.FooterShown)
        {
            LayOutMusicFooter(width, footerTop, measureOnly: false);
        }
        else
        {
            // The tiniest stage keeps only its title: the status and the button give way, taking no room.
            _musicLyricStatusPill!.Frame = CGRect.Empty;
            if (_musicLyricsWindowButton is { } button) button.Frame = CGRect.Empty;
        }
        var boxWidth = Math.Min(720, dockWidth);
        var boxBottom = (footerHeight > 0 && layout.FooterShown ? footerTop - 8 : height - layout.DockBottom);
        LayOutMusicLyrics((width - boxWidth) / 2, boxBottom - layout.LyricsHeight, boxWidth, layout.LyricsHeight);

        var band = layout.Band;
        var bandRect = new CGRect(band.Left, band.Top, Math.Max(0, width - band.Left - band.Right), Math.Max(0, height - band.Top - band.Bottom));
        _musicVisual.Frame = bandRect;
        _musicBars.Frame = bandRect;
        if (_musicRecalibrate is { } recalibrate)
        {
            var size = recalibrate.IntrinsicContentSize;
            recalibrate.Frame = new CGRect(bandRect.X + 24, bandRect.Y + Math.Max(12, (double)bandRect.Height - 120), size.Width, size.Height);
        }

        ShowMusicVisualizer(layout.VisualizerShown && _musicVisualWanted);
    }

    /// <summary>Places the title block (and the cover); returns its height, the cover included when it stands above the words.</summary>
    private double LayOutMusicTitle(in StageLayoutView layout, double width, double height, bool measureOnly)
    {
        var labels = new[] { _musicKicker!, _musicTitle!, _musicArtist!, _musicAlbum!, _musicMeta! };
        if (Math.Abs((double)_musicTitle!.Font!.PointSize - layout.TitleSize) > 0.1)
        {
            // Only on a change: setting a font asks for another layout pass.
            _musicTitle.Font = NSFont.SystemFontOfSize((nfloat)layout.TitleSize, NSFontWeight.Semibold)!;
        }

        var cover = layout.CoverShown ? layout.CoverSize : 0;
        var beside = layout.TextBesideCover && cover > 0;
        var room = Math.Max(0, width - (2 * layout.Inset) - (beside ? cover + layout.ColumnSpacing : 0));
        var textWidth = Math.Min(layout.TextMaxWidth, room);
        const double Spacing = 7;
        var sizes = new CGSize[labels.Length];
        double textHeight = 0, widest = 0;
        for (var i = 0; i < labels.Length; i++)
        {
            if (labels[i].Hidden)
            {
                continue;
            }

            var size = labels[i].Cell.CellSizeForBounds(new CGRect(0, 0, textWidth, 10000));
            var tall = (double)size.Height;
            if (labels[i] == _musicTitle)
            {
                // At most two lines of title, as on Windows; the rest is cut short.
                var font = _musicTitle.Font!;
                tall = Math.Min(tall, Math.Ceiling(2 * (double)(font.Ascender - font.Descender + font.Leading)) + 2);
            }

            sizes[i] = new CGSize(Math.Min(textWidth, Math.Ceiling((double)size.Width)), Math.Ceiling(tall));
            textHeight += (textHeight > 0 ? Spacing : 0) + (double)sizes[i].Height;
            widest = Math.Max(widest, (double)sizes[i].Width);
        }

        var blockHeight = beside ? Math.Max(cover, textHeight) : cover + (cover > 0 && textHeight > 0 ? layout.RowSpacing : 0) + textHeight;
        if (measureOnly)
        {
            return blockHeight;
        }

        var blockWidth = beside ? cover + layout.ColumnSpacing + widest : Math.Max(cover, widest);
        var left = Math.Max(layout.Inset, (width - blockWidth) / 2);
        var top = layout.TitleAtTop ? layout.Inset
            : Math.Max(layout.Inset, layout.Inset + ((height - layout.Inset - layout.BottomInset - blockHeight) / 2));
        _musicArtwork!.Hidden = cover <= 0;
        if (cover > 0)
        {
            _musicArtwork.Frame = beside
                ? new CGRect(left, top + ((blockHeight - cover) / 2), cover, cover)
                : new CGRect((width - cover) / 2, top, cover, cover);
        }

        var textLeft = beside ? left + cover + layout.ColumnSpacing : (width - widest) / 2;
        var y = beside ? top + ((blockHeight - textHeight) / 2) : top + cover + (cover > 0 && textHeight > 0 ? layout.RowSpacing : 0);
        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            var alignment = beside ? NSTextAlignment.Left : NSTextAlignment.Center;
            if (label.Alignment != alignment)
            {
                label.Alignment = alignment;
            }

            if (label.Hidden)
            {
                continue;
            }

            label.Frame = new CGRect(textLeft, y, widest, sizes[i].Height);
            y += (double)sizes[i].Height + Spacing;
        }

        return blockHeight;
    }

    /// <summary>Places the lyrics status pill and the lyrics window's button from <paramref name="top"/>; returns their height.</summary>
    private double LayOutMusicFooter(double width, double top, bool measureOnly)
    {
        double height = 0;
        if (_musicLyricStatusPill is { Hidden: false } pill && _musicLyricStatus is { } status)
        {
            var inner = Math.Max(0, Math.Min(560, width - 48) - 24);
            var size = status.Cell.CellSizeForBounds(new CGRect(0, 0, inner, 10000));
            var (across, down) = (Math.Ceiling((double)size.Width) + 24, Math.Ceiling((double)size.Height) + 12);
            if (!measureOnly)
            {
                pill.Frame = new CGRect((width - across) / 2, top, across, down);
                status.Frame = new CGRect(12, 6, across - 24, down - 12);
            }

            height += down;
        }

        if (_musicLyricsWindowButton is { Hidden: false } button)
        {
            height += height > 0 ? 8 : 0;
            var size = button.IntrinsicContentSize;
            if (!measureOnly)
            {
                button.Frame = new CGRect((width - (double)size.Width) / 2, top + height, size.Width, size.Height);
            }

            height += (double)size.Height;
        }

        return height;
    }

    /// <summary>The lyric lines, each on its own chip, centred in the lyrics box; rows that do not fit give way from the outside.</summary>
    private void LayOutMusicLyrics(double left, double top, double width, double height)
    {
        const double Gap = 6;
        var inner = Math.Max(0, Math.Min(640, width) - 24);
        var sizes = new CGSize[_musicLyricRows.Length];
        for (var row = 0; row < _musicLyricRowsShown; row++)
        {
            var size = _musicLyricRows[row].Cell.CellSizeForBounds(new CGRect(0, 0, inner, 10000));
            sizes[row] = new CGSize(Math.Ceiling((double)size.Width) + 24, Math.Ceiling((double)size.Height) + 14);
        }

        var (first, last) = (0, _musicLyricRowsShown - 1);
        double Total() => Enumerable.Range(first, Math.Max(0, last - first + 1)).Sum(row => (double)sizes[row].Height) + (Gap * Math.Max(0, last - first));
        while (last > first && Total() > height)
        {
            // Keep the line being sung: drop the line after it first, then the one before.
            if (last != _musicLyricCurrentRow)
            {
                last--;
            }
            else
            {
                first++;
            }
        }

        var y = top + Math.Max(0, (height - Total()) / 2);
        for (var row = 0; row < _musicLyricRows.Length; row++)
        {
            var chip = _musicLyricChips[row];
            if (row >= _musicLyricRowsShown)
            {
                chip.Hidden = true;
                continue;
            }

            chip.Hidden = row < first || row > last;
            if (chip.Hidden)
            {
                continue;
            }

            var size = sizes[row];
            chip.Frame = new CGRect(left + ((width - (double)size.Width) / 2), y, size.Width, size.Height);
            _musicLyricRows[row].Frame = new CGRect(12, 7, (double)size.Width - 24, (double)size.Height - 14);
            y += (double)size.Height + Gap;
        }
    }

    private void ToggleMusicLyrics()
    {
        _musicShowLyrics = !_musicShowLyrics;
        _settings = _settings with { ShowLyrics = _musicShowLyrics };
        SaveMusicSettings();
        _musicLyricLine = -2;
        RefreshMusicPresentation();
        RefreshLyricsCompanion();
    }

    /// <summary>
    /// The music stage for scripted checks: "visualisers" reports the visualisation panel, the
    /// "companion" ids drive the lyrics window, and anything else describes the stage itself.
    /// <c>meter</c> is how many bars the stage draws now: the spectrum's, or the two level meters.
    /// </summary>
    private AutomationReply MusicSmokeAction(string? id)
    {
        if (id == "visualisers")
        {
            return MacVisualizerSmokeAction();
        }

        if (id is "companion" or "companion-open" or "companion-close")
        {
            return LyricsCompanionSmokeAction(id);
        }

        RefreshMusicPresentation();
        RefreshMusicPosition();
        return new AutomationReply
        {
            Ok = true,
            Text = $"visible={_musicStage?.Hidden == false};art={_musicImage is not null};heading={_musicHeadingText};lyrics={_musicLyricsText.Length > 0};visualizer={_settings.Visualizer};meter={_musicBars?.BarsShown ?? 0};text={_musicLyricsText};companion={LyricsCompanionOpen}",
        };
    }

    private void SaveMusicSettings()
    {
        _player.Settings = _settings;
        try
        {
            SettingsStore.Save(_roots.SettingsPath, _settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "Music preferences could not be saved: " + ex.Message);
        }
    }

    private void DisposeMusicPresentation()
    {
        _musicTimer?.Invalidate();
        _musicTimer = null;
        _musicVisualizing = false;
        StopMusicFrames();
        _musicFrameCancellation.Cancel();
        var cancellation = _musicFrameCancellation;
        if (_musicStageWork is { IsCompleted: false } work)
        {
            // The frame being drawn stops at its next check; its canvas is let go with it.
            _ = work.ContinueWith(_ => cancellation.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        else
        {
            cancellation.Dispose();
        }

        _musicStageWork = null;
        DisposeLyricsCompanion();
        if (_musicArtwork is not null) _musicArtwork.Image = null;
        _musicVisual?.Clear();
        _musicBars?.Clear();
        _musicScene = null;
        _musicVisualCover = null;
        StopMacVisualizerCamera();
        _musicImage?.Dispose();
        _musicImage = null;
    }
}
