namespace Rex.Media.Vision;

/// <summary>Where a model's installation stands.</summary>
public enum InstallPhase
{
    /// <summary>Nothing usable is on disk. A partial download may be kept for resuming.</summary>
    NotInstalled,

    /// <summary>Files are being fetched; <see cref="InstallStatus.Done"/> counts the bytes so far.</summary>
    Downloading,

    /// <summary>Every byte has arrived and each file's SHA-256 is being checked.</summary>
    Verifying,

    /// <summary>The files on disk are exactly the ones the catalogue pins.</summary>
    Installed,

    /// <summary>The last attempt stopped; <see cref="InstallStatus.Reason"/> says why.</summary>
    Failed,

    /// <summary>An earlier version is installed and still usable; the catalogue now pins newer files.</summary>
    UpdateAvailable,
}

/// <summary>One moment of an installation, as the Preferences section shows it.</summary>
public readonly record struct InstallStatus(InstallPhase Phase, long Done = 0, long Total = 0, string? Reason = null)
{
    /// <summary>How much of the download has arrived, from 0 to 1.</summary>
    public double Fraction => Total <= 0 ? 0 : Math.Clamp((double)Done / Total, 0, 1);

    /// <summary>Whether a download or a check is running, so nothing else may start.</summary>
    public bool Busy => Phase is InstallPhase.Downloading or InstallPhase.Verifying;
}

/// <summary>
/// The installation of one model as a state machine. Every move is checked against the phase it
/// starts from, so a second download cannot start over a running one and a removal cannot pull
/// files out from under a check. Safe to call from a download thread and the window thread.
/// </summary>
public sealed class InstallStateMachine
{
    private readonly Lock _gate = new();
    private InstallStatus _status;
    private InstallPhase _resting;

    /// <summary>Starts in the phase the store found on disk.</summary>
    public InstallStateMachine(InstallPhase found = InstallPhase.NotInstalled)
    {
        _status = new InstallStatus(Resting(found));
        _resting = found;
    }

    /// <summary>Raised after every move, outside the lock, on the thread that made the move.</summary>
    public event Action<InstallStatus>? Changed;

    public InstallStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    /// <summary>A download of <paramref name="total"/> bytes starts.</summary>
    public void Begin(long total)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(total);
        Move("start a download", status => status.Phase is InstallPhase.NotInstalled or InstallPhase.Failed or InstallPhase.UpdateAvailable,
            status =>
            {
                _resting = status.Phase == InstallPhase.Failed ? _resting : status.Phase;
                return new InstallStatus(InstallPhase.Downloading, 0, total);
            });
    }

    /// <summary>Bytes received so far across every file of the model.</summary>
    public void Report(long done) =>
        Move("report progress", status => status.Phase == InstallPhase.Downloading,
            status => status with { Done = Math.Clamp(done, 0, status.Total) });

    /// <summary>Every byte has arrived; the hashes are being checked.</summary>
    public void Verify() =>
        Move("check the files", status => status.Phase == InstallPhase.Downloading,
            status => status with { Phase = InstallPhase.Verifying, Done = status.Total });

    /// <summary>Every file matched its pinned hash and is in place.</summary>
    public void Complete() =>
        Move("finish", status => status.Phase == InstallPhase.Verifying,
            _ =>
            {
                _resting = InstallPhase.Installed;
                return new InstallStatus(InstallPhase.Installed);
            });

    /// <summary>The download or the check stopped for <paramref name="reason"/>, in plain words for the user.</summary>
    public void Fail(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Move("fail", status => status.Busy, status => status with { Phase = InstallPhase.Failed, Reason = reason });
    }

    /// <summary>The user stopped the download; the model is back where it was, and a partial file is kept.</summary>
    public void Cancel() =>
        Move("cancel", status => status.Busy, _ => new InstallStatus(_resting));

    /// <summary>The files were deleted.</summary>
    public void Removed() =>
        Move("remove the files", status => !status.Busy,
            _ =>
            {
                _resting = InstallPhase.NotInstalled;
                return new InstallStatus(InstallPhase.NotInstalled);
            });

    /// <summary>The store was inspected again (for example after another window changed it).</summary>
    public void Refresh(InstallPhase found)
    {
        var phase = Resting(found);
        Move("refresh", status => !status.Busy,
            _ =>
            {
                _resting = phase;
                return new InstallStatus(phase);
            });
    }

    private static InstallPhase Resting(InstallPhase found) =>
        found is InstallPhase.NotInstalled or InstallPhase.Installed or InstallPhase.UpdateAvailable
            ? found
            : throw new ArgumentOutOfRangeException(nameof(found), found, "Only a phase found on disk can start or refresh the machine.");

    private void Move(string action, Func<InstallStatus, bool> allowed, Func<InstallStatus, InstallStatus> next)
    {
        InstallStatus moved;
        lock (_gate)
        {
            if (!allowed(_status))
            {
                throw new InvalidOperationException($"Cannot {action} while the model is {_status.Phase}.");
            }

            _status = moved = next(_status);
        }

        Changed?.Invoke(moved);
    }
}
