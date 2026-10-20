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
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Transcription;
using Rex.Media.Codecs.MediaFoundation;
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
    private const int TranscriptionQueueRowsLimit = 50;
    private readonly TranscriptionLedger _ledger = new(TranscriptionLedger.DefaultPath(App.DataRoot));
    private readonly List<string> _askedTranscripts = [];
    private readonly HashSet<string> _queuedTranscripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Rectangle> _bufferMarks = [];
    private readonly Dictionary<string, (TextBlock State, ProgressBar Progress)> _transcriptionQueueRows = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<QueuedFile>? _shownTranscriptionQueue;
    private string? _shownTranscriptionWorker;
    private bool _transcriptionDetailsOpen;
    private TranscriptionWorker? _worker;
    private DispatcherQueueTimer? _transcriptionTimer;
    private TranscriptionPlan? _plannedTranscription;
    private IReadOnlyList<QueuedFile> _transcriptionQueue = [];
    private bool _transcriptionStarted;
    private bool _queueDirty = true;
    private bool _queueRefreshing;
    private bool? _modelInstalled;
    private readonly Dictionary<string, Task<bool>> _modelDownloads = new(StringComparer.Ordinal);
    private TranscriptModelChoice? _playingModelChoice;
    private long _transcriptGeneration;

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
    private readonly SubtitleHold _subtitleHold = new();
    private bool _holdingForSubtitles => _subtitleHold.Holding;
    private string? _announcedTranscription;
    private string? _workerPath;

    /// <summary>The model the settings name.</summary>
    private SpeechModel TranscriptModel => SpeechModels.For(_playingModelChoice ?? _settings.KineticCaptions.TranscriptModel);

    private TranscriptModelChoice ActiveTranscriptModelChoice =>
        _playingModelChoice ?? _settings.KineticCaptions.TranscriptModel;

    /// <summary>Wires transcription without touching the disk: the first look happens once the launch has settled.</summary>
    private void WireTranscription()
    {
        _transcriptionTimer = DispatcherQueue.CreateTimer();
        _transcriptionTimer.Interval = WindowClocks.TranscriptionEvery;
        _transcriptionTimer.Tick += (_, _) => TranscriptionTick();
        var first = DispatcherQueue.CreateTimer();
        first.Interval = LaunchPlan.FirstLibraryScan;
        first.IsRepeating = false;
        first.Tick += (_, _) => StartTranscription();
        first.Start();
        _player.PositionChanged += (_, _) => MoveTranscriptionPlayhead();
        _player.Changed += (_, _) => HoldForSubtitles();
    }

    private void StartTranscription()
    {
        if (_transcriptionStarted || _closing)
        {
            return;
        }

        _transcriptionStarted = true;
        _library!.Changed += (_, _) => _queueDirty = true;

        // The ledger is read off the window's thread, then the badges show. The tick starts the
        // clock when there is something to look after.
        _ = Task.Run(() => _ledger.All).ContinueWith(_ => OnWindowThread(RefreshTranscriptBadges), TaskScheduler.Default);
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
        var worker = new TranscriptionWorker(_transcripts, _ledger, file => new SpeechAudioReader(file, decoders, CancellationToken.None), () => FakeSpeechHearer.PerPiece is { } perPiece ? new FakeSpeechHearer(perPiece) : (ISpeechHearer)new WhisperHearer(path), model.Id)
        {
            Language = _settings.KineticCaptions.TranscriptLanguage is { Length: > 0 } code ? code : null,
            Log = message => App.Log.Warning(LogSource, message),
        };

        // Only what plays crosses to the window, and only the stretches heard: its track fills by itself.
        worker.Opened += (_, e) => OnHearing(worker, e);
        worker.Heard += (_, e) => OnHearing(worker, e);
        worker.Changed += (_, _) => OnWindowThread(() =>
        {
            if (!ReferenceEquals(worker, _worker) || worker.Model != TranscriptModel.Id)
            {
                return;
            }

            _queueDirty = true;
            ShowTranscriptionStatus();
            RefreshTranscriptBadges();
            // A failed model or file must release a held viewer rather than leave playback
            // paused indefinitely while the worker cannot create more captions.
            if (worker.Status.Error is not null && _holdingForSubtitles)
            {
                _bufferHearing = false;
                HoldForSubtitles();
                ShowSubtitleBuffer();
            }
        });
        worker.Start();
        (_worker, _plannedTranscription, _toldWorker, _queueDirty) = (worker, null, null, true);
        return worker;
    }

    private void OnHearing(TranscriptionWorker worker, HearingEventArgs e)
    {
        WriteTranscriptSrt(worker, e.Path);
        if (worker != _worker || !string.Equals(e.Path, _playingTranscript, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        OnWindowThread(() =>
        {
            if (!ReferenceEquals(worker, _worker) || worker.Model != TranscriptModel.Id || !string.Equals(e.Path, _player.Item?.Location, StringComparison.OrdinalIgnoreCase))
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

    /// <summary>
    /// Once a second while there is a worker or work for one: what the computer allows, what plays,
    /// the queue, the line and holding for subtitles. With neither, the clock stops; everything that
    /// asks for transcription calls this, which starts it again.
    /// </summary>
    private void TranscriptionTick()
    {
        if (_closing || !_transcriptionStarted)
        {
            return;
        }

        var settings = _settings.Transcription;
        var wantsWorker = settings.Library || _askedTranscripts.Count > 0 || (_playingWanted && _playingTranscript is not null);
        var clock = _transcriptionTimer!;
        if (!WindowClocks.Transcription(_worker is not null, wantsWorker))
        {
            clock.Stop();
            ShowTranscriptionStatus();
            return;
        }

        if (!clock.IsRunning)
        {
            clock.Start();
        }

        var model = TranscriptModel;
        _modelInstalled ??= SpeechModelHere(model);
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
    private void OpenTranscriptOf(string? item, bool preserveHold = false)
    {
        // A new item or model invalidates pending exact-variant coverage reads.
        ++_variantGeneration;
        _coverageVariant = null;
        _variantDone = null;
        _variantDuration = null;
        var generation = ++_transcriptGeneration;
        if (preserveHold)
        {
            // A wait in progress outlives the reload of the new quality's coverage.
            _subtitleHold.AwaitCoverage();
        }
        else
        {
            _subtitleHold.Reset();
        }

        (_bufferDone, _bufferDuration, _bufferHearing) = (null, null, false);
        ShowSubtitleBuffer();
        (_playingTranscript, _playingWanted) = (null, false);
        _playingModelChoice = null;
        _modelInstalled = null;
        if (_askedPlaying != item)
        {
            _askedPlaying = null;
        }

        ShowSubtitleBuffer();
        if (item is null || !File.Exists(item))
        {
            _subtitleHold.CoverageReady();
            return;
        }

        QueueTranscriptVariants(item, generation);
        var modelChoice = _settings.KineticCaptions.TranscriptModel;
        var model = SpeechModels.For(modelChoice);
        var settings = _settings.Transcription;
        var asked = _askedPlaying == item;
        var hasVideo = HasVideo;
        var entry = _library?.Entry(item);
        var folders = _library?.Folders ?? [];
        _ = Task.Run(() =>
        {
            var full = _transcripts.Load(item, model.Id);
            var partial = full is null ? _transcripts.LoadPartial(item, model.Id) : null;
            var backgroundEligible = settings.Library && entry is not null
                && TranscriptionEligibility.Of(entry, settings, folders, _ledger.Find(TranscriptionOrder.Sign(entry))) == TranscriptionSkip.None;
            var wanted = full is null && TranscriptionEligibility.Playing(asked, hasVideo, settings.WhatPlays, backgroundEligible);
            var track = full is not null ? full.ToTrack(TranscriptionWorker.TrackName(model.Id))
                : !wanted && partial is not null ? partial.ToTranscript().ToTrack(TranscriptionWorker.TrackName(model.Id)) : null;
            return (full, track, partial?.Done, partial?.Duration, wanted);
        }).ContinueWith(task => OnWindowThread(() =>
        {
            if (generation == _transcriptGeneration)
            {
                _subtitleHold.CoverageReady();
            }

            if (!task.IsCompletedSuccessfully || generation != _transcriptGeneration || _player.Item?.Location != item || TranscriptModel.Id != model.Id)
            {
                HoldForSubtitles();
                return;
            }

            var (full, track, done, duration, wanted) = task.Result;
            if (track is not null)
            {
                ShowTranscriptTrack(track, full);
            }

            // A new transcription starts with an empty coverage rail, not with a missing
            // buffer. If the user opted into waiting, hold before playback outruns the
            // first recognised piece; an already-complete transcript never holds.
            var requested = full is null && (wanted || _askedPlaying == item);
            _bufferDuration = duration ?? _player.Duration;
            _bufferDone = Rex.Media.AppCore.Transcription.SubtitleBuffer.Coverage(full is not null, done, requested, _bufferDuration.Value);
            _bufferHearing = full is null && requested;
            (_playingTranscript, _playingWanted) = (item, requested);
            _playingModelChoice = requested ? modelChoice : null;
            ShowSubtitleBuffer();
            if (TranscriptionConditions.StartsAtOnce(requested, settings.HoldForSubtitles))
            {
                // Launched with a file: wait for its captions now, not once the launch settles.
                StartTranscription();
            }

            TranscriptionTick();
            HoldForSubtitles();
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

    private void DetachGeneratedTranscript()
    {
        if (_transcript is { } generated)
        {
            _player.RemoveSubtitles(generated);
        }

        _transcript = null;
        _heard = null;
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
        _playingModelChoice = _settings.KineticCaptions.TranscriptModel;
        (_playingTranscript, _playingWanted) = (item, true);
        Say("Transcribing from here: subtitles appear as the speech is heard, ahead of where you are.");
        TranscriptionTick();
    }

    /// <summary>Whether <paramref name="model"/> can hear now: downloaded, or stood in for in the window tests.</summary>
    private bool SpeechModelHere(SpeechModel model) => FakeSpeechHearer.PerPiece is not null || _speechModels.IsInstalled(model);

    /// <summary>
    /// The speech model, downloaded once on the user's say-so (ADR-0020); on a connection that
    /// costs by the byte only when they asked there and then. False when it is not here.
    /// </summary>
    private async Task<bool> EnsureSpeechModelAsync(bool askedNow, SpeechModel? requestedModel = null)
    {
        var model = requestedModel ?? TranscriptModel;
        if (_modelDownloads.TryGetValue(model.Id, out var running))
        {
            // One download per model: a second request joins it rather than racing it for the file.
            return await running;
        }

        var download = DownloadSpeechModelAsync(askedNow, model);
        if (!download.IsCompleted)
        {
            _modelDownloads[model.Id] = download;
        }

        try
        {
            return await download;
        }
        finally
        {
            _modelDownloads.Remove(model.Id);
        }
    }

    /// <summary>Another quality was chosen: its model is fetched (never on a metered connection unasked) when transcription is in use.</summary>
    private void FetchModelForNewQuality()
    {
        if (TranscriptionConditions.FetchesModelForNewQuality(_settings.Transcription, _askedPlaying is not null || _askedTranscripts.Count > 0))
        {
            _ = EnsureSpeechModelAsync(askedNow: false);
        }
    }

    private async Task<bool> DownloadSpeechModelAsync(bool askedNow, SpeechModel model)
    {
        if (SpeechModelHere(model))
        {
            if (model == TranscriptModel)
            {
                _modelInstalled = true;
            }

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
            if (model == TranscriptModel)
            {
                _modelInstalled = true;
            }
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

    /// <summary>Short status on the toolbar; the button opens all filenames and their live progress.</summary>
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
        var index = QueueIndexOf(status.Path);
        var count = _transcriptionQueue.Count + (status.Path is not null && index < 0 ? 1 : 0);
        var position = Math.Max(1, index + 1);
        var left = _worker?.Speed.TimeFor(TranscriptionOrder.SoundLeft(_transcriptionQueue));
        var full = TranscriptionStatus.Line(position, count, current, status.Share, left, waiting);
        var compact = status.Path is not null
            ? count <= 1 ? "Transcribing · 1 file" : $"1 active · {count - 1} queued"
            : waiting is not null ? $"Paused · {count} queued"
            : count > 0 ? $"{count} waiting" : "Transcription ready";
        if (LibraryTranscriptionText.Text != compact)
        {
            LibraryTranscriptionText.Text = compact;
        }

        var accessible = full + ". Open to see the queue.";
        if (AutomationProperties.GetName(LibraryTranscription) != accessible)
        {
            AutomationProperties.SetName(LibraryTranscription, accessible);
            ToolTipService.SetToolTip(LibraryTranscription, full + "\nOpen to see every file.");
        }

        // Announce transitions, not changing percentages or time estimates.
        var state = TranscriptionStatus.State(status.Path is not null || count > 0, waiting, count);
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

        if (_transcriptionDetailsOpen)
        {
            UpdateTranscriptionDetails(status, waiting);
        }

        ShowHearingProgress(status);
    }

    /// <summary>Where <paramref name="path"/> is in the queue, or -1; looked up in place, as this runs every second.</summary>
    private int QueueIndexOf(string? path)
    {
        if (path is null)
        {
            return -1;
        }

        var queue = _transcriptionQueue;
        for (var i = 0; i < queue.Count; i++)
        {
            if (string.Equals(queue[i].Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Populate the flyout with a real scrollable queue, not a second menu of commands.</summary>
    private void OnTranscriptionDetailsOpened(object sender, object e)
    {
        _transcriptionDetailsOpen = true;
        _shownTranscriptionQueue = null;
        UpdateTranscriptionDetails(_worker?.Status ?? WorkerStatus.Idle, _plannedTranscription?.Waiting);
    }

    private void OnTranscriptionDetailsClosed(object sender, object e)
    {
        _transcriptionDetailsOpen = false;
        _transcriptionQueueRows.Clear();
        _shownTranscriptionQueue = null;
    }

    private void UpdateTranscriptionDetails(WorkerStatus status, string? waiting)
    {
        var current = status.Path;
        if (!ReferenceEquals(_shownTranscriptionQueue, _transcriptionQueue) ||
            !string.Equals(current, _shownTranscriptionWorker, StringComparison.OrdinalIgnoreCase))
        {
            _shownTranscriptionQueue = _transcriptionQueue;
            _shownTranscriptionWorker = current;
            _transcriptionQueueRows.Clear();
            TranscriptionQueueRows.Children.Clear();

            // What plays can be outside the library. It is still the active job and should appear first.
            var paths = new List<(string Path, double Share)>();
            if (current is not null)
            {
                paths.Add((current, status.Share));
            }

            foreach (var file in _transcriptionQueue)
            {
                if (!string.Equals(file.Path, current, StringComparison.OrdinalIgnoreCase))
                {
                    paths.Add((file.Path, file.Share));
                }
            }

            if (paths.Count == 0)
            {
                TranscriptionQueueRows.Children.Add(new TextBlock
                {
                    Text = waiting ?? "No files waiting. Everything selected is ready.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8),
                });
            }

            for (var i = 0; i < Math.Min(paths.Count, TranscriptionQueueRowsLimit); i++)
            {
                var (path, share) = paths[i];
                var active = string.Equals(path, current, StringComparison.OrdinalIgnoreCase);
                var name = new TextBlock
                {
                    Text = MediaLibrary.TitleOf(path),
                    FontWeight = active ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 1,
                };
                ToolTipService.SetToolTip(name, path);

                var state = new TextBlock
                {
                    FontSize = 12,
                    Text = active ? "Transcribing" : "Waiting",
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                var bar = new ProgressBar
                {
                    Maximum = 100,
                    Value = Math.Clamp(share, 0, 1) * 100,
                    Height = 4,
                    MinWidth = 80,
                };
                var item = new StackPanel { Spacing = 5 };
                item.Children.Add(name);
                item.Children.Add(state);
                item.Children.Add(bar);
                var back = new Border
                {
                    Padding = new Thickness(10, 8, 10, 8),
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.10 },
                    Child = item,
                };
                AutomationProperties.SetName(back, $"{i + 1}. {name.Text}");
                var open = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(0),
                    Content = back,
                };
                open.Click += (_, _) => OpenTranscriptionQueueItem(path);
                ToolTipService.SetToolTip(open, $"Open {path}");
                TranscriptionQueueRows.Children.Add(open);
                _transcriptionQueueRows[path] = (state, bar);
            }

            if (paths.Count > TranscriptionQueueRowsLimit)
            {
                TranscriptionQueueRows.Children.Add(new TextBlock
                {
                    Text = $"{paths.Count - TranscriptionQueueRowsLimit} more files will follow.",
                    Margin = new Thickness(10, 4, 10, 2),
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                });
            }
        }

        var count = _transcriptionQueueRows.Count;
        TranscriptionQueueCount.Text = $"{count} {(count == 1 ? "file" : "files")}";
        var left = _worker?.Speed.TimeFor(TranscriptionOrder.SoundLeft(_transcriptionQueue));
        TranscriptionQueueSummary.Text = status.Path is not null
            ? $"Now: {MediaLibrary.TitleOf(status.Path)}" +
              (left is { } estimate ? $" · about {Math.Max(1, (int)Math.Ceiling(estimate.TotalMinutes))} min remaining" : "")
            : waiting ?? (count > 0 ? "Waiting for the next available slot" : "Nothing left to transcribe.");

        foreach (var file in _transcriptionQueue)
        {
            if (_transcriptionQueueRows.TryGetValue(file.Path, out var row) &&
                !string.Equals(status.Path, file.Path, StringComparison.OrdinalIgnoreCase))
            {
                var percent = Math.Clamp(file.Share, 0, 1) * 100;
                row.Progress.Value = percent;
                row.State.Text = percent >= 1 ? "Ready" : percent > 0 ? $"Queued · {percent:0}% already done" : "Queued";
            }
        }

        if (status.Path is { } pathNow && _transcriptionQueueRows.TryGetValue(pathNow, out var activeRow))
        {
            var percent = Math.Clamp(status.Share, 0, 1) * 100;
            activeRow.Progress.Value = percent;
            activeRow.State.Text = $"Transcribing · {percent:0}% complete";
        }

        var library = _settings.Transcription.Library;
        TranscriptionPauseButton.Visibility = library ? Visibility.Visible : Visibility.Collapsed;
        TranscriptionPauseButton.Content = _settings.Transcription.Paused ? "Resume" : "Pause";
        TranscriptionStopButton.Visibility = library || _askedTranscripts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TranscriptionStopButton.Content = library ? "Stop library" : "Cancel requests";
    }

    /// <summary>Close the queue and play the chosen file immediately.</summary>
    private void OpenTranscriptionQueueItem(string path)
    {
        TranscriptionDetails.Hide();
        if (!string.Equals(_player.Item?.Location, path, StringComparison.OrdinalIgnoreCase))
        {
            _player.OpenHandedOver([path], enqueue: false, App.LaunchedAt);
        }
    }

    private void OnTranscriptionPauseClick(object sender, RoutedEventArgs e) =>
        SetTranscription(_settings.Transcription with { Paused = !_settings.Transcription.Paused });

    private void OnTranscriptionStopClick(object sender, RoutedEventArgs e)
    {
        _askedTranscripts.Clear();
        SetTranscription(_settings.Transcription with { Library = false, Paused = false });
    }

    private void OnTranscriptionSettingsClick(object sender, RoutedEventArgs e)
    {
        TranscriptionDetails.Hide();
        _ = ShowPreferencesAsync();
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
