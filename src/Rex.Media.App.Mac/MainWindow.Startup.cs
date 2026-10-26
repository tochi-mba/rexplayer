using Rex.Media.AppCore;
using Rex.Media.AppCore.Automation;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>First-run welcome and release notes, with their state in the shared settings.</summary>
public sealed partial class MainWindow
{
    private NSWindow? _firstRunWindow;
    private NSButton? _firstRunMetadata;
    private NSButton? _firstRunUpdates;
    private NSTextField? _firstRunAssociations;
    private NSWindow? _whatsNewWindow;
    private NSButton? _whatsNewDone;

    private static string RunningVersion =>
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    private void ShowStartupGuidance()
    {
        if (!_settings.FirstRunDone)
        {
            ShowFirstRun();
        }
        else if (!string.Equals(_settings.LastSeenVersion, RunningVersion, StringComparison.Ordinal))
        {
            ShowWhatsNew();
        }
    }

    private void ShowFirstRun()
    {
        if (_firstRunWindow is null)
        {
            _firstRunWindow = new NSWindow(new CGRect(0, 0, 620, 520),
                NSWindowStyle.Titled | NSWindowStyle.Closable,
                NSBackingStore.Buffered, false)
            {
                Title = "Welcome to rexplayer",
            };
            _firstRunWindow.ReleaseWhenClosed(false);

            var title = NSTextField.CreateLabel("Welcome to rexplayer");
            title.Font = NSFont.SystemFontOfSize(27, NSFontWeight.Semibold)!;
            var privacy = NSTextField.CreateWrappingLabel(
                "Your media stays on your Mac. Watching folders is opt-in; playback history " +
                "and resume points can be cleared or switched off in Preferences.");
            privacy.TextColor = NSColor.SecondaryLabel;
            _firstRunMetadata = NSButton.CreateCheckbox(
                "Look up posters, episode details and album art online", () => { });
            _firstRunMetadata.State = NSCellStateValue.Off;
            _firstRunUpdates = NSButton.CreateCheckbox(
                "Check daily for rexplayer updates", () => { });
            _firstRunUpdates.State = NSCellStateValue.On;
            _firstRunAssociations = NSTextField.CreateWrappingLabel(
                "File associations: rexplayer is offered in Finder's Open With menu for supported " +
                "media and playlists. Choose Always Open there if you want it as the default.");
            _firstRunAssociations.TextColor = NSColor.SecondaryLabel;
            var guidance = NSTextField.CreateWrappingLabel(
                "Quick tour\n1. Drop a file here or press Cmd+O.\n" +
                "2. Open Library to add an optional watched folder.\n" +
                "3. Use the rexplayer menu for subtitles, effects, playback tools and shortcuts.");
            guidance.TextColor = NSColor.SecondaryLabel;
            var start = NSButton.CreateButton("Start using rexplayer", FinishFirstRun);
            var open = NSButton.CreateButton("Choose a file…", () =>
            {
                FinishFirstRun();
                OpenWithPanel(folders: false);
            });
            var browse = NSButton.CreateButton("Browse library", () =>
            {
                FinishFirstRun();
                ShowLibrary();
            });
            var actions = NSStackView.FromViews([start, open, browse]);
            actions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            actions.Spacing = 8;
            var stack = NSStackView.FromViews([
                title, privacy, _firstRunMetadata, _firstRunUpdates,
                _firstRunAssociations, guidance, actions,
            ]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 20;
            stack.EdgeInsets = new NSEdgeInsets(24, 28, 24, 28);
            stack.TranslatesAutoresizingMaskIntoConstraints = false;
            var root = new NSView(new CGRect(0, 0, 550, 380));
            root.AddSubview(stack);
            stack.TopAnchor.ConstraintEqualTo(root.TopAnchor).Active = true;
            stack.BottomAnchor.ConstraintEqualTo(root.BottomAnchor).Active = true;
            stack.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor).Active = true;
            stack.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor).Active = true;
            privacy.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -56).Active = true;
            _firstRunAssociations.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -56).Active = true;
            guidance.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -56).Active = true;
            _firstRunWindow.ContentView = root;
            _firstRunWindow.Center();
        }

        _firstRunWindow.MakeKeyAndOrderFront(null);
    }

    private void FinishFirstRun()
    {
        var metadata = IsChecked(_firstRunMetadata);
        var candidate = _settings with
        {
            Metadata = _settings.Metadata with
            {
                TvShows = metadata,
                Movies = metadata,
                Music = metadata,
            },
            UpdateChecks = IsChecked(_firstRunUpdates) ? UpdateCadence.Daily : UpdateCadence.Off,
        };
        SaveSeenVersion(candidate, firstRun: true);
        _firstRunWindow?.OrderOut(null);
    }

    private void ShowWhatsNew()
    {
        if (_whatsNewWindow is null)
        {
            _whatsNewWindow = new NSWindow(new CGRect(0, 0, 660, 540),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "What's new in rexplayer",
                MinSize = new CGSize(510, 390),
            };
            _whatsNewWindow.ReleaseWhenClosed(false);
            var title = NSTextField.CreateLabel("What's new in rexplayer " + RunningVersion);
            title.Font = NSFont.SystemFontOfSize(22, NSFontWeight.Semibold)!;
            var changes = new NSTextView(new CGRect(0, 0, 600, 350))
            {
                Editable = false,
                Selectable = true,
                Font = NSFont.SystemFontOfSize(13, NSFontWeight.Regular)!,
            };
            changes.Value = LoadReleaseNotes();
            var scroll = new NSScrollView(new CGRect(0, 0, 600, 350))
            {
                HasVerticalScroller = true,
                DocumentView = changes,
            };
            scroll.HeightAnchor.ConstraintGreaterThanOrEqualTo(230).Active = true;
            var site = NSButton.CreateButton("Release notes online", () =>
                NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(HelpUrl)));
            var done = NSButton.CreateButton("Done", () =>
            {
                SaveSeenVersion(_settings, firstRun: false);
                _whatsNewWindow?.OrderOut(null);
            });
            _whatsNewDone = done;
            var actions = NSStackView.FromViews([site, done]);
            actions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            actions.Spacing = 8;
            var stack = NSStackView.FromViews([title, scroll, actions]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 14;
            stack.EdgeInsets = new NSEdgeInsets(20, 24, 20, 24);
            scroll.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _whatsNewWindow.ContentView = stack;
            _whatsNewWindow.Center();
        }

        _whatsNewWindow.MakeKeyAndOrderFront(null);
    }

    private static string LoadReleaseNotes()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
        try
        {
            if (File.Exists(file))
            {
                var markdown = File.ReadAllText(file);
                return Changelog.Section(markdown, RunningVersion)
                    ?? "Release notes for this version are not available in this build.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Release notes could not be read from this app: " + ex.Message;
        }

        return "Release notes were not included in this app bundle.";
    }

    private void SaveSeenVersion(PlayerSettings settings, bool firstRun)
    {
        var candidate = settings with
        {
            FirstRunDone = firstRun || _settings.FirstRunDone,
            LastSeenVersion = RunningVersion,
        };
        try
        {
            SettingsStore.Save(_roots.SettingsPath, candidate);
            _settings = candidate;
            _player.Settings = candidate;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "Could not remember the introduction: " + ex.Message);
            Say("The welcome setting could not be saved.");
        }
    }

    private void DisposeStartupGuidance()
    {
        _firstRunWindow?.Close();
        _firstRunWindow?.Dispose();
        _firstRunWindow = null;
        _whatsNewWindow?.Close();
        _whatsNewWindow?.Dispose();
        _whatsNewWindow = null;
    }

    private AutomationReply StartupGuidanceSmokeAction(string? action)
    {
        switch (action)
        {
            case "show-first-run":
                ShowFirstRun();
                break;
            case "enable-metadata":
                _firstRunMetadata!.State = NSCellStateValue.On;
                break;
            case "disable-updates":
                _firstRunUpdates!.State = NSCellStateValue.Off;
                break;
            case "finish-first-run":
                FinishFirstRun();
                break;
            case "done-whats-new":
                _whatsNewDone?.PerformClick(_whatsNewDone);
                break;
            case "check-again":
                ShowStartupGuidance();
                break;
            case "status":
                break;
            default:
                return AutomationReply.Failed("Unknown startup-guidance action: " + action);
        }

        return new AutomationReply
        {
            Ok = true,
            Text = $"first={_firstRunWindow?.IsVisible == true};whats={_whatsNewWindow?.IsVisible == true};" +
                $"metadata={_settings.Metadata.TvShows || _settings.Metadata.Movies || _settings.Metadata.Music};" +
                $"metadataChoice={IsChecked(_firstRunMetadata)};updates={_settings.UpdateChecks};" +
                $"updateChoice={IsChecked(_firstRunUpdates)};associations={_firstRunAssociations is not null};" +
                $"seen={_settings.LastSeenVersion}",
        };
    }
}
