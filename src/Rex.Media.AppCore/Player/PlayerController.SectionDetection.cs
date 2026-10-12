namespace Rex.Media.AppCore.Player;

/// <summary>Background proposal scanning. No decoder or filesystem lookup runs on the playback thread.</summary>
public sealed partial class PlayerController
{
    /// <summary>The files a season folder is searched for, for siblings and the next episode.</summary>
    private static readonly string[] EpisodeExtensions = [".mkv", ".mp4", ".m4v", ".avi", ".mov", ".webm", ".ts"];

    private CancellationTokenSource? _sectionStop;
    private SectionProposals? _activeProposals;
    private string? _nextEpisode;
    private ShowSkipping _showSkips = new(false, false);
    private (EpisodeSectionKind Kind, TimeSpan Due)? _automaticSkip;
    private readonly HashSet<EpisodeSectionKind> _automaticallySkipped = [];

    /// <summary>Host-supplied analysis decoder: production uses MediaRegistries with Windows codecs.</summary>
    public Func<string, CancellationToken, EpisodeAnalysis>? SectionAnalyzer { get; set; }

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
            TimeSpan? due = _automaticSkip is { } pending && pending.Kind == skip.Kind ? pending.Due : null;
            return EpisodeSkipPrompt.For(skip, detected, Duration, Position, _nextEpisode is not null, due);
        }
    }

    private void BeginSectionDetection()
    {
        _sectionStop?.Cancel();
        _sectionStop?.Dispose();
        _sectionStop = null;
        _activeProposals = null;
        _nextEpisode = null;
        _automaticSkip = null;
        _automaticallySkipped.Clear();
        // Only local files are analysed; the file system itself is touched on the worker.
        if (!CanMarkEpisodeSections || CurrentEpisode is not { } identity || !Path.IsPathFullyQualified(Item!.Location))
        {
            _showSkips = new ShowSkipping(false, false);
            return;
        }

        _showSkips = Memory.ShowSkips(identity.Show);
        var path = Item.Location;
        var analyzer = SectionAnalyzer;
        var stop = new CancellationTokenSource();
        // Capture the token before the worker is scheduled: a rapid episode change can
        // cancel and dispose its source before the background delegate starts running.
        var token = stop.Token;
        _sectionStop = stop;
        var worker = new Thread(() => FindSections(path, analyzer, token))
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "rexplayer section detection",
        };
        worker.Start();
    }

    /// <summary>
    /// Finds the next episode, then publishes cached proposals or analyses this episode and its
    /// siblings. Sibling features are cached independently: adding an episode doesn't rescan
    /// previously decoded recordings, only the across-episode match is repeated. An unreadable
    /// sibling is left out; an unreadable current episode ends the analysis.
    /// </summary>
    private void FindSections(string path, Func<string, CancellationToken, EpisodeAnalysis>? analyzer,
        CancellationToken token)
    {
        void Publish(SectionProposals? remembered) => _dispatch(() =>
        {
            if (!token.IsCancellationRequested && Item?.Location == path)
            {
                _activeProposals = remembered;
                Changed?.Invoke(this, EventArgs.Empty);
                PositionChanged?.Invoke(this, EventArgs.Empty);
            }
        });

        try
        {
            var folder = Path.GetDirectoryName(path)!;
            var files = Directory.EnumerateFiles(folder)
                .Where(file => EpisodeExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .ToArray();
            var next = EpisodeGrouping.Next(path, files);
            _dispatch(() =>
            {
                if (!token.IsCancellationRequested && Item?.Location == path && next is not null)
                {
                    _nextEpisode = next;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            });

            var signature = SectionAnalysis.SignatureOf(path);
            var cached = Memory.Proposals(path, signature);
            // An empty cached result is tried again: a sibling added since may now agree.
            if (cached is { Candidates.Count: > 0 } || analyzer is null)
            {
                Publish(cached);
                return;
            }

            var targets = new[] { path }.Concat(EpisodeGrouping.Siblings(path, files).Take(24)).ToArray();
            var analyses = new List<EpisodeAnalysis>();
            foreach (var candidate in targets)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var stamp = SectionAnalysis.SignatureOf(candidate);
                    var analysis = Memory.Analysis(candidate, stamp);
                    if (analysis is null)
                    {
                        analysis = analyzer(candidate, token);
                        token.ThrowIfCancellationRequested();
                        Memory.SetAnalysis(analysis);
                    }

                    analyses.Add(analysis);
                }
                catch (Exception ex) when (candidate != path &&
                    ex is IOException or UnauthorizedAccessException or NotSupportedException or
                        Rex.Media.Primitives.MediaFormatException or InvalidOperationException)
                {
                    _log.Warning("sections", "Could not analyse sibling " + candidate + ": " + ex.Message);
                }
            }

            token.ThrowIfCancellationRequested();
            var main = analyses.FirstOrDefault(item => item.Path == path);
            if (main is null)
            {
                return;
            }

            var proposed = SectionDetector.Detect(main, analyses.Where(item => item.Path != path).ToArray());
            token.ThrowIfCancellationRequested();
            Memory.SetProposals(path, proposed);
            Publish(Memory.Proposals(path, signature));
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
    /// An opted-in automatic skip waits a few seconds of playback, shown as a countdown on the
    /// button, so the viewer can cancel it; it never fires twice for one section of one episode.
    /// </summary>
    private void MaybeSkipAutomatically()
    {
        if (!CanMarkEpisodeSections || AvailableEpisodeSkip is not { } available ||
            _automaticallySkipped.Contains(available.Kind) || !SkipsAutomatically(available.Kind))
        {
            _automaticSkip = null;
            return;
        }

        if (_automaticSkip is not { } pending || pending.Kind != available.Kind)
        {
            var due = Position + EpisodeSkipPrompt.AutomaticDelay;
            var latest = available.End - TimeSpan.FromSeconds(1);
            pending = (available.Kind, due < latest ? due : latest);
            _automaticSkip = pending;
        }

        if (Position >= pending.Due)
        {
            _automaticallySkipped.Add(available.Kind);
            _automaticSkip = null;
            SkipEpisodeSection();
        }
    }

    private bool SkipsAutomatically(EpisodeSectionKind kind) => kind == EpisodeSectionKind.Intro
        ? Settings.SkipIntrosAutomatically || _showSkips.Intros
        : Settings.SkipCreditsAutomatically || _showSkips.Credits;

    /// <summary>Stops a counting-down automatic skip; the section plays on and is not skipped again.</summary>
    public bool CancelAutomaticSkip()
    {
        if (_automaticSkip is not { } pending)
        {
            return false;
        }

        _automaticallySkipped.Add(pending.Kind);
        _automaticSkip = null;
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

        _automaticSkip = null;
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

    /// <summary>Reject only this kind of machine suggestion, without touching manual markers.</summary>
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

    /// <summary>Save the selected proposal as explicitly user-approved playback markers.</summary>
    public bool ConfirmDetectedSection(EpisodeSectionKind kind)
    {
        return _activeProposals?.Best(kind, Duration) is { } candidate &&
            SetSectionBounds(kind, candidate.Start, candidate.End);
    }

    /// <summary>Adjust one bound exactly without moving or clearing the other section.</summary>
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
        Say((kind == EpisodeSectionKind.Intro ? "Intro" : "Credits") +
            " boundaries saved. You can adjust or clear them at any time.");
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
