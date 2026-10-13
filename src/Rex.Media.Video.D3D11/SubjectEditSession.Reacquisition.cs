using Rex.Media.Primitives;

namespace Rex.Media.Video.D3D11;

/// <summary>
/// Finding the subject again once it has gone: round a corner, behind a car, out of the frame
/// entirely. Nothing about its previous position helps here, so the whole picture is searched at
/// many sizes. Two things make that affordable and make the answer trustworthy.
///
/// The search runs on a reduced copy of the picture and spends real work only where there is
/// enough local contrast for the subject to be, which is the variance pre-filter of
/// Kalal, Mikolajczyk and Matas, "Tracking-Learning-Detection" (IEEE TPAMI 34(7), 2012); the
/// survivors are then re-checked against the full-resolution picture.
///
/// A match is accepted on evidence relative to the picture it was found in, not against a fixed
/// distance. The best candidate must look more like the subject than like the remembered
/// background, must beat every rival elsewhere in the frame by a clear margin, and must be seen
/// twice. Where it cannot, the lock stays lost and says so: a wrong lock is worse than none.
/// </summary>
public sealed partial class SubjectEditSession
{
    // Accepting the subject back.
    private const double ReturnSubject = 0.655;
    // Unrelated surfaces already score around a half on this similarity, so a subject that has
    // turned or moved into shade rarely beats the remembered background by much. Asking only
    // that it be no less like itself than like the background keeps the comparison honest; the
    // absolute threshold, the rival margin and the second sighting carry the safety.
    private const double ReturnRelative = 0.50;
    // How far ahead of the best rival elsewhere in the picture the winner must be. Two people in
    // the same coat land within this margin of each other, and that is reported as ambiguous.
    private const double RivalMargin = 0.035;

    private const int ScanExtent = 480;
    private const int PlacesPerSize = 4;
    private const int ScanPositions = 12000;
    private const int Verifiable = 24;
    private const int Settleable = 4;
    private byte[]? _scan;
    private byte[]? _scanColour;
    private int _scanWidth;
    private int _scanHeight;
    private int _scanReduction = 1;
    private int _searchUncertainty = 1;

    private bool TryReacquire(VideoFrame frame, CancellationToken cancellation = default)
    {
        LastSearchComparisons = 0;
        LastReturn = default;
        _searchUncertainty = 1;
        ForgetSightingAcrossACut();
        if (_positive.Count == 0 || _identityWidth <= 0 || _identityHeight <= 0) return false;

        // A search on a worker holds a picture of its own, so it reads the light for itself.
        MeasureLight(frame);
        BuildScanImage(frame);
        var anchor = _positive[0];
        var recent = _positive[^1];
        // Reducing the picture blurs it, which lowers local contrast, so the threshold the
        // original view sets has to be forgiving enough to survive its own reduction.
        var floor = anchor.Deviation * 0.4f;
        List<(double Score, int Left, int Top, int Width, int Height)> shortlist = new(48);
        List<(double Score, int Left, int Top)> places = new(PlacesPerSize + 1);
        Span<float> structure = stackalloc float[Samples * Samples];
        Span<int> columns = stackalloc int[Samples * 2];
        Span<int> rows = stackalloc int[Samples * 2];

        ReadOnlySpan<float> scales = stackalloc float[] { 0.4f, 0.55f, 0.7f, 0.85f, 1f, 1.2f, 1.45f, 1.75f, 2.1f, 2.5f };
        foreach (var scale in scales)
        {
            cancellation.ThrowIfCancellationRequested();
            var fullWidth = Math.Max(4, (int)Math.Round(_identityWidth * scale));
            var fullHeight = Math.Max(4, (int)Math.Round(_identityHeight * scale));
            // A size has to fit whole. Clamping the sides separately would stretch a standing
            // person into a wide strip of pavement whenever the box reached a frame edge.
            if (fullWidth > frame.Width || fullHeight > frame.Height) continue;
            var width = Math.Max(3, fullWidth / _scanReduction);
            var height = Math.Max(3, fullHeight / _scanReduction);
            if (width > _scanWidth || height > _scanHeight) continue;
            // Two taps per cell on each axis, matching the full-resolution descriptor. Reading a
            // single pixel per cell makes the score depend on which pixels the grid happens to
            // land on, which is how a sub-window of a patterned subject can outscore the subject.
            for (var i = 0; i < Samples; i++)
            {
                columns[i] = Math.Min(width - 1, (i * 4 + 1) * width / (Samples * 4));
                columns[Samples + i] = Math.Min(width - 1, (i * 4 + 3) * width / (Samples * 4));
                rows[i] = Math.Min(height - 1, (i * 4 + 1) * height / (Samples * 4));
                rows[Samples + i] = Math.Min(height - 1, (i * 4 + 3) * height / (Samples * 4));
            }

            // A tenth of the subject keeps the peak within reach of the full-resolution check,
            // but a small subject on a large picture would be read at hundreds of thousands of
            // places. Bounding the count per size bounds how late a return can be noticed.
            var step = Math.Max(Math.Max(2, Math.Min(width, height) / 10),
                (int)Math.Sqrt((double)_scanWidth * _scanHeight / ScanPositions));
            // Several places per size, each a different part of the picture. Keeping only the
            // single best would throw away the true subject whenever some other surface happened
            // to score fractionally higher at that size; neighbouring offsets, on the other hand,
            // describe the same physical thing and only one of them is worth a place.
            places.Clear();
            for (var top = 0; top + height <= _scanHeight; top += step)
            {
                cancellation.ThrowIfCancellationRequested();
                for (var left = 0; left + width <= _scanWidth; left += step)
                {
                    if (!ScanStructure(left, top, columns, rows, structure, floor)) continue;
                    // Shape alone cannot shortlist a subject that returns turned around: the
                    // correlation of a back view with a front view is near nothing, while its
                    // colours are unchanged. A coarse chromaticity reading costs a fraction of
                    // the shape comparison and keeps those returns in the running.
                    var shape = (Math.Max(Correlation(structure, anchor.Structure),
                        Correlation(structure, recent.Structure)) + 1) / 2;
                    var score = 0.55 * shape + 0.45 * ScanColour(left, top, columns, rows, anchor, recent);
                    if (score < 0.4) continue;
                    var replaces = -1;
                    for (var i = 0; i < places.Count; i++)
                    {
                        if (Math.Abs(places[i].Left - left) >= width * 0.5 || Math.Abs(places[i].Top - top) >= height * 0.5) continue;
                        replaces = i;
                        break;
                    }

                    if (replaces >= 0)
                    {
                        if (places[replaces].Score >= score) continue;
                        places.RemoveAt(replaces);
                    }

                    places.Add((score, left, top));
                    places.Sort((a, b) => b.Score.CompareTo(a.Score));
                    if (places.Count > PlacesPerSize) places.RemoveAt(PlacesPerSize);
                }
            }

            // The shortlist position is only accurate to one step of the reduced grid, so the
            // full-resolution check is told how far it may have to look to find the real peak.
            _searchUncertainty = Math.Max(_searchUncertainty, step * _scanReduction);
            foreach (var place in places)
            {
                shortlist.Add((place.Score, place.Left * _scanReduction, place.Top * _scanReduction, fullWidth, fullHeight));
            }
        }

        shortlist.Sort((a, b) => b.Score.CompareTo(a.Score));
        List<Reading> verified = new(shortlist.Count);
        foreach (var candidate in shortlist.Take(Verifiable))
        {
            cancellation.ThrowIfCancellationRequested();
            verified.Add(Verify(frame, candidate.Left, candidate.Top, candidate.Width, candidate.Height));
        }

        verified.RemoveAll(candidate => Distortion(candidate.Width, candidate.Height) > MaxDistortion);
        verified.Sort((a, b) => Preference(b).CompareTo(Preference(a)));
        if (verified.Count > 0) LastReturn = (verified[0].Resemblance.ToSubject, verified[0].Resemblance.Relative, 0);
        // Settling is the most expensive reading in the search, so only the few readings that
        // could still be accepted are put through it; the rest are already out on their score.
        var best = default(Reading);
        var attempts = 0;
        foreach (var candidate in verified)
        {
            cancellation.ThrowIfCancellationRequested();
            if (candidate.Resemblance.ToSubject < ReturnSubject || candidate.Resemblance.Relative < ReturnRelative) continue;
            if (++attempts > Settleable) break;
            var box = candidate;
            if (!Settle(frame, ref box)) continue;
            best = box;
            break;
        }

        if (best.Width == 0)
        {
            _returnCandidate = null;
            _returnConfirmations = 0;
            return false;
        }

        var rival = verified.Where(other =>
            Math.Abs(other.Left + other.Width / 2 - best.Left - best.Width / 2) >= Math.Max(other.Width, best.Width) * 0.7 ||
            Math.Abs(other.Top + other.Height / 2 - best.Top - best.Height / 2) >= Math.Max(other.Height, best.Height) * 0.7)
            .OrderByDescending(other => other.Resemblance.ToSubject).FirstOrDefault();
        LastReturn = (best.Resemblance.ToSubject, best.Resemblance.Relative, rival.Resemblance.ToSubject);
        if (rival.Width > 0 && rival.Resemblance.ToSubject > best.Resemblance.ToSubject - RivalMargin)
        {
            // Two places in this picture look equally like the subject. Guessing between them is
            // how a lock silently moves to the wrong person, so neither is taken.
            _returnCandidate = null;
            _returnConfirmations = 0;
            Status = "Similar-looking regions: watching for a clear match. Select again if necessary.";
            return false;
        }

        // One sighting can be a passing reflection or a frame of motion blur. A second sighting in
        // nearly the same place and at nearly the same size is required before the box moves.
        if (_returnCandidate is { } previous
            && Math.Abs(previous.X + previous.Width / 2 - best.Left - best.Width / 2) < Math.Max(16, best.Width * 2)
            && Math.Abs(previous.Y + previous.Height / 2 - best.Top - best.Height / 2) < Math.Max(16, best.Height * 2)
            && Math.Abs(previous.Width - best.Width) <= Math.Max(6, best.Width / 2)
            && Math.Abs(previous.Height - best.Height) <= Math.Max(6, best.Height / 2))
        {
            _returnConfirmations++;
        }
        else
        {
            _returnConfirmations = 1;
        }

        _returnCandidate = (best.Left, best.Top, best.Width, best.Height);
        if (_returnConfirmations < 2) return false;

        Region = ((float)best.Left / frame.Width, (float)best.Top / frame.Height,
            (float)best.Width / frame.Width, (float)best.Height / frame.Height);
        if (rival.Width > 0) RememberRival(frame, rival.Left, rival.Top, rival.Width, rival.Height);
        Describe(frame, best.Left, best.Top, best.Width, best.Height, _probe);
        RememberSubject(_probe);
        Tracking = true;
        Erase = false;
        _lostFrames = 0;
        _returnCandidate = null;
        _returnConfirmations = 0;
        _reacquiredFrames = 12;
        Confidence = Math.Clamp((best.Resemblance.ToSubject - 0.5) / 0.42, 0, 1);
        Status = "Original visual texture found again. Tracking resumed.";
        return true;
    }

    /// <summary>The latest search's best candidate: resemblance to the subject, relative resemblance, and its closest rival.</summary>
    internal (double Subject, double Relative, double Rival) LastReturn { get; private set; }

    /// <summary>A sighting from before a cut and one from after it are not two sightings of one thing.</summary>
    private void ForgetSightingAcrossACut()
    {
        if (!_shotChanged) return;
        _shotChanged = false;
        _returnCandidate = null;
        _returnConfirmations = 0;
    }

    private double Preference(Reading candidate) =>
        candidate.Resemblance.ToSubject + SizePreference(candidate.Width)
        + ShapePreference(candidate.Width, candidate.Height);

    /// <summary>
    /// Between two readings of the picture that resemble the subject equally, prefer the one
    /// closest to the size it was selected at, and prefer the larger of the two. Most subjects
    /// are made partly of themselves: a patterned dress contains smaller patches of the same
    /// pattern, a brick wall contains smaller walls. Those sub-patches match perfectly, so
    /// without a reason to prefer the whole the box collapses onto a detail and stays there.
    /// Growing is treated more gently than shrinking because a box slightly too large still
    /// contains the subject, whereas one too small has already lost most of it.
    /// </summary>
    private double SizePreference(int width)
    {
        var ratio = Math.Log((double)_identityWidth / Math.Max(1, width));
        return ratio > 0 ? -0.03 * ratio : 0.015 * ratio;
    }

    /// <summary>Re-check a reduced-resolution proposal against the real picture, refining where and how big.</summary>
    private Reading Verify(VideoFrame frame, int left, int top, int width, int height)
    {
        var (bestLeft, bestTop, bestWidth, bestHeight) = (left, top, width, height);
        var bestScore = double.MinValue;
        void Consider(int x, int y, int candidateWidth, int candidateHeight)
        {
            if (x < 0 || y < 0 || candidateWidth < 4 || candidateHeight < 4
                || x + candidateWidth > frame.Width || y + candidateHeight > frame.Height
                || Distortion(candidateWidth, candidateHeight) > MaxDistortion) return;
            DescribeStructure(frame, x, y, candidateWidth, candidateHeight, _spare);
            LastSearchComparisons++;
            var score = StructuralResemblance(_spare) + SizePreference(candidateWidth) + ShapePreference(candidateWidth, candidateHeight);
            if (score <= bestScore) return;
            (bestScore, bestLeft, bestTop, bestWidth, bestHeight) = (score, x, y, candidateWidth, candidateHeight);
        }

        // Walk down from the spacing of the reduced grid to single pixels, re-centring each time.
        for (var step = Math.Max(1, _searchUncertainty); ; step = step / 2)
        {
            var (centreLeft, centreTop) = (bestLeft, bestTop);
            for (var dy = -step; dy <= step; dy += step)
            {
                for (var dx = -step; dx <= step; dx += step) Consider(centreLeft + dx, centreTop + dy, bestWidth, bestHeight);
            }

            if (step == 1) break;
        }

        // Then the size, each side independently so a subject that turned side-on can be found.
        ReadOnlySpan<float> sides = stackalloc float[] { 0.84f, 0.92f, 1f, 1.09f, 1.19f };
        for (var pass = 0; pass < 2; pass++)
        {
            var (centreLeft, centreTop, centreWidth, centreHeight) = (bestLeft, bestTop, bestWidth, bestHeight);
            foreach (var horizontal in sides)
            {
                foreach (var vertical in sides)
                {
                    var candidateWidth = Math.Max(4, (int)Math.Round(centreWidth * horizontal));
                    var candidateHeight = Math.Max(4, (int)Math.Round(centreHeight * vertical));
                    Consider(centreLeft + (centreWidth - candidateWidth) / 2,
                        centreTop + (centreHeight - candidateHeight) / 2, candidateWidth, candidateHeight);
                }
            }
        }

        Describe(frame, bestLeft, bestTop, bestWidth, bestHeight, _spare);
        return new Reading(Compare(_spare), bestLeft, bestTop, bestWidth, bestHeight);
    }

    // How far the box may grow in one step, and how many steps it may take before the thing it
    // is sitting on is treated as an expanse rather than an object.
    private const int SettleStep = 30;
    private const int SettleRounds = 6;

    private bool Settle(VideoFrame frame, ref Reading box) =>
        Extend(frame, ref box, SettleRounds, SettleStep, 0.015, MaxDistortion);

    /// <summary>A patch of the picture, where it is, and how much it looks like the subject.</summary>
    private record struct Reading(Resemblance Resemblance, int Left, int Top, int Width, int Height);

    /// <summary>
    /// Push the box outwards until its surroundings stop looking like the subject, and report
    /// whether they ever do. Two different faults are settled by the same movement.
    ///
    /// A box that stopped short of the subject's outline stays short, and worse, keeps shrinking:
    /// a crop of a person is still a good match for that person, so nothing in a frame-to-frame
    /// search objects to a slightly smaller box, and a slightly smaller box is what it is offered
    /// forty times a second. Extending one side at a time while the new room still looks like the
    /// subject is the opposing force; together they settle on the outline.
    ///
    /// A box that is not around an object at all - one panel of a patterned wall, a slice of a
    /// much larger copy of the same surface - never stops growing, because there is always more
    /// of the same thing beyond it. When that growth can only be halted by the subject's own
    /// proportions, the match is discarded: a confident lock on part of a larger expanse is the
    /// kind of wrong answer that looks right, and it is worse than reporting nothing.
    /// </summary>
    private bool Extend(VideoFrame frame, ref Reading box, int rounds, int step, double slack, double distortion)
    {
        for (var round = 0; ; round++)
        {
            var sideways = Math.Max(2, box.Width * step / 100);
            var upright = Math.Max(2, box.Height * step / 100);
            // Room added at the edge of a subject fills with background and the reading falls.
            // Any allowance keeps a row of motion blur from ending the growth early, at the cost
            // of letting a box creep outwards, so a search that must not creep passes none.
            var floor = box.Resemblance.ToSubject - slack;
            var grown = default(Reading);
            var misshapen = false;
            Consider(box.Left, box.Top, box.Width + sideways, box.Height, box.Left + box.Width + sideways <= frame.Width);
            Consider(box.Left - sideways, box.Top, box.Width + sideways, box.Height, box.Left >= sideways);
            Consider(box.Left, box.Top, box.Width, box.Height + upright, box.Top + box.Height + upright <= frame.Height);
            Consider(box.Left, box.Top - upright, box.Width, box.Height + upright, box.Top >= upright);
            if (grown.Width > 0)
            {
                box = grown;
                // A box that starts far too small needs several pushes to reach the outline, and
                // needing them says nothing about what it is sitting on. Running out of pushes is
                // therefore not a verdict; it just stops the work.
                if (round + 1 < rounds) continue;
                return true;
            }

            // Nothing beyond any reachable side resembles the subject: this is an outline. Sides
            // the picture itself cuts off are not evidence either way, so a subject standing
            // against the edge of the frame is settled by the sides that remain. But if the only
            // direction the thing carries on in is one the subject's own proportions forbid, then
            // it is not the subject: it is a larger expanse of something that looks like it.
            return !misshapen;

            void Consider(int x, int y, int width, int height, bool insidePicture)
            {
                if (!insidePicture) return;
                Describe(frame, x, y, width, height, _spare);
                var resemblance = Compare(_spare);
                if (resemblance.ToSubject < floor) return;
                if (Distortion(width, height) > distortion)
                {
                    misshapen = true;
                    return;
                }

                if (resemblance.ToSubject <= grown.Resemblance.ToSubject) return;
                grown = new Reading(resemblance, x, y, width, height);
            }
        }
    }

    /// <summary>Hold the proportions the subject was selected with unless the evidence is strong.</summary>
    private double ShapePreference(int width, int height) => -0.05 * Distortion(width, height);

    /// <summary>
    /// How far a candidate's proportions have been stretched away from the subject's. Beyond
    /// <see cref="MaxDistortion"/> the candidate is not considered at all: allowing the box to
    /// take any shape lets a search squeeze the subject's texture over something that merely
    /// contains a stretched version of it, which is a confident-looking wrong answer.
    /// </summary>
    private double Distortion(int width, int height) => Math.Abs(
        Math.Log((double)width / Math.Max(1, height)) - Math.Log((double)_identityWidth / Math.Max(1, _identityHeight)));

    // Half as wide again, or half as tall, relative to the selection's own proportions.
    private const double MaxDistortion = 0.405;

    /// <summary>The thing that nearly stole the lock is worth remembering as not-the-subject.</summary>
    private void RememberRival(VideoFrame frame, int left, int top, int width, int height)
    {
        Describe(frame, left, top, width, height, _spare);
        RememberOther(_spare);
    }

    /// <summary>
    /// Read one candidate from the reduced picture. Areas too smooth to hold the subject's
    /// structure are rejected before any comparison, which is what keeps a whole-picture search
    /// at many sizes affordable: most of a frame is sky, road, wall or blurred background.
    /// </summary>
    private bool ScanStructure(int left, int top, ReadOnlySpan<int> columns, ReadOnlySpan<int> rows,
        Span<float> structure, float floor)
    {
        var scan = _scan!;
        var total = 0f;
        var i = 0;
        for (var y = 0; y < Samples; y++)
        {
            var upper = (top + rows[y]) * _scanWidth + left;
            var lower = (top + rows[Samples + y]) * _scanWidth + left;
            for (var x = 0; x < Samples; x++)
            {
                var near = columns[x];
                var far = columns[Samples + x];
                var value = (scan[upper + near] + scan[upper + far] + scan[lower + near] + scan[lower + far]) * 0.25f;
                structure[i++] = value;
                total += value;
            }
        }

        var mean = total / structure.Length;
        var square = 0f;
        for (var j = 0; j < structure.Length; j++)
        {
            var centred = structure[j] - mean;
            structure[j] = centred;
            square += centred * centred;
        }

        if (square <= 0 || MathF.Sqrt(square / structure.Length) < floor) return false;
        var normalize = 1f / MathF.Sqrt(square);
        for (var j = 0; j < structure.Length; j++) structure[j] *= normalize;
        return true;
    }

    /// <summary>
    /// A sixteen-sample chromaticity reading of a candidate, compared with the subject's own.
    /// Coarse on purpose: this only has to keep a plausible candidate on the shortlist, and the
    /// full-resolution check that follows reads the whole patch properly.
    /// </summary>
    private double ScanColour(int left, int top, ReadOnlySpan<int> columns, ReadOnlySpan<int> rows,
        Patch anchor, Patch recent)
    {
        Span<float> histogram = stackalloc float[ChromaBins * ChromaBins];
        histogram.Clear();
        var colour = _scanColour!;
        for (var y = 1; y < Samples; y += 4)
        {
            var row = (top + rows[y]) * _scanWidth;
            for (var x = 1; x < Samples; x += 4)
            {
                var at = (row + left + columns[x]) * 3;
                Accumulate(histogram, colour[at], colour[at + 1], colour[at + 2]);
            }
        }

        // Sixteen readings, scaled to the share-of-the-patch the stored views are kept as.
        for (var i = 0; i < histogram.Length; i++) histogram[i] /= 16;
        return Math.Max(Intersection(histogram, _lightRed, _lightGreen, anchor),
            Intersection(histogram, _lightRed, _lightGreen, recent));
    }

    /// <summary>
    /// A sixteen-sample chromaticity reading of a rectangle of the full picture, compared with
    /// the subject's anchor and newest views. The local search's counterpart of
    /// <see cref="ScanColour"/>: cheap enough to run at every coarse position.
    /// </summary>
    private double QuickColour(VideoFrame frame, ReadOnlySpan<int> columns, ReadOnlySpan<int> rows, int left, int top)
    {
        Span<float> histogram = stackalloc float[ChromaBins * ChromaBins];
        histogram.Clear();
        for (var y = 0; y < 4; y++)
        {
            var row = frame.Row(0, top + rows[y]);
            for (var x = 0; x < 4; x++)
            {
                var at = (left * 4) + columns[x];
                Accumulate(histogram, row[at], row[at + 1], row[at + 2]);
            }
        }

        for (var i = 0; i < histogram.Length; i++) histogram[i] /= 16;
        var colour = Intersection(histogram, _lightRed, _lightGreen, _positive[0]);
        return _positive.Count > 1 ? Math.Max(colour, Intersection(histogram, _lightRed, _lightGreen, _positive[^1])) : colour;
    }

    /// <summary>
    /// Where <see cref="QuickColour"/> reads a rectangle of a given size, relative to its corner,
    /// at the middle of each sixteenth: columns as byte offsets into a row.
    /// </summary>
    private static void RelativeColourTaps(int width, int height, Span<int> columns, Span<int> rows)
    {
        for (var i = 0; i < 4; i++)
        {
            columns[i] = ((i * 2) + 1) * width / 8 * 4;
            rows[i] = ((i * 2) + 1) * height / 8;
        }
    }

    // A coarse reading has to allow for a change of light as well, or a subject walking back in
    // under a different lamp never reaches the full-resolution check that would recognise it.
    private static double Intersection(ReadOnlySpan<float> histogram, float red, float green, Patch view)
    {
        var alongRed = Math.Clamp((int)Math.Round(view.Red - red), -MaxCast, MaxCast);
        var alongGreen = Math.Clamp((int)Math.Round(view.Green - green), -MaxCast, MaxCast);
        var direct = 0d;
        var shifted = 0d;
        for (var i = 0; i < ChromaBins; i++)
        {
            for (var j = 0; j < ChromaBins; j++)
            {
                var count = histogram[i * ChromaBins + j];
                direct += Math.Min(count, view.Colour[i * ChromaBins + j]);
                var toRed = i + alongRed;
                var toGreen = j + alongGreen;
                if ((uint)toRed < ChromaBins && (uint)toGreen < ChromaBins)
                {
                    shifted += Math.Min(count, view.Colour[toRed * ChromaBins + toGreen]);
                }
            }
        }

        return Math.Max(direct, shifted - CastPenalty * (Math.Abs(alongRed) + Math.Abs(alongGreen)));
    }

    private static double Correlation(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var total = 0d;
        for (var i = 0; i < a.Length; i++) total += a[i] * b[i];
        return Math.Clamp(total, -1, 1);
    }

    /// <summary>A reduced luma copy of the picture, reused between searches.</summary>
    private void BuildScanImage(VideoFrame frame)
    {
        var reduction = Math.Max(1, (Math.Max(frame.Width, frame.Height) + ScanExtent - 1) / ScanExtent);
        _scanReduction = reduction;
        _scanWidth = Math.Max(1, frame.Width / reduction);
        _scanHeight = Math.Max(1, frame.Height / reduction);
        var required = _scanWidth * _scanHeight;
        if (_scan is null || _scan.Length < required)
        {
            _scan = new byte[required];
            _scanColour = new byte[required * 3];
        }

        var scan = _scan;
        var colour = _scanColour!;
        for (var y = 0; y < _scanHeight; y++)
        {
            var source = y * reduction;
            var first = frame.Row(0, source);
            // Averaging two lines costs little and keeps a reduced picture from losing the
            // fine texture that distinguishes a patterned dress from a plain one.
            var second = frame.Row(0, Math.Min(frame.Height - 1, source + reduction / 2));
            var destination = y * _scanWidth;
            for (var x = 0; x < _scanWidth; x++)
            {
                var at = x * reduction * 4;
                var other = Math.Min(frame.Width - 1, x * reduction + reduction / 2) * 4;
                var blue = (first[at] + second[other]) >> 1;
                var green = (first[at + 1] + second[other + 1]) >> 1;
                var red = (first[at + 2] + second[other + 2]) >> 1;
                scan[destination + x] = (byte)((blue * 29 + green * 150 + red * 77) >> 8);
                var pixel = (destination + x) * 3;
                colour[pixel] = (byte)blue;
                colour[pixel + 1] = (byte)green;
                colour[pixel + 2] = (byte)red;
            }
        }
    }
}
