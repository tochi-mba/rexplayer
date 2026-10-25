using System.Globalization;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.Primitives;

namespace Rex.Media.App.Mac;

public sealed partial class MainWindow
{
    private NSWindow? _playbackTools;
    private NSPopUpButton? _bookmarkPicker;
    private NSTextField? _bookmarkName;
    private NSTextField? _audioDescription;
    private NSTextField? _audioDelayDescription;
    private NSTextField? _audioDelayEntry;
    private NSTextField? _playbackToolsTitle;
    private NSButton? _nextAudioTrack;
    private readonly List<NSButton> _bookmarkActions = [];
    private readonly List<NSButton> _seekActions = [];
    private string? _bookmarkSignature;

    /// <summary>Native controls over the shared player's bookmarks, sound and saved progress.</summary>
    private void ShowPlaybackTools()
    {
        if (_playbackTools is null)
        {
            _playbackTools = new NSWindow(new CGRect(0, 0, 520, 430),
                NSWindowStyle.Titled | NSWindowStyle.Closable, NSBackingStore.Buffered, false)
            {
                Title = "Playback tools",
            };
            _playbackTools.ReleaseWhenClosed(false);
            _playbackToolsTitle = NSTextField.CreateLabel("Open media to use playback tools.");
            _playbackToolsTitle.Font = NSFont.SystemFontOfSize(18, NSFontWeight.Semibold)!;
            _playbackToolsTitle.LineBreakMode = NSLineBreakMode.TruncatingTail;

            var resume = NSButton.CreateButton("Resume saved position", () => Run(CommandCatalog.Resume));
            var restart = NSButton.CreateButton("Start from beginning", () => _player.StartOver());
            _seekActions.AddRange([resume, restart]);

            _bookmarkPicker = new NSPopUpButton(new CGRect(0, 0, 460, 28), false);
            _bookmarkPicker.Activated += (_, _) => ShowSelectedBookmarkName();
            _bookmarkPicker.ToolTip = "Choose a saved moment in this file";
            _bookmarkName = new NSTextField(new CGRect(0, 0, 460, 24)) { PlaceholderString = "Name this moment" };
            _bookmarkName.ToolTip = "Bookmark name";
            var add = NSButton.CreateButton("Save this moment", () =>
            {
                _player.AddBookmark(_bookmarkName.StringValue);
                RefreshPlaybackTools();
                if (_player.Bookmarks.Count > 0)
                {
                    _bookmarkPicker.SelectItem(_player.Bookmarks.Count - 1);
                    ShowSelectedBookmarkName();
                }
            });
            _seekActions.Add(add);
            var jump = NSButton.CreateButton("Go to", () => _player.GoToBookmark((int)_bookmarkPicker.IndexOfSelectedItem));
            var rename = NSButton.CreateButton("Rename", () =>
            {
                _player.RenameBookmark((int)_bookmarkPicker.IndexOfSelectedItem, _bookmarkName.StringValue);
                RefreshPlaybackTools();
            });
            var remove = NSButton.CreateButton("Remove", () =>
            {
                var index = (int)_bookmarkPicker.IndexOfSelectedItem;
                if (index < 0 || index >= _player.Bookmarks.Count) return;
                var name = _player.Bookmarks[index].Name;
                ConfirmInWindow("Remove saved moment '" + name + "' from this file?", () =>
                {
                    _player.DeleteBookmark(index);
                    RefreshPlaybackTools();
                });
            });
            _bookmarkActions.AddRange([jump, rename, remove]);

            _audioDescription = NSTextField.CreateLabel("No audio track");
            _audioDescription.LineBreakMode = NSLineBreakMode.TruncatingTail;
            _nextAudioTrack = NSButton.CreateButton("Next audio track", () => Run(CommandCatalog.CycleAudioTrack));
            _audioDelayDescription = NSTextField.CreateLabel("Audio in sync");
            _audioDelayDescription.Font = NSFont.MonospacedDigitSystemFontOfSize(12, NSFontWeight.Regular)!;
            _audioDelayEntry = new NSTextField(new CGRect(0, 0, 120, 24))
            {
                PlaceholderString = "Milliseconds",
                ToolTip = "Audio delay from -10000 to 10000 milliseconds",
            };
            var earlier = NSButton.CreateButton("Earlier 50 ms", () => _player.SetAudioDelay(_player.AudioDelay - TimeSpan.FromMilliseconds(50)));
            var later = NSButton.CreateButton("Later 50 ms", () => _player.SetAudioDelay(_player.AudioDelay + TimeSpan.FromMilliseconds(50)));
            var reset = NSButton.CreateButton("Reset sync", () => _player.SetAudioDelay(TimeSpan.Zero));
            var apply = NSButton.CreateButton("Set exact delay", SetExactAudioDelay);
            var hint = NSTextField.CreateLabel("Changes apply immediately. Bookmarks stay with this file.");
            hint.TextColor = NSColor.SecondaryLabel;

            var stack = NSStackView.FromViews([
                _playbackToolsTitle,
                PlaybackToolsRow(resume, restart),
                PlaybackToolsHeading("Saved moments"),
                _bookmarkPicker,
                _bookmarkName,
                PlaybackToolsRow(add, jump, rename, remove),
                PlaybackToolsHeading("Sound & sync"),
                PlaybackToolsRow(_audioDescription, _nextAudioTrack),
                _audioDelayDescription,
                PlaybackToolsRow(earlier, later, reset),
                PlaybackToolsRow(_audioDelayEntry, apply),
                hint,
            ]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 12;
            stack.EdgeInsets = new NSEdgeInsets(24, 24, 24, 24);
            _playbackTools.ContentView = stack;
            _bookmarkPicker.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _bookmarkName.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _playbackToolsTitle.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _audioDescription.WidthAnchor.ConstraintEqualTo(270).Active = true;
            _playbackTools.Center();
        }

        RefreshPlaybackTools();
        _playbackTools.MakeKeyAndOrderFront(null);
    }

    private static NSStackView PlaybackToolsRow(params NSView[] views)
    {
        var row = NSStackView.FromViews(views);
        row.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        row.Spacing = 8;
        return row;
    }

    private static NSTextField PlaybackToolsHeading(string title)
    {
        var label = NSTextField.CreateLabel(title);
        label.Font = NSFont.SystemFontOfSize(13, NSFontWeight.Semibold)!;
        return label;
    }

    private void ShowSelectedBookmarkName()
    {
        var index = (int)(_bookmarkPicker?.IndexOfSelectedItem ?? -1);
        if (_bookmarkName is not null)
        {
            _bookmarkName.StringValue = index >= 0 && index < _player.Bookmarks.Count ? _player.Bookmarks[index].Name : "";
        }
    }

    private void SetExactAudioDelay()
    {
        if (double.TryParse(_audioDelayEntry?.StringValue, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var milliseconds))
        {
            _player.SetAudioDelay(TimeSpan.FromMilliseconds(milliseconds));
            RefreshPlaybackTools();
            return;
        }

        Say("Enter an audio delay in milliseconds, from -10000 to 10000.");
    }

    private void RefreshPlaybackTools()
    {
        if (_playbackTools is null || _bookmarkPicker is null)
        {
            return;
        }

        _playbackToolsTitle!.StringValue = _player.Item is null ? "Open media to use playback tools." : _player.Title;
        var bookmarks = _player.Bookmarks;
        var signature = _player.Item?.Location + "\n" + string.Join("\n", bookmarks.Select(bookmark => bookmark.Name + "\t" + bookmark.At.Ticks.ToString(CultureInfo.InvariantCulture)));
        if (_bookmarkSignature != signature)
        {
            var selected = (int)_bookmarkPicker.IndexOfSelectedItem;
            _bookmarkSignature = signature;
            _bookmarkPicker.RemoveAllItems();
            if (bookmarks.Count == 0)
            {
                _bookmarkPicker.AddItem("No saved moments yet");
            }
            else
            {
                foreach (var bookmark in bookmarks)
                {
                    _bookmarkPicker.AddItem($"{TimeText.Format(bookmark.At)} · {bookmark.Name}");
                }

                _bookmarkPicker.SelectItem(Math.Clamp(selected, 0, bookmarks.Count - 1));
            }

            ShowSelectedBookmarkName();
        }

        _bookmarkPicker.Enabled = bookmarks.Count > 0;
        foreach (var button in _bookmarkActions)
        {
            button.Enabled = bookmarks.Count > 0 && _player.CanSeek;
        }

        foreach (var button in _seekActions)
        {
            button.Enabled = _player.CanSeek;
        }

        var track = _player.AudioTracks.FirstOrDefault(candidate => candidate.Id == _player.AudioTrack);
        _audioDescription!.StringValue = track is null ? "No audio track" : $"{track.Title ?? track.Language ?? track.Codec.DisplayName()} · {_player.AudioTracks.Count} track(s)";
        _nextAudioTrack!.Enabled = _player.AudioTracks.Count > 1;
        _audioDelayDescription!.StringValue = string.Create(CultureInfo.InvariantCulture, $"Audio delay: {_player.AudioDelay.TotalMilliseconds:+0;-0;0} ms");
        _audioDelayEntry!.StringValue = _player.AudioDelay.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture);
    }

    private void DisposePlaybackTools()
    {
        _playbackTools?.Close();
        _playbackTools?.Dispose();
        _playbackTools = null;
    }
}
