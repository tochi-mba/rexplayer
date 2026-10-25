using System.Reflection;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Automation;
using Rex.Media.Diagnostics;

namespace Rex.Media.App.Mac;

public sealed partial class MainWindow
{
    private NSWindow? _logWindow;
    private NSTextView? _logText;
    private NSSearchField? _logSearch;
    private NSPopUpButton? _logLevel;
    private NSPopUpButton? _logSource;
    private bool _logCleared;

    /// <summary>The native log console and its local filters (UI-09).</summary>
    private void ShowLogConsole()
    {
        if (_logWindow is null)
        {
            BuildLogConsole();
        }

        _logCleared = false;
        RefreshLogConsole();
        _logWindow!.MakeKeyAndOrderFront(null);
    }

    private void BuildLogConsole()
    {
        _logWindow = new NSWindow(new CGRect(0, 0, 820, 560),
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable,
            NSBackingStore.Buffered, false)
        {
            Title = "Log - rexplayer",
            MinSize = new CGSize(600, 380),
        };
        _logWindow.ReleaseWhenClosed(false);

        _logLevel = new NSPopUpButton(new CGRect(0, 0, 120, 28), false);
        foreach (var level in new[] { "Debug", "Info", "Warning", "Error" })
        {
            _logLevel.AddItem(level);
        }

        _logLevel.Activated += (_, _) => RefreshLogConsole();
        _logSource = new NSPopUpButton(new CGRect(0, 0, 170, 28), false);
        _logSource.Activated += (_, _) => RefreshLogConsole();
        _logSearch = new NSSearchField(new CGRect(0, 0, 260, 28)) { PlaceholderString = "Search log" };
        _logSearch.Changed += (_, _) => RefreshLogConsole();
        var filters = NSStackView.FromViews([_logLevel, _logSource, _logSearch]);
        filters.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        filters.Spacing = 10;

        _logText = new NSTextView(new CGRect(0, 0, 760, 390))
        {
            Editable = false,
            Selectable = true,
            Font = NSFont.UserFixedPitchFontOfSize(11)!,
        };
        var scroll = new NSScrollView(new CGRect(0, 0, 760, 390))
        {
            HasVerticalScroller = true,
            DocumentView = _logText,
        };
        scroll.HeightAnchor.ConstraintGreaterThanOrEqualTo(260).Active = true;

        var copy = NSButton.CreateButton("Copy", CopyVisibleLog);
        var save = NSButton.CreateButton("Save log…", SaveVisibleLog);
        var clear = NSButton.CreateButton("Clear view", () =>
        {
            _logCleared = true;
            RefreshLogConsole();
        });
        var bundle = NSButton.CreateButton("Save diagnostics…", SaveDiagnostics);
        var openFolder = NSButton.CreateButton("Open log folder", () => Run(Rex.Media.AppCore.Commands.CommandCatalog.OpenLogFolder));
        var actions = NSStackView.FromViews([copy, save, clear, bundle, openFolder]);
        actions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        actions.Spacing = 10;

        var stack = NSStackView.FromViews([filters, scroll, actions]);
        stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        stack.Alignment = NSLayoutAttribute.Leading;
        stack.Spacing = 12;
        stack.EdgeInsets = new NSEdgeInsets(18, 18, 18, 18);
        scroll.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -36).Active = true;
        _logWindow.ContentView = stack;
        _logWindow.Center();
        _log.EntryWritten += LogEntryWritten;
    }

    private void LogEntryWritten(object? sender, LogEntry entry) => OnWindowThread(RefreshLogConsole);

    private void RefreshLogConsole()
    {
        if (_logText is null || _logLevel is null || _logSource is null || _logSearch is null)
        {
            return;
        }

        var entries = _logCleared ? [] : _log.Tail(_log.Options.RingCapacity);
        var previousSource = _logSource.TitleOfSelectedItem;
        _logSource.RemoveAllItems();
        _logSource.AddItem("All sources");
        foreach (var source in entries.Select(entry => entry.Source).Where(source => source.Length > 0).Distinct(StringComparer.Ordinal).Order())
        {
            _logSource.AddItem(source);
        }

        if (previousSource is not null && _logSource.ItemTitles().Contains(previousSource, StringComparer.Ordinal))
        {
            _logSource.SelectItem(previousSource);
        }

        var minimum = (LogLevel)Math.Clamp((int)_logLevel.IndexOfSelectedItem, 0, 3);
        var sourceFilter = _logSource.IndexOfSelectedItem == 0 ? null : _logSource.TitleOfSelectedItem;
        var search = _logSearch.StringValue;
        var visible = entries.Where(entry => entry.Level >= minimum)
            .Where(entry => sourceFilter is null || entry.Source == sourceFilter)
            .Where(entry => search.Length == 0 || entry.Format().Contains(search, StringComparison.CurrentCultureIgnoreCase));
        _logText.Value = string.Join(Environment.NewLine, visible.Select(entry => entry.Format()));
        _logText.ScrollRangeToVisible(new NSRange(_logText.Value.Length, 0));
    }

    private void CopyVisibleLog()
    {
        var pasteboard = NSPasteboard.GeneralPasteboard;
        pasteboard.ClearContents();
        pasteboard.SetStringForType(_logText?.Value ?? "", NSPasteboardType.String.GetConstant()!);
        Say("The visible log is on the clipboard.");
    }

    private void SaveVisibleLog()
    {
        var panel = NSSavePanel.SavePanel;
        panel.NameFieldStringValue = "rexplayer.log";
        panel.Begin(response =>
        {
            if (response == 1 && panel.Url?.Path is { } path)
            {
                TryWrite(path, _logText?.Value ?? "", "Log saved");
            }
        });
    }

    /// <summary>Saves redacted settings, logs and system facts into one local zip (TOOL-10).</summary>
    private void SaveDiagnostics()
    {
        var panel = NSSavePanel.SavePanel;
        panel.NameFieldStringValue = DiagnosticsBundle.FileName(DateTimeOffset.Now);
        panel.Begin(response =>
        {
            if (response == 1 && panel.Url?.Path is { } path &&
                !WriteDiagnostics(path, out var error))
            {
                Say("The diagnostics could not be saved: " + error);
            }
        });
    }

    private bool WriteDiagnostics(string path, out string? error)
    {
        try
        {
            _log.Flush();
            using var output = File.Create(path);
            DiagnosticsBundle.Write(output,
                [("rexplayer.log", ReadShared(_log.FilePath)), ("settings.json", ReadShared(_roots.SettingsPath)), ("system.txt", DescribeMac())],
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            Say("Diagnostics saved: " + Path.GetFileName(path));
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    private AutomationReply DiagnosticsSmokeAction(string? action, string? path)
    {
        switch (action)
        {
            case "show":
                ShowLogConsole();
                return new AutomationReply { Ok = true, Text = _logText?.Value };
            case "verify-controls" when _logWindow?.IsVisible == true:
                _logLevel!.SelectItem("Warning");
                _logSearch!.StringValue = "player";
                RefreshLogConsole();
                return new AutomationReply
                {
                    Ok = _logSource!.ItemTitles().Contains("app", StringComparer.Ordinal) && _logText is { Editable: false },
                    Text = _logText?.Value,
                };
            case "clear" when _logWindow?.IsVisible == true:
                _logCleared = true;
                RefreshLogConsole();
                return new AutomationReply { Ok = _logText?.Value.Length == 0 };
            case "bundle" when path is not null:
                return WriteDiagnostics(path, out var error)
                    ? new AutomationReply { Ok = true, Text = path }
                    : AutomationReply.Failed(error ?? "Diagnostics could not be saved.");
            default:
                return AutomationReply.Failed("The diagnostics smoke action is unavailable: " + action);
        }
    }

    private void TryWrite(string path, string text, string success)
    {
        try
        {
            File.WriteAllText(path, text);
            Say(success + ": " + Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say("The file could not be saved: " + ex.Message);
        }
    }

    private static string? ReadShared(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string DescribeMac() => string.Join(Environment.NewLine,
        "rexplayer " + (typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown"),
        System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        System.Runtime.InteropServices.RuntimeInformation.OSArchitecture,
        ".NET " + Environment.Version);

    private void DisposeDiagnostics()
    {
        _log.EntryWritten -= LogEntryWritten;
        _logWindow?.Close();
        _logWindow?.Dispose();
        _logWindow = null;
    }
}
