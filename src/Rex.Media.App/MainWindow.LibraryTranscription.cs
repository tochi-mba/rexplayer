using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Windows.System.Power;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Transcription;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.Engine;
using Rex.Media.Library;
using Rex.Media.Settings;
using Rex.Media.Speech;
using Rex.Media.Subtitles;

namespace Rex.Media.App;

/// <summary>
/// Transcription ahead of time (SUB-20, SUB-21) in the window. What plays is heard from where it
/// plays, its subtitles filling in piece by piece, with the stretches ready shown under the seek
/// bar; the library is heard in the background, at low priority, as Preferences allows. Every
/// decision is AppCore's (<see cref="TranscriptionConditions"/>, <see cref="TranscriptionOrder"/>,
/// <see cref="SubtitleBuffer"/>, <see cref="TranscriptionStatus"/>); the worker hears on a thread
/// of its own. This only reads the computer's state once a second, hands the worker what it needs,
/// and draws what it is told. Nothing here touches the disk on the window's thread, and nothing
/// starts before the window has settled after launch.
/// </summary>
public sealed partial class MainWindow
{
    private readonly TranscriptionLedger _ledger = new(TranscriptionLedger.DefaultPath(App.DataRoot));
    private readonly List<string> _askedTranscripts = [];
    private readonly HashSet<string> _queuedTranscripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Rectangle> _bufferMarks = [];
    private TranscriptionWorker? _worker;
    private DispatcherQueueTimer? _transcriptionTimer;
    private TranscriptionPlan? _plannedTranscription;
    private IReadOnlyList<QueuedFile> _transcriptionQueue = [];
    private bool _transcriptionStarted;
    private bool _queueDirty = true;
    private bool _queueRefreshing;
    private bool? _modelInstalled;

    // What plays, as the worker was last told, and whether its speech is wanted.
    private string? _playingTranscript;
    private bool _playingWanted;
    private string? _askedPlaying;
    private string? _toldWorker;
    private bool _toldWanted;

    // The subtitle buffer for what plays.
    private TimeRanges? _bufferDone;
    private TimeSpan? _bufferDuration;
    private bool _bufferHearing;
    private string _shownBuffer = "";
    private BufferState _shownBufferState;
    private bool _holdingForSubtitles;
    private string? _announcedTranscription;
    private string? _workerPath;

    /// <summary>The model the settings name.</summary>
    private SpeechModel TranscriptModel => SpeechModels.For(_budget.SpeechModel(_settings.KineticCaptions.TranscriptModel));

    /// <summary>Wires transcription without touching the disk: the first look happens once the launch has settled.</summary>
    private void WireTranscription()
    {
        _transcriptionTimer = DispatcherQueue.CreateTimer();
        _transcriptionTimer.Interval = TimeSpan.FromSeconds(1);
        _transcriptionTimer.Tick += (_, _) => TranscriptionTick();
        var first = DispatcherQueue.CreateTimer();
        first.Interval = LaunchPlan.FirstLibraryScan;
        first.IsRepeating = false;
        first.Tick += (_, _) => StartTranscription();
        first.Start();
        _player.PositionChanged += (_, _) => MoveTranscriptionPlayhead();
        LibraryTranscription.Flyout = TranscriptionMenu();
    }

    private void StartTranscription()
    {
        if (_transcriptionStarted || _closing)
        {
            return;
        }

        _transcriptionStarted = true;
        _library!.Changed += (_, _) => _queueDirty = true;

        // The ledger is read off the window's thread, then the badges show.
        _ = Task.Run(() => _ledger.All).ContinueWith(_ => OnWindowThread(RefreshTranscriptBadges), TaskScheduler.Default);
        _transcriptionTimer!.Start();
        TranscriptionTick();
    }

    private void CloseTranscription()
    {
        _transcriptionTimer?.Stop();
        _worker?.Dispose();
        _worker = null;
    }

    /// <summary>The worker for the model the settings name, made the first time there is work, and made again when the model changes.</summary>
    private TranscriptionWorker Worker()
    {
        var model = TranscriptModel;
        if (_worker is { } kept && kept.Model == model.Id)
        {
            return kept;
        }

        _worker?.Dispose();
        var decoders = MediaRegistries.Decoders(new MfDecoderFactory(), new WicDecoderFactory());
        var path = _speechModels.PathOf(model);
        var worker = new TranscriptionWorker(_transcripts, _ledger, file => new SpeechAudioReader(file, decoders, CancellationToken.None), () => new WhisperHearer(path), model.Id)
        {
            Language = _settings.KineticCaptions.TranscriptLanguage is { Length: > 0 } code ? code : null,
            Log = message => App.Log.Warning(LogSource, message),
        };

        // Only what plays crosses to the window, and only the stretches heard: its track fills by itself.
        worker.Opened += (_, e) => OnHearing(e);
        worker.Heard += (_, e) => OnHearing(e);
        worker.Changed += (_, _) => OnWindowThread(() =>
        {
            _queueDirty = true;
            ShowTranscriptionStatus();
            RefreshTranscriptBadges();
        });
        worker.Start();
        (_worker, _plannedTranscription, _toldWorker, _queueDirty) = (worker, null, null, true);
        return worker;
    }

    private void OnHearing(HearingEventArgs e)
    {
        if (!string.Equals(e.Path, _playingTranscript, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        OnWindowThread(() =>
        {
            if (!string.Equals(e.Path, _player.Item?.Location, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_transcript != e.Track)
            {
                ShowTranscriptTrack(e.Track, null);
            }

            (_bufferDone, _bufferDuration, _bufferHearing) = (e.Done, e.Duration, !e.Complete);
            ShowSubtitleBuffer();
            // A newly completed chunk may immediately release a held player.
            HoldForSubtitles();
        });
    }

    /// <summary>Once a second: what the computer allows, what plays, the queue, the line and holding for subtitles.</summary>
    private void TranscriptionTick()
    {
        if (_closing || !_transcriptionStarted)
        {
            return;
        }

        var settings = _settings.Transcription;
        var wantsWorker = settings.Library || _askedTranscripts.Count > 0 || (_playingWanted && _playingTranscript is not null);
        if (_worker is null && !wantsWorker)
        {
            ShowTranscriptionStatus();
            return;
        }

        var model = TranscriptModel;
        _modelInstalled ??= _speechModels.IsInstalled(model);
        var plan = TranscriptionConditions.Plan(_budget.TranscribeWhilePlaying ? settings : settings with { WhilePlaying = false }, new TranscriptionMoment(_player.IsPlaying, OnBattery(), EnergySaver(), _modelInstalled.Value), Environment.ProcessorCount);
        var worker = Worker();
        worker.Language = _settings.KineticCaptions.TranscriptLanguage is { Length: > 0 } code ? code : null;
        if (plan != _plannedTranscription)
        {
            _plannedTranscription = plan;
            worker.SetPlan(plan);
        }

        if (_toldWorker != _playingTranscript || _toldWanted != _playingWanted)
        {
            (_toldWorker, _toldWanted) = (_playingTranscript, _playingWanted);
            worker.SetPlaying(_playingTranscript, _playingWanted, _player.Position);
        }

        if (_queueDirty && !_queueRefreshing)
        {
            RefreshTranscriptionQueue(worker);
        }

        HoldForSubtitles();
        ShowTranscriptionStatus();
    }

    /// <summary>The playhead to the worker, at the rate the player reports it; a seek reaches it at once.</summary>
    private void MoveTranscriptionPlayhead()
    {
        if (_worker is { } worker && _playingWanted && _playingTranscript is not null)
        {
            worker.MovePlayhead(_player.Position);
        }

        if (_bufferDone is not null)
        {
            ShowSubtitleBufferState();
            // Evaluate at the playhead instead of waiting up to a second after the last cue.
            HoldForSubtitles();
        }
    }

    /// <summary>The queue worked out off the window's thread from a copy of the library, then handed to the worker.</summary>
    private void RefreshTranscriptionQueue(TranscriptionWorker worker)
    {
        _queueDirty = false;
        _queueRefreshing = true;
        var entries = _library!.Entries;
        var folders = _library.Folders;
        var asked = _askedTranscripts.ToList();
        var settings = _settings.Transcription;
        var now = DateTimeOffset.UtcNow;

        // Where videos were left is the player's to say, on its own thread: asked here, once.
        var leftPartWay = entries.Where(entry => entry.Kind == LibraryKind.Video && _player.LeftAt(entry.Path) is not null).Select(entry => entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _ = Task.Run(() => TranscriptionOrder.Order(entries, asked, settings, folders, _ledger.Find, leftPartWay.Contains, now)).ContinueWith(task => OnWindowThread(() =>
        {
            _queueRefreshing = false;
            if (task.IsCompletedSuccessfully)
            {
                _transcriptionQueue = task.Result;
                _queuedTranscripts.Clear();
                _queuedTranscripts.UnionWith(task.Result.Select(file => file.Path));
                worker.SetQueue(task.Result);
                RefreshTranscriptBadges();
                ShowTranscriptionStatus();
            }
        }), TaskScheduler.Default);
    }

    /// <summary>
    /// A new item plays: the transcript kept for it, or the part heard, is read off the window's
    /// thread; then whether its speech is wanted (asked for, what plays is transcribed, or in the
    /// library's scope) goes to the worker, which hears it from the playhead.
    /// </summary>
    private void OpenTranscriptOf(string? item)
    {
        (_bufferDone, _bufferDuration, _bufferHearing, _holdingForSubtitles) = (null, null, false, false);
        (_playingTranscript, _playingWanted) = (null, false);
        if (_askedPlaying != item)
        {
            _askedPlaying = null;
        }

        ShowSubtitleBuffer();
        if (item is null || !File.Exists(item))
        {
            return;
        }

        var model = TranscriptModel;
        var settings = _settings.Transcription;
        var asked = _askedPlaying == item;
        var entry = _library?.Entry(item);
        var folders = _library?.Folders ?? [];
        _ = Task.Run(() =>
        {
            var full = _transcripts.Load(item, model.Id);
            var partial = full is null ? _transcripts.LoadPartial(item, model.Id) : null;
            var wanted = full is null && (asked || settings.WhatPlays || (settings.Library && entry is not null
                && TranscriptionEligibility.Of(entry, settings, folders, _ledger.Find(TranscriptionOrder.Sign(entry))) == TranscriptionSkip.None));
            var track = full is not null ? full.ToTrack(TranscriptionWorker.TrackName(model.Id))
                : !wanted && partial is not null ? partial.ToTranscript().ToTrack(TranscriptionWorker.TrackName(model.Id)) : null;
            return (full, track, partial?.Done, partial?.Duration, wanted);
        }).ContinueWith(task => OnWindowThread(() =>
        {
            if (!task.IsCompletedSuccessfully || _player.Item?.Location != item)
            {
                return;
            }

            var (full, track, done, duration, wanted) = task.Result;
            if (track is not null)
            {
                ShowTranscriptTrack(track, full);
            }

            // A whole transcript needs no buffer; a part shows what is ready.
            (_bufferDone, _bufferDuration) = full is not null ? (null, null) : (done, duration);
            (_playingTranscript, _playingWanted) = (item, wanted || _askedPlaying == item);
            ShowSubtitleBuffer();
            TranscriptionTick();
        }), TaskScheduler.Default);
    }

    /// <summary>A transcript's track joins what plays, shown when no other subtitles were.</summary>
    private void ShowTranscriptTrack(SubtitleTrack track, Transcript? whole)
    {
        _heard = whole;
        _transcript = track;
        _player.AddSubtitles(track, show: _player.Subtitles is null || _player.Subtitles.Name == track.Name);
        _toldNoWords = false;
        ApplyKineticCaptions();
    }

    /// <summary>Transcribe: what plays is heard from here, its subtitles appearing as each piece is heard.</summary>
    private async Task TranscribeWhatPlaysAsync()
    {
        if (_player.Item?.Location is not { } item || !File.Exists(item))
        {
            Say("Open a file to hear its speech first.");
            return;
        }

        if (_playingWanted && _playingTranscript == item)
        {
            Say("Already transcribing this, ahead of where you are.");
            return;
        }

        if (!await EnsureSpeechModelAsync(askedNow: true))
        {
            return;
        }

        StartTranscription();
        _askedPlaying = item;
        (_playingTranscript, _playingWanted) = (item, true);
        Say("Transcribing from here: subtitles appear as the speech is heard, ahead of where you are.");
        TranscriptionTick();
    }

    /// <summary>
    /// The speech model, downloaded once on the user's say-so (ADR-0020); on a connection that
    /// costs by the byte only when they asked there and then. False when it is not here.
    /// </summary>
    private async Task<bool> EnsureSpeechModelAsync(bool askedNow)
    {
        var model = TranscriptModel;
        if (_speechModels.IsInstalled(model))
        {
            _modelInstalled = true;
            return true;
        }

        var (online, metered) = Connection();
        if (!TranscriptionConditions.MayDownloadModel(online, metered, askedNow))
        {
            Say(online
                ? $"The {model.Name} speech model ({model.Size}) downloads once you are on a connection that is not metered, or choose Download now in Preferences."
                : $"The {model.Name} speech model ({model.Size}) downloads once you are online.");
            return false;
        }

        try
        {
            Say($"Downloading the {model.Name} speech model ({model.Size}) once, to {_speechModels.Root}.");
            await _speechModels.DownloadAsync(model, Progress("Downloading " + model.Name), CancellationToken.None);
            _modelInstalled = true;
            _plannedTranscription = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidDataException or UnauthorizedAccessException)
        {
            App.Log.Warning(LogSource, "The speech model could not be downloaded: " + ex.Message);
            Say("The speech model could not be downloaded: " + ex.Message);
            return false;
        }
    }

    /// <summary>Asks for files to be heard now, ahead of the rest of the queue.</summary>
    private async Task TranscribeNowAsync(IReadOnlyList<LibraryEntry> entries)
    {
        var heard = entries.Where(entry => entry.Kind != LibraryKind.Picture).Select(entry => entry.Path).ToList();
        if (heard.Count == 0)
        {
            Say("There is nothing here with speech to transcribe.");
            return;
        }

        if (!await EnsureSpeechModelAsync(askedNow: true))
        {
            return;
        }

        StartTranscription();
        _askedTranscripts.RemoveAll(path => heard.Contains(path, StringComparer.OrdinalIgnoreCase));
        _askedTranscripts.InsertRange(0, heard);
        _worker?.Unsettle(heard);
        _queueDirty = true;
        Say(Rex.Media.AppCore.Library.LibraryActions.Count(heard.Count, "file") + " to transcribe next");
        TranscriptionTick();
    }

    /// <summary>Failed files tried again at once.</summary>
    private void RetryTranscripts(IReadOnlyList<LibraryEntry> entries)
    {
        foreach (var record in entries.Select(entry => _ledger.Find(TranscriptionOrder.Sign(entry))).OfType<TranscriptRecord>())
        {
            _ledger.Set(TranscriptionRetry.Retry(record));
        }

        _worker?.Unsettle([.. entries.Select(entry => entry.Path)]);
        _queueDirty = true;
        RefreshTranscriptBadges();
        TranscriptionTick();
    }

    /// <summary>Forgets the transcripts of <paramref name="entries"/>, off the window's thread.</summary>
    private void ForgetTranscripts(IReadOnlyList<LibraryEntry> entries)
    {
        var model = TranscriptModel.Id;
        var signatures = entries.Select(TranscriptionOrder.Sign).ToHashSet();
        _ = Task.Run(() =>
        {
            foreach (var record in _ledger.Remove(record => signatures.Contains(record.Signature)))
            {
                _transcripts.Forget(record.Signature, record.Model);
            }

            foreach (var signature in signatures)
            {
                _transcripts.Forget(signature, model);
            }
        }).ContinueWith(_ => OnWindowThread(() =>
        {
            _worker?.Unsettle([.. entries.Select(entry => entry.Path)]);
            _queueDirty = true;
            RefreshTranscriptBadges();
            Say(entries.Count == 1 ? "Its transcript is forgotten" : $"{entries.Count} transcripts are forgotten");
        }), TaskScheduler.Default);
    }

    /// <summary>The library menu's transcription items, for the files chosen or a watched folder.</summary>
    private void AddTranscriptionMenuItems(MenuFlyout menu, IReadOnlyList<LibraryEntry> entries, string? folder)
    {
        var heard = entries.Where(entry => entry.Kind != LibraryKind.Picture).ToList();
        if (heard.Count == 0)
        {
            return;
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        if (folder is not null)
        {
            menu.Items.Add(MenuItem("Transcribe this folder now", "LibraryMenuTranscribeFolder", () => _ = TranscribeNowAsync([.. heard.Where(entry => entry.Kind == LibraryKind.Video)])));
            var left = _settings.Transcription.Excluded.Contains(folder, StringComparer.OrdinalIgnoreCase);
            menu.Items.Add(MenuItem(left ? "Transcribe this folder in the background again" : "Never transcribe this folder in the background", "LibraryMenuExcludeFolder", () => LeaveOutFolder(folder, !left)));
            return;
        }

        menu.Items.Add(MenuItem(heard.Count == 1 ? "Transcribe now" : $"Transcribe {heard.Count} now", "LibraryMenuTranscribe", () => _ = TranscribeNowAsync(heard)));
        var records = heard.Select(entry => _ledger.Loaded ? _ledger.Find(TranscriptionOrder.Sign(entry)) : null).ToList();
        if (records.Any(record => record?.State == TranscriptState.Failed))
        {
            menu.Items.Add(MenuItem("Retry transcription", "LibraryMenuRetryTranscript", () => RetryTranscripts(heard)));
        }

        if (records.Any(record => record?.State is TranscriptState.Done or TranscriptState.Partial))
        {
            menu.Items.Add(MenuItem(heard.Count == 1 ? "Forget its transcript" : "Forget their transcripts", "LibraryMenuForgetTranscript", () => ForgetTranscripts(heard)));
        }
    }

    private void LeaveOutFolder(string folder, bool leaveOut)
    {
        var settings = _settings.Transcription;
        _settings = _settings with
        {
            Transcription = (settings with
            {
                Excluded = leaveOut ? [.. settings.Excluded, folder] : [.. settings.Excluded.Where(left => !string.Equals(left, folder, StringComparison.OrdinalIgnoreCase))],
            }).Normalize(),
        };
        SaveSettings();
        _queueDirty = true;
        Say(leaveOut ? "The folder is left out of background transcription" : "The folder is transcribed in the background again");
    }

    /// <summary>The badge of a library file, from what is known without touching the disk.</summary>
    private (string Glyph, string Text) TranscriptBadgeOf(LibraryEntry entry)
    {
        if (!_transcriptionStarted || !_ledger.Loaded)
        {
            return ("", "");
        }

        var record = _ledger.Find(TranscriptionOrder.Sign(entry));
        var status = _worker?.Status ?? WorkerStatus.Idle;
        var working = string.Equals(status.Path, entry.Path, StringComparison.OrdinalIgnoreCase);
        var skip = _settings.Transcription.Library ? TranscriptionEligibility.Of(entry, _settings.Transcription, _library!.Folders, record) : TranscriptionSkip.OutOfScope;
        var badge = TranscriptionStatus.Badge(record, skip, working, _queuedTranscripts.Contains(entry.Path), status.Share, DateTimeOffset.UtcNow);
        return (badge.Glyph, badge.Text);
    }

    /// <summary>The badges of the lines in view, brought up to date.</summary>
    private void RefreshTranscriptBadges()
    {
        foreach (var row in _libraryRows)
        {
            if (row.Item is LibraryEntry entry)
            {
                var (glyph, text) = TranscriptBadgeOf(entry);
                row.SetTranscript(glyph, text);
            }
        }
    }

    /// <summary>The library's transcription line, and what a screen reader hears when its state changes.</summary>
    private void ShowTranscriptionStatus()
    {
        var settings = _settings.Transcription;
        var shown = settings.Library || _askedTranscripts.Count > 0 || _worker is not null;
        LibraryTranscription.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown)
        {
            return;
        }

        var status = _worker?.Status ?? WorkerStatus.Idle;
        var waiting = _plannedTranscription?.Waiting;
        var current = status.Path is { } path ? MediaLibrary.TitleOf(path) : null;
        var position = Math.Max(1, _transcriptionQueue.ToList().FindIndex(file => string.Equals(file.Path, status.Path, StringComparison.OrdinalIgnoreCase)) + 1);
        var left = _worker?.Speed.TimeFor(TranscriptionOrder.SoundLeft(_transcriptionQueue));
        var line = TranscriptionStatus.Line(position, _transcriptionQueue.Count, current, status.Share, left, waiting);
        if (LibraryTranscriptionText.Text != line)
        {
            LibraryTranscriptionText.Text = line;
            AutomationProperties.SetName(LibraryTranscription, line);
        }

        // The first state is the one the user set up, not news; after that each change is said once.
        var state = TranscriptionStatus.State(status.Path is not null || _transcriptionQueue.Count > 0, waiting, _transcriptionQueue.Count);
        if (_announcedTranscription is null)
        {
            _announcedTranscription = state;
        }
        else if (TranscriptionStatus.Announcement(_announcedTranscription, state) is { } said)
        {
            _announcedTranscription = said;
            var peer = FrameworkElementAutomationPeer.FromElement(Root) ?? FrameworkElementAutomationPeer.CreatePeerForElement(Root);
            peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, said, "rexplayer.transcription");
        }

        if (status.Path != _workerPath)
        {
            _workerPath = status.Path;
            RefreshTranscriptBadges();
        }

        ShowHearingProgress(status);
    }

    /// <summary>The transcription menu: the line, pause or resume, stop, and the settings.</summary>
    private MenuFlyout TranscriptionMenu()
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            menu.Items.Add(new MenuFlyoutItem { Text = LibraryTranscriptionText.Text, IsEnabled = false });
            var paused = _settings.Transcription.Paused;
            if (_settings.Transcription.Library)
            {
                menu.Items.Add(MenuItem(paused ? "Resume" : "Pause", "TranscriptionPause", () => SetTranscription(_settings.Transcription with { Paused = !paused })));
                menu.Items.Add(MenuItem("Stop transcribing the library", "TranscriptionStop", () =>
                {
                    _askedTranscripts.Clear();
                    SetTranscription(_settings.Transcription with { Library = false, Paused = false });
                }));
            }
            else if (_askedTranscripts.Count > 0)
            {
                menu.Items.Add(MenuItem("Cancel what was asked for", "TranscriptionCancel", () =>
                {
                    _askedTranscripts.Clear();
                    _queueDirty = true;
                    TranscriptionTick();
                }));
            }

            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem("Transcription settings...", "TranscriptionSettings", () => _ = ShowPreferencesAsync()));
        };
        return menu;
    }

    private void SetTranscription(TranscriptionSettings settings)
    {
        _settings = _settings with { Transcription = settings.Normalize() };
        SaveSettings();
        _queueDirty = true;
        _plannedTranscription = null;
        if (settings.Library)
        {
            StartTranscription();
        }

        TranscriptionTick();
        RefreshTranscriptBadges();
    }

    /// <summary>The stretches ready, drawn under the rail only when they change: one mark a stretch.</summary>
    private void ShowSubtitleBuffer()
    {
        var view = Rex.Media.AppCore.Transcription.SubtitleBuffer.View(_bufferDone, _bufferDuration ?? _player.Duration, _player.Position, _bufferHearing);
        var key = string.Join(";", view.Spans);
        if (key != _shownBuffer)
        {
            _shownBuffer = key;
            SubtitleBuffer.Visibility = view.State == BufferState.None ? Visibility.Collapsed : Visibility.Visible;
            while (_bufferMarks.Count < view.Spans.Count)
            {
                var mark = new Rectangle { Height = 2, Fill = new SolidColorBrush(Microsoft.UI.Colors.White), Opacity = 0.45, IsHitTestVisible = false };
                SubtitleBuffer.Children.Add(mark);
                _bufferMarks.Add(mark);
            }

            for (var i = 0; i < _bufferMarks.Count; i++)
            {
                _bufferMarks[i].Visibility = i < view.Spans.Count ? Visibility.Visible : Visibility.Collapsed;
            }

            PositionSubtitleBuffer();
        }

        ShowSubtitleBufferState(force: true);
    }

    private void PositionSubtitleBuffer()
    {
        var view = Rex.Media.AppCore.Transcription.SubtitleBuffer.View(_bufferDone, _bufferDuration ?? _player.Duration, _player.Position, _bufferHearing);
        var width = SubtitleBuffer.ActualWidth;
        for (var i = 0; i < view.Spans.Count && i < _bufferMarks.Count; i++)
        {
            var (from, to) = view.Spans[i];
            _bufferMarks[i].Width = Math.Max(1, (to - from) * width);
            Canvas.SetLeft(_bufferMarks[i], from * width);
            Canvas.SetTop(_bufferMarks[i], (SubtitleBuffer.ActualHeight / 2) + 7);
        }
    }

    /// <summary>
    /// The small words beside the seek bar, and its tooltip; changed only with the state. Called at
    /// every position report, so all it does then is ask the state (two binary searches, nothing
    /// made); the words are built only when the state changes or a piece was heard.
    /// </summary>
    private void ShowSubtitleBufferState(bool force = false)
    {
        var duration = _bufferDuration ?? _player.Duration;
        var state = Rex.Media.AppCore.Transcription.SubtitleBuffer.StateAt(_bufferDone, duration, _player.Position, _bufferHearing);
        if (!force && state == _shownBufferState)
        {
            return;
        }

        _shownBufferState = state;
        var view = Rex.Media.AppCore.Transcription.SubtitleBuffer.View(_bufferDone, duration, _player.Position, _bufferHearing);
        if (SubtitleBufferState.Text != view.Label)
        {
            SubtitleBufferState.Text = view.Label;
            SubtitleBufferState.Visibility = view.Label.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (AutomationProperties.GetName(SubtitleBuffer) != view.ToolTip)
        {
            AutomationProperties.SetName(SubtitleBuffer, view.ToolTip);
            ToolTipService.SetToolTip(SubtitleBufferState, view.ToolTip.Length > 0 ? view.ToolTip : null);
        }
    }

    /// <summary>What the timeline preview says of the subtitles at the pointer, while any are being heard.</summary>
    private void ShowSeekPreviewSubtitles(TimeSpan at)
    {
        var show = _bufferDone is not null && _bufferHearing;
        SeekPreviewSubtitles.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            SeekPreviewSubtitles.Text = Rex.Media.AppCore.Transcription.SubtitleBuffer.HintAt(_bufferDone!, at);
        }
    }

    /// <summary>Playback held while it has caught up with the subtitles, when the user allows it.</summary>
    private void HoldForSubtitles()
    {
        if (_bufferDone is not { } done || (!_holdingForSubtitles && !_player.IsPlaying))
        {
            return;
        }

        // Played again by the user while held: theirs to decide now.
        if (_holdingForSubtitles && _player.IsPlaying)
        {
            _holdingForSubtitles = false;
            return;
        }

        switch (Rex.Media.AppCore.Transcription.SubtitleBuffer.Hold(_settings.Transcription.HoldForSubtitles, _holdingForSubtitles,
            _bufferHearing, done, _player.Position, _bufferDuration ?? _player.Duration, _worker?.Speed.SafeFactor, _player.Speed))
        {
            case HoldAction.Hold:
                _holdingForSubtitles = true;
                _player.Pause();
                Say("Waiting a moment for the subtitles...");
                break;
            case HoldAction.Release:
                _holdingForSubtitles = false;
                if (_player.State == SessionState.Paused)
                {
                    _player.Execute(CommandCatalog.PlayPause);
                }

                break;
        }
    }

    private static bool OnBattery()
    {
        try
        {
            return PowerManager.PowerSupplyStatus == PowerSupplyStatus.NotPresent && PowerManager.BatteryStatus != BatteryStatus.NotPresent;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static bool EnergySaver()
    {
        try
        {
            return PowerManager.EnergySaverStatus == EnergySaverStatus.On;
        }
        catch (COMException)
        {
            return false;
        }
    }
}
