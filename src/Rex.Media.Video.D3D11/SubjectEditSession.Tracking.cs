using Rex.Media.Primitives;

namespace Rex.Media.Video.D3D11;

/// <summary>Frame-to-frame following of a subject that is currently visible.</summary>
public sealed partial class SubjectEditSession
{
    // Following a visible subject and finding a vanished one are different problems. This search
    // assumes the subject is near where it was a fortieth of a second ago and spends its budget on
    // precision; the re-detection search assumes nothing about position and spends its budget on
    // coverage. Keeping them apart stops a brief stumble from triggering a whole-picture hunt.
    private const double HoldSubject = 0.575;
    private const double HoldRelative = 0.50;
    // One modest push per frame. Following runs on every picture, so the correction only has to
    // cancel one frame's worth of creep, and a larger one would let the box inflate just as fast.
    private const int OutlineRounds = 1;
    private const int OutlineStep = 12;

    private void Track(VideoFrame frame)
    {
        if (_positive.Count == 0) return;

        // A subject that just returned may be moving quickly, and the box that recovery produced
        // is only approximately placed, so widen the window for the first few frames afterwards.
        var span = Math.Min(frame.Width * Region.Width, frame.Height * Region.Height);
        var radius = Math.Clamp((int)Math.Round(span * (_reacquiredFrames > 0 ? 2.4 : 0.8)), 12, 110);
        // One step must not move the subject by more than about one cell of the descriptor, or
        // the search samples a jagged score surface and the refinement that follows climbs
        // whichever false peak it happened to land nearest.
        var coarse = Math.Clamp((int)(span / Samples), 1, Math.Max(1, radius / 4));
        var best = new Match(double.MinValue, 0, 0, Region.Width, Region.Height);

        var shape = Math.Log((double)_identityWidth / Math.Max(1, _identityHeight));
        // Returns how much the place resembles the subject, or nothing when it is not a place.
        double TryMatch(int dx, int dy, float width, float height)
        {
            var left = Region.Left + (float)dx / frame.Width;
            var top = Region.Top + (float)dy / frame.Height;
            if (left < 0 || top < 0 || left + width > 1 || top + height > 1) return double.MinValue;
            if (Distortion((int)Math.Round(width * frame.Width),
                (int)Math.Round(height * frame.Height)) > MaxDistortion) return double.MinValue;
            Describe(frame, (int)Math.Round(left * frame.Width), (int)Math.Round(top * frame.Height),
                Math.Max(2, (int)Math.Round(width * frame.Width)),
                Math.Max(2, (int)Math.Round(height * frame.Height)), _probe);
            // A repeating pattern - railings, paving, a row of identical seats - matches equally
            // well in many places. Preferring the nearest of those keeps the box on the subject
            // instead of letting it slide along the repetition.
            // Measured against the subject's own size, so the same preference applies whether it
            // fills the picture or sits far away: a thing does not usually move its own width
            // between two frames a fortieth of a second apart.
            var alike = Resembles(_probe);
            var score = alike
                - (_reacquiredFrames > 0 ? 0.02 : 0.10) * Math.Sqrt((double)dx * dx + (double)dy * dy) / Math.Max(1, span)
                - 0.015 * Math.Abs(Math.Log(width / Region.Width))
                // Size may change freely as the subject approaches or recedes, but its shape may
                // not. Without this the box slowly settles onto whichever part of the subject
                // matches best - a pair of trousers rather than the person - and once it has,
                // every learned view reinforces the smaller thing.
                - 0.06 * Math.Abs(Math.Log(width * frame.Width / (height * frame.Height)) - shape);
            if (score > best.Score) best = new Match(score, dx, dy, width, height, alike);
            return alike;
        }

        // The coarse grid is the bulk of the work and nearly all of it is rejected, so it is
        // ranked on shape plus a sixteen-sample colour reading, which together cost about half
        // a full reading. Only the few places that could be the subject are then read fully,
        // and the choice between them is made on the full comparison. Several places are kept
        // rather than one because a repeating pattern or a shadow can put a coarse peak a
        // little way from the real subject.
        Span<Match> leads = stackalloc Match[Leads];
        leads.Fill(new Match(double.MinValue, 0, 0, Region.Width, Region.Height));
        var boxLeft = (int)Math.Round(Region.Left * frame.Width);
        var boxTop = (int)Math.Round(Region.Top * frame.Height);
        var boxWidth = Math.Max(2, (int)Math.Round(Region.Width * frame.Width));
        var boxHeight = Math.Max(2, (int)Math.Round(Region.Height * frame.Height));
        var nearPenalty = (_reacquiredFrames > 0 ? 0.02 : 0.10) / Math.Max(1, span);
        for (var dy = -radius; dy <= radius; dy += coarse)
        {
            if (boxTop + dy < 0 || boxTop + dy + boxHeight > frame.Height) continue;
            for (var dx = -radius; dx <= radius; dx += coarse)
            {
                if (boxLeft + dx < 0 || boxLeft + dx + boxWidth > frame.Width) continue;
                DescribeStructure(frame, boxLeft + dx, boxTop + dy, boxWidth, boxHeight, _spare);
                var score = 0.55 * StructuralResemblance(_spare)
                    + 0.45 * QuickColour(frame, boxLeft + dx, boxTop + dy, boxWidth, boxHeight)
                    - nearPenalty * Math.Sqrt((double)dx * dx + (double)dy * dy);
                Lead(leads, score, dx, dy, coarse * 2);
            }
        }

        // The full comparison decides between the leads. Two leads a box apart that both look
        // like the subject within the rival margin are two things the model cannot tell apart,
        // and following either is a guess: a lock that quietly slides onto a team-mate in the
        // same strip. Reporting the ambiguity costs a few frames of tracking; guessing costs the
        // subject, because every view learned from then on teaches the model the wrong thing.
        TryMatch(0, 0, Region.Width, Region.Height);
        Span<double> alike = stackalloc double[Leads];
        for (var i = 0; i < Leads; i++)
        {
            alike[i] = double.MinValue;
            if (leads[i].Score == double.MinValue) continue;
            alike[i] = TryMatch(leads[i].Dx, leads[i].Dy, Region.Width, Region.Height);
        }

        for (var i = 0; i < Leads; i++)
        {
            if (alike[i] < HoldSubject) continue;
            if (Math.Abs(leads[i].Dx - best.Dx) < boxWidth * 0.7 && Math.Abs(leads[i].Dy - best.Dy) < boxHeight * 0.7) continue;
            if (alike[i] <= best.Alike - RivalMargin) continue;
            LoseTracking("Similar-looking regions: watching for a clear match. Select again if necessary.");
            return;
        }

        for (var step = coarse; step > 1;)
        {
            var (roughX, roughY, window) = (best.Dx, best.Dy, step);
            step = Math.Max(1, step / 2);
            for (var dy = roughY - window; dy <= roughY + window; dy += step)
            {
                for (var dx = roughX - window; dx <= roughX + window; dx += step)
                {
                    if (Math.Abs(dx) <= radius && Math.Abs(dy) <= radius) TryMatch(dx, dy, Region.Width, Region.Height);
                }
            }
        }

        // Size is estimated after position and allowed to change each axis separately. A box that
        // cannot change shape slowly fills with background as a subject walks away or turns, and
        // every accepted view after that teaches the model a little more of the background.
        var (centreX, centreY) = (best.Dx, best.Dy);
        ReadOnlySpan<float> sides = stackalloc float[] { 0.93f, 1f, 1.08f };
        foreach (var horizontal in sides)
        {
            foreach (var vertical in sides)
            {
                if (horizontal == 1 && vertical == 1) continue;
                var width = Math.Max(4, (int)Math.Round(Region.Width * frame.Width * horizontal)) / (float)frame.Width;
                var height = Math.Max(4, (int)Math.Round(Region.Height * frame.Height * vertical)) / (float)frame.Height;
                if (width * frame.Width < Math.Max(6, _identityWidth / 5f)
                    || height * frame.Height < Math.Max(6, _identityHeight / 5f)
                    || width * frame.Width > _identityWidth * 5f
                    || height * frame.Height > _identityHeight * 5f) continue;
                TryMatch(centreX + (int)Math.Round((Region.Width - width) * frame.Width / 2),
                    centreY + (int)Math.Round((Region.Height - height) * frame.Height / 2), width, height);
            }
        }

        // Resizing keeps the old centre, but a subject that is receding is usually also moving,
        // so the box has to be placed again at its new size. Without this the box creeps away
        // from a shrinking subject a pixel at a time until it is no longer on it at all.
        var (sizedWidth, sizedHeight) = (best.Width, best.Height);
        for (var step = Math.Max(1, coarse); ; step /= 2)
        {
            var (settledX, settledY) = (best.Dx, best.Dy);
            for (var dy = -step; dy <= step; dy += step)
            {
                for (var dx = -step; dx <= step; dx += step) TryMatch(settledX + dx, settledY + dy, sizedWidth, sizedHeight);
            }

            if (step == 1) break;
        }

        var placed = new Reading(default,
            (int)Math.Round((Region.Left + (float)best.Dx / frame.Width) * frame.Width),
            (int)Math.Round((Region.Top + (float)best.Dy / frame.Height) * frame.Height),
            Math.Max(2, (int)Math.Round(best.Width * frame.Width)),
            Math.Max(2, (int)Math.Round(best.Height * frame.Height)));
        Describe(frame, placed.Left, placed.Top, placed.Width, placed.Height, _probe);
        placed.Resemblance = Compare(_probe);
        // Nothing above objects to a box a little smaller than the subject, because a crop of
        // the subject still looks like it, and a little smaller is what every frame offers. Left
        // alone the box walks inwards until it is a thumbnail of a trouser leg and the lock is
        // lost for good. Pushing each side back out while the room beyond it still reads as the
        // subject is the force that opposes that, and it costs four readings a frame.
        // No allowance is given here: a box nudged outwards forty times a second on a reading
        // that merely held would swallow the background just as surely as it shrank.
        Extend(frame, ref placed, OutlineRounds, OutlineStep, 0, MaxDistortion);
        var resemblance = placed.Resemblance;
        Confidence = Math.Clamp((resemblance.ToSubject - 0.5) / 0.42, 0, 1);
        if (resemblance.ToSubject < HoldSubject || resemblance.Relative < HoldRelative)
        {
            LoseTracking(resemblance.ToSubject >= HoldSubject
                ? "Similar-looking regions: watching for a clear match. Select again if necessary."
                : "Tracking uncertain: looking for the selected object to return.");
            return;
        }

        var left = (float)placed.Left / frame.Width;
        var top = (float)placed.Top / frame.Height;
        Region = (left, top, (float)placed.Width / frame.Width, (float)placed.Height / frame.Height);
        Describe(frame, placed.Left, placed.Top, placed.Width, placed.Height, _probe);
        // Only a confident, settled view is worth remembering, and only one view every so often:
        // learning from every frame turns the memory into twelve copies of the last second.
        if (_reacquiredFrames == 0 && resemblance.ToSubject > 0.78
            && Similarity(_probe, _positive[0]) > 0.72
            && resemblance.Relative > ReturnRelative && --_untilRemember <= 0)
        {
            _untilRemember = RememberInterval;
            RememberSubject(_probe);
            LearnDistractors(frame);
        }

        Status = _reacquiredFrames > 0
            ? "Original visual texture found again. Tracking resumed."
            : "Tracking selected region.";
        if (_reacquiredFrames > 0) _reacquiredFrames--;
    }

    /// <summary>A place tried by the local search: its penalised score and its raw resemblance.</summary>
    private readonly record struct Match(double Score, int Dx, int Dy, float Width, float Height, double Alike = 0);

    // How many shape-only peaks are read in colour before the position is chosen.
    private const int Leads = 4;

    /// <summary>
    /// Keep the best few places, counting offsets within <paramref name="apart"/> of each other as
    /// one place: neighbouring offsets describe the same physical thing and only one is worth a lead.
    /// </summary>
    private static void Lead(Span<Match> leads, double score, int dx, int dy, int apart)
    {
        var lowest = 0;
        for (var i = 0; i < leads.Length; i++)
        {
            if (Math.Abs(leads[i].Dx - dx) < apart && Math.Abs(leads[i].Dy - dy) < apart && leads[i].Score > double.MinValue)
            {
                if (score > leads[i].Score) leads[i] = leads[i] with { Score = score, Dx = dx, Dy = dy };
                return;
            }

            if (leads[i].Score < leads[lowest].Score) lowest = i;
        }

        if (score > leads[lowest].Score) leads[lowest] = leads[lowest] with { Score = score, Dx = dx, Dy = dy };
    }

    private const int RememberInterval = 12;
    private int _untilRemember = RememberInterval;

    /// <summary>Best resemblance to the subject using the anchor and newest views only.</summary>
    private double Resembles(Patch candidate)
    {
        var anchor = Similarity(candidate, _positive[0]);
        return _positive.Count > 1 ? Math.Max(anchor, Similarity(candidate, _positive[^1])) : anchor;
    }

    /// <summary>Shape agreement alone, for ranking candidate positions before colour is read.</summary>
    private double StructuralResemblance(Patch candidate)
    {
        if (candidate.IsFlat) return 0;
        var best = Correlation(candidate.Structure, _positive[0].Structure);
        if (_positive.Count > 1) best = Math.Max(best, Correlation(candidate.Structure, _positive[^1].Structure));
        return (best + 1) / 2;
    }

    private void LoseTracking(string status)
    {
        Tracking = false;
        Erase = false;
        Confidence = 0;
        _lostFrames = 0;
        _returnCandidate = null;
        _returnConfirmations = 0;
        _reacquiredFrames = 0;
        Status = status;
    }

    /// <summary>
    /// Learn the things nearby that currently look most like the subject. A second person in
    /// similar clothing is the hardest case there is, and the only moment it can be learned
    /// safely is while the real subject is still visible and being followed.
    /// </summary>
    private void LearnDistractors(VideoFrame frame)
    {
        var box = Bounds(frame.Width, frame.Height);
        var width = Math.Max(8, box.Right - box.Left);
        var height = Math.Max(8, box.Bottom - box.Top);
        var found = 0;
        // This runs on the presenting thread between two frames. A small subject on a large
        // picture would otherwise be compared at thousands of places; a bounded grid read on
        // shape first keeps it to a hundred cheap readings and a handful of full ones.
        var stepX = Math.Max(Math.Max(8, width / 2), frame.Width / 10);
        var stepY = Math.Max(Math.Max(8, height / 2), frame.Height / 10);
        for (var top = 0; top + height <= frame.Height && found < 2; top += stepY)
        {
            for (var left = 0; left + width <= frame.Width && found < 2; left += stepX)
            {
                if (left < box.Right && left + width > box.Left
                    && top < box.Bottom && top + height > box.Top) continue;
                DescribeStructure(frame, left, top, width, height, _spare);
                if (StructuralResemblance(_spare) < 0.6) continue;
                Describe(frame, left, top, width, height, _spare);
                if (Resembles(_spare) < 0.7) continue;
                RememberOther(_spare);
                found++;
            }
        }
    }
}
