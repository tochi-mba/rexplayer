using System.Globalization;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Design;
using Rex.Media.AppCore.Player;
using Rex.Media.Primitives;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

public sealed partial class MainWindow
{
    private NSWindow? _playbackTools;
    private NSPopUpButton? _bookmarkPicker;
    private NSTextField? _bookmarkName;
    private RexLabel? _audioDescription;
    private RexLabel? _audioDelayDescription;
    private NSTextField? _audioDelayEntry;
    private RexLabel? _playbackToolsTitle;
    private NSPopUpButton? _playbackSpeed;
    private NSButton? _playbackPreservePitch;
    private NSButton? _nextAudioTrack;
    private readonly List<NSButton> _bookmarkActions = [];
    private readonly List<NSButton> _seekActions = [];
    private string? _bookmarkSignature;
    private NSButton? _bookmarkAdd;
    private NSButton? _bookmarkJump;
    private NSButton? _bookmarkRename;
    private NSButton? _bookmarkRemove;

    /// <summary>Native controls over the shared player's bookmarks, sound and saved progress.</summary>
    private void ShowPlaybackTools()
    {
        if (_playbackTools is null)
        {
            _playbackTools = MacToolViews.ToolWindow("Playback tools", 560, 640, 500, 420);
            _playbackToolsTitle = MacToolViews.ToolTitle("Open media to use playback tools.");

            var resume = MacTheme.TextButton("Resume saved position", () => Run(CommandCatalog.Resume), RexButtonLook.Accent);
            var restart = MacTheme.TextButton("Start from beginning", () => _player.StartOver());
            _seekActions.AddRange([resume, restart]);

            _bookmarkPicker = new NSPopUpButton(new CGRect(0, 0, 300, 28), false);
            _bookmarkPicker.Activated += (_, _) => ShowSelectedBookmarkName();
            _bookmarkPicker.ToolTip = "Choose a saved moment in this file";
            ((NSView)_bookmarkPicker).AccessibilityLabel = "Saved moment";
            _bookmarkPicker.WidthAnchor.ConstraintEqualTo(300).Active = true;
            _bookmarkName = new NSTextField(new CGRect(0, 0, 300, 24)) { PlaceholderString = "Name this moment" };
            _bookmarkName.ToolTip = "Bookmark name";
            ((NSView)_bookmarkName).AccessibilityLabel = "Bookmark name";
            _bookmarkName.WidthAnchor.ConstraintEqualTo(300).Active = true;
            var add = MacTheme.TextButton("Save this moment", () =>
            {
                _player.AddBookmark(_bookmarkName.StringValue);
                RefreshPlaybackTools();
                if (_player.Bookmarks.Count > 0)
                {
                    _bookmarkPicker.SelectItem(_player.Bookmarks.Count - 1);
                    ShowSelectedBookmarkName();
                }
            }, RexButtonLook.Accent, "plus");
            _bookmarkAdd = add;
            _seekActions.Add(add);
            var jump = MacTheme.TextButton("Go to", () => _player.GoToBookmark((int)_bookmarkPicker.IndexOfSelectedItem));
            _bookmarkJump = jump;
            var rename = MacTheme.TextButton("Rename", () =>
            {
                _player.RenameBookmark((int)_bookmarkPicker.IndexOfSelectedItem, _bookmarkName.StringValue);
                RefreshPlaybackTools();
            });
            _bookmarkRename = rename;
            var remove = MacTheme.TextButton("Remove", () =>
            {
                var index = (int)_bookmarkPicker.IndexOfSelectedItem;
                if (index < 0 || index >= _player.Bookmarks.Count) return;
                var name = _player.Bookmarks[index].Name;
                ConfirmInWindow("Remove saved moment '" + name + "' from this file?", () =>
                {
                    _player.DeleteBookmark(index);
                    RefreshPlaybackTools();
                });
            }, symbol: "trash");
            _bookmarkRemove = remove;
            _bookmarkActions.AddRange([jump, rename, remove]);

            _audioDescription = MacTheme.Label("No audio track", 2, DesignRole.TextSecondary);
            _audioDescription.WidthAnchor.ConstraintLessThanOrEqualTo(240).Active = true;
            _nextAudioTrack = MacTheme.TextButton("Next audio track", () => Run(CommandCatalog.CycleAudioTrack));
            _audioDelayDescription = MacTheme.Label("Audio in sync");
            _audioDelayDescription.Font = MacTheme.DigitFont(2);
            _audioDelayEntry = new NSTextField(new CGRect(0, 0, 120, 24))
            {
                PlaceholderString = "Milliseconds",
                ToolTip = "Audio delay from -10000 to 10000 milliseconds",
            };
            ((NSView)_audioDelayEntry).AccessibilityLabel = "Audio delay in milliseconds";
            _audioDelayEntry.WidthAnchor.ConstraintEqualTo(120).Active = true;
            var earlier = MacTheme.TextButton("Earlier 50 ms", () => _player.SetAudioDelay(_player.AudioDelay - TimeSpan.FromMilliseconds(50)));
            var later = MacTheme.TextButton("Later 50 ms", () => _player.SetAudioDelay(_player.AudioDelay + TimeSpan.FromMilliseconds(50)));
            var reset = MacTheme.TextButton("Reset sync", () => _player.SetAudioDelay(TimeSpan.Zero));
            var apply = MacTheme.TextButton("Set exact delay", SetExactAudioDelay);
            _playbackSpeed = new NSPopUpButton(new CGRect(0, 0, 140, 28), false);
            foreach (var speed in PlayerController.SpeedSteps)
            {
                _playbackSpeed.AddItem(speed.ToString("0.##", CultureInfo.InvariantCulture) + "×");
            }
            _playbackSpeed.Font = MacTheme.DigitFont(2);
            _playbackSpeed.Activated += (_, _) =>
            {
                var index = (int)_playbackSpeed.IndexOfSelectedItem;
                if (index >= 0 && index < PlayerController.SpeedSteps.Count)
                {
                    _player.SetSpeed(PlayerController.SpeedSteps[index]);
                    RefreshPlaybackTools();
                }
            };
            ((NSView)_playbackSpeed).AccessibilityLabel = "Playback speed, pitch preserved";
            _playbackPreservePitch = NSButton.CreateCheckbox("Preserve pitch at different speeds", () =>
            {
                var preserve = IsChecked(_playbackPreservePitch);
                _settings = _settings with { PreservePitch = preserve };
                _player.SetPreservePitch(preserve);
                try
                {
                    SettingsStore.Save(_roots.SettingsPath, _settings);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log.Warning(LogSource, "Pitch mode could not be saved: " + ex.Message);
                }
            });

            var page = MacToolViews.ToolPage(_playbackToolsTitle, null,
                MacToolViews.ToolCard(null, MacToolViews.ToolButtons(resume, restart)),
                MacToolViews.ToolCard("Saved moments",
                    MacToolViews.ToolRow("Saved moment", null, _bookmarkPicker),
                    MacToolViews.ToolRow("Name", null, _bookmarkName),
                    MacToolViews.ToolButtons(add, jump, rename, remove)),
                MacToolViews.ToolCard("Sound & sync",
                    MacToolViews.ToolRow("Audio track", null, MacToolViews.ToolButtons(_audioDescription, _nextAudioTrack)),
                    MacToolViews.ToolRow("Playback speed", "WSOLA keeps pitch, or use chipmunk mode.", _playbackSpeed),
                    MacToolViews.ToolCheck(_playbackPreservePitch),
                    MacTheme.Separator(),
                    _audioDelayDescription,
                    MacToolViews.ToolButtons(earlier, later, reset),
                    MacToolViews.ToolRow("Exact delay", null, MacToolViews.ToolButtons(_audioDelayEntry, apply))),
                MacTheme.WrappingLabel("Changes apply immediately. Bookmarks stay with this file.", 1, DesignRole.TextSecondary));
            MacToolViews.Host(_playbackTools, page, scrolls: true);
            _playbackTools.Center();
        }

        RefreshPlaybackTools();
        _playbackTools.MakeKeyAndOrderFront(null);
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
        if (_playbackPreservePitch is not null)
        {
            _playbackPreservePitch.State = State(_settings.PreservePitch);
        }
        if (_playbackSpeed is not null)
        {
            var index = PlayerController.SpeedSteps.ToList().FindIndex(
                speed => Math.Abs(speed - _player.Speed) < 0.001);
            _playbackSpeed.SelectItem(Math.Max(0, index));
        }
    }

    private void DisposePlaybackTools()
    {
        _playbackTools?.Close();
        _playbackTools?.Dispose();
        _playbackTools = null;
    }

    /// <summary>Drive the same native bookmark controls used by a person in the playback-tools window.</summary>
    private AutomationReply PitchSpeedSmokeAction(string? action)
    {
        ShowPlaybackTools();
        switch (action)
        {
            case "minimum":
                _playbackSpeed!.SelectItem(0);
                _player.SetSpeed(PlayerController.SpeedSteps[0]);
                break;
            case "maximum":
                _playbackSpeed!.SelectItem(PlayerController.SpeedSteps.Count - 1);
                _player.SetSpeed(PlayerController.SpeedSteps[^1]);
                break;
            case "toggle-pitch":
                _playbackPreservePitch!.PerformClick(_playbackPreservePitch);
                break;
            case "status":
                break;
            default:
                return AutomationReply.Failed("Unknown Mac pitch-speed action: " + action);
        }

        RefreshPlaybackTools();
        return new AutomationReply
        {
            Ok = true,
            Text = $"speed={_player.Speed.ToString("0.##", CultureInfo.InvariantCulture)};" +
                $"preserve={_settings.PreservePitch};choices={PlayerController.SpeedSteps.Count}",
        };
    }

    private AutomationReply BookmarkSmokeAction(string? action)
    {
        ShowPlaybackTools();
        switch (action)
        {
            case "add":
                _bookmarkName!.StringValue = "Mac moment";
                _bookmarkAdd!.PerformClick(_bookmarkAdd);
                break;
            case "rename":
                _bookmarkName!.StringValue = "Mac renamed moment";
                _bookmarkRename!.PerformClick(_bookmarkRename);
                break;
            case "jump":
                _bookmarkJump!.PerformClick(_bookmarkJump);
                break;
            case "remove":
                _bookmarkRemove!.PerformClick(_bookmarkRemove);
                var accept = _confirmationAccept;
                HideConfirmation();
                accept?.Invoke();
                break;
            case "status":
                break;
            default:
                return AutomationReply.Failed("Unknown bookmark action: " + action);
        }

        RefreshPlaybackTools();
        return new AutomationReply
        {
            Ok = true,
            Text = string.Join("|", _player.Bookmarks.Select(bookmark => bookmark.Name)),
            Position = _player.Position.TotalSeconds,
        };
    }
}
