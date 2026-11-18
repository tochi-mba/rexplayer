using System.Globalization;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Transcription;
using Rex.Media.Settings;
using Rex.Media.Subtitles;

namespace Rex.Media.App.Mac;

/// <summary>
/// Reads the speech saved for what plays as a resizable list of timed lines: a search finds words
/// through the portable transcript search, a line jumps playback to its time, and the line being
/// said stays marked while the search is empty. When several qualities or editions are saved, a
/// picker chooses which one is read, from the same named variants the Windows reader uses.
/// </summary>
public sealed partial class MainWindow
{
    private const string MacReaderMenuTitle = "Read transcript…";
    private NSWindow? _macReaderWindow;
    private NSTableView? _macReaderTable;
    private MacReaderRows? _macReaderSource;
    private NSSearchField? _macReaderSearch;
    private NSPopUpButton? _macReaderVariants;
    private NSTextField? _macReaderStatus;
    private IReadOnlyList<SubtitleCue> _macReaderCues = [];
    private List<int> _macReaderRows = [];
    private Transcript? _macReaderShown;
    private int _macReaderCurrent = -1;
    private int _macReaderMatches;
    private int _macReaderVariant;
    private List<MacSavedVariant> _macSavedVariants = [];
    private int _macVariantGeneration;

    /// <summary>A saved quality or edition, its words and the stretches heard for it.</summary>
    private sealed record MacSavedVariant(TranscriptVariant Variant, Transcript Content, TimeRanges? Done, TimeSpan? Duration);

    /// <summary>Every transcript of what plays, the live quality's first, then each saved one.</summary>
    private List<MacSavedVariant> MacTranscriptVariants()
    {
        var variants = new List<MacSavedVariant>();
        var live = _macTranscript ?? _macPartialTranscript
            ?? (_macTranscriptItem is { } path ? _macWorker?.Snapshot(path) : null);
        if (live is { IsEmpty: false })
        {
            variants.Add(new MacSavedVariant(new TranscriptVariant(MacModel.Id, false), live, null, null));
        }

        variants.AddRange(_macSavedVariants);
        return variants;
    }

    /// <summary>
    /// Loads each other quality's original and every edited version off the window thread, and lets
    /// them join the track list unshown, as on Windows.
    /// </summary>
    private void QueueMacTranscriptVariants(string media)
    {
        var generation = ++_macVariantGeneration;
        _macSavedVariants = [];
        var active = MacModel.Id;
        foreach (var stale in _player.SubtitleTracks.Where(track => TranscriptVariant.ReplacedByLiveTrack(track.Name, active)).ToArray())
        {
            _player.RemoveSubtitles(stale);
        }

        _ = Task.Run(() =>
        {
            var found = new List<MacSavedVariant>();
            foreach (var choice in Enum.GetValues<TranscriptModelChoice>())
            {
                var model = SpeechModels.For(choice).Id;
                // The active original is the live track and is never added twice.
                if (model != active)
                {
                    var complete = _macTranscriptCache.Load(media, model);
                    var partial = complete is null ? _macTranscriptCache.LoadPartial(media, model) : null;
                    if ((complete ?? partial?.ToTranscript()) is { IsEmpty: false } generated)
                    {
                        // A complete transcript leaves its coverage to the duration known when it is shown.
                        found.Add(new MacSavedVariant(new TranscriptVariant(model, false), generated,
                            partial?.Done, partial?.Duration));
                    }
                }

                if (_macTranscriptCache.LoadEdited(media, model) is { Content.IsEmpty: false } edited)
                {
                    found.Add(new MacSavedVariant(new TranscriptVariant(model, true), edited.Content, edited.Done, edited.Duration));
                }
            }

            return found;
        }).ContinueWith(task => OnWindowThread(() =>
        {
            if (!task.IsCompletedSuccessfully || generation != _macVariantGeneration || _player.Item?.Location != media)
            {
                return;
            }

            _macSavedVariants = task.Result;
            foreach (var variant in _macSavedVariants)
            {
                _player.AddSubtitles(variant.Content.ToTrack(variant.Variant.TrackName), show: false);
            }

            RefreshTrackTools();
            ShowMacCoverage();
            RefreshMacReader();
        }), TaskScheduler.Default);
    }

    /// <summary>Puts "Read transcript…" at the end of the Subtitles menu, again after the menus are rebuilt.</summary>
    private void EnsureMacReaderMenu()
    {
        if (NSApplication.SharedApplication.MainMenu?.ItemWithTitle("Subtitles")?.Submenu is not { } subtitles ||
            subtitles.ItemWithTitle(MacReaderMenuTitle) is not null)
        {
            return;
        }

        subtitles.AddItem(NSMenuItem.SeparatorItem);
        subtitles.AddItem(new NSMenuItem(MacReaderMenuTitle, (_, _) => ShowMacReader()));
    }

    private void ShowMacReader()
    {
        if (_player.Item?.Location is not { } media || !File.Exists(media))
        {
            Say("Select an available local file before reading its transcript.");
            return;
        }

        if (MacTranscriptVariants().Count == 0)
        {
            Say("No transcript is saved for this file yet. Transcribe it first.");
            return;
        }

        if (_macReaderWindow is null)
        {
            _macReaderWindow = new NSWindow(new CGRect(0, 0, 640, 560),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable | NSWindowStyle.Miniaturizable,
                NSBackingStore.Buffered, false)
            {
                Title = "Transcript - rexplayer",
                MinSize = new CGSize(420, 320),
            };
            _macReaderWindow.ReleaseWhenClosed(false);
            var heading = NSTextField.CreateLabel("Read the transcript");
            heading.Font = NSFont.SystemFontOfSize(22, NSFontWeight.Semibold)!;
            _macReaderStatus = NSTextField.CreateWrappingLabel("");
            _macReaderStatus.TextColor = NSColor.SecondaryLabel;
            _macReaderVariants = new NSPopUpButton(new CGRect(0, 0, 220, 28), false);
            ((NSView)_macReaderVariants).AccessibilityLabel = "Transcript version";
            _macReaderVariants.ToolTip = "Which saved quality or edition to read";
            _macReaderVariants.Activated += (_, _) =>
            {
                _macReaderVariant = (int)_macReaderVariants.IndexOfSelectedItem;
                RefreshMacReader();
            };
            _macReaderSearch = new NSSearchField(new CGRect(0, 0, 280, 28)) { PlaceholderString = "Find speech" };
            ((NSView)_macReaderSearch).AccessibilityLabel = "Find speech in the transcript";
            _macReaderSearch.Changed += (_, _) => FilterMacReader();
            var tools = NSStackView.FromViews([_macReaderSearch, _macReaderVariants]);
            tools.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            tools.Spacing = 10;
            var column = new NSTableColumn("line") { Title = "Speech", Width = 580 };
            _macReaderTable = new NSTableView(new CGRect(0, 0, 592, 400))
            {
                AllowsColumnReordering = false,
                AllowsEmptySelection = true,
                AllowsMultipleSelection = false,
                UsesAlternatingRowBackgroundColors = true,
                HeaderView = null,
                ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.FirstColumnOnly,
            };
            _macReaderTable.AddColumn(column);
            _macReaderSource = new MacReaderRows(this);
            _macReaderTable.Source = _macReaderSource;
            _macReaderTable.Activated += (_, _) => SeekMacReaderRow((int)_macReaderTable.ClickedRow);
            ((NSView)_macReaderTable).AccessibilityLabel = "Transcript lines; choose one to jump to its time";
            var scroll = new NSScrollView
            {
                HasVerticalScroller = true,
                DocumentView = _macReaderTable,
            };
            scroll.HeightAnchor.ConstraintGreaterThanOrEqualTo(200).Active = true;
            var close = NSButton.CreateButton("Done", () => _macReaderWindow?.Close());
            var stack = NSStackView.FromViews([heading, _macReaderStatus, tools, scroll, close]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 12;
            stack.EdgeInsets = new NSEdgeInsets(20, 24, 20, 24);
            stack.TranslatesAutoresizingMaskIntoConstraints = false;
            var root = new NSView(new CGRect(0, 0, 640, 560));
            root.AddSubview(stack);
            stack.TopAnchor.ConstraintEqualTo(root.TopAnchor).Active = true;
            stack.BottomAnchor.ConstraintEqualTo(root.BottomAnchor).Active = true;
            stack.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor).Active = true;
            stack.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor).Active = true;
            _macReaderStatus.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            scroll.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _macReaderWindow.ContentView = root;
            _macReaderWindow.Center();
        }

        _macReaderVariant = 0;
        RefreshMacReader();
        _macReaderWindow.MakeKeyAndOrderFront(null);
    }

    /// <summary>Shows the chosen variant's lines, keeping the search and marking where playback is.</summary>
    private void RefreshMacReader()
    {
        if (_macReaderWindow is not { IsVisible: true } || _macReaderVariants is null || _macReaderStatus is null)
        {
            return;
        }

        var variants = MacTranscriptVariants();
        _macReaderVariants.RemoveAllItems();
        foreach (var variant in variants)
        {
            _macReaderVariants.AddItem(variant.Variant.DisplayName);
        }

        _macReaderVariants.Hidden = variants.Count < 2;
        _macReaderVariant = variants.Count == 0 ? 0 : Math.Clamp(_macReaderVariant, 0, variants.Count - 1);
        if (variants.Count > 0)
        {
            _macReaderVariants.SelectItem(_macReaderVariant);
        }

        var chosen = variants.Count == 0 ? null : variants[_macReaderVariant];
        if (!ReferenceEquals(chosen?.Content, _macReaderShown))
        {
            _macReaderShown = chosen?.Content;
            _macReaderCues = chosen?.Content.ToCues() ?? [];
        }

        var status = chosen is null ? "No transcript is saved for this file yet."
            : ReferenceEquals(chosen.Content, _macTranscript) ? "Saved transcript · Local speech · Choose a line to jump to its time."
            : chosen.Variant.Edited ? "Edited words shown; generated words remain unchanged."
            : "Partial or other-quality transcript · Some speech may not be transcribed yet.";
        if (chosen is not null && chosen.Variant.Model != MacModel.Id)
        {
            status += " · Using another locally saved model.";
        }

        _macReaderStatus.StringValue = status;
        if (_macReaderWindow is not null && chosen is not null)
        {
            _macReaderWindow.Title = "Read " + chosen.Variant.Quality + " transcript - rexplayer";
        }

        _macReaderCurrent = -1;
        FilterMacReader();
    }

    /// <summary>Keeps only the lines holding a match of the search, found word by word by the portable search.</summary>
    private void FilterMacReader()
    {
        var query = _macReaderSearch?.StringValue ?? "";
        var rows = new List<int>();
        _macReaderMatches = 0;
        if (query.Trim().Length == 0 || _macReaderShown is not { } transcript)
        {
            rows.AddRange(Enumerable.Range(0, _macReaderCues.Count));
        }
        else
        {
            var seen = new HashSet<int>();
            var match = TranscriptSearch.FindNext(transcript.Words, query, -1);
            var first = match;
            while (match >= 0)
            {
                _macReaderMatches++;
                var cue = MacReaderCueAt(transcript.Words[match].Start);
                if (cue >= 0 && seen.Add(cue)) rows.Add(cue);
                match = TranscriptSearch.FindNext(transcript.Words, query, match);
                // The search wraps to the start once every hit has been found.
                if (match <= first || match < 0) break;
            }

            rows.Sort();
        }

        _macReaderRows = rows;
        _macReaderTable?.ReloadData();
        ShowMacReaderPosition();
    }

    /// <summary>The line holding <paramref name="at"/>, or the next line after it.</summary>
    private int MacReaderCueAt(TimeSpan at)
    {
        var cues = _macReaderCues;
        if (cues.Count == 0) return -1;
        var low = 0;
        var high = cues.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (cues[middle].End <= at) low = middle + 1;
            else high = middle;
        }

        return Math.Min(low, cues.Count - 1);
    }

    /// <summary>Marks the line being said and, while nothing is searched, keeps it in view.</summary>
    private void ShowMacReaderPosition()
    {
        if (_macReaderWindow is not { IsVisible: true } || _macReaderTable is null) return;
        var at = _player.Position;
        var index = MacReaderCueAt(at);
        if (index >= 0 && (at < _macReaderCues[index].Start || at >= _macReaderCues[index].End))
        {
            index = -1;
        }

        if (index == _macReaderCurrent) return;
        _macReaderCurrent = index;
        _macReaderTable.ReloadData();
        var row = _macReaderRows.IndexOf(index);
        if (row >= 0 && (_macReaderSearch?.StringValue ?? "").Trim().Length == 0)
        {
            _macReaderTable.ScrollRowToVisible(row);
        }
    }

    private void SeekMacReaderRow(int row)
    {
        if (row >= 0 && row < _macReaderRows.Count)
        {
            SeekMacReaderCue(_macReaderRows[row]);
        }
    }

    private bool SeekMacReaderCue(int cue)
    {
        if (cue < 0 || cue >= _macReaderCues.Count || !_player.CanSeek) return false;
        var at = _macReaderCues[cue].Start;
        _player.Seek(at < TimeSpan.Zero ? TimeSpan.Zero : at);
        return true;
    }

    /// <summary>The reader's, the variants' and the coverage's automation ids, answered for the smoke journey.</summary>
    private AutomationReply? MacReaderSmokeAction(string? action)
    {
        if (action == "reader")
        {
            if (_macReaderWindow?.IsVisible != true) ShowMacReader();
            return MacReaderReply();
        }

        if (action?.StartsWith("reader-search:", StringComparison.Ordinal) == true)
        {
            if (_macReaderWindow?.IsVisible != true) ShowMacReader();
            if (_macReaderSearch is null) return new AutomationReply { Ok = false, Error = "The transcript reader is not open." };
            _macReaderSearch.StringValue = action["reader-search:".Length..];
            FilterMacReader();
            return new AutomationReply { Ok = true, Text = "matches=" + _macReaderMatches };
        }

        if (action?.StartsWith("reader-seek:", StringComparison.Ordinal) == true)
        {
            return int.TryParse(action["reader-seek:".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cue) && SeekMacReaderCue(cue)
                ? MacReaderReply()
                : new AutomationReply { Ok = false, Error = "No such transcript line to jump to." };
        }

        if (action == "variants")
        {
            return new AutomationReply { Ok = true, Text = $"count={MacTranscriptVariants().Count};active={_macReaderVariant}" };
        }

        if (action == "coverage")
        {
            var spans = _seek.Coverage;
            var covered = spans.Sum(span => span.To - span.From);
            return new AutomationReply
            {
                Ok = true,
                Text = string.Create(CultureInfo.InvariantCulture, $"spans={spans.Count};covered={covered:0.00}"),
            };
        }

        return null;
    }

    private AutomationReply MacReaderReply() => new()
    {
        Ok = true,
        Text = $"visible={_macReaderWindow?.IsVisible == true};lines={_macReaderRows.Count};current={_macReaderCurrent}",
    };

    private void DisposeMacReader()
    {
        _macReaderWindow?.Close();
        _macReaderWindow?.Dispose();
        _macReaderWindow = null;
    }

    /// <summary>The reader's rows: a time and its words, the line being said in semibold.</summary>
    private sealed class MacReaderRows : NSTableViewSource
    {
        private readonly MainWindow _owner;

        public MacReaderRows(MainWindow owner) => _owner = owner;

        public override nint GetRowCount(NSTableView tableView) => _owner._macReaderRows.Count;

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            var index = _owner._macReaderRows[(int)row];
            var cue = _owner._macReaderCues[index];
            var time = TimeText.Format(cue.Start, _owner._player.Duration);
            var label = NSTextField.CreateLabel(time + "   " + cue.Text.Replace('\n', ' '));
            label.LineBreakMode = NSLineBreakMode.TruncatingTail;
            label.Font = index == _owner._macReaderCurrent
                ? NSFont.SystemFontOfSize(13, NSFontWeight.Semibold)!
                : NSFont.SystemFontOfSize(13)!;
            ((NSView)label).AccessibilityLabel = $"Jump to {time}. {cue.Text}";
            return label;
        }

        public override void SelectionDidChange(NSNotification notification)
        {
            // A click jumps through the table's action; moving with the keyboard jumps from here.
            if (NSApplication.SharedApplication.CurrentEvent?.Type == NSEventType.KeyDown &&
                _owner._macReaderTable is { } table)
            {
                _owner.SeekMacReaderRow((int)table.SelectedRow);
            }
        }
    }
}
