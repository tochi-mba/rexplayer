using System.Security.Cryptography;
using System.Text;

namespace Rex.Media.AppCore.Player;

/// <summary>
/// Finding the intro and credits in the background, and acting on them: the skip offer, opted-in
/// automatic skipping and the next episode. Nothing here runs a decoder or touches the disk on the
/// window's thread, and the analysis never touches the playback session.
/// </summary>
public sealed partial class PlayerController
{
    /// <summary>The files a season folder is searched for, for siblings and the next episode.</summary>
    private static readonly string[] EpisodeExtensions = [".mkv", ".mp4", ".m4v", ".avi", ".mov", ".webm", ".ts"];

    private CancellationTokenSource? _sectionStop;
    private readonly object _sectionStopGate = new();
    private SectionProposals? _activeProposals;
    private string? _nextEpisode;
    private ShowSkipping _showSkips = new(false, false);
    private readonly AutomaticSkip _automaticSkip = new();

    /// <summary>Analyses one episode (production decodes with Windows' decoders); null leaves detection off.</summary>
    public Func<string, CancellationToken, EpisodeAnalysis>? SectionAnalyzer { get; set; }

    /// <summary>Where analyses are kept between runs, so each episode of a season is decoded once.</summary>
    public SectionAnalysisCache SectionCache { get; set; } = new();

    public SectionProposals? CurrentProposals => _activeProposals;

    /// <summary>
    /// Whether the background analysis is still running, so a test can wait for the worker to
    /// finish rather than guess how long a below-normal thread takes on a busy machine.
    /// </summary>
    internal bool FindingSections
    {
        get
        {
            lock (_sectionStopGate)
            {
                return _sectionStop is not null;
            }
        }
    }

    /// <summary>The file of the episode after this one in its folder, once the background lookup has found it.</summary>
    public string? NextEpisode => _nextEpisode;

    /// <summary>The show and season of the current item, or null for anything that is not an episode.</summary>
    public EpisodeIdentity? CurrentEpisode =>
        Item is { } item && !string.IsNullOrWhiteSpace(item.Location) ? EpisodeGrouping.Identify(item.Location) : null;

    /// <summary>Whether this show's intros and credits are skipped without a click.</summary>
    public ShowSkipping CurrentShowSkipping => _showSkips;

    /// <summary>
    /// What the window offers while playback is inside a section: the main action, a second one,
    /// and the countdown of an automatic skip. Null outside every section.
    /// </summary>
    public EpisodeSkipPrompt? SkipPrompt
    {
        get
        {
            if (AvailableEpisodeSkip is not { } skip)
            {
                return null;
            }

            var detected = CurrentSections.Offer(Position, Duration) is null ? _activeProposals?.Best(skip.Kind, Duration) : null;
            TimeSpan? due = _automaticSkip.Pending is { } pending && pending.Kind == skip.Kind ? pending.Due : null;
            return EpisodeSkipPrompt.For(skip, detected, Duration, Position, _nextEpisode is not null, due);
        }
    }

    private void CancelSectionDetection()
    {
        lock (_sectionStopGate)
        {
            _sectionStop?.Cancel();
            _sectionStop = null;
        }
    }

    private void BeginSectionDetection()
    {
        CancelSectionDetection();
        _activeProposals = null;
        _nextEpisode = null;
        _automaticSkip.Reset();
        if (!CanMarkEpisodeSections || CurrentEpisode is not { } identity || !Path.IsPathFullyQualified(Item!.Location))
        {
            _showSkips = new ShowSkipping(false, false);
            return;
        }

        _showSkips = Memory.ShowSkips(identity.Show);
        var path = Item.Location;
        var analyzer = SectionAnalyzer;
        var cache = SectionCache;
        var stop = new CancellationTokenSource();
        // Capture the token before the worker is scheduled: a rapid episode change can
        // cancel and dispose its source before the background delegate starts running.
        var token = stop.Token;
        lock (_sectionStopGate)
        {
            _sectionStop = stop;
        }

        var worker = new Thread(() =>
        {
            try
            {
                FindSections(path, analyzer, cache, token);
            }
            finally
            {
                // The worker owns disposal. A new episode can cancel it while a decoder is
                // still waiting on token.WaitHandle; disposing on the UI thread breaks that wait.
                lock (_sectionStopGate)
                {
                    if (ReferenceEquals(_sectionStop, stop))
                    {
                        _sectionStop = null;
                    }

                    stop.Dispose();
                }
            }
        })
        {
            IsBackground = true,
            // Analysis yields to playback, which shares the processor with it.
            Priority = ThreadPriority.BelowNormal,
            Name = "rexplayer section detection",
        };
        worker.Start();
    }

    /// <summary>
    /// Finds the next episode, then (unless the proposals were cached) analyses this episode and
    /// its nearest siblings, each once: analyses are cached per file, so opening the next episode
    /// decodes only what is new. Results are shown as they firm up: credits from this episode's
    /// pictures at once, then both sections once two siblings agree (one sibling alone cannot tell
    /// a title sequence from a recap), and finally from every sibling compared.
    /// </summary>
    private void FindSections(string path, Func<string, CancellationToken, EpisodeAnalysis>? analyzer,
        SectionAnalysisCache cache, CancellationToken token)
    {
        try
        {
            var folder = Path.GetDirectoryName(path)!;
            var files = Directory.EnumerateFiles(folder)
                .Where(file => EpisodeExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .ToArray();
            var siblings = EpisodeGrouping.Siblings(path, files, SectionDetector.SiblingsCompared);
            var signature = DetectionSignature(path, siblings);
            var cached = Memory.Proposals(path, signature);
            Publish(path, cached, token);
            var next = EpisodeGrouping.Next(path, files) ?? NextSeason(path, folder);
            _dispatch(() =>
            {
                if (!token.IsCancellationRequested && Item?.Location == path && next is not null)
                {
                    _nextEpisode = next;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            });
            // An empty result is still a result. Re-decoding an episode and its season on every
            // open merely because no intro or credits were found defeats the cache.
            if (analyzer is null || cached is not null)
            {
                return;
            }

            EpisodeAnalysis? main = null;
            var others = new List<EpisodeAnalysis>();
            foreach (var candidate in siblings.Prepend(path))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var stamp = SectionAnalysis.SignatureOf(candidate);
                    var analysis = cache.Get(candidate, stamp);
                    if (analysis is null)
                    {
                        analysis = analyzer(candidate, token);
                        token.ThrowIfCancellationRequested();
                        cache.Set(analysis);
                    }

                    if (candidate == path)
                    {
                        main = analysis.Path == path ? analysis : null;
                    }
                    else
                    {
                        others.Add(analysis);
                    }
                }
                catch (Exception ex) when (candidate != path &&
                    ex is IOException or UnauthorizedAccessException or NotSupportedException or
                        Rex.Media.Primitives.MediaFormatException or InvalidOperationException)
                {
                    _log.Warning("sections", "Could not analyse sibling " + candidate + ": " + ex.Message);
                }

                if (main is null)
                {
                    return;
                }

                if (others.Count != 1 && candidate != siblings.LastOrDefault())
                {
                    Publish(path, SectionDetector.Detect(main, others) with { Signature = signature }, token);
                }
            }

            token.ThrowIfCancellationRequested();
            var proposals = SectionDetector.Detect(main!, others) with { Signature = signature };
            Memory.SetProposals(path, proposals);
            Publish(path, proposals, token);
        }
        catch (OperationCanceledException)
        {
            // A different episode opened: never publish stale results.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or
            Rex.Media.Primitives.MediaFormatException or InvalidOperationException)
        {
            _log.Warning("sections", "Background analysis unavailable: " + ex.Message);
        }
    }

    /// <summary>
    /// Identity of every recording which can affect a detection. The selected sibling paths are
    /// included as well as their file signatures, so adding, removing, moving or replacing an
    /// episode invalidates the result. <see cref="SectionAnalysis.FeatureVersion"/> is already in
    /// every file signature and therefore invalidates results when detector parameters change.
    /// </summary>
    internal static string DetectionSignature(string path, IReadOnlyList<string> siblings)
    {
        var text = new StringBuilder();
        foreach (var file in siblings.Prepend(path))
        {
            var identity = Path.GetFullPath(file);
            if (OperatingSystem.IsWindows())
            {
                identity = identity.ToUpperInvariant();
            }

            text.Append(identity).Append('\0').Append(SectionAnalysis.SignatureOf(file)).Append('\n');
        }

        return "set:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>After a season's last episode in a "Season 1" folder, the first episode in the folder of the next season.</summary>
    private static string? NextSeason(string path, string folder)
    {
        if (!EpisodeGrouping.IsSeasonFolder(Path.GetFileName(folder)) || Path.GetDirectoryName(folder) is not { } show)
        {
            return null;
        }

        var files = Directory.EnumerateDirectories(show)
            .Where(other => other != folder && EpisodeGrouping.IsSeasonFolder(Path.GetFileName(other)))
            .SelectMany(Directory.EnumerateFiles)
            .Where(file => EpisodeExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));
        return EpisodeGrouping.Next(path, files);
    }

    /// <summary>Shows <paramref name="proposals"/> if this episode is still the one playing.</summary>
    private void Publish(string path, SectionProposals? proposals, CancellationToken token) => _dispatch(() =>
    {
        if (!token.IsCancellationRequested && Item?.Location == path)
        {
            // Rejections stay made, including those made while the analysis ran.
            _activeProposals = proposals is null ? null : Memory.WithRejections(path, proposals);
            Changed?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
    });

    /// <summary>
    /// Counts down and performs an opted-in automatic skip (<see cref="AutomaticSkip"/>); called on
    /// every position report.
    /// </summary>
    internal void MaybeSkipAutomatically()
    {
        var available = CanMarkEpisodeSections ? AvailableEpisodeSkip : null;
        if (_automaticSkip.Advance(available, available is not null && SkipsAutomatically(available.Kind), Position))
        {
            SkipEpisodeSection();
        }
    }

    private bool SkipsAutomatically(EpisodeSectionKind kind) => kind == EpisodeSectionKind.Intro
        ? Settings.SkipIntrosAutomatically || _showSkips.Intros
        : Settings.SkipCreditsAutomatically || _showSkips.Credits;

    /// <summary>Stops a counting-down automatic skip; the section plays on and is not skipped again.</summary>
    public bool CancelAutomaticSkip()
    {
        if (!_automaticSkip.Cancel())
        {
            return false;
        }

        Say("Automatic skip cancelled.");
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Skip this show's intros or credits automatically ("Always skip for this show"), or stop.</summary>
    public bool SetShowSkipping(EpisodeSectionKind kind, bool skip)
    {
        if (CurrentEpisode is not { } identity)
        {
            return false;
        }

        _showSkips = kind switch
        {
            EpisodeSectionKind.Intro => _showSkips with { Intros = skip },
            EpisodeSectionKind.Credits => _showSkips with { Credits = skip },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        Memory.SetShowSkips(identity.Show, _showSkips);
        var what = kind == EpisodeSectionKind.Intro ? "intros" : "credits";
        Say(skip ? $"This show's {what} will be skipped automatically." : $"This show's {what} will no longer be skipped automatically.");
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Plays the next episode of this folder: the playlist's next item when it is that episode,
    /// otherwise the episode is put right after this one and started.
    /// </summary>
    public bool PlayNextEpisode()
    {
        if (_nextEpisode is not { } next)
        {
            return false;
        }

        if (string.Equals(Playlist.PeekNext(automatic: false)?.Location, next, StringComparison.OrdinalIgnoreCase))
        {
            Start(Playlist.Next(automatic: false));
            return true;
        }

        var at = Playlist.NextIndex;
        Playlist.Insert(at, [new PlaylistItem(next)]);
        Start(Playlist.JumpTo(at));
        return true;
    }

    /// <summary>Hides this kind of detected section for this episode (or shows it again); saved boundaries are untouched.</summary>
    public bool RejectDetectedSection(EpisodeSectionKind kind, bool reject = true)
    {
        if (Item is not { } item || _activeProposals is null)
        {
            return false;
        }

        _activeProposals = _activeProposals.Reject(kind, reject);
        Memory.RejectProposal(item.Location, kind, reject);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Saves the detected section as this episode's own boundaries.</summary>
    public bool ConfirmDetectedSection(EpisodeSectionKind kind)
    {
        return _activeProposals?.Best(kind, Duration) is { } candidate &&
            SetSectionBounds(kind, candidate.Start, candidate.End);
    }

    /// <summary>
    /// Saves boundaries typed into the section editor, in seconds, onto <paramref name="editing"/>
    /// only: another episode may have opened while the editor was showing. An empty box (NaN), an
    /// infinite value or one outside the video is refused before it reaches
    /// <see cref="TimeSpan.FromSeconds(double)"/>, which would throw for it.
    /// </summary>
    public bool SetSectionBounds(EpisodeSectionKind kind, double startSeconds, double endSeconds, PlaylistItem editing)
    {
        var length = Duration.TotalSeconds;
        return ReferenceEquals(editing, Item) &&
            double.IsFinite(startSeconds) && double.IsFinite(endSeconds) &&
            startSeconds >= 0 && startSeconds <= length && endSeconds >= 0 && endSeconds <= length &&
            SetSectionBounds(kind, TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(endSeconds));
    }

    /// <summary>Sets both boundaries of one section exactly, leaving the other section alone.</summary>
    public bool SetSectionBounds(EpisodeSectionKind kind, TimeSpan start, TimeSpan end)
    {
        if (!CanMarkEpisodeSections || Item is not { } item || start < TimeSpan.Zero ||
            end <= start + TimeSpan.FromSeconds(2) || end > Duration)
        {
            return false;
        }

        var saved = CurrentSections;
        var changed = kind switch
        {
            EpisodeSectionKind.Intro => saved with { IntroStart = start, IntroEnd = end },
            EpisodeSectionKind.Credits => saved with { CreditsStart = start, CreditsEnd = end },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        Memory.SetSections(MemoryKey(item), changed);
        Say((kind == EpisodeSectionKind.Intro ? "Intro" : "Credits") + " saved for this episode.");
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}


/// <summary>
/// Analyses numbered TV episodes in the library even before any is opened. One below-normal
/// worker shares the player's per-file feature cache and saved proposals, yields immediately to
/// playback, and never opens a playback session. Re-queuing checks signatures, so unchanged
/// episodes require no decoding and a replaced sibling invalidates every dependent proposal.
/// </summary>
public sealed class LibrarySectionAnalysis : IDisposable
{
    private static readonly HashSet<string> Extensions = new(
        [".mkv", ".mp4", ".m4v", ".avi", ".mov", ".webm", ".ts"],
        StringComparer.OrdinalIgnoreCase);

    private readonly PlayerMemory _memory;
    private readonly SectionAnalysisCache _cache;
    private readonly Func<string, CancellationToken, EpisodeAnalysis> _analyzer;
    private readonly Rex.Media.Diagnostics.RexLog _log;
    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread? _worker;
    private string[] _files = [];
    private List<string> _pending = [];
    private CancellationTokenSource? _active;
    private bool _paused;

    /// <summary>False leaves the queue for deterministic calls to <see cref="RunPending"/> in tests.</summary>
    public LibrarySectionAnalysis(PlayerMemory memory, SectionAnalysisCache cache,
        Func<string, CancellationToken, EpisodeAnalysis> analyzer, Rex.Media.Diagnostics.RexLog log, bool background = true)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        if (background)
        {
            _worker = new Thread(Work)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "rexplayer library section analysis",
            };
            _worker.Start();
        }
    }

    /// <summary>Replaces the library's known video files, including when a scan adds or removes files.</summary>
    public void SetQueue(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var eligible = paths.Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
                && Extensions.Contains(Path.GetExtension(path)) && EpisodeGrouping.Identify(path) is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        lock (_gate)
        {
            if (_stop.IsCancellationRequested)
            {
                return;
            }

            _files = eligible;
            _pending = [.. eligible];
            _active?.Cancel();
        }

        _wake.Set();
    }

    /// <summary>When playback begins, interrupt even the currently decoding episode.</summary>
    public void SetPaused(bool paused)
    {
        lock (_gate)
        {
            if (_stop.IsCancellationRequested || _paused == paused)
            {
                return;
            }

            _paused = paused;
            if (paused)
            {
                _active?.Cancel();
            }
        }

        _wake.Set();
    }

    /// <summary>Consume everything currently queued, or return when paused or stopped.</summary>
    public void RunPending()
    {
        while (true)
        {
            string path;
            string[] files;
            CancellationTokenSource active;
            lock (_gate)
            {
                if (_stop.IsCancellationRequested || _paused || _pending.Count == 0)
                {
                    return;
                }

                path = _pending[0];
                _pending.RemoveAt(0);
                files = _files;
                _active = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                active = _active;
            }

            try
            {
                Analyze(path, files, active.Token);
            }
            catch (OperationCanceledException) when (active.IsCancellationRequested)
            {
                // Playing takes priority; this episode is placed back in the queue below.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or
                Rex.Media.Primitives.MediaFormatException or InvalidOperationException or ArgumentException)
            {
                _log.Warning("sections", "Could not analyse library episode " + path + ": " + ex.Message);
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_active, active))
                    {
                        _active = null;
                    }

                    if (active.IsCancellationRequested && !_stop.IsCancellationRequested &&
                        _files.Contains(path, StringComparer.OrdinalIgnoreCase) &&
                        !_pending.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        _pending.Insert(0, path);
                    }
                }

                active.Dispose();
            }
        }
    }

    private void Analyze(string path, IReadOnlyList<string> files, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var folder = Path.GetDirectoryName(path);
        var inFolder = files.Where(other => string.Equals(Path.GetDirectoryName(other), folder,
            StringComparison.OrdinalIgnoreCase));
        var siblings = EpisodeGrouping.Siblings(path, inFolder, SectionDetector.SiblingsCompared);
        var signature = PlayerController.DetectionSignature(path, siblings);
        if (_memory.Proposals(path, signature) is not null)
        {
            return;
        }

        EpisodeAnalysis Features(string file)
        {
            token.ThrowIfCancellationRequested();
            var stamp = SectionAnalysis.SignatureOf(file);
            if (_cache.Get(file, stamp) is { } kept)
            {
                return kept;
            }

            var made = _analyzer(file, token);
            token.ThrowIfCancellationRequested();
            _cache.Set(made);
            return made;
        }

        var episode = Features(path);
        var compared = new List<EpisodeAnalysis>();
        foreach (var sibling in siblings)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                compared.Add(Features(sibling));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or
                Rex.Media.Primitives.MediaFormatException or InvalidOperationException)
            {
                _log.Warning("sections", "Could not analyse sibling " + sibling + ": " + ex.Message);
            }
        }

        token.ThrowIfCancellationRequested();
        var result = SectionDetector.Detect(episode, compared) with { Signature = signature };
        _memory.SetProposals(path, result);
    }

    private void Work()
    {
        while (!_stop.IsCancellationRequested)
        {
            RunPending();
            _wake.WaitOne(TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>Stop promptly, without preventing the app from closing if a decoder misbehaves.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_stop.IsCancellationRequested)
            {
                return;
            }

            _stop.Cancel();
            _active?.Cancel();
        }

        _wake.Set();
        if (_worker is null || _worker.Join(TimeSpan.FromSeconds(5)))
        {
            _wake.Dispose();
            _stop.Dispose();
        }
    }
}
