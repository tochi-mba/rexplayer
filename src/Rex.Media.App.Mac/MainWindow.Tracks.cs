using System.Globalization;
using Rex.Media.AppCore.Player;
using Rex.Media.Primitives;

namespace Rex.Media.App.Mac;

/// <summary>Native primary/secondary subtitle, audio-track and sync controls.</summary>
public sealed partial class MainWindow
{
    private NSWindow? _tracksWindow;
    private NSPopUpButton? _tracksAudio;
    private NSPopUpButton? _tracksPrimary;
    private NSPopUpButton? _tracksSecondary;
    private NSTextField? _tracksDelay;
    private NSTextField? _tracksExactDelay;
    private NSTextField? _tracksSyncMarks;
    private TimeSpan? _audioSyncMark;
    private TimeSpan? _subtitleSyncMark;
    private bool _tracksUpdating;

    private void ShowTrackTools()
    {
        if (_tracksWindow is null)
        {
            _tracksWindow = new NSWindow(new CGRect(0, 0, 600, 490),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "Tracks & subtitle synchronisation - rexplayer",
                MinSize = new CGSize(520, 410),
            };
            _tracksWindow.ReleaseWhenClosed(false);
            var heading = NSTextField.CreateLabel("Tracks & subtitles");
            heading.Font = NSFont.SystemFontOfSize(23, NSFontWeight.Semibold)!;
            var info = NSTextField.CreateWrappingLabel(
                "Select an audio track and up to two simultaneous subtitle tracks. " +
                "Move subtitles earlier or later to match the soundtrack.");
            info.TextColor = NSColor.SecondaryLabel;
            _tracksAudio = new NSPopUpButton(new CGRect(0, 0, 310, 28), false);
            _tracksPrimary = new NSPopUpButton(new CGRect(0, 0, 310, 28), false);
            _tracksSecondary = new NSPopUpButton(new CGRect(0, 0, 310, 28), false);
            _tracksAudio.Activated += (_, _) =>
            {
                if (!_tracksUpdating && _tracksAudio.IndexOfSelectedItem >= 0 &&
                    _tracksAudio.IndexOfSelectedItem < _player.AudioTracks.Count)
                {
                    _player.SelectAudioTrack(_player.AudioTracks[(int)_tracksAudio.IndexOfSelectedItem].Id);
                }
            };
            _tracksPrimary.Activated += (_, _) =>
            {
                if (!_tracksUpdating)
                {
                    var index = (int)_tracksPrimary.IndexOfSelectedItem - 1;
                    _player.ShowSubtitles(index >= 0 && index < _player.SubtitleTracks.Count
                        ? _player.SubtitleTracks[index] : null);
                }
            };
            _tracksSecondary.Activated += (_, _) =>
            {
                if (!_tracksUpdating)
                {
                    var index = (int)_tracksSecondary.IndexOfSelectedItem - 1;
                    _player.ShowSecondarySubtitles(index >= 0 && index < _player.SubtitleTracks.Count
                        ? _player.SubtitleTracks[index] : null);
                }
            };
            var add = NSButton.CreateButton("Add subtitle file…", () =>
            {
                AddSubtitleWithPanel();
                RefreshTrackTools();
            });
            _tracksDelay = NSTextField.CreateLabel("Subtitles in sync");
            _tracksDelay.Font = NSFont.MonospacedDigitSystemFontOfSize(13, NSFontWeight.Regular)!;
            _tracksExactDelay = new NSTextField(new CGRect(0, 0, 140, 26))
            {
                PlaceholderString = "Milliseconds",
                ToolTip = "An exact subtitle offset between -10000 and 10000 milliseconds",
            };
            var earlier = NSButton.CreateButton("Earlier 50 ms", () =>
                _player.SetSubtitleDelay(_player.SubtitleDelay - TimeSpan.FromMilliseconds(50)));
            var later = NSButton.CreateButton("Later 50 ms", () =>
                _player.SetSubtitleDelay(_player.SubtitleDelay + TimeSpan.FromMilliseconds(50)));
            var reset = NSButton.CreateButton("Reset delay", () => _player.SetSubtitleDelay(TimeSpan.Zero));
            var set = NSButton.CreateButton("Set exact delay", ApplyExactSubtitleDelay);
            _tracksSyncMarks = NSTextField.CreateWrappingLabel(
                "Mark the audio and subtitle events you want aligned, then apply their difference.");
            _tracksSyncMarks.TextColor = NSColor.SecondaryLabel;
            var markAudio = NSButton.CreateButton("Mark audio event", () =>
            {
                _audioSyncMark = _player.Position;
                ShowSubtitleSyncMarks();
            });
            var markSubtitle = NSButton.CreateButton("Mark subtitle event", () =>
            {
                _subtitleSyncMark = _player.Position;
                ShowSubtitleSyncMarks();
            });
            var sync = NSButton.CreateButton("Align marked events", AlignSubtitleMarks);
            var delayButtons = NSStackView.FromViews([earlier, later, reset]);
            delayButtons.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            delayButtons.Spacing = 8;
            var exact = NSStackView.FromViews([_tracksExactDelay, set]);
            exact.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            exact.Spacing = 8;
            var marks = NSStackView.FromViews([markAudio, markSubtitle, sync]);
            marks.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            marks.Spacing = 8;
            var stack = NSStackView.FromViews([
                heading, info, EffectChoiceRow("Audio", _tracksAudio),
                EffectChoiceRow("Subtitles", _tracksPrimary),
                EffectChoiceRow("Second subtitle", _tracksSecondary),
                add, EffectsHeading("Subtitle synchronisation"),
                _tracksDelay, delayButtons, exact, _tracksSyncMarks, marks,
            ]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 11;
            stack.EdgeInsets = new NSEdgeInsets(20, 24, 20, 24);
            stack.TranslatesAutoresizingMaskIntoConstraints = false;
            var root = new NSView(new CGRect(0, 0, 600, 490));
            root.AddSubview(stack);
            stack.TopAnchor.ConstraintEqualTo(root.TopAnchor).Active = true;
            stack.BottomAnchor.ConstraintEqualTo(root.BottomAnchor).Active = true;
            stack.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor).Active = true;
            stack.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor).Active = true;
            info.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _tracksSyncMarks.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _tracksWindow.ContentView = root;
            _tracksWindow.Center();
        }

        RefreshTrackTools();
        _tracksWindow.MakeKeyAndOrderFront(null);
    }

    private void RefreshTrackTools()
    {
        if (_tracksWindow is null || _tracksAudio is null ||
            _tracksPrimary is null || _tracksSecondary is null)
        {
            return;
        }

        _tracksUpdating = true;
        _tracksAudio.RemoveAllItems();
        foreach (var track in _player.AudioTracks)
        {
            _tracksAudio.AddItem(track.Title ?? track.Language ?? track.Codec.DisplayName());
        }

        var selectedAudio = _player.AudioTracks.ToList().FindIndex(track => track.Id == _player.AudioTrack);
        if (selectedAudio >= 0)
        {
            _tracksAudio.SelectItem(selectedAudio);
        }

        _tracksAudio.Enabled = _player.AudioTracks.Count > 0;
        foreach (var picker in new[] { _tracksPrimary, _tracksSecondary })
        {
            picker.RemoveAllItems();
            picker.AddItem("Off");
            foreach (var track in _player.SubtitleTracks)
            {
                picker.AddItem(track.Name);
            }
        }

        var primary = _player.SubtitleTracks.ToList().IndexOf(_player.Subtitles!);
        var secondary = _player.SubtitleTracks.ToList().IndexOf(_player.SecondarySubtitles!);
        _tracksPrimary.SelectItem(primary + 1);
        _tracksSecondary.SelectItem(secondary + 1);
        _tracksDelay!.StringValue = string.Create(CultureInfo.InvariantCulture,
            $"Subtitle delay: {_player.SubtitleDelay.TotalMilliseconds:+0;-0;0} ms");
        _tracksExactDelay!.StringValue = _player.SubtitleDelay.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture);
        _tracksUpdating = false;
    }

    private void ApplyExactSubtitleDelay()
    {
        if (double.TryParse(_tracksExactDelay?.StringValue,
            NumberStyles.Float, CultureInfo.InvariantCulture, out var delay) &&
            double.IsFinite(delay) && delay is >= -10000 and <= 10000)
        {
            _player.SetSubtitleDelay(TimeSpan.FromMilliseconds(delay));
        }
        else
        {
            Say("Enter a subtitle delay between -10000 and 10000 milliseconds.");
        }
    }

    private void ShowSubtitleSyncMarks()
    {
        if (_tracksSyncMarks is not null)
        {
            _tracksSyncMarks.StringValue =
                "Audio: " + (_audioSyncMark?.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture) ?? "not marked") +
                " · subtitle: " + (_subtitleSyncMark?.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture) ?? "not marked");
        }
    }

    private void AlignSubtitleMarks()
    {
        if (_audioSyncMark is not { } audio || _subtitleSyncMark is not { } subtitle)
        {
            Say("Mark one audio event and one matching subtitle event first.");
            return;
        }

        _player.SetSubtitleDelay(_player.SubtitleDelay + audio - subtitle);
        _audioSyncMark = null;
        _subtitleSyncMark = null;
        ShowSubtitleSyncMarks();
    }

    private void DisposeTrackTools()
    {
        _tracksWindow?.Close();
        _tracksWindow?.Dispose();
        _tracksWindow = null;
    }
}
