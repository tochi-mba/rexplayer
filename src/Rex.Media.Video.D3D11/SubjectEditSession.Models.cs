using System.Diagnostics;
using Rex.Media.Primitives;
using Rex.Media.Vision;

namespace Rex.Media.Video.D3D11;

/// <summary>
/// The optional models (ADR-0019) in Subject Lock. They take seconds a picture on most computers,
/// so they work only on a picture that stays on screen, a paused one, and always off the presenting
/// thread: a click is outlined by the segmenter, and a removal preview is filled by the inpainter,
/// while the picture shows as it was (or with the hand-written fill) and the status says the model
/// is working. Any new picture, a new click or a reset cancels the work, and whenever a model is
/// missing, slow (<see cref="SubjectModelPolicy"/>), unsure or failing, the hand-written methods
/// are used instead. A finished answer is applied the next time the picture is processed, which
/// the presenter arranges (<see cref="HasModelAnswer"/>).
/// </summary>
public sealed partial class SubjectEditSession
{
    private readonly FrameBudget _segmentBudget = new();
    private readonly FrameBudget _fillBudget = new();
    private IPromptSegmenter? _segmenter;
    private IInpainter? _inpainter;
    private IMaskTracker? _maskTracker;
    private bool _clickPaused;
    private ModelWork<PictureMask>? _outlining;
    private ModelWork<byte[]>? _filling;
    private VideoFrame? _modelFill;
    private (long Generation, MediaTime Pts, ulong Hole) _modelFillKey;
    private byte[] _hole = [];

    /// <summary>The status while the segmenter outlines a clicked object.</summary>
    public const string FindingStatus = "Finding the object...";

    /// <summary>Where the removal preview's estimated pixels come from, or that a model is working on them.</summary>
    public RemovalFillSource FillSource { get; private set; }

    /// <summary>
    /// Lets Subject Lock use the optional models: <paramref name="segmenter"/> outlines a clicked
    /// object, <paramref name="inpainter"/> fills a removal, and <paramref name="tracker"/> confirms
    /// a return the hand-written search proposed, when it is fast enough here. Null for any keeps the
    /// hand-written method. The session never disposes them; work in flight is cancelled.
    /// </summary>
    public void UseModels(IPromptSegmenter? segmenter, IInpainter? inpainter, IMaskTracker? tracker)
    {
        CancelModelWork();
        (_segmenter, _inpainter, _maskTracker) = (segmenter, inpainter, tracker);
    }

    /// <summary>
    /// Whether a model has answered for the picture on screen, so processing it again (a paused
    /// redraw) shows the answer.
    /// </summary>
    public bool HasModelAnswer =>
        _outlining is { Done: true } || (_filling is { Done: true } && Erase);

    /// <summary>Drop any model work in flight and the fill kept for a paused picture.</summary>
    private void CancelModelWork()
    {
        _outlining?.Cancel();
        _outlining = null;
        _filling?.Cancel();
        _filling = null;
        _modelFill?.Dispose();
        _modelFill = null;
        FillSource = RemovalFillSource.Synthesised;
    }

    /// <summary>
    /// The model's part in settling a click: null when the hand-written choice should settle it now,
    /// true when the segmenter's outline was accepted and set as the region, false while it works.
    /// </summary>
    private bool? SettleClickWithModel(VideoFrame frame, float x, float y)
    {
        if (_outlining is { } work)
        {
            if (!work.Describes(frame))
            {
                // The video moved on (playback resumed or the position changed) before the answer.
                CancelModelWork();
                return null;
            }

            if (!work.Done)
            {
                Status = FindingStatus;
                return false;
            }

            _outlining = null;
            _segmentBudget.Record(work.Elapsed);
            var mask = work.Answer;
            var verdict = mask is null ? MaskVerdict.Empty : SubjectModelPolicy.Judge(mask);
            if (verdict != MaskVerdict.Accepted)
            {
                LastModelVerdict = verdict;
                return null;
            }

            var bounds = mask!.Bounds;
            Region = (bounds.Left / (float)frame.Width, bounds.Top / (float)frame.Height,
                bounds.Width / (float)frame.Width, bounds.Height / (float)frame.Height);
            _removal.UseShape(mask.Values, mask.Width, mask.Height, frame.Pts);
            _maskTracker?.Start(mask);
            LastModelVerdict = verdict;
            return true;
        }

        if (!SubjectModelPolicy.UseOnPicture(_segmenter is not null, _clickPaused, _segmentBudget))
        {
            return null;
        }

        var segmenter = _segmenter!;
        var picture = Copy(frame);
        var key = new PictureKey(frame.Generation, frame.Pts.IsKnown ? frame.Pts.Ticks : 0);
        var (px, py) = (Math.Clamp((int)(x * frame.Width), 0, frame.Width - 1), Math.Clamp((int)(y * frame.Height), 0, frame.Height - 1));
        _outlining = ModelWork<PictureMask>.Start(frame, () =>
        {
            return segmenter.Segment(new BgraPicture(picture.Pixels, picture.Width, picture.Height, picture.Stride), key, SegmentPrompt.At(px, py));
        });
        Status = FindingStatus;
        return false;
    }

    /// <summary>What became of the last outline the segmenter drew for a click.</summary>
    public MaskVerdict? LastModelVerdict { get; private set; }

    /// <summary>
    /// The inpainter's fill for a paused preview: the preview already drawn by hand is the model's
    /// picture and the estimated pixels its hole. Returns the fill when it is ready for exactly this
    /// picture and hole; otherwise starts the work (once) and returns null.
    /// </summary>
    private VideoFrame? ModelFill(VideoFrame frame, VideoFrame preview)
    {
        FillSource = RemovalFillSource.Synthesised;
        if (!SubjectModelPolicy.UseOnPicture(_inpainter is not null, paused: true, _fillBudget))
        {
            return null;
        }

        var pixels = frame.Width * frame.Height;
        if (_hole.Length != pixels)
        {
            _hole = new byte[pixels];
        }

        if (_removal.EstimatedHole(_hole, frame.Width) == 0)
        {
            return null;
        }

        var key = (frame.Generation, frame.Pts, Hash(_hole));
        if (_modelFill is not null && _modelFillKey == key)
        {
            FillSource = RemovalFillSource.Model;
            return _modelFill;
        }

        if (_filling is { } work && _modelFillKey == key)
        {
            if (!work.Done)
            {
                FillSource = RemovalFillSource.ModelWorking;
                return null;
            }

            _filling = null;
            _fillBudget.Record(work.Elapsed);
            if (work.Answer is not { } filled)
            {
                return null;
            }

            _modelFill?.Dispose();
            _modelFill = FrameFrom(filled, frame);
            FillSource = RemovalFillSource.Model;
            return _modelFill;
        }

        // A new picture or a new hole (the sliders moved): the old work no longer applies.
        _filling?.Cancel();
        _modelFill?.Dispose();
        _modelFill = null;
        _modelFillKey = key;
        var inpainter = _inpainter!;
        var picture = Copy(preview);
        var hole = new PictureMask((byte[])_hole.Clone(), frame.Width, frame.Height, 1);
        _filling = ModelWork<byte[]>.Start(frame, () =>
        {
            var output = new byte[picture.Pixels.Length];
            inpainter.Fill(new BgraPicture(picture.Pixels, picture.Width, picture.Height, picture.Stride), hole, output);
            return output;
        });
        FillSource = RemovalFillSource.ModelWorking;
        return null;
    }

    /// <summary>A packed copy of a BGRA picture that a worker may own.</summary>
    private static (byte[] Pixels, int Width, int Height, int Stride) Copy(VideoFrame frame)
    {
        var stride = frame.Width * 4;
        var pixels = new byte[stride * frame.Height];
        for (var y = 0; y < frame.Height; y++)
        {
            frame.Row(0, y)[..stride].CopyTo(pixels.AsSpan(y * stride, stride));
        }

        return (pixels, frame.Width, frame.Height, stride);
    }

    private static VideoFrame FrameFrom(byte[] pixels, VideoFrame like)
    {
        var frame = VideoFrame.Rent(PixelFormat.Bgra32, like.Width, like.Height);
        var stride = like.Width * 4;
        for (var y = 0; y < like.Height; y++)
        {
            pixels.AsSpan(y * stride, stride).CopyTo(frame.Row(0, y));
        }

        return frame;
    }

    /// <summary>FNV-1a over the hole, eight bytes at a time: equal holes give equal keys.</summary>
    private static ulong Hash(ReadOnlySpan<byte> hole)
    {
        var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(hole);
        var hash = 14695981039346656037UL;
        foreach (var word in words)
        {
            hash = (hash ^ word) * 1099511628211UL;
        }

        for (var i = words.Length * 8; i < hole.Length; i++)
        {
            hash = (hash ^ hole[i]) * 1099511628211UL;
        }

        return hash;
    }
}


/// <summary>
/// One piece of model work on a worker, for one picture. A model call cannot be interrupted, so
/// cancelling drops the work: its answer is never read. A failure is logged and reads as no answer,
/// so the caller falls back to its own method.
/// </summary>
internal sealed class ModelWork<T>
    where T : class
{
    private readonly Task<T?> _task;
    private readonly long _generation;
    private readonly MediaTime _pts;
    private long _elapsedTicks;
    private volatile bool _cancelled;

    private ModelWork(VideoFrame frame, Func<T?> work)
    {
        (_generation, _pts) = (frame.Generation, frame.Pts);
        _task = Task.Run(() =>
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                // Work dropped before it started never runs the model.
                return _cancelled ? null : work();
            }
            catch (Exception failure)
            {
                Trace.TraceError("A Subject Lock model failed; the hand-written method is used instead: {0}", failure);
                return null;
            }
            finally
            {
                Interlocked.Exchange(ref _elapsedTicks, Stopwatch.GetElapsedTime(started).Ticks);
            }
        }, CancellationToken.None);
    }

    public static ModelWork<T> Start(VideoFrame frame, Func<T?> work) => new(frame, work);

    public bool Done => _task.IsCompleted;

    /// <summary>How long the model took, once <see cref="Done"/>.</summary>
    public TimeSpan Elapsed => TimeSpan.FromTicks(Interlocked.Read(ref _elapsedTicks));

    /// <summary>The answer once <see cref="Done"/>; null when the model failed or found nothing.</summary>
    public T? Answer => _task.IsCompletedSuccessfully ? _task.Result : null;

    /// <summary>Whether this work is about the picture <paramref name="frame"/>.</summary>
    public bool Describes(VideoFrame frame) => frame.Generation == _generation && frame.Pts == _pts;

    /// <summary>Drops the work; the worker sees the cancellation if it has not started the model yet.</summary>
    public void Cancel() => _cancelled = true;

    /// <summary>Waits for the worker, so tests can settle its answer.</summary>
    internal void Wait() => _task.Wait();
}