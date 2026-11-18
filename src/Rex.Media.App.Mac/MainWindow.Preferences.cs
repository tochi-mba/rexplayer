using System.Globalization;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Shell;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>
/// The native Mac surface for the shared settings this shell honours (UI-10), framed as the Windows
/// dialog is: the search above a rail of sections, the page of the section chosen beside them, and
/// nothing applied until Save. The search filters the rail to the sections that say what was typed
/// and shows the one <see cref="SettingsSearch"/> chooses.
/// </summary>
public sealed partial class MainWindow
{
    private NSWindow? _preferencesWindow;
    private LazySections<NSStackView, PlayerSettings>? _preferencesSections;
    private NSSearchField? _preferencesSearch;
    private NSTableView? _preferencesRail;
    private PreferenceSectionRows? _preferencesRailRows;
    private NSScrollView? _preferencesPage;
    private NSTextField? _preferencesSearchEmpty;
    private int _preferencesShown;
    private bool _preferencesRailUpdating;
    private NSButton? _preferencesHistory;
    private NSButton? _preferencesRestoreQueue;
    private NSButton? _preferencesEnqueue;
    private NSPopUpButton? _preferencesResumeMode;
    private NSPopUpButton? _preferencesTheme;
    private NSButton? _preferencesSingleInstance;
    private NSButton? _preferencesHardwareDecoding;
    private NSButton? _preferencesH264Hardware;
    private NSButton? _preferencesHevcHardware;
    private NSSlider? _preferencesMaxVolume;
    private NSSlider? _preferencesPictureSeconds;
    private NSTextField? _preferencesMaxVolumeValue;
    private NSTextField? _preferencesPictureSecondsValue;
    private NSTextField? _preferencesSnapshotPattern;
    private NSButton? _preferencesSnapshotSequential;
    private NSButton? _preferencesOsd;
    private NSSlider? _preferencesOsdSeconds;
    private NSTextField? _preferencesOsdSecondsValue;
    private NSSlider? _preferencesTitleSeconds;
    private NSTextField? _preferencesTitleSecondsValue;
    private NSPopUpButton? _preferencesOsdPosition;
    private NSButton? _preferencesCrashDumps;

    /// <summary>Shows the native Mac surface for the shared settings used by this shell.</summary>
    private void ShowPreferences()
    {
        BuildPreferences();
        _preferencesWindow!.MakeKeyAndOrderFront(null);
    }

    /// <summary>
    /// Builds the window's content afresh each time it opens, as the Windows dialog does, so every
    /// control starts from what is saved. Only the section shown is built; the others are built
    /// when first chosen (or searched).
    /// </summary>
    private void BuildPreferences()
    {
        if (_preferencesWindow is null)
        {
            _preferencesWindow = new NSWindow(new CGRect(0, 0, 832, 600),
                NSWindowStyle.Titled | NSWindowStyle.Closable, NSBackingStore.Buffered, false)
            {
                Title = "Preferences - rexplayer",
            };
            _preferencesWindow.ReleaseWhenClosed(false);
            _preferencesWindow.Center();
        }

        _preferencesSections = new LazySections<NSStackView, PlayerSettings>
        {
            // Every page keeps the shared width, so its rows line up across sections.
            Built = page => page.Frame = new CGRect(0, 0, PreferencesLayout.PageWidth, page.FittingSize.Height),
        };
        AddMacPreferenceSections(_preferencesSections);
        _preferencesShown = 0;

        var heading = NSTextField.CreateLabel("Preferences");
        heading.Font = NSFont.SystemFontOfSize(24, NSFontWeight.Semibold)!;
        var explanation = NSTextField.CreateWrappingLabel(
            "These choices are shared with the player and saved for the next launch. Changes in every section apply when you select Save.");
        explanation.TextColor = NSColor.SecondaryLabel;

        _preferencesSearch = new NSSearchField(new CGRect(0, 0, PreferencesLayout.RailWidth, 28))
        {
            PlaceholderString = "Search preferences",
        };
        _preferencesSearch.Changed += (_, _) => SearchPreferences();
        _preferencesSearch.ToolTip = "Type to find a setting; the rail keeps the sections that match.";
        _preferencesSearch.AccessibilityLabel = "Search preferences";

        _preferencesRailRows = new PreferenceSectionRows(this);
        _preferencesRailRows.Rows.AddRange(_preferencesSections.Names);
        _preferencesRail = new NSTableView(new CGRect(0, 0, PreferencesLayout.RailWidth, 400))
        {
            HeaderView = null,
            RowHeight = 26,
            AllowsMultipleSelection = false,
        };
        _preferencesRail.AddColumn(new NSTableColumn("section") { Width = (nfloat)(PreferencesLayout.RailWidth - 24) });
        _preferencesRail.Source = _preferencesRailRows;
        ((NSView)_preferencesRail).AccessibilityLabel = "Preferences sections";
        var railScroll = new NSScrollView(new CGRect(0, 0, PreferencesLayout.RailWidth, 400))
        {
            HasVerticalScroller = true,
            DocumentView = _preferencesRail,
        };
        railScroll.WidthAnchor.ConstraintEqualTo((nfloat)PreferencesLayout.RailWidth).Active = true;
        railScroll.HeightAnchor.ConstraintGreaterThanOrEqualTo(380).Active = true;

        // Nothing found: said in the page's place, with the way back.
        _preferencesSearchEmpty = NSTextField.CreateWrappingLabel("");
        _preferencesSearchEmpty.Hidden = true;
        _preferencesPage = new NSScrollView(new CGRect(0, 0, PreferencesLayout.PageWidth, 420))
        {
            HasVerticalScroller = true,
        };
        _preferencesPage.WidthAnchor.ConstraintEqualTo((nfloat)PreferencesLayout.PageWidth).Active = true;
        _preferencesPage.HeightAnchor.ConstraintGreaterThanOrEqualTo(380).Active = true;
        var pageArea = NSStackView.FromViews([_preferencesSearchEmpty, _preferencesPage]);
        pageArea.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        pageArea.Alignment = NSLayoutAttribute.Leading;
        pageArea.Spacing = 8;
        _preferencesSearchEmpty.WidthAnchor.ConstraintEqualTo((nfloat)PreferencesLayout.PageWidth).Active = true;

        var rail = NSStackView.FromViews([_preferencesSearch, railScroll]);
        rail.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        rail.Alignment = NSLayoutAttribute.Leading;
        rail.Spacing = 8;
        _preferencesSearch.WidthAnchor.ConstraintEqualTo((nfloat)PreferencesLayout.RailWidth).Active = true;

        var columns = NSStackView.FromViews([rail, pageArea]);
        columns.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        columns.Alignment = NSLayoutAttribute.Top;
        columns.Spacing = (nfloat)PreferencesLayout.Gap;

        var cancel = NSButton.CreateButton("Cancel", () => _preferencesWindow.OrderOut(null));
        var save = NSButton.CreateButton("Save", SavePreferences);
        save.KeyEquivalent = "\r";
        var actions = NSStackView.FromViews([cancel, save]);
        actions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        actions.Spacing = 10;

        var stack = NSStackView.FromViews([heading, explanation, columns, actions]);
        stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        stack.Alignment = NSLayoutAttribute.Leading;
        stack.Spacing = 14;
        stack.EdgeInsets = new NSEdgeInsets(24, 24, 24, 24);
        stack.TranslatesAutoresizingMaskIntoConstraints = false;
        var root = new NSView(new CGRect(0, 0, 832, 600));
        root.AddSubview(stack);
        stack.TopAnchor.ConstraintEqualTo(root.TopAnchor).Active = true;
        stack.BottomAnchor.ConstraintEqualTo(root.BottomAnchor).Active = true;
        stack.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor).Active = true;
        stack.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor).Active = true;
        explanation.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
        _preferencesWindow.ContentView = root;
        ShowPreferenceSection(0);
    }

    /// <summary>Shows a section's page, from its top, and keeps the rail's selection on it.</summary>
    private void ShowPreferenceSection(int index)
    {
        if (_preferencesSections is null || _preferencesPage is null || _preferencesRailRows is null)
        {
            return;
        }

        _preferencesShown = index;
        if (!_preferencesRailRows.Rows.Contains(_preferencesSections.Names[index]) && _preferencesSearch is not null)
        {
            // A section chosen outside a search's matches ends the search, so the rail always holds the section shown.
            _preferencesSearch.StringValue = "";
            SearchPreferences();
            return;
        }

        var page = _preferencesSections.Page(index);
        _preferencesPage.Hidden = false;
        _preferencesSearchEmpty!.Hidden = true;
        _preferencesPage.DocumentView = page;
        // AppKit's origin is the bottom, so the top of a page taller than the view is at its far end.
        _preferencesPage.ContentView.ScrollToPoint(new CGPoint(0, Math.Max(0, page.Frame.Height - _preferencesPage.ContentSize.Height)));
        _preferencesPage.ReflectScrolledClipView(_preferencesPage.ContentView);
        SelectPreferenceRailRow();
    }

    private void SelectPreferenceRailRow()
    {
        if (_preferencesRail is null || _preferencesRailRows is null || _preferencesSections is null)
        {
            return;
        }

        var row = _preferencesRailRows.Rows.IndexOf(_preferencesSections.Names[_preferencesShown]);
        _preferencesRailUpdating = true;
        if (row >= 0)
        {
            _preferencesRail.SelectRow(row, byExtendingSelection: false);
            _preferencesRail.ScrollRowToVisible(row);
        }
        else
        {
            _preferencesRail.DeselectAll(null);
        }

        _preferencesRailUpdating = false;
    }

    /// <summary>The rail's answer to a click or an arrow key: show the section of the row chosen.</summary>
    private void PreferencesRailSelected()
    {
        if (_preferencesRailUpdating || _preferencesRail is null || _preferencesRailRows is null || _preferencesSections is null)
        {
            return;
        }

        var row = (int)_preferencesRail.SelectedRow;
        if (row >= 0 && row < _preferencesRailRows.Rows.Count
            && _preferencesSections.IndexOf(_preferencesRailRows.Rows[row]) is var index and >= 0 && index != _preferencesShown)
        {
            ShowPreferenceSection(index);
        }
    }

    /// <summary>
    /// Filters the rail to the sections that say what was typed - their names, their labels and
    /// choices, and their tooltips - and shows the section <see cref="SettingsSearch"/> chooses,
    /// building pages not yet shown; the page's place says so when nothing matches.
    /// </summary>
    private IReadOnlyList<string> SearchPreferences()
    {
        if (_preferencesSections is null || _preferencesSearch is null || _preferencesRailRows is null)
        {
            return [];
        }

        var query = _preferencesSearch.StringValue.Trim();
        var names = _preferencesSections.Names;
        List<string> matching = query.Length == 0
            ? [.. names]
            : [.. _preferencesSections.All()
                .Where(section => SettingsSearch.Says(section.Name, query) || SectionSays(section.Page, query))
                .Select(section => section.Name)];
        _preferencesRailRows.Rows.Clear();
        _preferencesRailRows.Rows.AddRange(matching);
        _preferencesRailUpdating = true;
        _preferencesRail!.ReloadData();
        _preferencesRailUpdating = false;
        var chosen = SettingsSearch.Section(names, matching, names[_preferencesShown], query);
        if (chosen is null)
        {
            _preferencesSearchEmpty!.StringValue =
                $"Nothing in Preferences matches \"{query}\". Clear the search to see every section.";
            _preferencesSearchEmpty.Hidden = false;
            _preferencesPage!.Hidden = true;
            _preferencesRail.DeselectAll(null);
            return matching;
        }

        ShowPreferenceSection(_preferencesSections.IndexOf(chosen));
        return matching;
    }

    /// <summary>Whether anything in a page's tree says the query: a label, a button or a choice's title, a tooltip or a placeholder.</summary>
    private static bool SectionSays(NSView view, string query) =>
        view switch
        {
            NSPopUpButton popup => popup.Items().Any(item => SettingsSearch.Says(item.Title, query)),
            NSButton button => SettingsSearch.Says(button.Title, query),
            NSTextField field => SettingsSearch.Says(field.StringValue, query) || SettingsSearch.Says(field.PlaceholderString, query),
            _ => false,
        }
        || SettingsSearch.Says(view.ToolTip, query)
        || view.Subviews.Any(child => SectionSays(child, query));

    private static NSButton CheckBox(string title) => NSButton.CreateCheckbox(title, () => { });

    private static NSSlider Slider(double minimum, double maximum, Action changed)
    {
        var slider = NSSlider.FromValue(minimum, minimum, maximum, changed);
        slider.Continuous = true;
        slider.WidthAnchor.ConstraintGreaterThanOrEqualTo(250).Active = true;
        return slider;
    }

    private static NSStackView PreferenceRow(string title, NSSlider slider, NSTextField value)
    {
        var label = NSTextField.CreateLabel(title);
        label.WidthAnchor.ConstraintEqualTo(150).Active = true;
        value.WidthAnchor.ConstraintEqualTo(74).Active = true;
        var row = NSStackView.FromViews([label, slider, value]);
        row.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        row.Spacing = 10;
        return row;
    }

    /// <summary>Each slider built says its value beside it; a section not yet built has nothing to say.</summary>
    private void UpdatePreferenceLabels()
    {
        if (_preferencesMaxVolume is not null)
        {
            _preferencesMaxVolumeValue!.StringValue = $"{Math.Round(_preferencesMaxVolume.DoubleValue):0} %";
        }

        if (_preferencesPictureSeconds is not null)
        {
            _preferencesPictureSecondsValue!.StringValue = $"{Math.Round(_preferencesPictureSeconds.DoubleValue):0} sec";
        }

        if (_preferencesOsdSeconds is not null)
        {
            _preferencesOsdSecondsValue!.StringValue = $"{_preferencesOsdSeconds.DoubleValue:0.0} sec";
        }

        if (_preferencesTitleSeconds is not null)
        {
            _preferencesTitleSecondsValue!.StringValue = $"{_preferencesTitleSeconds.DoubleValue:0.0} sec";
        }
    }

    /// <summary>
    /// Folds the edits of every section that was shown into the settings - one never shown changes
    /// nothing - normalizes them, and saves them for the player and the next launch.
    /// </summary>
    private void SavePreferences()
    {
        _settings = _preferencesSections!.Apply(_settings).Normalize();
        _player.Settings = _settings;
        AppHost.ConfigureCrashDumps(_settings.LocalCrashDumps);
        ApplyMacTheme();
        ApplyMacMessagePosition();
        _volume.MaxValue = _settings.MaxVolumePercent / 100.0;
        _player.SetVolume(Math.Min(_player.Volume, _volume.MaxValue));
        try
        {
            SettingsStore.Save(_roots.SettingsPath, _settings);
            Say("Preferences saved");
            _preferencesWindow!.OrderOut(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogSource, "Preferences could not be saved: " + ex.Message);
            Say("Preferences could not be saved.");
        }
    }

    private void ApplyMacTheme()
    {
        NSApplication.SharedApplication.Appearance = _settings.Theme switch
        {
            ThemeChoice.Light => NSAppearance.GetAppearance(NSAppearance.NameAqua),
            ThemeChoice.Dark => NSAppearance.GetAppearance(NSAppearance.NameDarkAqua),
            _ => null,
        };
    }

    private static NSCellStateValue State(bool value) => value ? NSCellStateValue.On : NSCellStateValue.Off;

    private static bool IsChecked(NSButton? button) => button?.State == NSCellStateValue.On;

    /// <summary>
    /// CI-only Preferences smoke actions (UI-10). The request's id is "&lt;action&gt;" or
    /// "&lt;action&gt;:&lt;argument&gt;": "show" opens the window, "select:&lt;section&gt;" shows
    /// that section, "search:&lt;query&gt;" types the query and answers
    /// "query=...;matches=...;matching=...;selected=..." (selected is "none" when nothing matches),
    /// "set-max-volume:&lt;percent&gt;" moves the Playback slider, and "save" presses Save. Every
    /// other answer is "sections=&lt;comma list&gt;;selected=&lt;name&gt;;maxVolume=&lt;percent&gt;".
    /// </summary>
    private AutomationReply PreferencesSmokeAction(string? action)
    {
        if (Environment.GetEnvironmentVariable("REXPLAYER_MAC_PREFERENCES_TESTS") != "1")
        {
            return AutomationReply.Failed("Set REXPLAYER_MAC_PREFERENCES_TESTS=1 to drive the Mac Preferences window.");
        }

        var separator = action?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        var name = separator < 0 ? action : action![..separator];
        var argument = separator < 0 ? null : action![(separator + 1)..];
        if (name != "show" && (_preferencesWindow is null || _preferencesSections is null))
        {
            return AutomationReply.Failed("Open Preferences first with the show action.");
        }

        switch (name)
        {
            case "show":
                ShowPreferences();
                break;
            case "select" when argument is not null && _preferencesSections!.IndexOf(argument) is var section and >= 0:
                ShowPreferenceSection(section);
                break;
            case "select":
                return AutomationReply.Failed("There is no Preferences section \"" + argument + "\".");
            case "search" when argument is not null && _preferencesSearch is not null:
                _preferencesSearch.StringValue = argument;
                var matching = SearchPreferences();
                var found = _preferencesPage?.Hidden == true ? "none" : _preferencesSections!.Names[_preferencesShown];
                return new AutomationReply
                {
                    Ok = true,
                    Text = $"query={argument};matches={matching.Count};matching={string.Join(",", matching)};selected={found}",
                };
            case "set-max-volume" when _preferencesMaxVolume is not null
                && int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent):
                _preferencesMaxVolume.DoubleValue = percent;
                UpdatePreferenceLabels();
                break;
            case "save":
                SavePreferences();
                break;
            default:
                return AutomationReply.Failed("Unknown Mac preferences action: " + action);
        }

        var maxVolume = _preferencesMaxVolume is { } slider
            ? (int)Math.Round(slider.DoubleValue)
            : _settings.MaxVolumePercent;
        return new AutomationReply
        {
            Ok = true,
            Text = $"sections={string.Join(",", _preferencesSections!.Names)};" +
                $"selected={_preferencesSections.Names[_preferencesShown]};maxVolume={maxVolume}",
        };
    }

    private void DisposePreferences()
    {
        _preferencesWindow?.Close();
        _preferencesWindow?.Dispose();
        _preferencesWindow = null;
        _preferencesSections = null;
    }

    /// <summary>The rail's rows: the section names a search leaves, each drawn as a plain label.</summary>
    private sealed class PreferenceSectionRows : NSTableViewSource
    {
        private readonly MainWindow _owner;

        public PreferenceSectionRows(MainWindow owner) => _owner = owner;

        public List<string> Rows { get; } = [];

        public override nint GetRowCount(NSTableView tableView) => Rows.Count;

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row) =>
            NSTextField.CreateLabel(Rows[(int)row]);

        public override void SelectionDidChange(NSNotification notification) => _owner.PreferencesRailSelected();
    }
}
