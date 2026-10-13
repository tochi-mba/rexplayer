using System.Diagnostics;
using Rex.Media.Primitives;

namespace Rex.Media.Video.D3D11;

/// <summary>
/// Click to lock: one click becomes the box around it that this tracker can follow best. The
/// candidate boxes and their objectness come from <see cref="SubjectProposals"/>; the choice
/// between them is made here, with the tracker's own measures, because only the tracker knows
/// what it can hold on to. A box is lockable when the acquisition check would accept it (visible
/// structure, or a colour unlike its border) and when no place beside it looks so alike that the
/// tracker could slide onto it. Only boxes where something stands out from its surroundings are
/// considered (<see cref="SubjectProposals.StandsOut"/>), so a click on foliage, a tiled wall or
/// a plain surface does not lock a patch of background. The most object-like lockable box wins;
/// when none qualifies the click is refused and the status says why, instead of locking a box
/// that would fail later.
/// </summary>
public sealed partial class SubjectEditSession
{
    private readonly SubjectProposals _proposals = new();
    private readonly Patch _candidate = new();
    private readonly Patch _beside = new();
    private (float X, float Y)? _click;

    /// <summary>How long the last click took to settle into a box, in milliseconds.</summary>
    public double LastSelectionMilliseconds { get; private set; }

    /// <summary>
    /// Select the subject around a click at (<paramref name="x"/>, <paramref name="y"/>), in
    /// uncropped decoded picture coordinates from 0 to 1. The click is settled on the next picture
    /// <see cref="Process"/> sees, playing or paused: that picture's best lockable box becomes the
    /// <see cref="Region"/>, or, when nothing there can be locked, the selection is dropped and
    /// <see cref="Status"/> says why. False, with a status, when the click is outside the picture.
    /// </summary>
    public bool SelectAround(float x, float y)
    {
        if (!(x >= 0 && x <= 1 && y >= 0 && y <= 1))
        {
            Status = "Click inside the picture to select a subject.";
            return false;
        }

        Reset();
        _click = (x, y);
        // A placeholder until the click is settled; nothing is tracked or drawn from it.
        Region = (Math.Clamp(x - 0.005f, 0, 0.99f), Math.Clamp(y - 0.005f, 0, 0.99f), 0.01f, 0.01f);
        Locked = true;
        Status = "Acquiring: finding the best box around the click.";
        return true;
    }

    /// <summary>
    /// Turn the pending click into a region on this picture. True when a lockable box was chosen
    /// and set as the region; false when the click was refused, which also drops the selection.
    /// </summary>
    private bool SettleClick(VideoFrame frame)
    {
        var (x, y) = _click!.Value;
        _click = null;
        var started = Stopwatch.GetTimestamp();
        MeasureLight(frame);
        _proposals.Find(frame, x, y);
        var chosen = -1;
        var best = double.MinValue;
        var ambiguous = false;
        var proposals = _proposals.Proposals;
        for (var i = 0; i < proposals.Length; i++)
        {
            var p = proposals[i];
            // Proposals come best first and a neighbour can only lower a rank, so once one
            // cannot beat the best so far, none after it can.
            if (p.Score <= best || !SubjectProposals.StandsOut(p.Score)) break;
            if (!Lockable(frame, p)) continue;
            var resemblance = NearestLookalike(frame, p);
            if (SubjectProposals.IsAmbiguous(resemblance))
            {
                ambiguous = true;
                continue;
            }

            var rank = SubjectProposals.Rank(p.Score, resemblance);
            if (rank <= best) continue;
            (chosen, best) = (i, rank);
        }

        LastSelectionMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (chosen < 0)
        {
            Locked = false;
            Status = ambiguous
                ? "Similar-looking regions around the click: the tracker could not tell them apart. Click a more distinctive part, or drag a box."
                : "Nothing to lock around the click: nothing there stands out with clear edges and enough detail to follow. Click a distinct object, or drag a box.";
            return false;
        }

        var box = proposals[chosen];
        Region = (box.Left / (float)frame.Width, box.Top / (float)frame.Height,
            box.Width / (float)frame.Width, box.Height / (float)frame.Height);
        return true;
    }

    /// <summary>The acquisition check itself: visible structure, or a colour unlike the box's border.</summary>
    private bool Lockable(VideoFrame frame, SubjectProposal box)
    {
        Describe(frame, box.Left, box.Top, box.Width, box.Height, _candidate);
        if (IsDistinctive(_candidate)) return true;
        var (centre, border, _) = CompareSubjectWithBorder(frame, (box.Left, box.Top, box.Left + box.Width, box.Top + box.Height));
        return Math.Abs(centre.B - border.B) + Math.Abs(centre.G - border.G) + Math.Abs(centre.R - border.R) >= 40;
    }

    /// <summary>
    /// The tracker's similarity between the box (already in <c>_candidate</c>) and the most alike
    /// of the same-sized places around it, from three quarters of a box away to one and a half:
    /// the ring the tracker searches each picture, where a lookalike could pull the lock across.
    /// Several distances are needed because a repeating pattern matches itself only at its own
    /// period, which is rarely a whole box-width.
    /// </summary>
    private double NearestLookalike(VideoFrame frame, SubjectProposal box)
    {
        var most = 0d;
        ReadOnlySpan<float> distances = [0.75f, 1f, 1.25f, 1.5f];
        foreach (var distance in distances)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var left = box.Left + (int)(dx * distance * box.Width);
                    var top = box.Top + (int)(dy * distance * box.Height);
                    if ((dx == 0 && dy == 0) || left < 0 || top < 0
                        || left + box.Width > frame.Width || top + box.Height > frame.Height) continue;
                    Describe(frame, left, top, box.Width, box.Height, _beside);
                    most = Math.Max(most, Similarity(_candidate, _beside));
                }
            }
        }

        return most;
    }
}
