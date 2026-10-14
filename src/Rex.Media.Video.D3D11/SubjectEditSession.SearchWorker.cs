using Rex.Media.Primitives;
using Rex.Media.Vision;

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
                // A result found in a picture from before a cut describes a shot that is gone.
                var stale = _shotChanged || (frame.Pts.IsKnown && _recoveryTime.IsKnown
                    && frame.Pts - _recoveryTime > MediaTime.FromSeconds(0.5));
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
                    LastReturn = found.LastReturn;
                    if (found.Tracking)
                    {
                        // A result describes its captured frame, not today's pixels.
                        // Verify continuity on the current frame before moving the box.
                        Region = found.Region;
                        _reacquiredFrames = 12;
                        Tracking = true;
                        Erase = false;
                        Track(frame);
                        if (Tracking)
                        {
                            // Views the worker learned while confirming the return are worth
                            // keeping, but only once the live picture has agreed with them.
                            // At capacity, adding a view replaces an old one without
                            // changing Count. Transfer new instances, not a suffix by count.
                            foreach (var view in found._positive)
                                if (!_positive.Contains(view)) RememberSubject(view);
                            foreach (var view in found._negative)
                                if (!_negative.Contains(view)) RememberOther(view);
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

        ForgetSightingAcrossACut();
        var snapshot = ColorConverter.ToBgra(frame);
        var worker = new SubjectEditSession
        {
            _identityWidth = _identityWidth,
            _identityHeight = _identityHeight,
            _returnCandidate = _returnCandidate,
            _returnConfirmations = _returnConfirmations,
        };
        // The worker reads the model and never writes back into this session's copy, so the
        // presenting thread can keep tracking and learning while a search is in flight.
        worker._positive.AddRange(_positive);
        worker._negative.AddRange(_negative);
        _weights.CopyTo(worker._weights, 0);
        worker._weightTotal = _weightTotal;
        _recoveryTime = frame.Pts;
        _recoveryCancellation = new CancellationTokenSource();
        var cancellation = _recoveryCancellation.Token;
        // A model confirms the return only where it answers within the interactive budget here.
        var secondOpinion = SubjectModelPolicy.SecondOpinion(_maskTracker is not null, _segmentBudget, FramesPerSecond(frame))
            ? _maskTracker
            : null;
        // No queue: one immutable snapshot is owned by one worker. New video frames
        // keep presenting and the next search captures the latest available picture.
        _recoveryTask = Task.Factory.StartNew(() =>
        {
            using (snapshot)
            {
                try
                {
                    worker.TryReacquire(snapshot, cancellation);
                    if (secondOpinion is not null && worker.Tracking)
                    {
                        worker.Tracking = Confirms(secondOpinion, snapshot, worker.Region);
                    }
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

    /// <summary>
    /// The model's second opinion on a return the hand-written search found: the object it outlines
    /// in the proposed box must overlap that box by at least a third (intersection over union).
    /// </summary>
    private static bool Confirms(IMaskTracker tracker, VideoFrame snapshot, (float Left, float Top, float Width, float Height) region)
    {
        var (w, h) = (snapshot.Width, snapshot.Height);
        var box = new PixelBox((int)(region.Left * w), (int)(region.Top * h), Math.Max(1, (int)(region.Width * w)), Math.Max(1, (int)(region.Height * h)));
        var picture = new BgraPicture(snapshot.Plane(0), w, h, snapshot.Stride(0));
        var answer = tracker.Reacquire(picture, new PictureKey(snapshot.Generation, snapshot.Pts.IsKnown ? snapshot.Pts.Ticks : 0), box);
        if (answer is not { Outcome: TrackOutcome.Tracked, Mask: { } mask })
        {
            return false;
        }

        var found = mask.Bounds;
        var across = Math.Max(0, Math.Min(found.Right, box.Right) - Math.Max(found.Left, box.Left));
        var down = Math.Max(0, Math.Min(found.Bottom, box.Bottom) - Math.Max(found.Top, box.Top));
        var both = (double)across * down;
        return both / ((double)found.Width * found.Height + (double)box.Width * box.Height - both) >= 1 / 3d;
    }

    /// <summary>The video's frame rate from a picture's duration, or 24 when it does not say.</summary>
    private static double FramesPerSecond(VideoFrame frame) =>
        frame.Duration.IsKnown && frame.Duration.TotalSeconds > 0 ? 1 / frame.Duration.TotalSeconds : 24;
}
