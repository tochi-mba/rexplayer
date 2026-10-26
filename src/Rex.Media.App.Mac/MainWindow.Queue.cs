using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App.Mac;

/// <summary>The native Up Next window, backed directly by the shared playlist and transport.</summary>
public sealed partial class MainWindow
{
    private NSWindow? _queueWindow;
    private NSScrollView? _queueScroll;
    private NSTextField? _queueSummary;
    private bool _closingQueue;
    private int _queuePage;
    private NSTextField? _queueHeader;
    private NSStackView? _queueToolbar;
    private NSStackView? _queueLayout;
    private NSButton? _queueShuffle;
    private NSButton? _queueRepeat;
    private NSButton? _queueRelink;
    private NSButton? _previousQueuePage;
    private NSButton? _nextQueuePage;
    private const int QueuePageSize = 75;

    private void InitializeQueue() =>
        _player.Playlist.Changed += (_, _) => OnWindowThread(() =>
        {
            RefreshQueue();
            ShowState();
        });

    private void ShowQueue()
    {
        if (_queueWindow is null)
        {
            _queuePage = Math.Max(0, _player.Playlist.CurrentIndex) / QueuePageSize;
            _queueWindow = new NSWindow(new CGRect(0, 0, 660, 560),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "Up Next - rexplayer",
                MinSize = new CGSize(520, 400),
            };
            _queueWindow.ReleaseWhenClosed(false);
            var header = NSTextField.CreateLabel("Up Next");
            header.Font = NSFont.SystemFontOfSize(26, NSFontWeight.Semibold)!;
            _queueHeader = header;
            var hint = NSTextField.CreateLabel("Play, remove, or move a track without leaving your media.");
            hint.TextColor = NSColor.SecondaryLabel;
            _queueShuffle = NSButton.CreateButton("Shuffle: Off", () => Run(Rex.Media.AppCore.Commands.CommandCatalog.ToggleShuffle));
            _queueRepeat = NSButton.CreateButton("Repeat: Off", () => Run(Rex.Media.AppCore.Commands.CommandCatalog.CycleRepeat));
            var relink = NSButton.CreateButton("Find missing in folder…", RelinkMissingQueueFiles);
            _queueRelink = relink;
            ((NSView)relink).AccessibilityLabel = "Find every missing queue file in a folder";
            var open = NSButton.CreateButton("Add files…", () =>
            {
                var panel = NSOpenPanel.OpenPanel;
                panel.AllowsMultipleSelection = true;
                panel.CanChooseFiles = true;
                panel.CanChooseDirectories = false;
                panel.Begin(response =>
                {
                    if (response == 1)
                    {
                        _player.Open(panel.Urls.Select(url => url.Path!).ToList(), enqueue: true);
                    }
                });
            });
            _previousQueuePage = NSButton.CreateButton("Previous page", () =>
            {
                _queuePage = Math.Max(0, _queuePage - 1);
                RefreshQueue();
            });
            _nextQueuePage = NSButton.CreateButton("Next page", () =>
            {
                _queuePage++;
                RefreshQueue();
            });
            var tools = NSStackView.FromViews([_queueShuffle, _queueRepeat, open, relink]);
            tools.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            tools.Spacing = 8;
            _queueToolbar = tools;
            var pages = NSStackView.FromViews([_previousQueuePage, _nextQueuePage]);
            pages.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            pages.Spacing = 8;
            _queueSummary = NSTextField.CreateLabel("");
            _queueSummary.TextColor = NSColor.SecondaryLabel;
            _queueSummary.LineBreakMode = NSLineBreakMode.TruncatingTail;
            _queueScroll = new NSScrollView(new CGRect(0, 0, 600, 380))
            {
                HasVerticalScroller = true,
            };
            _queueScroll.HeightAnchor.ConstraintGreaterThanOrEqualTo(170).Active = true;
            var stack = NSStackView.FromViews([header, hint, tools, pages, _queueSummary, _queueScroll]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            _queueLayout = stack;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 8;
            stack.EdgeInsets = new NSEdgeInsets(16, 24, 16, 24);
            stack.TranslatesAutoresizingMaskIntoConstraints = false;
            // Give the stack a fully constrained content view: assigning an unanchored
            // translates-mask=false stack directly as ContentView leaves its arranged
            // header and toolbar outside the visible bounds on some macOS releases.
            var container = new NSView(new CGRect(0, 0, 660, 560));
            container.AddSubview(stack);
            stack.TopAnchor.ConstraintEqualTo(container.TopAnchor).Active = true;
            stack.BottomAnchor.ConstraintEqualTo(container.BottomAnchor).Active = true;
            stack.LeadingAnchor.ConstraintEqualTo(container.LeadingAnchor).Active = true;
            stack.TrailingAnchor.ConstraintEqualTo(container.TrailingAnchor).Active = true;
            _queueWindow.ContentView = container;
            _queueScroll.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _queueWindow.Center();
        }

        RefreshQueue();
        _queueWindow.MakeKeyAndOrderFront(null);
    }

    private void RefreshQueue()
    {
        if (_closingQueue || _queueWindow is null || _queueScroll is null || _queueSummary is null)
        {
            return;
        }

        var queue = _player.Playlist.Items;
        // A removed last-page entry must never leave Up Next on an empty, stale page.
        var page = LibraryPageSlice.For(queue.Count, _queuePage, QueuePageSize);
        _queuePage = page.Page;
        var (start, end) = (page.Start, page.End);
        if (_previousQueuePage is not null)
        {
            _previousQueuePage.Enabled = page.HasPrevious;
        }
        if (_nextQueuePage is not null)
        {
            _nextQueuePage.Enabled = page.HasNext;
        }
        if (_queueShuffle is not null)
        {
            _queueShuffle.Title = _player.Playlist.Shuffle ? "Shuffle: On" : "Shuffle: Off";
        }
        if (_queueRepeat is not null)
        {
            _queueRepeat.Title = "Repeat: " + _player.Playlist.Repeat;
        }

        var rows = new List<NSView>();
        if (queue.Count == 0)
        {
            var empty = NSTextField.CreateLabel("Your queue is empty. Open media or add files to get started.");
            empty.TextColor = NSColor.SecondaryLabel;
            rows.Add(empty);
        }

        for (var i = start; i < end; i++)
        {
            var index = i;
            var item = queue[i];
            var playing = i == _player.Playlist.CurrentIndex;
            var missing = _player.IsMissing(item);
            var play = NSButton.CreateButton((playing ? "Now playing: " : "Play: ") + item.Title, () =>
            {
                var at = _player.Playlist.IndexOf(item);
                if (at >= 0)
                {
                    _player.PlayAt(at);
                    RefreshQueue();
                }
            });
            play.Alignment = NSTextAlignment.Left;
            play.Enabled = !missing;
            play.ToolTip = missing ? "Missing file: " + item.Location : item.Location;
            ((NSView)play).AccessibilityLabel = playing ? "Now playing: " + item.Title : "Play: " + item.Title;
            play.WidthAnchor.ConstraintGreaterThanOrEqualTo(300).Active = true;
            var source = NSTextField.CreateLabel(missing
                ? "Missing file - choose Locate to point at its new location."
                : item.Artist ?? Rex.Media.AppCore.Player.PlaylistItem.SourceOf(item.Location) ?? "Local media");
            source.TextColor = NSColor.SecondaryLabel;
            source.Font = NSFont.SystemFontOfSize(11, NSFontWeight.Regular)!;
            source.LineBreakMode = NSLineBreakMode.TruncatingTail;
            var media = NSStackView.FromViews([play, source]);
            media.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            media.Alignment = NSLayoutAttribute.Leading;
            media.Spacing = 1;
            var next = NSButton.CreateButton("Play next", () =>
            {
                var at = _player.Playlist.IndexOf(item);
                if (at >= 0 && _player.Playlist.Current is not null)
                {
                    _player.Playlist.QueueNext(at);
                }
            });
            next.Enabled = _player.Playlist.Current is not null && !playing && !missing;
            next.ToolTip = "Make this entry play next, including when shuffle is enabled";
            ((NSView)next).AccessibilityLabel = "Play " + item.Title + " next";
            var up = NSButton.CreateButton("Earlier", () =>
            {
                var at = _player.Playlist.IndexOf(item);
                if (at > 0)
                {
                    _player.Playlist.Move(at, at - 1);
                }
            });
            var down = NSButton.CreateButton("Later", () =>
            {
                var at = _player.Playlist.IndexOf(item);
                if (at >= 0 && at < _player.Playlist.Items.Count - 1)
                {
                    _player.Playlist.Move(at, at + 1);
                }
            });
            var remove = NSButton.CreateButton("Remove", () =>
            {
                var at = _player.Playlist.IndexOf(item);
                if (at >= 0)
                {
                    _player.Playlist.RemoveAt(at);
                }
            });
            up.Enabled = index > 0;
            down.Enabled = index < queue.Count - 1;
            up.ToolTip = "Move earlier in the queue";
            ((NSView)up).AccessibilityLabel = "Move " + item.Title + " earlier";
            down.ToolTip = "Move later in the queue";
            ((NSView)down).AccessibilityLabel = "Move " + item.Title + " later";
            remove.ToolTip = "Remove from Up Next";
            ((NSView)remove).AccessibilityLabel = "Remove " + item.Title + " from Up Next";
            var timing = NSButton.CreateButton("Timing…", () => EditQueueTiming(item));
            timing.ToolTip = "Set start, stop or maximum run time for this entry";
            var actions = NSStackView.FromViews([next, up, down, timing, remove]);
            if (missing)
            {
                var locate = NSButton.CreateButton("Locate…", () => LocateMissingQueueItem(item));
                locate.ToolTip = "Point this playlist entry at its new file path";
                actions.AddArrangedSubview(locate);
            }

            actions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            actions.Spacing = 8;
            var row = NSStackView.FromViews([media, actions]);
            row.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            row.Alignment = NSLayoutAttribute.Leading;
            row.Spacing = 5;
            rows.Add(row);
        }

        var content = NSStackView.FromViews([.. rows]);
        content.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        content.Alignment = NSLayoutAttribute.Leading;
        content.Spacing = 8;
        content.EdgeInsets = new NSEdgeInsets(12, 12, 14, 12);
        content.Frame = new CGRect(0, 0, 580, Math.Max(200, (end - start) * 90 + 35));
        _queueScroll.DocumentView = content;
        // AppKit document views use bottom-left coordinates by default. Place the first
        // entry at the visible top when opening a page, not the last visible row.
        var clip = _queueScroll.ContentView;
        clip.ScrollToPoint(new CGPoint(0, Math.Max(0, content.Frame.Height - clip.Bounds.Height)));
        _queueScroll.ReflectScrolledClipView(clip);
        _queueSummary.StringValue = (queue.Count == 1 ? "1 item" : $"{queue.Count} items")
            + (queue.Count > QueuePageSize ? $" • viewing {start + 1}–{end}" : "")
            + (_player.Playlist.PeekNext(automatic: false) is { } nextItem ? $" • Next: {nextItem.Title}" : "");
    }

    private void LocateMissingQueueItem(PlaylistItem item)
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.Title = "Locate " + item.Title;
        panel.CanChooseFiles = true;
        panel.CanChooseDirectories = false;
        panel.AllowsMultipleSelection = false;
        panel.Begin(response =>
        {
            if (response != 1 || panel.Url?.Path is not { } path) return;
            var index = _player.Playlist.IndexOf(item);
            if (index >= 0)
            {
                _player.Relocate(index, path);
                RefreshQueue();
            }
        });
    }

    private void RelinkMissingQueueFiles()
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.Title = "Choose a folder containing moved media";
        panel.CanChooseDirectories = true;
        panel.CanChooseFiles = false;
        panel.AllowsMultipleSelection = false;
        panel.Begin(response =>
        {
            if (response != 1 || panel.Url?.Path is not { } folder) return;
            // The controller intentionally relinks only unique file-name matches.
            _player.RelinkMissing(folder);
            RefreshQueue();
        });
    }

    /// <summary>
    /// CI-only AppKit smoke actions. These exercise the same NSButton callbacks a user presses and
    /// capture the rendered native window without relying on screen-recording permissions.
    /// </summary>
    private bool QueueSmokeAction(string? action)
    {
        if (Environment.GetEnvironmentVariable("REXPLAYER_MAC_QUEUE_TESTS") != "1")
        {
            return false;
        }

        if (action == "show")
        {
            ShowQueue();
            return true;
        }

        if (_queueWindow is null)
        {
            return false;
        }

        if (action == "minimum")
        {
            var frame = _queueWindow.Frame;
            var minimum = _queueWindow.MinSize;
            _queueWindow.SetFrame(new CGRect(frame.X, frame.Y, minimum.Width, minimum.Height), true);
            _queueWindow.ContentView?.LayoutSubtreeIfNeeded();
            return true;
        }

        if (action == "verify-accessibility")
        {
            // The actual native controls must provide disambiguated VoiceOver names, not
            // merely repeat identical labels for every queue row.
            var next = FindQueueSmokeButton("Play next");
            return next is not null
                && next.AccessibilityLabel?.StartsWith("Play ", StringComparison.Ordinal) == true
                && next.AccessibilityLabel.EndsWith(" next", StringComparison.Ordinal)
                && _queueShuffle is not null && _queueRepeat is not null;
        }

        if (action == "verify-missing")
        {
            var missing = new PlaylistItem(Path.Combine(Path.GetTempPath(),
                "rexplayer-missing-" + Guid.NewGuid().ToString("N") + ".wav"))
            {
                Title = "Moved recording",
            };
            _player.Playlist.Add([missing]);
            RefreshQueue();
            var play = QueueSmokeButtons().FirstOrDefault(button => button.Title == "Play: Moved recording");
            var locate = QueueSmokeButtons().FirstOrDefault(button => button.Title == "Locate…");
            var proved = play is { Enabled: false }
                && play.ToolTip?.StartsWith("Missing file: ", StringComparison.Ordinal) == true
                && locate is { Enabled: true }
                && _queueRelink is { Enabled: true }
                && _queueRelink.AccessibilityLabel == "Find every missing queue file in a folder";
            var index = _player.Playlist.IndexOf(missing);
            if (index >= 0) _player.Playlist.RemoveAt(index);
            RefreshQueue();
            return proved;
        }

        if (action == "fill-long")
        {
            _player.Playlist.Add(Enumerable.Range(1, 90).Select(index =>
                new PlaylistItem($"/queue-smoke/track-{index:000}.mp3")
                {
                    Title = $"Queue sample {index:000}",
                }));
            if (_player.Playlist.Current is null)
            {
                _player.Playlist.JumpTo(0);
            }

            RefreshQueue();
            return true;
        }

        if (action == "remove-last-page")
        {
            // Use the same row button as a real user, not Playlist.Clear().
            // The last page has 16 entries after fill-long; removing them
            // must return to page one rather than strand an empty scroll view.
            var pageBefore = _queuePage;
            if (pageBefore == 0)
            {
                return false;
            }

            for (var attempt = 0; attempt < QueuePageSize; attempt++)
            {
                if (_queuePage != pageBefore)
                {
                    break;
                }

                var remove = FindQueueSmokeButton("Remove", last: true);
                if (remove is not { Enabled: true })
                {
                    return false;
                }

                remove.PerformClick(remove);
                RefreshQueue();
            }

            return _queuePage == 0 && !_nextQueuePage!.Enabled;
        }

        if (action == "next-page" || action == "previous-page"
            || action == "shuffle" || action == "repeat" || action == "last-play-next")
        {
            // Use the actual controls. Direct references keep pagination tests fast when
            // the document view contains the maximum 75 visible entries.
            var button = action switch
            {
                "next-page" => _nextQueuePage,
                "previous-page" => _previousQueuePage,
                "shuffle" => _queueShuffle,
                "repeat" => _queueRepeat,
                _ => FindQueueSmokeButton("Play next", last: true),
            };
            if (button is not { Enabled: true })
            {
                return false;
            }

            button.PerformClick(button);
            if (action is "shuffle" or "repeat" or "last-play-next")
            {
                // Playlist.Changed schedules a refresh on the UI thread; update the result
                // synchronously so the automation reply reports the new mode / next item.
                RefreshQueue();
            }

            return true;
        }

        if (action is "capture-empty" or "capture-current" or "capture-long"
            or "capture-page-2" or "capture-shuffled" or "capture-minimum"
            or "capture-shrunk")
        {
            var folder = Environment.GetEnvironmentVariable("REXPLAYER_MAC_QUEUE_SCREENSHOTS");
            if (string.IsNullOrWhiteSpace(folder) || _queueWindow.ContentView is not { } view)
            {
                return false;
            }

            Directory.CreateDirectory(folder);
            view.LayoutSubtreeIfNeeded();
            File.WriteAllText(Path.Combine(folder, "layout.txt"), QueueSmokeLayoutReport());
            using var bitmap = view.BitmapImageRepForCachingDisplayInRect(view.Bounds);
            if (bitmap is null)
            {
                return false;
            }

            view.CacheDisplay(view.Bounds, bitmap);
            using var data = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png);
            return data?.Save(NSUrl.FromFilename(Path.Combine(folder, action["capture-".Length..] + ".png")), true) == true;
        }

        return false;
    }

    /// <summary>Frame measurements saved alongside CI screenshots for clipped layout diagnosis.</summary>
    private string QueueSmokeLayoutReport()
    {
        static string Frame(NSView? view)
        {
            if (view is null)
            {
                return "missing";
            }

            var frame = view.Frame;
            return $"x={frame.X:0.0} y={frame.Y:0.0} w={frame.Width:0.0} h={frame.Height:0.0}";
        }

        var container = _queueWindow?.ContentView;
        container?.LayoutSubtreeIfNeeded();
        return "container " + Frame(container) + Environment.NewLine
            + "stack " + Frame(_queueLayout) + Environment.NewLine
            + "header " + Frame(_queueHeader) + Environment.NewLine
            + "toolbar " + Frame(_queueToolbar) + Environment.NewLine
            + "summary " + Frame(_queueSummary) + Environment.NewLine
            + "scroll " + Frame(_queueScroll) + Environment.NewLine
            + "document " + Frame(_queueScroll?.DocumentView) + Environment.NewLine;
    }

    private NSButton? FindQueueSmokeButton(string label, bool last = false)
    {
        if (_queueScroll?.DocumentView is not { } root)
        {
            return null;
        }

        var matches = QueueSmokeButtons().Where(button => button.Enabled && button.Title == label);
        return last ? matches.LastOrDefault() : matches.FirstOrDefault();
    }

    private IEnumerable<NSButton> QueueSmokeButtons()
    {
        if (_queueScroll?.DocumentView is not { } root)
        {
            return [];
        }

        static IEnumerable<NSButton> Buttons(NSView view)
        {
            if (view is NSButton button)
            {
                yield return button;
            }

            foreach (var child in view.Subviews)
            {
                foreach (var candidate in Buttons(child))
                {
                    yield return candidate;
                }
            }
        }

        return Buttons(root);
    }

    private void CloseQueue()
    {
        _closingQueue = true;
        _queueWindow?.Close();
        _queueWindow?.Dispose();
        _queueWindow = null;
    }
}
