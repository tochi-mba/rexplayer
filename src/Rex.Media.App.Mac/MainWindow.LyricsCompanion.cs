using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Player;
using Rex.Media.Primitives;

namespace Rex.Media.App.Mac;

/// <summary>
/// An independently resizable lyrics window (META-09): the song's lyrics large, the line being
/// sung picked out and kept in view as playback moves. It only reads the player and asks it to
/// seek when a timed line is clicked; closing it never stops, disposes or seeks playback.
/// </summary>
public sealed partial class MainWindow
{
    private NSWindow? _lyricsCompanion;
    private NSScrollView? _lyricsCompanionScroll;
    private NSStackView? _lyricsCompanionLines;
    private NSView? _lyricsCompanionDocument;
    private NSTextField? _lyricsCompanionHeading;
    private NSTextField? _lyricsCompanionEvidence;
    private NSButton? _lyricsCompanionFollow;
    private NSButton? _musicLyricsWindowButton;
    private NSObject? _lyricsCompanionScrollWatch;
    private Lyrics? _lyricsCompanionSource;
    private bool _lyricsCompanionBuilt;
    private bool _lyricsCompanionWasLoading;
    private bool _lyricsCompanionWasHidden;
    private readonly List<NSTextField> _lyricsCompanionTexts = [];
    private int _lyricsCompanionLine = -1;
    private int _lyricsCompanionWord = -2;
    private bool _lyricsCompanionFollows = true;

    private bool LyricsCompanionOpen => _lyricsCompanion?.IsVisible == true;

    /// <summary>The stage button that opens the window, shown only while the song has lyrics.</summary>
    private NSButton CreateLyricsWindowButton()
    {
        _musicLyricsWindowButton = NSButton.CreateButton("Open lyrics window", OpenLyricsCompanion);
        _musicLyricsWindowButton.Hidden = true;
        ((NSView)_musicLyricsWindowButton).AccessibilityLabel = "Open the lyrics in their own window";
        return _musicLyricsWindowButton;
    }

    private void OpenLyricsCompanion()
    {
        if (_lyricsCompanion is null)
        {
            BuildLyricsCompanion();
        }
        else if (!_lyricsCompanion.IsVisible)
        {
            // A reopened window starts fresh but keeps the size and place it was left at.
            _lyricsCompanionBuilt = false;
            _lyricsCompanionFollows = true;
        }

        _lyricsCompanion!.MakeKeyAndOrderFront(null);
        RefreshLyricsCompanion();
    }

    private void BuildLyricsCompanion()
    {
        var window = new NSWindow(new CGRect(0, 0, 460, 620),
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable,
            NSBackingStore.Buffered, false)
        {
            Title = "Lyrics - rexplayer",
            MinSize = new CGSize(320, 240),
        };
        window.ReleaseWhenClosed(false);

        var heading = NSTextField.CreateWrappingLabel("");
        heading.Font = NSFont.SystemFontOfSize(18, NSFontWeight.Semibold)!;
        ((NSView)heading).AccessibilityLabel = "Song";
        var evidence = NSTextField.CreateLabel("");
        evidence.Font = NSFont.SystemFontOfSize(13)!;
        evidence.TextColor = NSColor.SecondaryLabel;
        var follow = NSButton.CreateButton("Follow current line", () =>
        {
            _lyricsCompanionFollows = true;
            if (_lyricsCompanionFollow is { } button) button.Hidden = true;
            BringCompanionLyricIntoView();
        });
        follow.Hidden = true;
        ((NSView)follow).AccessibilityLabel = "Resume following the current lyric line";

        var header = NSStackView.FromViews([heading, follow]);
        header.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        header.Spacing = 8;
        heading.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);

        var lines = NSStackView.FromViews([]);
        lines.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        lines.Alignment = NSLayoutAttribute.Leading;
        lines.Spacing = 10;
        lines.EdgeInsets = new NSEdgeInsets(8, 4, 8, 4);
        lines.TranslatesAutoresizingMaskIntoConstraints = false;

        // A flipped document keeps the first line at the top, as reading order expects.
        var document = new LyricsCompanionDocument { TranslatesAutoresizingMaskIntoConstraints = false };
        document.AddSubview(lines);
        var scroll = new NSScrollView(new CGRect(0, 0, 428, 520))
        {
            HasVerticalScroller = true,
            DrawsBackground = false,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        scroll.AccessibilityLabel = "Detached lyrics";
        scroll.DocumentView = document;
        document.TopAnchor.ConstraintEqualTo(scroll.ContentView.TopAnchor).Active = true;
        document.LeadingAnchor.ConstraintEqualTo(scroll.ContentView.LeadingAnchor).Active = true;
        document.WidthAnchor.ConstraintEqualTo(scroll.ContentView.WidthAnchor).Active = true;
        lines.TopAnchor.ConstraintEqualTo(document.TopAnchor).Active = true;
        lines.LeadingAnchor.ConstraintEqualTo(document.LeadingAnchor).Active = true;
        lines.TrailingAnchor.ConstraintEqualTo(document.TrailingAnchor).Active = true;
        lines.BottomAnchor.ConstraintEqualTo(document.BottomAnchor).Active = true;

        var root = NSStackView.FromViews([header, evidence, scroll]);
        root.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        root.Alignment = NSLayoutAttribute.Leading;
        root.Spacing = 10;
        root.EdgeInsets = new NSEdgeInsets(16, 16, 16, 16);
        root.TranslatesAutoresizingMaskIntoConstraints = false;
        var content = window.ContentView!;
        content.AddSubview(root);
        root.TopAnchor.ConstraintEqualTo(content.TopAnchor).Active = true;
        root.BottomAnchor.ConstraintEqualTo(content.BottomAnchor).Active = true;
        root.LeadingAnchor.ConstraintEqualTo(content.LeadingAnchor).Active = true;
        root.TrailingAnchor.ConstraintEqualTo(content.TrailingAnchor).Active = true;
        header.WidthAnchor.ConstraintEqualTo(root.WidthAnchor, 1, -32).Active = true;
        scroll.WidthAnchor.ConstraintEqualTo(root.WidthAnchor, 1, -32).Active = true;

        // Only a person scrolling starts a live scroll; the window's own following never does,
        // so reading ahead pauses the following until they ask for it back.
        _lyricsCompanionScrollWatch = NSNotificationCenter.DefaultCenter.AddObserver(
            NSScrollView.WillStartLiveScrollNotification, _ => PauseCompanionFollow(), scroll);

        _lyricsCompanion = window;
        _lyricsCompanionScroll = scroll;
        _lyricsCompanionLines = lines;
        _lyricsCompanionDocument = document;
        _lyricsCompanionHeading = heading;
        _lyricsCompanionEvidence = evidence;
        _lyricsCompanionFollow = follow;
        _lyricsCompanionBuilt = false;
        _lyricsCompanionFollows = true;
        window.Center();
    }

    private void CloseLyricsCompanion()
    {
        _lyricsCompanion?.Close();
        if (_musicLyricsWindowButton is { } button) button.Title = "Open lyrics window";
    }

    private void PauseCompanionFollow()
    {
        if (_lyricsCompanionSource is not { IsTimed: true })
        {
            return;
        }

        _lyricsCompanionFollows = false;
        if (_lyricsCompanionFollow is { } follow) follow.Hidden = false;
    }

    /// <summary>Scroll the line being sung to the middle of the window, without animation.</summary>
    private void BringCompanionLyricIntoView()
    {
        var index = _lyricsCompanionLine;
        if (_lyricsCompanionScroll is not { } scroll || _lyricsCompanionDocument is not { } document
            || index < 0 || index >= _lyricsCompanionTexts.Count)
        {
            return;
        }

        document.LayoutSubtreeIfNeeded();
        var row = _lyricsCompanionTexts[index];
        var frame = row.ConvertRectToView(row.Bounds, document);
        var visible = (double)scroll.ContentView.Bounds.Height;
        var furthest = Math.Max(0, (double)document.Frame.Height - visible);
        var top = Math.Clamp((double)frame.Y + (double)frame.Height / 2 - visible / 2, 0, furthest);
        scroll.ContentView.ScrollToPoint(new CGPoint(0, top));
        scroll.ReflectScrolledClipView(scroll.ContentView);
    }

    /// <summary>
    /// Called from the music stage's refreshes (track changes and the position timer) on the
    /// window thread. Rebuilds only when the lyrics themselves change; otherwise it only moves
    /// the highlight.
    /// </summary>
    private void RefreshLyricsCompanion()
    {
        var lyrics = _musicShowLyrics && _player.Item is not null && _player.Info?.FirstTrack(MediaKind.Video) is null
            ? _player.Lyrics : null;
        if (_musicLyricsWindowButton is { } stageButton)
        {
            stageButton.Hidden = lyrics is null;
            var title = LyricsCompanionOpen ? "Focus lyrics window" : "Open lyrics window";
            if (stageButton.Title != title) stageButton.Title = title;
        }

        if (!LyricsCompanionOpen || _lyricsCompanion is not { } window || _lyricsCompanionLines is not { } content
            || _lyricsCompanionHeading is not { } heading || _lyricsCompanionEvidence is not { } evidence)
        {
            return;
        }

        var trackName = _player.Item?.Title ?? "No track";
        window.Title = trackName + " - Lyrics - rexplayer";
        if (heading.StringValue != trackName) heading.StringValue = trackName;
        var waitingForMetadata = _player.Item is not null && _player.Info is null;
        var hasVideo = _player.Info?.FirstTrack(MediaKind.Video) is not null;
        if (!_lyricsCompanionBuilt || !ReferenceEquals(_lyricsCompanionSource, lyrics)
            || (lyrics is null && (_lyricsCompanionWasLoading != waitingForMetadata || _lyricsCompanionWasHidden != !_musicShowLyrics)))
        {
            _lyricsCompanionBuilt = true;
            _lyricsCompanionWasLoading = waitingForMetadata;
            _lyricsCompanionWasHidden = !_musicShowLyrics;
            _lyricsCompanionSource = lyrics;
            _lyricsCompanionLine = -1;
            _lyricsCompanionWord = -2;
            _lyricsCompanionFollows = true;
            if (_lyricsCompanionFollow is { } follow) follow.Hidden = true;
            _lyricsCompanionTexts.Clear();
            foreach (var old in content.ArrangedSubviews)
            {
                content.RemoveArrangedSubview(old);
                old.RemoveFromSuperview();
            }

            if (lyrics is null || lyrics.Lines.Count == 0)
            {
                evidence.StringValue = "";
                var message = NSTextField.CreateWrappingLabel(
                    !_musicShowLyrics ? "Lyrics are hidden in settings."
                    : hasVideo ? "This track does not show music lyrics."
                    : waitingForMetadata ? "Loading local lyrics…"
                    : _player.LyricsProblem ?? "No lyrics are available for this track.");
                message.Font = NSFont.SystemFontOfSize(16)!;
                content.AddArrangedSubview(message);
                message.WidthAnchor.ConstraintEqualTo(content.WidthAnchor, 1, -8).Active = true;
            }
            else
            {
                // Say only what the source itself marks; no timing is ever inferred.
                evidence.StringValue = lyrics.DisplayMode switch
                {
                    LyricsDisplayMode.Plain => "Untimed source: no timestamps",
                    LyricsDisplayMode.LineTimed => "Source-timed lines",
                    LyricsDisplayMode.WordTimed => "Source-timed words",
                    LyricsDisplayMode.MixedTimed => "Some words have source timings",
                    _ => "Lyrics",
                };
                foreach (var line in lyrics.Lines)
                {
                    var text = NSTextField.CreateWrappingLabel(line.Text.Length == 0 ? " " : line.Text);
                    text.Font = NSFont.SystemFontOfSize(20)!;
                    text.TextColor = NSColor.SecondaryLabel;
                    if (line.At is { } at)
                    {
                        var described = $"Go to {TimeText.Format(at, _player.Duration)}: {line.Text}";
                        ((NSView)text).AccessibilityLabel = described;
                        text.ToolTip = described;
                        text.AddGestureRecognizer(new NSClickGestureRecognizer(() =>
                        {
                            // Lyrics can load from a sidecar before the session can seek.
                            if (!_player.CanSeek) return;
                            _player.Seek(at);
                            _lyricsCompanionFollows = true;
                            if (_lyricsCompanionFollow is { } button) button.Hidden = true;
                        }));
                    }

                    _lyricsCompanionTexts.Add(text);
                    content.AddArrangedSubview(text);
                    // Constrained once it shares the stack, so long lines wrap to the window's width.
                    text.WidthAnchor.ConstraintEqualTo(content.WidthAnchor, 1, -8).Active = true;
                }
            }

            if (_lyricsCompanionScroll is { } scroll)
            {
                scroll.ContentView.ScrollToPoint(new CGPoint(0, 0));
                scroll.ReflectScrolledClipView(scroll.ContentView);
            }
        }

        var current = lyrics?.IndexAt(_player.Position) ?? -1;
        if (current != _lyricsCompanionLine)
        {
            if (_lyricsCompanionLine >= 0 && _lyricsCompanionLine < _lyricsCompanionTexts.Count && lyrics is not null)
            {
                var prior = _lyricsCompanionTexts[_lyricsCompanionLine];
                prior.Font = NSFont.SystemFontOfSize(20)!;
                prior.TextColor = NSColor.SecondaryLabel;
                prior.StringValue = LineText(lyrics.Lines[_lyricsCompanionLine]);
            }

            _lyricsCompanionLine = current;
            _lyricsCompanionWord = -2;
            if (current >= 0 && current < _lyricsCompanionTexts.Count)
            {
                var row = _lyricsCompanionTexts[current];
                row.Font = NSFont.SystemFontOfSize(20, NSFontWeight.Semibold)!;
                row.TextColor = NSColor.ControlAccent;
                if (_lyricsCompanionFollows) BringCompanionLyricIntoView();
            }
        }

        // Word-timed lines colour each word as it is reached, from the source's own word times.
        if (lyrics is not null && current >= 0 && current < _lyricsCompanionTexts.Count
            && lyrics.Lines[current] is { Words.Count: > 0 } sung)
        {
            var word = Lyrics.WordIndexAt(sung, _player.Position);
            if (word != _lyricsCompanionWord)
            {
                _lyricsCompanionWord = word;
                var font = NSFont.SystemFontOfSize(20, NSFontWeight.Semibold)!;
                var reached = new NSStringAttributes { Font = font, ForegroundColor = NSColor.ControlAccent };
                var ahead = new NSStringAttributes { Font = font, ForegroundColor = NSColor.SecondaryLabel };
                var coloured = new NSMutableAttributedString();
                for (var i = 0; i < sung.Words.Count; i++)
                {
                    coloured.Append(new NSAttributedString(sung.Words[i].Text, i <= word ? reached : ahead));
                }

                _lyricsCompanionTexts[current].AttributedStringValue = coloured;
            }
        }

        static string LineText(LyricLine line) => line.Text.Length == 0 ? " " : line.Text;
    }

    /// <summary>
    /// Scripted checks of the window: "companion" reports it, "companion-open" presses the stage's
    /// own button, "companion-close" closes it the way its close button does.
    /// </summary>
    private AutomationReply LyricsCompanionSmokeAction(string? id)
    {
        RefreshMusicPresentation();
        switch (id)
        {
            case "companion":
                break;
            case "companion-open":
                if (_musicLyricsWindowButton is not { Hidden: false } button)
                {
                    return AutomationReply.Failed("The stage offers no lyrics window: this song has no lyrics shown.");
                }

                button.PerformClick(button);
                break;
            case "companion-close":
                CloseLyricsCompanion();
                break;
            default:
                return AutomationReply.Failed("Unknown lyrics window action: " + id);
        }

        RefreshLyricsCompanion();
        var line = _lyricsCompanionLine;
        var current = line >= 0 && line < _lyricsCompanionTexts.Count ? _lyricsCompanionTexts[line].StringValue : "";
        return new AutomationReply
        {
            Ok = true,
            Text = $"companion={LyricsCompanionOpen};line={line};rows={_lyricsCompanionTexts.Count};" +
                $"mode={_lyricsCompanionSource?.DisplayMode.ToString() ?? "None"};follow={_lyricsCompanionFollows};current={current}",
            Position = _player.Position.TotalSeconds,
        };
    }

    private void DisposeLyricsCompanion()
    {
        if (_lyricsCompanionScrollWatch is { } watch)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(watch);
            _lyricsCompanionScrollWatch = null;
        }

        _lyricsCompanion?.Close();
        _lyricsCompanion?.Dispose();
        _lyricsCompanion = null;
        _lyricsCompanionScroll = null;
        _lyricsCompanionLines = null;
        _lyricsCompanionDocument = null;
        _lyricsCompanionHeading = null;
        _lyricsCompanionEvidence = null;
        _lyricsCompanionFollow = null;
        _lyricsCompanionSource = null;
        _lyricsCompanionTexts.Clear();
    }

    private sealed class LyricsCompanionDocument : NSView
    {
        public override bool IsFlipped => true;
    }
}
