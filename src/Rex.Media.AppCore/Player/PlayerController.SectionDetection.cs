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

        var at = Playlist.CurrentIndex + 1;
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
