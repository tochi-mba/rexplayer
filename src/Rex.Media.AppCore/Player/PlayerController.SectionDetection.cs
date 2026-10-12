namespace Rex.Media.AppCore.Player;

/// <summary>Background proposal scanning. No decoder or filesystem lookup runs on the playback thread.</summary>
public sealed partial class PlayerController
{
    private CancellationTokenSource? _sectionStop;
    private SectionProposals? _activeProposals;
    private readonly HashSet<EpisodeSectionKind> _automaticallySkipped = [];

    /// <summary>Host-supplied analysis decoder: production uses MediaRegistries with Windows codecs.</summary>
    public Func<string, CancellationToken, EpisodeAnalysis>? SectionAnalyzer { get; set; }

    public SectionProposals? CurrentProposals => _activeProposals;

    private void BeginSectionDetection()
    {
        _sectionStop?.Cancel();
        _sectionStop?.Dispose();
        _sectionStop = null;
        _activeProposals = null;
        _automaticallySkipped.Clear();
        // Only local files are analysed; the file system itself is touched on the worker.
        if (!CanMarkEpisodeSections || Item is not { } current || !Path.IsPathFullyQualified(current.Location) ||
            EpisodeGrouping.Identify(current.Location) is null || SectionAnalyzer is null)
        {
            return;
        }

        var path = current.Location;
        var analyzer = SectionAnalyzer;
        var stop = new CancellationTokenSource();
        // Capture the token before the worker is scheduled: a rapid episode change can
        // cancel and dispose its source before the background delegate starts running.
        var token = stop.Token;
        _sectionStop = stop;
        var worker = new Thread(() =>
        {
            try
            {
                var signature = SectionAnalysis.SignatureOf(path);
                var cached = Memory.Proposals(path, signature);
                if (cached is { Candidates.Count: > 0 })
                {
                    Publish(cached);
                    return;
                }

                // Sibling features are cached independently: adding an episode doesn't rescan
                // previously decoded recordings, only the across-episode match is repeated.
                var identity = EpisodeGrouping.Identify(path)!;
                var folder = Path.GetDirectoryName(path)!;
                var siblingPaths = EpisodeGrouping.Siblings(path, Directory.EnumerateFiles(folder)
                    .Where(file => Path.GetExtension(file).ToLowerInvariant() is ".mkv" or ".mp4" or ".m4v" or ".avi"));
                var targets = new[] { path }.Concat(siblingPaths.Take(24)).ToArray();
                var analyses = new List<EpisodeAnalysis>();
                foreach (var candidate in targets)
                {
                    token.ThrowIfCancellationRequested();
                    if (EpisodeGrouping.Identify(candidate) is not { } match ||
                        match.Show != identity.Show || match.Season != identity.Season)
                    {
                        continue;
                    }

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

            void Publish(SectionProposals? remembered)
            {
                _dispatch(() =>
                {
                    if (!token.IsCancellationRequested && Item?.Location == path)
                    {
                        _activeProposals = remembered;
                        Changed?.Invoke(this, EventArgs.Empty);
                        PositionChanged?.Invoke(this, EventArgs.Empty);
                    }
                });
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "rexplayer section detection",
        };
        worker.Start();
    }

    private void MaybeSkipAutomatically()
    {
        if (!CanMarkEpisodeSections || AvailableEpisodeSkip is not { } available ||
            _automaticallySkipped.Contains(available.Kind) ||
            !(available.Kind == EpisodeSectionKind.Intro
                ? Settings.SkipIntrosAutomatically : Settings.SkipCreditsAutomatically))
        {
            return;
        }

        _automaticallySkipped.Add(available.Kind);
        SkipEpisodeSection();
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
