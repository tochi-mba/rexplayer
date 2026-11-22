using System.Globalization;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Design;
using Rex.Media.Primitives;

namespace Rex.Media.App.Mac;

/// <summary>Native primary/secondary subtitle, audio-track and sync controls.</summary>
public sealed partial class MainWindow
{
    private AutomationReply SubtitleSmokeAction(string? action)
    {
        if (action == "select-secondary")
        {
            var second = _player.SubtitleTracks.FirstOrDefault(track =>
                !ReferenceEquals(track, _player.Subtitles));
            if (second is null)
            {
                return AutomationReply.Failed("Two subtitle tracks are required.");
            }

            _player.ShowSecondarySubtitles(second);
            RefreshTrackTools();
            UpdateSubtitleOverlays();
        }
        else if (action is not null and not "status")
        {
            return AutomationReply.Failed("Unknown Mac subtitle action: " + action);
        }

        return new AutomationReply
        {
            Ok = true,
            Text = $"tracks={_player.SubtitleTracks.Count};" +
                $"primary={_player.Subtitles?.Name ?? "off"};" +
                $"secondary={_player.SecondarySubtitles?.Name ?? "off"};" +
                $"primaryPlacement={_primaryPlacement};" +
                $"secondaryPlacement={_secondaryPlacement}",
        };
    }

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
            _tracksWindow = MacToolViews.ToolWindow("Tracks & subtitle synchronisation - rexplayer", 600, 600, 520, 420);
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
            var add = MacTheme.TextButton("Add subtitle file…", () =>
            {
                AddSubtitleWithPanel();
                RefreshTrackTools();
            }, RexButtonLook.Accent, "plus");
            _tracksDelay = MacTheme.Label("Subtitles in sync");
            _tracksDelay.Font = MacTheme.DigitFont(2);
            _tracksExactDelay = new NSTextField(new CGRect(0, 0, 140, 26))
            {
                PlaceholderString = "Milliseconds",
                ToolTip = "An exact subtitle offset between -10000 and 10000 milliseconds",
            };
            _tracksExactDelay.WidthAnchor.ConstraintEqualTo(120).Active = true;
            ((NSView)_tracksExactDelay).AccessibilityLabel = "Exact subtitle delay in milliseconds";
            var earlier = MacTheme.TextButton("Earlier 50 ms", () =>
                _player.SetSubtitleDelay(_player.SubtitleDelay - TimeSpan.FromMilliseconds(50)));
            var later = MacTheme.TextButton("Later 50 ms", () =>
                _player.SetSubtitleDelay(_player.SubtitleDelay + TimeSpan.FromMilliseconds(50)));
            var reset = MacTheme.TextButton("Reset delay", () => _player.SetSubtitleDelay(TimeSpan.Zero));
            var set = MacTheme.TextButton("Set exact delay", ApplyExactSubtitleDelay);
            _tracksSyncMarks = MacTheme.WrappingLabel(
                "Mark the audio and subtitle events you want aligned, then apply their difference.", 1, DesignRole.TextSecondary);
            var markAudio = MacTheme.TextButton("Mark audio event", () =>
            {
                _audioSyncMark = _player.Position;
                ShowSubtitleSyncMarks();
            });
            var markSubtitle = MacTheme.TextButton("Mark subtitle event", () =>
            {
                _subtitleSyncMark = _player.Position;
                ShowSubtitleSyncMarks();
            });
            var sync = MacTheme.TextButton("Align marked events", AlignSubtitleMarks);
            var page = MacToolViews.ToolPage("Tracks & subtitles",
                "Select an audio track and up to two simultaneous subtitle tracks. " +
                "Move subtitles earlier or later to match the soundtrack.",
                MacToolViews.ToolCard("Audio",
                    MacToolViews.ToolRow("Audio track", null, _tracksAudio)),
                MacToolViews.ToolCard("Subtitles",
                    MacToolViews.ToolRow("Subtitles", null, _tracksPrimary),
                    MacToolViews.ToolRow("Second subtitle", null, _tracksSecondary),
                    MacToolViews.ToolButtons(add)),
                MacToolViews.ToolCard("Subtitle synchronisation",
                    _tracksDelay,
                    MacToolViews.ToolButtons(earlier, later, reset),
                    MacToolViews.ToolRow("Exact delay", "Between -10000 and 10000 milliseconds",
                        MacToolViews.ToolButtons(_tracksExactDelay, set)),
                    MacTheme.Separator(),
                    _tracksSyncMarks,
                    MacToolViews.ToolButtons(markAudio, markSubtitle, sync)));
            MacToolViews.Host(_tracksWindow, page, scrolls: true);
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
