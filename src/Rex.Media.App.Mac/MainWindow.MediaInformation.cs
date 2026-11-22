using Rex.Media.AppCore.Design;
using Rex.Media.Primitives;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App.Mac;

/// <summary>Inspect the current media's tracks and metadata without interrupting playback.</summary>
public sealed partial class MainWindow
{
    private NSWindow? _mediaInformationWindow;
    private MacToolTabs? _mediaInformationTabs;
    private NSImageView? _mediaInformationArt;

    /// <summary>
    /// The facts Windows lists in its media information, sorted into what the file is, the tags it
    /// carries and how each track is coded, as label and value that can be selected and copied.
    /// </summary>
    private void ShowMediaInformation()
    {
        if (_player.Info is not { } info)
        {
            Say("Open something to see its media information.");
            return;
        }

        var general = new List<NSView>
        {
            MacToolViews.ToolValue("Title", _player.Title),
            MacToolViews.ToolValue("Location", _player.Item?.Location ?? ""),
            MacToolViews.ToolValue("Format", info.FormatName),
            MacToolViews.ToolValue("Length", _player.Duration > TimeSpan.Zero ? TimeText.Format(_player.Duration) : "unknown"),
        };
        if (MediaInformationArt() is { } art)
        {
            general.Insert(0, art);
        }

        var codecs = info.Tracks.Select(track =>
        {
            var detail = track.Video is { } video ? $"{video.Width}×{video.Height}"
                : track.Audio is { } audio ? $"{audio.SampleRate} Hz, {audio.Channels} channel(s)"
                : "";
            return (NSView)MacToolViews.ToolValue($"Track {track.Id}",
                $"{track.Kind} {track.Codec.DisplayName()} {detail}".TrimEnd(), monospaced: true);
        }).ToArray();
        var metadata = info.Metadata.Select(entry => (NSView)MacToolViews.ToolValue(entry.Key, entry.Value)).ToArray();
        var chosen = _mediaInformationTabs?.Selected ?? 0;
        _mediaInformationTabs = MacToolViews.ToolTabs("Media information section", ["General", "Metadata", "Codec"],
        [
            MacToolViews.ToolCard(null, [.. general]),
            metadata.Length == 0
                ? MacToolViews.ToolCard(null, MacTheme.Label("This file carries no metadata.", 2, DesignRole.TextSecondary))
                : MacToolViews.ToolCard(null, metadata),
            codecs.Length == 0
                ? MacToolViews.ToolCard(null, MacTheme.Label("This file has no tracks.", 2, DesignRole.TextSecondary))
                : MacToolViews.ToolCard(null, codecs),
        ]);
        _mediaInformationTabs.Show(chosen);
        if (_mediaInformationWindow is null)
        {
            _mediaInformationWindow = MacToolViews.ToolWindow("Media information - rexplayer", 640, 520, 460, 320);
            _mediaInformationWindow.Center();
        }

        MacToolViews.Host(_mediaInformationWindow, MacToolViews.ToolPage("Media information", null, _mediaInformationTabs), scrolls: true);
        _mediaInformationWindow.MakeKeyAndOrderFront(null);
    }

    /// <summary>The cover the media carries, rounded as a card is, in a row of its own; null when it has none or it cannot be read.</summary>
    private NSStackView? MediaInformationArt()
    {
        _mediaInformationArt?.Image?.Dispose();
        _mediaInformationArt = null;
        if (_player.CoverArt is not { Length: > 0 } bytes)
        {
            return null;
        }

        try
        {
            using var data = NSData.FromArray(bytes);
            var image = new NSImage(data);
            if (!image.IsValid)
            {
                image.Dispose();
                return null;
            }

            _mediaInformationArt = new NSImageView
            {
                Image = image,
                ImageScaling = NSImageScale.ProportionallyUpOrDown,
                WantsLayer = true,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            _mediaInformationArt.Layer!.CornerRadius = MacTheme.Radius(2);
            _mediaInformationArt.Layer.MasksToBounds = true;
            _mediaInformationArt.WidthAnchor.ConstraintEqualTo(120).Active = true;
            _mediaInformationArt.HeightAnchor.ConstraintEqualTo(120).Active = true;
            ((NSView)_mediaInformationArt).AccessibilityLabel = "Cover art";
            return MacTheme.Stack(false, 0, _mediaInformationArt);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _log.Warning(LogSource, "Media information artwork could not be decoded: " + ex.Message);
            return null;
        }
    }

    private void DisposeMediaInformation()
    {
        _mediaInformationWindow?.Close();
        _mediaInformationWindow?.Dispose();
        _mediaInformationWindow = null;
    }
}
