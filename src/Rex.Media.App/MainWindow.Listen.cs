using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Player;
using Rex.Media.Audio;
using Rex.Media.Interop.Audio;
using Rex.Media.Settings;

namespace Rex.Media.App;

/// <summary>
/// The visualisations listening to another app's sound, or everything this computer plays (AU-25).
/// The choice is made from the visualisation's menu ("Listen to") or the chip over the stage, and
/// remembered by the app's program name. The sound is captured only while a visualisation shows,
/// kept in a half-second ring in memory, and never recorded or sent. rexplayer's own music always
/// comes first: while it plays, its sound is drawn.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan ListenRetry = TimeSpan.FromSeconds(2);

    private readonly AudioScope _listenScope = new();
    private CaptureFeed? _listenFeed;
    private WasapiLoopbackCapture? _capture;
    private SoundApp? _listenApp;
    private DateTime _listenTriedAt;
    private long _listenHeard;
    private Button? _listenChip;
    private string _listenChipText = "";

    /// <summary>Whether the visualisations draw another app's sound now.</summary>
    private bool ListensElsewhere => ListenChooser.DrawsOtherSound(_settings.ListenTo, _player.IsPlaying);

    /// <summary>The sound the visualisations draw: rexplayer's, or the captured app's (which needs no lead: it is heard as it arrives).</summary>
    private bool ReadVisualSound(Span<float> left, Span<float> right, TimeSpan lead)
    {
        if (!ListensElsewhere)
        {
            StopListening();
            return _player.ReadSound(left, right, lead);
        }

        KeepListening();
        return _listenScope.Read(left, right);
    }

    private int VisualSampleRate => ListensElsewhere && _listenScope.SampleRate > 0 ? _listenScope.SampleRate : _player.SoundSampleRate;

    /// <summary>Starts capturing the chosen source when it is not yet, and tries again a while after it could not.</summary>
    private void KeepListening()
    {
        if (_capture is { Failure: null })
        {
            return;
        }

        StopListening();
        if (DateTime.UtcNow - _listenTriedAt < ListenRetry)
        {
            return;
        }

        _listenTriedAt = DateTime.UtcNow;
        var feed = _listenFeed ??= new CaptureFeed(_listenScope);
        var format = default(CaptureFormat);
        WasapiLoopbackCapture.PacketHandler packet = (data, frames, silent) =>
        {
            feed.Write(data, frames, format, silent);
            if (!silent)
            {
                Interlocked.Add(ref _listenHeard, frames);
            }
        };
        WasapiLoopbackCapture? capture = null;
        if (_settings.ListenTo == ListenTo.Everything)
        {
            capture = WasapiLoopbackCapture.OpenEverything(packet);
        }
        else if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && ListenChooser.Find(RunningSoundApps(_settings.ListenApp), _settings.ListenApp) is { } app)
        {
            _listenApp = app;
            capture = WasapiLoopbackCapture.OpenApp(app.ProcessId, packet);
        }

        if (capture is null)
        {
            return;
        }

        var mix = capture.Format;
        format = new CaptureFormat(mix.SampleRate, mix.Channels, mix.BitsPerSample, mix.IsFloat);
        capture.Start();
        _capture = capture;
        App.Log.Info(LogSource, $"The visualisation listens to {(_settings.ListenTo == ListenTo.Everything ? "everything" : _listenApp?.Name)}.");
    }

    private void StopListening()
    {
        if (_capture is { } capture)
        {
            _capture = null;
            if (capture.Failure is { } failure)
            {
                App.Log.Info(LogSource, "Listening stopped: " + failure.Message);
            }

            capture.Dispose();
        }
    }

    /// <summary>The apps with sound on the default output, and, when <paramref name="also"/> names a program, its processes even if silent.</summary>
    private static List<SoundApp> RunningSoundApps(string? also = null)
    {
        var apps = new List<SoundApp>();
        foreach (var session in AudioSessionList.Read())
        {
            if (Describe(session.ProcessId, session.Active, session.Peak) is { } app)
            {
                apps.Add(app);
            }
        }

        if (!string.IsNullOrWhiteSpace(also))
        {
            foreach (var process in Process.GetProcessesByName(also))
            {
                using (process)
                {
                    apps.Add(new SoundApp((uint)process.Id, process.ProcessName, SafeTitle(process), false, 0));
                }
            }
        }

        return apps;
    }

    private static SoundApp? Describe(uint processId, bool playing, float peak)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return new SoundApp(processId, process.ProcessName, SafeTitle(process), playing, peak);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // The app closed while it was looked at.
            return null;
        }
    }

    private static string SafeTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch (InvalidOperationException)
        {
            return "";
        }
    }

    /// <summary>The "Listen to" choices, for the visualisation's menu and the chip.</summary>
    private MenuFlyoutSubItem ListenMenu()
    {
        var menu = new MenuFlyoutSubItem { Text = "Listen to" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(menu, "VisualizerListen");
        foreach (var item in ListenItems())
        {
            menu.Items.Add(item);
        }

        return menu;
    }

    private IEnumerable<MenuFlyoutItemBase> ListenItems()
    {
        yield return ListenItem("rexplayer", "VisualizerListenPlayer", _settings.ListenTo == ListenTo.Player, () => Listen(ListenTo.Player, ""));
        yield return ListenItem("Everything this computer plays", "VisualizerListenEverything", _settings.ListenTo == ListenTo.Everything, () => Listen(ListenTo.Everything, ""));
        if (!WasapiLoopbackCapture.CanListenToOneApp)
        {
            yield break;
        }

        var offered = ListenChooser.Offered(RunningSoundApps(), (uint)Environment.ProcessId);
        if (offered.Count > 0)
        {
            yield return new MenuFlyoutSeparator();
        }

        foreach (var app in offered)
        {
            var chosen = _settings.ListenTo == ListenTo.App && string.Equals(_settings.ListenApp, app.Name, StringComparison.OrdinalIgnoreCase);
            var label = app.Playing ? app.Label + "  · playing" : app.Label;
            yield return ListenItem(label, "VisualizerListenApp-" + app.Name, chosen, () => Listen(ListenTo.App, app.Name));
        }

        yield return new MenuFlyoutSeparator();
        var choose = new MenuFlyoutItem { Text = "Choose a window..." };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(choose, "VisualizerListenWindow");
        choose.Click += (_, _) => Forget(ChooseListenWindowAsync());
        yield return choose;
    }

    private static ToggleMenuFlyoutItem ListenItem(string text, string id, bool chosen, Action listen)
    {
        var item = new ToggleMenuFlyoutItem { Text = text, IsChecked = chosen };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, id);
        item.Click += (_, _) => listen();
        return item;
    }

    /// <summary>Listens to <paramref name="kind"/> from now on, remembered; the first time elsewhere, says what happens to the sound.</summary>
    private void Listen(ListenTo kind, string app)
    {
        var first = _settings.ListenTo == ListenTo.Player && kind != ListenTo.Player;
        _settings = _settings with { ListenTo = kind, ListenApp = app };
        _player.Settings = _settings;
        StopListening();
        _listenTriedAt = default;
        (_listenApp, _listenChipText) = (null, "");
        SaveSettings();
        ApplyVisualizer();
        BuildVisualizer();
        Say(kind == ListenTo.Player ? "Listening to rexplayer"
            : first ? "rexplayer listens only to draw the sound; nothing is recorded, kept or sent"
            : kind == ListenTo.Everything ? "Listening to everything" : "Listening to " + ListenChooser.Pretty(app));
    }

    /// <summary>A list of the open windows to pick one to listen to: those playing sound first.</summary>
    private async Task ChooseListenWindowAsync()
    {
        var playing = new HashSet<uint>(AudioSessionList.Read().Where(session => session.Active).Select(session => session.ProcessId));
        var windows = new List<SoundApp>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id != Environment.ProcessId && process.MainWindowHandle != 0 && SafeTitle(process) is { Length: > 0 } title)
                {
                    windows.Add(new SoundApp((uint)process.Id, process.ProcessName, title, playing.Contains((uint)process.Id), 0));
                }
            }
        }

        var list = new ListView { MaxHeight = 360, SelectionMode = ListViewSelectionMode.Single };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(list, "ListenWindowList");
        foreach (var window in windows.OrderByDescending(window => window.Playing).ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            list.Items.Add(new ListViewItem { Content = window.Playing ? $"{window.Title}  · {window.Name}, playing" : $"{window.Title}  · {window.Name}", Tag = window });
        }

        var search = new TextBox { PlaceholderText = "Search windows" };
        search.TextChanged += (_, _) =>
        {
            foreach (var item in list.Items.OfType<ListViewItem>())
            {
                item.Visibility = ((string)item.Content).Contains(search.Text, StringComparison.CurrentCultureIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            }
        };
        var content = new StackPanel { Spacing = 8, MinWidth = 420 };
        content.Children.Add(new TextBlock { Text = "The visualisation draws the sound of the app this window belongs to, and of the processes it started.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(search);
        content.Children.Add(list);
        if (await Ask("Listen to a window", content, "Listen", "Cancel") && list.SelectedItem is ListViewItem { Tag: SoundApp chosen })
        {
            Listen(ListenTo.App, chosen.Name);
        }
    }

    /// <summary>The chip over the stage while listening elsewhere: what is heard, and a way to change it.</summary>
    private void ShowListenChip()
    {
        var quiet = Interlocked.Exchange(ref _listenHeard, 0) == 0 && _capture is not null;
        var label = _listenApp?.Label ?? ListenChooser.Pretty(_settings.ListenApp);
        var state = ListenChooser.State(_settings.ListenTo, _capture is not null, quiet && _capture is not null, _player.IsPlaying);
        var text = ListenChooser.Chip(_settings.ListenTo, label, state);
        if (text.Length > 0)
        {
            text += "  · Change";
        }

        if (text == _listenChipText && (_listenChip is null || VisualCanvas.Children.Contains(_listenChip)))
        {
            return;
        }

        _listenChipText = text;
        if (_listenChip is null)
        {
            _listenChip = new Button { Padding = new Thickness(10, 4, 10, 4), Opacity = 0.85 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_listenChip, "VisualizerListenChip");
            _listenChip.Click += (_, _) =>
            {
                var flyout = new MenuFlyout();
                foreach (var item in ListenItems())
                {
                    flyout.Items.Add(item);
                }

                flyout.ShowAt(_listenChip);
            };
        }

        _listenChip.Content = text;
        _listenChip.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!VisualCanvas.Children.Contains(_listenChip))
        {
            Canvas.SetLeft(_listenChip, 16);
            Canvas.SetTop(_listenChip, 16);
            VisualCanvas.Children.Add(_listenChip);
        }
    }
}
