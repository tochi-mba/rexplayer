using Rex.Media.AppCore;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Design;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>First-run welcome and release notes, with their state in the shared settings, each laid out as a settings page.</summary>
public sealed partial class MainWindow
{
    private NSWindow? _firstRunWindow;
    private NSSwitch? _firstRunMetadata;
    private NSSwitch? _firstRunUpdates;
    private NSTextField? _firstRunAssociations;
    private NSWindow? _whatsNewWindow;
    private NSButton? _whatsNewDone;

    private static readonly string RunningVersion = ReadRunningVersion();

    /// <summary>
    /// The assembly's version; under the update smoke test (TOOL-09) only, the version the
    /// stand-in release's marker names, since that bundle is this build again and cannot be
    /// rebuilt with another version. A relaunch then counts as the update it was handed.
    /// </summary>
    private static string ReadRunningVersion()
    {
        var built = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        if (Environment.GetEnvironmentVariable("REXPLAYER_MAC_UPDATE_TESTS") != "1"
            || NSBundle.MainBundle.ResourcePath is not { } resources)
        {
            return built;
        }

        var marker = Path.Combine(resources, "rexplayer-update-smoke.txt");
        try
        {
            return File.Exists(marker) && Version.TryParse(File.ReadAllText(marker).Trim(), out var stated)
                ? stated.ToString(3) : built;
        }
        catch (IOException)
        {
            return built;
        }
    }

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

    /// <summary>
    /// The welcome, in the Windows welcome's words and order: how to start, the privacy promise,
    /// then the choices that reach the internet as switches, metadata lookups off and update checks
    /// on, and how file associations work on this Mac. Start is the way on.
    /// </summary>
    private void ShowFirstRun()
    {
        if (_firstRunWindow is null)
        {
            _firstRunWindow = new NSWindow(new CGRect(0, 0, 620, 600),
                NSWindowStyle.Titled | NSWindowStyle.Closable,
                NSBackingStore.Buffered, false)
            {
                Title = "Welcome to rexplayer",
                AutorecalculatesKeyViewLoop = true,
            };
            _firstRunWindow.ReleaseWhenClosed(false);
            MacTheme.Style(_firstRunWindow);

            var help = _keymap.Label(CommandCatalog.Help, ShortcutStyle.Mac) is { Length: > 0 } key
                ? key + " opens the help" : "the Help menu opens the help";
            var page = new SettingsPage("Welcome to rexplayer",
                "Drop media on the window, or press ⌘O to open a file. Every command and its shortcut is in the menus, and " + help + ".");
            var privacy = MacTheme.WrappingLabel(
                "rexplayer needs no account and plays local files offline. Optional metadata lookups and update checks contact online services only when enabled.", 2);
            _firstRunMetadata = MacSettings.Switch(false);
            _firstRunUpdates = MacSettings.Switch(true);
            _firstRunAssociations = MacTheme.WrappingLabel(
                "rexplayer is offered in Finder's Open With menu for supported media and playlists. " +
                "Choose Always Open there if you want it as the default.", 1, DesignRole.TextSecondary);
            var start = MacTheme.TextButton("Start", FinishFirstRun, RexButtonLook.Accent, label: "Start using rexplayer");
            start.KeyEquivalent = "\r";
            var open = MacTheme.TextButton("Choose a file…", () =>
            {
                FinishFirstRun();
                OpenWithPanel(folders: false);
            });
            var browse = MacTheme.TextButton("Browse library", () =>
            {
                FinishFirstRun();
                ShowLibrary();
            });
            var associations = MacTheme.Stack(true, 0, MacSettings.Row("File associations", null, null), _firstRunAssociations);
            associations.EdgeInsets = new NSEdgeInsets(0, 0, MacTheme.Space(3), 0);
            _firstRunAssociations.WidthAnchor.ConstraintEqualTo(associations.WidthAnchor).Active = true;
            page.Add(privacy,
                MacSettings.Card(null,
                    MacSettings.Row("Look up posters, episode details and album art online", "Off until you turn it on.", _firstRunMetadata),
                    MacSettings.Row("Look for new versions every day and download them in the background",
                        "You can turn this off here or later in Software updates.", _firstRunUpdates),
                    associations));
            page.AddFitted(MacSettings.Buttons(start, open, browse));
            var scroll = MacSettings.Scroller();
            _firstRunWindow.ContentView = scroll;
            MacSettings.Show(scroll, page);
            _firstRunWindow.Center();
        }

        _firstRunWindow.MakeKeyAndOrderFront(null);
    }

    private void FinishFirstRun()
    {
        var metadata = MacSettings.IsOn(_firstRunMetadata);
        var candidate = _settings with
        {
            Metadata = _settings.Metadata with
            {
                TvShows = metadata,
                Movies = metadata,
                Music = metadata,
            },
            UpdateChecks = MacSettings.IsOn(_firstRunUpdates) ? UpdateCadence.Daily : UpdateCadence.Off,
        };
        SaveSeenVersion(candidate, firstRun: true);
        _firstRunWindow?.OrderOut(null);
    }

    /// <summary>What changed in this version: the version as a kicker, the notes from the changelog, and Done.</summary>
    private void ShowWhatsNew()
    {
        if (_whatsNewWindow is null)
        {
            _whatsNewWindow = new NSWindow(new CGRect(0, 0, 660, 580),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                NSBackingStore.Buffered, false)
            {
                Title = "What's new in rexplayer",
                MinSize = new CGSize(510, 420),
                AutorecalculatesKeyViewLoop = true,
            };
            _whatsNewWindow.ReleaseWhenClosed(false);
            MacTheme.Style(_whatsNewWindow);
            var page = new SettingsPage("What's new in rexplayer " + RunningVersion);
            page.AddKicker("Version " + RunningVersion);
            var changes = new RexTextView { Text = LoadReleaseNotes() };
            ((NSView)changes).AccessibilityLabel = "What's new";
            var notes = MacTheme.Panel(DesignRole.Surface, 2, hairline: true);
            MacTheme.Fill(notes, changes.InScroller(320), (double)MacTheme.Space(3));
            var site = MacTheme.TextButton("Release notes online", () =>
                NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(HelpUrl)));
            var done = MacTheme.TextButton("Done", () =>
            {
                SaveSeenVersion(_settings, firstRun: false);
                _whatsNewWindow?.OrderOut(null);
            }, RexButtonLook.Accent);
            done.KeyEquivalent = "\r";
            _whatsNewDone = done;
            page.Add(notes);
            page.AddFitted(MacSettings.Buttons(done, site));
            var scroll = MacSettings.Scroller();
            _whatsNewWindow.ContentView = scroll;
            MacSettings.Show(scroll, page);
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
                _firstRunMetadata!.State = 1;
                break;
            case "disable-updates":
                _firstRunUpdates!.State = 0;
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
                $"metadataChoice={MacSettings.IsOn(_firstRunMetadata)};updates={_settings.UpdateChecks};" +
                $"updateChoice={MacSettings.IsOn(_firstRunUpdates)};associations={_firstRunAssociations is not null};" +
                $"seen={_settings.LastSeenVersion}",
        };
    }
}
