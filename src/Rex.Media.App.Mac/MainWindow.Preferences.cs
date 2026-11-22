using System.Globalization;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Design;
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
    /// <summary>The height of a section in the rail, as Windows' navigation items are.</summary>
    private const double PreferencesRailRowHeight = 44;

    private NSWindow? _preferencesWindow;
    private LazySections<NSView, PlayerSettings>? _preferencesSections;
    private NSSearchField? _preferencesSearch;
    private NSTableView? _preferencesRail;
    private PreferenceSectionRows? _preferencesRailRows;
    private NSScrollView? _preferencesPage;
    private NSView? _preferencesSearchEmptyArea;
    private RexLabel? _preferencesSearchEmpty;
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
            _preferencesWindow = new NSWindow(new CGRect(0, 0, PreferencesLayout.WideWidth + 2 * MacTheme.Space(4), 640),
                NSWindowStyle.Titled | NSWindowStyle.Closable, NSBackingStore.Buffered, false)
            {
                Title = "Preferences - rexplayer",
                AutorecalculatesKeyViewLoop = true,
            };
            _preferencesWindow.ReleaseWhenClosed(false);
            MacTheme.Style(_preferencesWindow);
            _preferencesWindow.Center();
        }

        _preferencesSections = new LazySections<NSView, PlayerSettings>();
        AddMacPreferenceSections(_preferencesSections);
        _preferencesShown = 0;

        _preferencesSearch = new NSSearchField
        {
            PlaceholderString = "Search Preferences",
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        _preferencesSearch.Changed += (_, _) => SearchPreferences();
        _preferencesSearch.ToolTip = "Type to find a setting; the rail keeps the sections that match.";
        _preferencesSearch.AccessibilityLabel = "Search Preferences";

        _preferencesRailRows = new PreferenceSectionRows(this);
        _preferencesRailRows.Rows.AddRange(_preferencesSections.Names);
        _preferencesRail = new NSTableView
        {
            HeaderView = null,
            RowHeight = (nfloat)PreferencesRailRowHeight,
            AllowsMultipleSelection = false,
            IntercellSpacing = new CGSize(0, MacTheme.Space(0) / 2),
            FocusRingType = NSFocusRingType.None,
            SelectionHighlightStyle = NSTableViewSelectionHighlightStyle.None,
        };
        _preferencesRail.AddColumn(new NSTableColumn("section") { Width = (nfloat)PreferencesLayout.RailWidth });
        _preferencesRail.Source = _preferencesRailRows;
        ((NSView)_preferencesRail).AccessibilityLabel = "Preferences sections";
        var railScroll = new NSScrollView
        {
            HasVerticalScroller = true,
            AutohidesScrollers = true,
            DocumentView = _preferencesRail,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        MacTheme.Style(_preferencesRail, railScroll);

        // Nothing found: said in the page's place, with the way back.
        _preferencesSearchEmpty = MacTheme.WrappingLabel("", 3, semibold: true);
        var emptyIcon = new NSImageView
        {
            Image = NSImage.GetSystemSymbol("magnifyingglass", null),
            SymbolConfiguration = NSImageSymbolConfiguration.Create(32, NSFontWeight.Regular),
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        emptyIcon.AccessibilityElement = false;
        var clearSearch = MacTheme.TextButton("Clear search", () =>
        {
            _preferencesSearch.StringValue = "";
            SearchPreferences();
        });
        var empty = MacTheme.Stack(true, 2, emptyIcon, _preferencesSearchEmpty,
            MacTheme.WrappingLabel("Try another word, or clear the search to see every section.", 1, DesignRole.TextSecondary),
            clearSearch);
        _preferencesSearchEmptyArea = new NSView { TranslatesAutoresizingMaskIntoConstraints = false, Hidden = true };
        _preferencesSearchEmptyArea.AddSubview(empty);
        empty.TopAnchor.ConstraintEqualTo(_preferencesSearchEmptyArea.TopAnchor, MacTheme.Space(4)).Active = true;
        empty.LeadingAnchor.ConstraintEqualTo(_preferencesSearchEmptyArea.LeadingAnchor, MacTheme.Space(4)).Active = true;
        empty.TrailingAnchor.ConstraintEqualTo(_preferencesSearchEmptyArea.TrailingAnchor, -MacTheme.Space(4)).Active = true;
        _preferencesSearchEmpty.WidthAnchor.ConstraintEqualTo(empty.WidthAnchor).Active = true;

        _preferencesPage = MacSettings.Scroller();
        var pageArea = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
        MacTheme.Fill(pageArea, _preferencesPage);
        MacTheme.Fill(pageArea, _preferencesSearchEmptyArea);

        // One line under the page about Save, which every section shares.
        var saveHint = MacTheme.Label("Changes in all sections apply when you select Save.", 1, DesignRole.TextSecondary);
        var cancel = MacTheme.TextButton("Cancel", () => _preferencesWindow.OrderOut(null));
        var save = MacTheme.TextButton("Save", SavePreferences, RexButtonLook.Accent);
        save.KeyEquivalent = "\r";
        var actions = MacSettings.Buttons(cancel, save);

        var root = new NSView();
        foreach (var view in new NSView[] { _preferencesSearch, railScroll, pageArea, saveHint, actions })
        {
            root.AddSubview(view);
        }

        var pad = MacTheme.Space(4);
        var rail = (nfloat)PreferencesLayout.RailWidth;
        _preferencesSearch.TopAnchor.ConstraintEqualTo(root.TopAnchor, pad + 12).Active = true;
        _preferencesSearch.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, pad).Active = true;
        _preferencesSearch.WidthAnchor.ConstraintEqualTo(rail).Active = true;
        railScroll.TopAnchor.ConstraintEqualTo(_preferencesSearch.BottomAnchor, MacTheme.Space(2)).Active = true;
        railScroll.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, pad).Active = true;
        railScroll.WidthAnchor.ConstraintEqualTo(rail).Active = true;
        railScroll.BottomAnchor.ConstraintEqualTo(actions.TopAnchor, -MacTheme.Space(3)).Active = true;
        pageArea.TopAnchor.ConstraintEqualTo(root.TopAnchor, 12).Active = true;
        pageArea.LeadingAnchor.ConstraintEqualTo(railScroll.TrailingAnchor, (nfloat)PreferencesLayout.Gap - pad).Active = true;
        pageArea.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor).Active = true;
        pageArea.BottomAnchor.ConstraintEqualTo(actions.TopAnchor, -MacTheme.Space(3)).Active = true;
        actions.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor, -pad).Active = true;
        actions.BottomAnchor.ConstraintEqualTo(root.BottomAnchor, -pad).Active = true;
        saveHint.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, pad).Active = true;
        saveHint.CenterYAnchor.ConstraintEqualTo(actions.CenterYAnchor).Active = true;
        saveHint.TrailingAnchor.ConstraintLessThanOrEqualTo(actions.LeadingAnchor, -MacTheme.Space(3)).Active = true;
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
        _preferencesSearchEmptyArea!.Hidden = true;
        if (_preferencesPage.DocumentView != page)
        {
            MacSettings.Show(_preferencesPage, page);
        }
        else
        {
            _preferencesPage.ContentView.ScrollToPoint(CGPoint.Empty);
            _preferencesPage.ReflectScrolledClipView(_preferencesPage.ContentView);
        }

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
        MarkPreferenceRailSelection();
    }

    /// <summary>Lights the rail item of the section shown, and only that one.</summary>
    private void MarkPreferenceRailSelection()
    {
        if (_preferencesRail is null)
        {
            return;
        }

        for (nint row = 0; row < _preferencesRail.RowCount; row++)
        {
            if (_preferencesRail.GetView(0, row, makeIfNecessary: false) is PreferenceRailCell cell)
            {
                cell.Selected = row == _preferencesRail.SelectedRow;
            }
        }
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
            _preferencesSearchEmpty!.StringValue = $"Nothing in Preferences matches \"{query}\".";
            _preferencesSearchEmptyArea!.Hidden = false;
            _preferencesPage!.Hidden = true;
            _preferencesRail.DeselectAll(null);
            MarkPreferenceRailSelection();
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

    private static NSSlider Slider(double minimum, double maximum, Action changed)
    {
        var slider = NSSlider.FromValue(minimum, minimum, maximum, changed);
        slider.Continuous = true;
        return slider;
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

    /// <summary>
    /// Sets the whole app's appearance from the theme setting. Every window follows at once: their
    /// panels, labels and buttons resolve their roles again when the appearance changes.
    /// </summary>
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

    /// <summary>The SF Symbol beside a section's name in the rail.</summary>
    private static string PreferenceSectionSymbol(string section) => section switch
    {
        "Playback" => "play.circle",
        "Video and pictures" => "photo",
        "Snapshots" => "camera",
        "Window" => "macwindow",
        "History and resume" => "clock",
        "Opening files" => "folder",
        "Diagnostics" => "stethoscope",
        _ => "gearshape",
    };

    /// <summary>The rail's rows: the section names a search leaves, each an icon and its name.</summary>
    private sealed class PreferenceSectionRows : NSTableViewSource
    {
        private readonly MainWindow _owner;

        public PreferenceSectionRows(MainWindow owner) => _owner = owner;

        public List<string> Rows { get; } = [];

        public override nint GetRowCount(NSTableView tableView) => Rows.Count;

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row) =>
            new PreferenceRailCell(PreferenceSectionSymbol(Rows[(int)row]), Rows[(int)row]) { Selected = tableView.SelectedRow == row };

        public override void SelectionDidChange(NSNotification notification)
        {
            _owner.PreferencesRailSelected();
            _owner.MarkPreferenceRailSelection();
        }
    }

    /// <summary>
    /// A rail item: the section's symbol and name in primary text whatever the theme becomes; the
    /// selected one sits on the raised colour with small rounded corners, as on Windows.
    /// </summary>
    private sealed class PreferenceRailCell : NSView
    {
        private readonly NSImageView _icon;
        private readonly RexPanel _back = MacTheme.Panel(DesignRole.Raised, 0);

        public PreferenceRailCell(string symbol, string title)
        {
            _back.Hidden = true;
            MacTheme.Fill(this, _back);
            _icon = new NSImageView
            {
                Image = NSImage.GetSystemSymbol(symbol, null),
                SymbolConfiguration = NSImageSymbolConfiguration.Create(15, NSFontWeight.Regular),
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            _icon.AccessibilityElement = false;
            var label = MacTheme.Label(title, 2);
            AddSubview(_icon);
            AddSubview(label);
            _icon.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, MacTheme.Space(2)).Active = true;
            _icon.CenterYAnchor.ConstraintEqualTo(CenterYAnchor).Active = true;
            _icon.WidthAnchor.ConstraintEqualTo(20).Active = true;
            label.LeadingAnchor.ConstraintEqualTo(_icon.TrailingAnchor, MacTheme.Space(2)).Active = true;
            label.TrailingAnchor.ConstraintLessThanOrEqualTo(TrailingAnchor, -MacTheme.Space(1)).Active = true;
            label.CenterYAnchor.ConstraintEqualTo(CenterYAnchor).Active = true;
            Tint();
        }

        /// <summary>Whether this is the section shown.</summary>
        public bool Selected
        {
            get => !_back.Hidden;
            set => _back.Hidden = !value;
        }

        public override void ViewDidChangeEffectiveAppearance()
        {
            base.ViewDidChangeEffectiveAppearance();
            Tint();
        }

        private void Tint() => _icon.ContentTintColor = MacTheme.Role(DesignRole.TextPrimary, EffectiveAppearance);
    }
}
