using Rex.Media.AppCore.Player;

namespace Rex.Media.App.Mac;

/// <summary>
/// Up Next's scripted checks: the "mac-queue" actions CI drives, the layout report saved beside
/// its screenshots, and the menu helpers they press. None of it runs in a release session.
/// </summary>
public sealed partial class MainWindow
{
    private CGRect? _queueSmokeFrame;

    /// <summary>
    /// CI-only AppKit smoke actions. These run the same menu items and buttons a user presses and
    /// capture the rendered pane without relying on screen-recording permissions.
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

        if (action == "clear")
        {
            _player.Playlist.Clear();
            RefreshQueue();
            return true;
        }

        if (_queueRoot is null)
        {
            return false;
        }

        if (action == "minimum")
        {
            // The pane keeps its width; the smallest player window gives it the least height.
            _queueSmokeFrame ??= _window.Frame;
            var frame = _window.Frame;
            var size = _window.MinSize;
            _window.SetFrame(new CGRect(frame.X, frame.Y, size.Width, size.Height), true);
            _window.ContentView?.LayoutSubtreeIfNeeded();
            return true;
        }

        if (action == "verify-accessibility")
        {
            // The list's lines and their menu items must give disambiguated VoiceOver names,
            // not repeat one label for every line.
            RefreshQueue();
            var line = _queueLines.FirstOrDefault(candidate => !candidate.Current && !candidate.Missing);
            var next = line is null ? null : QueueSmokeMenuItem(line.Item, "Play next");
            var cell = line is null ? null : _queueTable?.GetView(0, _queueLines.ToList().IndexOf(line), true);
            return next is not null
                && next.ToolTip?.StartsWith("Play ", StringComparison.Ordinal) == true
                && next.ToolTip.EndsWith(" next", StringComparison.Ordinal)
                && cell?.AccessibilityLabel?.Contains(line!.Title, StringComparison.Ordinal) == true
                && _queueShuffle is not null && _queueRepeat is not null
                && ((NSView?)_queueTable)?.AccessibilityLabel == "Playlist, drag entries to reorder";
        }

        if (action == "verify-missing")
        {
            var missing = new PlaylistItem(Path.Combine(Path.GetTempPath(),
                "rexplayer-missing-" + Guid.NewGuid().ToString("N") + ".wav"))
            {
                Title = "Moved recording",
            };
            _player.Playlist.Add([missing]);
            _queuePage = _player.Playlist.IndexOf(missing) / QueuePageSize;
            RefreshQueue();
            RefreshQueueMoreMenu();
            var line = _queueLines.FirstOrDefault(candidate => ReferenceEquals(candidate.Item, missing));
            var play = QueueSmokeMenuItem(missing, "Play");
            var locate = QueueSmokeMenuItem(missing, "Locate…");
            var cell = line is null ? null : _queueTable?.GetView(0, _queueLines.ToList().IndexOf(line), true);
            var proved = line is { Missing: true }
                && play is { Enabled: false }
                && cell?.ToolTip?.StartsWith("Missing file: ", StringComparison.Ordinal) == true
                && locate is { Enabled: true }
                && _queueRelink is { Enabled: true }
                && _queueRelink.ToolTip == "Find every missing queue file in a folder";
            var index = _player.Playlist.IndexOf(missing);
            if (index >= 0) _player.Playlist.RemoveAt(index);
            RefreshQueue();
            return proved;
        }

        if (action == "verify-list-drop")
        {
            var before = _player.Playlist.Items.Count;
            DropIntoQueue(["queue-drop-1.wav", "queue-drop-2.wav"], before);
            var inserted = _player.Playlist.Items.Skip(before).Select(item => item.Location).ToArray();
            for (var index = _player.Playlist.Items.Count - 1; index >= before; index--)
            {
                _player.Playlist.RemoveAt(index);
            }
            RefreshQueue();
            return inserted.SequenceEqual(["queue-drop-1.wav", "queue-drop-2.wav"]);
        }

        if (action == "fill-long")
        {
            var smokeLocation = _player.Playlist.Items
                .FirstOrDefault(item => !_player.IsMissing(item))?.Location;
            if (smokeLocation is null)
            {
                return false;
            }

            _player.Playlist.Add(Enumerable.Range(1, 90).Select(index =>
                new PlaylistItem(smokeLocation)
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
            // Use the same Remove menu item as a real user, not Playlist.Clear(). The last page
            // has 16 entries after fill-long; removing them must return to page one rather
            // than strand an empty list.
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

                if (_queueLines.Count == 0 || QueueSmokeMenuItem(_queueLines[^1].Item, "Remove") is not { Enabled: true } remove)
                {
                    return false;
                }

                InvokeQueueMenuItem(remove);
                RefreshQueue();
            }

            return _queuePage == 0 && !_nextQueuePage!.Enabled;
        }

        if (action is "next-page" or "previous-page")
        {
            var button = action == "next-page" ? _nextQueuePage : _previousQueuePage;
            if (button is not { Enabled: true })
            {
                return false;
            }

            button.PerformClick(button);
            return true;
        }

        if (action is "shuffle" or "repeat" or "last-play-next")
        {
            RefreshQueueMoreMenu();
            var item = action switch
            {
                "shuffle" => _queueShuffle,
                "repeat" => _queueRepeat,
                _ => _queueLines.LastOrDefault(line => !line.Current && !line.Missing) is { } last
                    ? QueueSmokeMenuItem(last.Item, "Play next") : null,
            };
            if (item is not { Enabled: true })
            {
                return false;
            }

            InvokeQueueMenuItem(item);
            // Playlist.Changed schedules a refresh on the UI thread; update the result
            // synchronously so the automation reply reports the new mode / next item.
            RefreshQueue();
            return true;
        }

        if (action is "capture-empty" or "capture-current" or "capture-long"
            or "capture-page-2" or "capture-shuffled" or "capture-minimum"
            or "capture-shrunk")
        {
            var folder = Environment.GetEnvironmentVariable("REXPLAYER_MAC_QUEUE_SCREENSHOTS");
            if (string.IsNullOrWhiteSpace(folder))
            {
                return false;
            }

            ShowQueue();
            var view = _queueRoot;
            Directory.CreateDirectory(folder);
            view.LayoutSubtreeIfNeeded();
            File.WriteAllText(Path.Combine(folder, "layout.txt"), QueueSmokeLayoutReport());
            bool saved;
            using (var bitmap = view.BitmapImageRepForCachingDisplayInRect(view.Bounds))
            {
                if (bitmap is null)
                {
                    return false;
                }

                view.CacheDisplay(view.Bounds, bitmap);
                using var data = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png);
                saved = data?.Save(NSUrl.FromFilename(Path.Combine(folder, action["capture-".Length..] + ".png")), true) == true;
            }

            if (action == "capture-minimum" && _queueSmokeFrame is { } restore)
            {
                _queueSmokeFrame = null;
                _window.SetFrame(restore, true);
            }

            return saved;
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

        _queueRoot?.LayoutSubtreeIfNeeded();
        return "container " + Frame(_queueRoot) + Environment.NewLine
            + "stack " + Frame(_queueLayout) + Environment.NewLine
            + "header " + Frame(_queueHeader) + Environment.NewLine
            + "status " + Frame(_queueStatus) + Environment.NewLine
            + "hint " + Frame(_queueHint) + Environment.NewLine
            + "scroll " + Frame(_queueScroll) + Environment.NewLine
            + "document " + Frame(_queueScroll?.DocumentView) + Environment.NewLine
            + "pages " + Frame(_queuePages) + Environment.NewLine;
    }

    /// <summary>The item titled <paramref name="title"/> in the menu a right click on <paramref name="entry"/> opens.</summary>
    private NSMenuItem? QueueSmokeMenuItem(PlaylistItem entry, string title)
    {
        SelectQueueEntries([entry]);
        var menu = QueueEntryMenu([entry]);
        return menu.Items.FirstOrDefault(item => item.Title == title);
    }

    /// <summary>Runs a menu item as choosing it does.</summary>
    private static void InvokeQueueMenuItem(NSMenuItem item)
    {
        if (item.Menu is { } menu)
        {
            menu.PerformActionForItem(menu.IndexOf(item));
        }
    }
}
