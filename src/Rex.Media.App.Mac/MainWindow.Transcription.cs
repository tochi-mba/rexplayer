using Rex.Media.AppCore;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Transcription;
using Rex.Media.Codecs.Apple;
using Rex.Media.Settings;
using Rex.Media.Speech;
using Rex.Media.Subtitles;

namespace Rex.Media.App.Mac;

/// <summary>Offline, opt-in speech recognition over the portable transcript worker and cache.</summary>
public sealed partial class MainWindow
{
    private readonly TranscriptCache _macTranscriptCache = new(TranscriptCache.DefaultRoot(AppHost.Roots.Root));
    private readonly SpeechModelStore _macModels = new(SpeechModelStore.DefaultRoot(AppHost.Roots.Root));
    private readonly TranscriptionLedger _macLedger = new(TranscriptionLedger.DefaultPath(AppHost.Roots.Root));
    private TranscriptionWorker? _macWorker;
    private Transcript? _macTranscript;
    private SubtitleTrack? _macTrack;
    private string? _macTranscriptItem;
    private string? _macAsked;
    private NSWindow? _macSpeechWindow;
    private NSTextField? _macSpeechStatus;
    private NSPopUpButton? _macSpeechQuality;
    private NSButton? _macSpeechLibrary;
    private NSButton? _macSpeechPlaying;
    private NSButton? _macSpeechPaused;
    private NSTimer? _macSpeechTimer;
    private CancellationTokenSource? _macDownloadingModel;
    private bool _macLibraryQueueDirty = true;

    private SpeechModel MacModel => SpeechModels.For(_settings.KineticCaptions.TranscriptModel);

    private void StartMacSpeech()
    {
        _macSpeechTimer = NSTimer.CreateRepeatingScheduledTimer(
            TimeSpan.FromSeconds(2), _ => MacSpeechTick());
        _player.PositionChanged += (_, _) => _macWorker?.MovePlayhead(_player.Position);
        if (_mediaLibrary is not null)
        {
            _mediaLibrary.Changed += (_, _) => _macLibraryQueueDirty = true;
        }
    }

    private void ShowMacSpeech()
    {
        if (_macSpeechWindow is null)
        {
            _macSpeechWindow = new NSWindow(new CGRect(0, 0, 620, 390),
                NSWindowStyle.Titled | NSWindowStyle.Closable,
                NSBackingStore.Buffered, false) { Title = "Speech transcription - rexplayer" };
            _macSpeechWindow.ReleaseWhenClosed(false);
            var title = NSTextField.CreateLabel("Transcribe speech offline");
            title.Font = NSFont.SystemFontOfSize(23, NSFontWeight.Semibold)!;
            var intro = NSTextField.CreateWrappingLabel(
                "Speech stays on this Mac. The open speech model is downloaded only on your request, " +
                "with a pinned SHA-256 check. Playback has priority over library jobs.");
            intro.TextColor = NSColor.SecondaryLabel;
            _macSpeechQuality = EffectChoices([.. SpeechModels.QualityChoices]);
            _macSpeechQuality.Activated += (_, _) => SaveMacSpeechOptions();
            _macSpeechLibrary = NSButton.CreateCheckbox("Transcribe watched videos in the background", SaveMacSpeechOptions);
            _macSpeechPlaying = NSButton.CreateCheckbox("Transcribe what plays from its playhead", SaveMacSpeechOptions);
            _macSpeechPaused = NSButton.CreateCheckbox("Pause background transcription", SaveMacSpeechOptions);
            _macSpeechStatus = NSTextField.CreateWrappingLabel("");
            _macSpeechStatus.TextColor = NSColor.SecondaryLabel;
            var download = NSButton.CreateButton("Download model", DownloadMacModel);
            var current = NSButton.CreateButton("Transcribe this file", AskMacSpeech);
            var export = NSButton.CreateButton("Export captions…", ExportMacSpeech);
            var controls = NSStackView.FromViews([download, current, export]);
            controls.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            controls.Spacing = 9;
            var stack = NSStackView.FromViews([
                title, intro, EffectChoiceRow("Model", _macSpeechQuality),
                _macSpeechLibrary, _macSpeechPlaying, _macSpeechPaused, _macSpeechStatus, controls,
            ]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 16;
            stack.EdgeInsets = new NSEdgeInsets(22, 24, 22, 24);
            stack.TranslatesAutoresizingMaskIntoConstraints = false;
            var root = new NSView(new CGRect(0, 0, 620, 390));
            root.AddSubview(stack);
            stack.TopAnchor.ConstraintEqualTo(root.TopAnchor).Active = true;
            stack.BottomAnchor.ConstraintEqualTo(root.BottomAnchor).Active = true;
            stack.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor).Active = true;
            stack.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor).Active = true;
            intro.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _macSpeechStatus.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _macSpeechWindow.ContentView = root;
            _macSpeechWindow.Center();
        }

        _macSpeechQuality!.SelectItem((int)_settings.KineticCaptions.TranscriptModel);
        _macSpeechLibrary!.State = State(_settings.Transcription.Library);
        _macSpeechPlaying!.State = State(_settings.Transcription.WhatPlays);
        _macSpeechPaused!.State = State(_settings.Transcription.Paused);
        ShowMacSpeechStatus();
        _macSpeechWindow.MakeKeyAndOrderFront(null);
    }

    private void SaveMacSpeechOptions()
    {
        if (_macSpeechQuality is null) return;
        var candidate = (_settings with
        {
            KineticCaptions = _settings.KineticCaptions with
            {
                TranscriptModel = (TranscriptModelChoice)(int)_macSpeechQuality.IndexOfSelectedItem,
            },
            Transcription = _settings.Transcription with
            {
                Library = IsChecked(_macSpeechLibrary),
                WhatPlays = IsChecked(_macSpeechPlaying),
                Paused = IsChecked(_macSpeechPaused),
            },
        }).Normalize();
        try
        {
            SettingsStore.Save(_roots.SettingsPath, candidate);
            _settings = candidate;
            _player.Settings = candidate;
            _macLibraryQueueDirty = true;
            if (_macWorker is { } worker && worker.Model != MacModel.Id)
            {
                worker.Dispose();
                _macWorker = null;
                _macTranscript = null;
                _macTrack = null;
                _macTranscriptItem = null;
            }

            MacSpeechTick();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say("Could not save speech settings: " + ex.Message);
        }
    }

    private void DownloadMacModel()
    {
        if (_macDownloadingModel is not null) return;
        var model = MacModel;
        if (_macModels.IsInstalled(model))
        {
            Say("This speech model is already downloaded.");
            return;
        }

        var cancellation = new CancellationTokenSource();
        _macDownloadingModel = cancellation;
        var progress = new Progress<double>(share => OnWindowThread(() =>
        {
            if (ReferenceEquals(cancellation, _macDownloadingModel) && _macSpeechStatus is not null)
            {
                _macSpeechStatus.StringValue = model.Name + ": " +
                    Math.Round(share * 100) + "% downloaded";
            }
        }));
        _ = Task.Run(async () =>
        {
            try
            {
                await _macModels.DownloadAsync(model, progress, cancellation.Token);
                OnWindowThread(() =>
                {
                    _macDownloadingModel = null;
                    cancellation.Dispose();
                    Say("Speech model verified and ready.");
                    MacSpeechTick();
                    ShowMacSpeechStatus();
                });
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or
                UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
            {
                _log.Warning(LogSource, "Speech model download: " + ex.Message);
                OnWindowThread(() =>
                {
                    _macDownloadingModel = null;
                    cancellation.Dispose();
                    if (_macSpeechStatus is not null) _macSpeechStatus.StringValue =
                        "Speech model download failed: " + ex.Message;
                });
            }
        });
    }

    private void AskMacSpeech()
    {
        if (_player.Item?.Location is not { } path || !File.Exists(path))
        {
            Say("Open a local file containing speech first.");
            return;
        }

        if (!_macModels.IsInstalled(MacModel))
        {
            ShowMacSpeech();
            Say("Download the chosen speech model before transcribing.");
            return;
        }

        _macAsked = path;
        MacSpeechTick();
    }

    private void MacSpeechTick()
    {
        var item = _player.Item?.Location;
        var model = MacModel;
        if (!string.Equals(_macTranscriptItem, item, StringComparison.OrdinalIgnoreCase))
        {
            _macTranscriptItem = item;
            _macTranscript = null;
            _macTrack = null;
            if (item is { } local && File.Exists(local))
            {
                var chosen = model.Id;
                _ = Task.Run(() => _macTranscriptCache.Load(local, chosen)).ContinueWith(
                    task => OnWindowThread(() =>
                    {
                        if (task.IsCompletedSuccessfully && task.Result is { } found &&
                            _player.Item?.Location == local && MacModel.Id == chosen)
                        {
                            _macTranscript = found;
                            _macTrack = found.ToTrack(TranscriptionWorker.TrackName(chosen));
                            _player.AddSubtitles(_macTrack, show: false);
                        }
                    }), TaskScheduler.Default);
            }
        }

        var installed = _macModels.IsInstalled(model);
        // Songs (a cover kept as a one-frame video track included) and files still downloading are
        // never heard unasked: only videos, audiobooks and podcasts are.
        var entry = item is null ? null : _mediaLibrary?.Entry(item);
        var wantsPlayback = item is not null && TranscriptionEligibility.Playing(_macAsked == item,
            TranscriptionEligibility.SourceOf(_player.Info, entry is null ? null
                : Rex.Media.AppCore.Library.AudioRules.Judge(entry, Rex.Media.AppCore.Library.MusicLookup.NotSearched)?.Shelf),
            _settings.Transcription.WhatPlays, backgroundEligible: false, TranscriptionEligibility.StillDownloading(item, entry));
        if (!installed || !wantsPlayback && !_settings.Transcription.Library)
        {
            _macWorker?.SetPlaying(null, false, TimeSpan.Zero);
            ShowMacSpeechStatus();
            return;
        }

        var worker = EnsureMacWorker();
        worker.SetPlan(TranscriptionConditions.Plan(_settings.Transcription,
            new TranscriptionMoment(_player.IsPlaying, false, false, installed), Environment.ProcessorCount));
        worker.SetPlaying(item is not null && File.Exists(item) ? item : null,
            wantsPlayback && _macTranscript is null, _player.Position);
        if (_macLibraryQueueDirty && _mediaLibrary is not null)
        {
            _macLibraryQueueDirty = false;
            var entries = _mediaLibrary.Entries;
            var folders = _mediaLibrary.Folders;
            var settings = _settings.Transcription;
            var asked = _macAsked is { } path ? new[] { path } : [];
            _ = Task.Run(() => TranscriptionOrder.Order(entries, asked, settings,
                folders, _macLedger.Find, _ => false, DateTimeOffset.UtcNow)).ContinueWith(
                task => OnWindowThread(() =>
                {
                    if (task.IsCompletedSuccessfully && ReferenceEquals(worker, _macWorker))
                    {
                        worker.SetQueue(task.Result);
                    }
                }), TaskScheduler.Default);
        }

        ShowMacSpeechStatus();
    }

    private TranscriptionWorker EnsureMacWorker()
    {
        var model = MacModel;
        if (_macWorker is { } current && current.Model == model.Id) return current;
        _macWorker?.Dispose();
        var decoders = MediaRegistries.Decoders(new AppleDecoderFactory());
        var worker = new TranscriptionWorker(_macTranscriptCache, _macLedger,
            path => new SpeechAudioReader(path, decoders, CancellationToken.None),
            () => new WhisperHearer(_macModels.PathOf(model)), model.Id)
        {
            Language = _settings.KineticCaptions.TranscriptLanguage is { Length: > 0 } code ? code : null,
            Log = message => _log.Warning(LogSource, message),
        };
        worker.Opened += (_, e) => OnMacHearing(worker, e);
        worker.Heard += (_, e) => OnMacHearing(worker, e);
        worker.Changed += (_, _) => OnWindowThread(ShowMacSpeechStatus);
        _macWorker = worker;
        worker.Start();
        return worker;
    }

    private void OnMacHearing(TranscriptionWorker worker, HearingEventArgs e)
    {
        OnWindowThread(() =>
        {
            if (!ReferenceEquals(worker, _macWorker) || _player.Item?.Location != e.Path) return;
            if (!ReferenceEquals(_macTrack, e.Track))
            {
                _macTrack = e.Track;
                _player.AddSubtitles(e.Track, show: _player.Subtitles is null);
                RefreshTrackTools();
            }

            if (e.Complete)
            {
                _macTranscript = worker.Snapshot(e.Path);
                _macAsked = null;
            }

            ShowMacSpeechStatus();
        });
    }

    private void ExportMacSpeech()
    {
        var path = _player.Item?.Location;
        var transcript = _macTranscript ?? (path is null ? null : _macWorker?.Snapshot(path));
        if (transcript is not { IsEmpty: false })
        {
            Say("Transcribe speech before exporting captions.");
            return;
        }

        var panel = NSSavePanel.SavePanel;
        panel.NameFieldStringValue = Path.GetFileNameWithoutExtension(path) + ".srt";
        panel.Begin(response =>
        {
            if (response != 1 || panel.Url?.Path is not { } output) return;
            _ = Task.Run(() =>
            {
                try
                {
                    TranscriptFileExport.Write(output, transcript);
                    OnWindowThread(() => Say("Transcript exported to " + Path.GetFileName(output)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    OnWindowThread(() => Say("Transcript export failed: " + ex.Message));
                }
            });
        });
    }

    private void ShowMacSpeechStatus()
    {
        if (_macSpeechStatus is null) return;
        var model = MacModel;
        var state = _macModels.IsInstalled(model)
            ? model.Name + " model is ready."
            : model.Name + " (" + model.Size + ") needs an opt-in download.";
        var worker = _macWorker?.Status;
        if (worker?.Path is { } path)
        {
            state += " Hearing " + Path.GetFileName(path) + " (" + Math.Round(worker.Share * 100) + "%).";
        }

        if (worker?.Error is { } error) state += " " + error;
        _macSpeechStatus.StringValue = state;
    }

    private AutomationReply MacSpeechSmokeAction(string? action)
    {
        if (action == "configure")
        {
            ShowMacSpeech();
            _macSpeechQuality!.SelectItem((int)TranscriptModelChoice.Base);
            _macSpeechLibrary!.State = NSCellStateValue.On;
            _macSpeechPlaying!.State = NSCellStateValue.On;
            _macSpeechPaused!.State = NSCellStateValue.On;
            SaveMacSpeechOptions();
        }
        else if (action == "resume")
        {
            ShowMacSpeech();
            _macSpeechPaused!.State = NSCellStateValue.Off;
            SaveMacSpeechOptions();
        }
        else if (action != "status")
        {
            return new AutomationReply { Ok = false, Error = "Unknown speech action." };
        }

        return new AutomationReply
        {
            Ok = true,
            Text = $"visible={_macSpeechWindow?.IsVisible == true};model={MacModel.Id};" +
                $"library={_settings.Transcription.Library};playing={_settings.Transcription.WhatPlays};" +
                $"paused={_settings.Transcription.Paused};installed={_macModels.IsInstalled(MacModel)}",
        };
    }

    private void DisposeMacSpeech()
    {
        _macSpeechTimer?.Invalidate();
        _macSpeechTimer = null;
        _macDownloadingModel?.Cancel();
        _macWorker?.Dispose();
        _macWorker = null;
        _macSpeechWindow?.Close();
        _macSpeechWindow?.Dispose();
        _macSpeechWindow = null;
    }
}
