using Rex.Media.Primitives;

namespace Rex.Media.Video.D3D11;

/// <summary>Bounded global re-detection of the originally selected visual texture.</summary>
public sealed partial class SubjectEditSession
{
    /// <summary>
    /// Periodic, bounded full-frame re-acquisition after an object moves outside the picture
    /// or is hidden. Sparse colour-template comparisons shortlist candidates; only a sharp,
    /// spatially unambiguous full-resolution match can restore tracking. This is not a
    /// person recognizer and cannot distinguish two physically identical appearances.
    /// </summary>
    private bool TryReacquire(VideoFrame frame)
    {
        LastSearchComparisons = 0;
        if (_returnReference is not { } fingerprint)
        {
            return false;
        }

        // The original appearance survives any number of missing frames. Search in
        // several sizes so a shirt that returns nearer or farther can still be found.
        // Limit candidate work and use sparse RGB samples before the full comparison.
        List<(double Error, int X, int Y, int Width, int Height)> shortlist = new(24);
        ReadOnlySpan<float> scales = stackalloc float[] { 0.5f, 0.625f, 0.75f, 0.875f, 1f, 1.25f, 1.5f, 1.75f, 2f };
        foreach (var scale in scales)
        {
            // Preserve a separate shortlist per scale: otherwise proposals from one
            // patch size can evict another size's correct match or a distant lookalike.
            List<(double Error, int X, int Y, int Width, int Height)> candidates = new(8);
            var patchWidth = Math.Clamp((int)Math.Round(_identityWidth * scale), 1, frame.Width);
            var patchHeight = Math.Clamp((int)Math.Round(_identityHeight * scale), 1, frame.Height);
            var maxX = frame.Width - patchWidth;
            var maxY = frame.Height - patchHeight;
            var stride = Math.Max(2, Math.Max(Math.Min(patchWidth, patchHeight) / 3,
                Math.Max(frame.Width / 200, frame.Height / 130)));
            for (var y = 0; y <= maxY; y += stride)
            {
                for (var x = 0; x <= maxX; x += stride)
                {
                    var error = SparseDistance(frame, x, y, patchWidth, patchHeight, fingerprint);
                    if (error > 105)
                    {
                        continue;
                    }

                    // Proposals for the same physical patch at this scale compete for
                    // one shortlist place; spatially separate matches remain to guard
                    // against visually identical subjects.
                    var existing = -1;
                    for (var i = 0; i < candidates.Count; i++)
                    {
                        var candidate = candidates[i];
                        if (Math.Abs(candidate.X + candidate.Width / 2 - x - patchWidth / 2) <
                                Math.Min(candidate.Width, patchWidth) * 0.75 &&
                            Math.Abs(candidate.Y + candidate.Height / 2 - y - patchHeight / 2) <
                                Math.Min(candidate.Height, patchHeight) * 0.75)
                        {
                            existing = i;
                            break;
                        }
                    }

                    if (existing >= 0)
                    {
                        if (candidates[existing].Error <= error)
                        {
                            continue;
                        }

                        candidates.RemoveAt(existing);
                    }

                    candidates.Add((error, x, y, patchWidth, patchHeight));
                    candidates.Sort((a, b) => a.Error.CompareTo(b.Error));
                    if (candidates.Count > 8)
                    {
                        candidates.RemoveAt(8);
                    }
                }
            }

            shortlist.AddRange(candidates);
        }

        List<(double Error, int X, int Y, int Width, int Height)> refined = new(shortlist.Count);
        foreach (var candidate in shortlist)
        {
            var bestError = double.MaxValue;
            var bestX = candidate.X;
            var bestY = candidate.Y;
            var maxX = frame.Width - candidate.Width;
            var maxY = frame.Height - candidate.Height;
            var radius = Math.Max(2, Math.Min(candidate.Width, candidate.Height) / 3);
            var refinementStep = Math.Max(1, radius / 4);
            for (var y = Math.Max(0, candidate.Y - radius); y <= Math.Min(maxY, candidate.Y + radius); y += refinementStep)
            {
                for (var x = Math.Max(0, candidate.X - radius); x <= Math.Min(maxX, candidate.X + radius); x += refinementStep)
                {
                    var error = ReturnDistance(frame, (float)x / frame.Width, (float)y / frame.Height,
                        (float)candidate.Width / frame.Width, (float)candidate.Height / frame.Height, fingerprint);
                    LastSearchComparisons++;
                    if (error < bestError)
                    {
                        (bestError, bestX, bestY) = (error, x, y);
                    }
                }
            }

            // Descend in bounded grids instead of scanning every pixel in a large
            // final square. A UHD-sized selection otherwise multiplies the expensive
            // full-template comparisons by hundreds of thousands per candidate.
            while (refinementStep > 1)
            {
                var centreX = bestX;
                var centreY = bestY;
                var window = refinementStep;
                refinementStep = Math.Max(1, refinementStep / 4);
                for (var y = Math.Max(0, centreY - window); y <= Math.Min(maxY, centreY + window); y += refinementStep)
                {
                    for (var x = Math.Max(0, centreX - window); x <= Math.Min(maxX, centreX + window); x += refinementStep)
                    {
                        var error = ReturnDistance(frame, (float)x / frame.Width, (float)y / frame.Height,
                            (float)candidate.Width / frame.Width, (float)candidate.Height / frame.Height, fingerprint);
                        LastSearchComparisons++;
                        if (error < bestError)
                        {
                            (bestError, bestX, bestY) = (error, x, y);
                        }
                    }
                }
            }

            // Matching only the middle of a larger object can favour an undersized
            // patch. When the original selection touched visible object boundaries,
            // compare that foreground-to-surroundings contrast as an additional spatial
            // cue. Keep texture-only matching for interior selections with no boundary.
            if (_originalBorderContrast >= 40)
            {
                var (centre, outside, _) = CompareSubjectWithBorder(frame,
                    (bestX, bestY, bestX + candidate.Width, bestY + candidate.Height));
                var contrast = Math.Abs(centre.B - outside.B)
                    + Math.Abs(centre.G - outside.G)
                    + Math.Abs(centre.R - outside.R);
                bestError += Math.Max(0, _originalBorderContrast - contrast) * 0.12;
            }

            refined.Add((bestError, bestX, bestY, candidate.Width, candidate.Height));
        }

        refined.Sort((a, b) => a.Error.CompareTo(b.Error));
        if (refined.Count == 0 || refined[0].Error > 22)
        {
            _returnCandidate = null;
            _returnConfirmations = 0;
            return false;
        }

        var best = refined[0];
        // A similarly patterned shirt at another location must not steal the lock.
        if (refined.Skip(1).Any(candidate =>
            (Math.Abs(candidate.X + candidate.Width / 2 - best.X - best.Width / 2) >=
                 Math.Min(candidate.Width, best.Width) * 0.75 ||
             Math.Abs(candidate.Y + candidate.Height / 2 - best.Y - best.Height / 2) >=
                 Math.Min(candidate.Height, best.Height) * 0.75) &&
            candidate.Error <= best.Error + 9))
        {
            _returnCandidate = null;
            _returnConfirmations = 0;
            return false;
        }

        if (_returnCandidate is { } previous &&
            Math.Abs(previous.X + previous.Width / 2 - best.X - best.Width / 2) <
                Math.Max(12, best.Width * 3) &&
            Math.Abs(previous.Y + previous.Height / 2 - best.Y - best.Height / 2) <
                Math.Max(12, best.Height * 3) &&
            Math.Abs(previous.Width - best.Width) <= Math.Max(4, best.Width / 3) &&
            Math.Abs(previous.Height - best.Height) <= Math.Max(4, best.Height / 3))
        {
            _returnConfirmations++;
        }
        else
        {
            _returnConfirmations = 1;
        }

        _returnCandidate = (best.X, best.Y, best.Width, best.Height);
        if (_returnConfirmations < 2)
        {
            return false;
        }

        Region = ((float)best.X / frame.Width, (float)best.Y / frame.Height,
            (float)best.Width / frame.Width, (float)best.Height / frame.Height);
        _reference = Sample(frame, Region.Left, Region.Top, Region.Width, Region.Height);
        Tracking = true;
        Erase = false;
        _returnFrames = 0;
        _returnCandidate = null;
        _returnConfirmations = 0;
        _reacquiredFrames = 15;
        Confidence = Math.Clamp(1 - best.Error / 75, 0, 1);
        Status = "Original visual texture found again. Tracking resumed.";
        return true;
    }

    /// <summary>
    /// Compare texture after allowing a small, uniform lighting shift. Absolute RGB matching
    /// misses the same moving object when the scene brightens or darkens. Keep a colour-shift
    /// penalty so a differently coloured lookalike is not treated as an identical match.
    /// Only the lost-subject search uses this; ordinary frame-to-frame tracking is unchanged.
    /// </summary>
    private static double ReturnDistance(VideoFrame frame, float left, float top, float width,
        float height, float[] reference)
    {
        Span<double> shift = stackalloc double[3];
        shift.Clear();
        for (var y = 2; y < Samples; y += 4)
        {
            var fy = Math.Clamp((int)((top + (y + 0.5f) / Samples * height) * frame.Height), 0, frame.Height - 1);
            var row = frame.Row(0, fy);
            for (var x = 2; x < Samples; x += 4)
            {
                var fx = Math.Clamp((int)((left + (x + 0.5f) / Samples * width) * frame.Width), 0, frame.Width - 1) * 4;
                var at = (y * Samples + x) * 3;
                for (var channel = 0; channel < 3; channel++)
                {
                    shift[channel] += row[fx + channel] - reference[at + channel];
                }
            }
        }

        for (var channel = 0; channel < 3; channel++)
        {
            shift[channel] = Math.Clamp(shift[channel] / 16, -60, 60);
        }

        var original = 0d;
        var adjusted = 0d;
        var i = 0;
        for (var y = 0; y < Samples; y++)
        {
            var fy = Math.Clamp((int)((top + (y + 0.5f) / Samples * height) * frame.Height), 0, frame.Height - 1);
            var row = frame.Row(0, fy);
            for (var x = 0; x < Samples; x++)
            {
                var fx = Math.Clamp((int)((left + (x + 0.5f) / Samples * width) * frame.Width), 0, frame.Width - 1) * 4;
                for (var channel = 0; channel < 3; channel++)
                {
                    var difference = row[fx + channel] - reference[i++];
                    original += Math.Abs(difference);
                    adjusted += Math.Abs(difference - shift[channel]);
                }
            }
        }

        const int components = Samples * Samples * 3;
        var lightingPenalty = (Math.Abs(shift[0]) + Math.Abs(shift[1]) + Math.Abs(shift[2])) * 0.16 / 3;
        return Math.Min(original / components, adjusted / components + lightingPenalty);
    }

    /// <summary>Cheap 4-by-4 thumbnail comparison before full template refinement.</summary>
    private static double SparseDistance(VideoFrame frame, int left, int top, int width, int height, float[] reference)
    {
        var error = 0d;
        for (var y = 2; y < Samples; y += 4)
        {
            var sourceY = Math.Min(frame.Height - 1, top + (int)((y + 0.5f) * height / Samples));
            var row = frame.Row(0, sourceY);
            for (var x = 2; x < Samples; x += 4)
            {
                var sourceX = Math.Min(frame.Width - 1, left + (int)((x + 0.5f) * width / Samples));
                var sample = sourceX * 4;
                var at = (y * Samples + x) * 3;
                error += Math.Abs(row[sample] - reference[at]);
                error += Math.Abs(row[sample + 1] - reference[at + 1]);
                error += Math.Abs(row[sample + 2] - reference[at + 2]);
            }
        }

        return error / 48;
    }
}
