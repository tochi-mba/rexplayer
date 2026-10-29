namespace Rex.Media.App.Mac;

/// <summary>
/// Stable accessibility identity for native, dynamically created AppKit controls.
/// macOS uses accessibilityIdentifier and accessibilityLabel instead of UIA AutomationId
/// and Name. Run this after panels are created and again when a library/queue changes.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>For row controls without a tooltip, use the visible sibling label.</summary>
    private static string? NearbyMacLabel(NSView node)
    {
        if (node.Superview is not { } parent) return null;
        foreach (var sibling in parent.Subviews)
        {
            if (ReferenceEquals(sibling, node)) break;
            if (sibling is NSTextField { Editable: false } label &&
                !string.IsNullOrWhiteSpace(label.StringValue))
                return label.StringValue;
        }
        return null;
    }

    private (int Controls, int Missing) NameMacControls()
    {
        var controls = 0;
        var missing = 0;
        NSWindow?[] windows = [
            _window, _libraryWindow, _playbackTools, _preferencesWindow,
            _mediaInformationWindow, _musicVisualizerWindow, _macCaptionSettings,
            _logWindow, _savedPlaylistsWindow, _shortcutsWindow, _firstRunWindow,
            _whatsNewWindow, _queueWindow, _queueTimingWindow, _effectsWindow,
            _historyWindow, _subtitleStyleWindow, _tracksWindow, _macSpeechWindow,
            _updatesWindow,
        ];
        foreach (var window in windows.OfType<NSWindow>())
        {
            if (window is null || window.ContentView is not { } root) continue;
            var identifier = new string(window.Title.Select(c =>
                char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-');
            var ordinal = 0;
            Visit(root);
            void Visit(NSView node)
            {
                var isControl = node is NSButton or NSSlider or NSPopUpButton or NSSearchField ||
                    node is NSTextField { Editable: true } or NSTextView { Editable: true };
                if (isControl)
                {
                    controls++;
                    var slot = ++ordinal;
                    if (string.IsNullOrWhiteSpace(node.AccessibilityIdentifier))
                        node.AccessibilityIdentifier = "rexplayer." + identifier + "." +
                            node.GetType().Name.ToLowerInvariant() + "." + slot;
                    if (string.IsNullOrWhiteSpace(node.AccessibilityLabel))
                    {
                        var name = node switch
                        {
                            NSPopUpButton popup => popup.ToolTip ?? NearbyMacLabel(node),
                            NSButton button => button.Title,
                            NSSearchField search => search.PlaceholderString ?? "Search media",
                            NSTextField input => input.ToolTip ?? NearbyMacLabel(node) ?? input.PlaceholderString,
                            NSTextView editor => editor.ToolTip ?? NearbyMacLabel(node),
                            NSControl control => control.ToolTip ?? NearbyMacLabel(node),
                            _ => node.ToolTip ?? NearbyMacLabel(node),
                        };
                        if (!string.IsNullOrWhiteSpace(name))
                            node.AccessibilityLabel = name;
                    }

                    if (string.IsNullOrWhiteSpace(node.AccessibilityLabel))
                    {
                        missing++;
                        _log.Warning(LogSource, "Unnamed Mac control: " + node.AccessibilityIdentifier +
                            "; window=" + window.Title + "; type=" + node.GetType().Name +
                            "; tooltip=" + node.ToolTip);
                    }
                    if (string.IsNullOrWhiteSpace(node.AccessibilityHelp) &&
                        !string.IsNullOrWhiteSpace(node.ToolTip))
                        node.AccessibilityHelp = node.ToolTip;
                }

                foreach (var child in node.Subviews) Visit(child);
            }
        }

        return (controls, missing);
    }
}
