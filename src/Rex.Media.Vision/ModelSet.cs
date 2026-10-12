namespace Rex.Media.Vision;

/// <summary>
/// A group of models installed and removed together, such as <see cref="ModelCatalogue.SubjectLock"/>:
/// one state machine per model, started from what the store finds on disk, and the combined view the
/// Preferences section shows. Downloads run one model after another and stop at the first failure.
/// </summary>
public sealed class ModelSet
{
    private readonly ModelStore _store;
    private readonly InstallStateMachine[] _machines;

    public ModelSet(ModelStore store, IReadOnlyList<VisionModel> models)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(models);
        if (models.Count == 0)
        {
            throw new ArgumentException("A set holds at least one model.", nameof(models));
        }

        Models = models;
        _machines = [.. models.Select(model => new InstallStateMachine(store.Inspect(model)))];
        foreach (var machine in _machines)
        {
            machine.Changed += _ => Changed?.Invoke();
        }
    }

    /// <summary>Raised after any model's state moves, on the thread that moved it.</summary>
    public event Action? Changed;

    public IReadOnlyList<VisionModel> Models { get; }

    public IReadOnlyList<InstallStatus> Statuses => [.. _machines.Select(machine => machine.Status)];

    /// <summary>Whether every model is installed at the catalogue's version and may be loaded.</summary>
    public bool Ready => _machines.All(machine => machine.Status.Phase == InstallPhase.Installed);

    public ModelDownloadView View => ModelDownloadText.Describe(Models, Statuses);

    /// <summary>Downloads every model not yet installed at the catalogue's version.</summary>
    public async Task DownloadAsync(ModelDownloader downloader, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        for (var i = 0; i < Models.Count; i++)
        {
            if (_machines[i].Status.Phase == InstallPhase.Installed)
            {
                continue;
            }

            await downloader.InstallAsync(Models[i], _machines[i], cancellation).ConfigureAwait(false);
            if (_machines[i].Status.Phase != InstallPhase.Installed)
            {
                return;
            }
        }
    }

    /// <summary>Deletes every model's files. Refused while a download runs.</summary>
    public void Remove()
    {
        if (_machines.Any(machine => machine.Status.Busy))
        {
            throw new InvalidOperationException("Stop the download before removing the models.");
        }

        for (var i = 0; i < Models.Count; i++)
        {
            _store.Remove(Models[i]);
            _machines[i].Removed();
        }
    }
}
