using Rex.Media.Primitives;

namespace Rex.Media.Video.D3D11;

public sealed partial class SubjectEditSession
{
    private readonly bool _asynchronousRecovery;
    private readonly TaskScheduler _recoveryScheduler;
    private Task<SubjectEditSession>? _recoveryTask;
    private CancellationTokenSource? _recoveryCancellation;
    private MediaTime _recoveryTime = MediaTime.Unknown;

    /// <summary>Skip GPU readback while a lost-target search already owns its snapshot.</summary>
    internal bool CanPresentWithoutAnalysis(VideoFrame frame)
    {
        if (_recoveryTask is not { IsCompleted: false } || Tracking
            || _generation != frame.Generation || _width != frame.Width || _height != frame.Height
            || (_lastPts.IsKnown && frame.Pts.IsKnown
                && (frame.Pts < _lastPts || frame.Pts - _lastPts > MediaTime.FromSeconds(2))))
            return false;
        _lastPts = frame.Pts;
        return true;
    }

    /// <summary>Screen presentation searches on a worker; offline analysis can run deterministically.</summary>
    public SubjectEditSession(bool asynchronousRecovery = false)
        : this(asynchronousRecovery, TaskScheduler.Default)
    {
    }

    internal SubjectEditSession(bool asynchronousRecovery, TaskScheduler recoveryScheduler)
    {
        _asynchronousRecovery = asynchronousRecovery;
        _recoveryScheduler = recoveryScheduler;
    }

    /// <summary>Cancel outstanding analysis and release cached pictures.</summary>
    public void Dispose() => Reset();

    private void CancelRecovery()
    {
        _recoveryCancellation?.Cancel();
        _recoveryCancellation?.Dispose();
        _recoveryCancellation = null;
        _recoveryTask = null;
        _recoveryTime = MediaTime.Unknown;
    }

    private bool RecoverWithoutBlocking(VideoFrame frame)
    {
        if (_recoveryTask is { IsCompleted: false }) return false;
        if (_recoveryTask is { } completed)
        {
            _recoveryTask = null;
            _recoveryCancellation?.Dispose();
            _recoveryCancellation = null;
            if (completed.IsCompletedSuccessfully)
            {
                var found = completed.Result;
                var stale = frame.Pts.IsKnown && _recoveryTime.IsKnown
                    && frame.Pts - _recoveryTime > MediaTime.FromSeconds(0.5);
                if (stale)
                {
                    _returnCandidate = null;
                    _returnConfirmations = 0;
                }
                if (!stale)
                {
                    _returnCandidate = found._returnCandidate;
                    _returnConfirmations = found._returnConfirmations;
                    LastSearchComparisons = found.LastSearchComparisons;
                    if (found.Tracking)
                    {
                        // A result describes its captured frame, not today's pixels.
                        // Verify continuity on the current frame before moving the box.
                        Region = found.Region;
                        _reference = found._reference;
                        _reacquiredFrames = 15;
                        Tracking = true;
                        Erase = false;
                        Track(frame);
                        if (Tracking)
                        {
                            _lostFrames = 0;
                            return true;
                        }
                    }
                }
            }
            else if (completed.Exception is { } failure)
            {
                // Observe a failed worker even when the target was reset meanwhile.
                System.Diagnostics.Trace.TraceError("Subject recovery failed: {0}", failure);
            }
        }

        var snapshot = ColorConverter.ToBgra(frame);
        var worker = new SubjectEditSession
        {
            _returnReference = _returnReference,
            _returnAppearance = _returnAppearance,
            _identityWidth = _identityWidth,
            _identityHeight = _identityHeight,
            _originalBorderContrast = _originalBorderContrast,
            _returnCandidate = _returnCandidate,
            _returnConfirmations = _returnConfirmations,
        };
        _recoveryTime = frame.Pts;
        _recoveryCancellation = new CancellationTokenSource();
        var cancellation = _recoveryCancellation.Token;
        // No queue: one immutable snapshot is owned by one worker. New video frames
        // keep presenting and the next search captures the latest available picture.
        _recoveryTask = Task.Factory.StartNew(() =>
        {
            using (snapshot)
            {
                try
                {
                    worker.TryReacquire(snapshot, cancellation);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // Reset/seek owns cancellation; no result is published to the session.
                }
                catch (Exception failure)
                {
                    System.Diagnostics.Trace.TraceError("Subject recovery failed: {0}", failure);
                }
                return worker;
            }
        }, CancellationToken.None, TaskCreationOptions.DenyChildAttach, _recoveryScheduler);
        return false;
    }
}
