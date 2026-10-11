using Rex.Media.Primitives;

namespace Rex.Media.Video.D3D11;

public sealed partial class SubjectEditSession
{
    private void Track(VideoFrame frame)
    {
        if (_reference is null) return;

        // Search a coarse motion window first, then refine to individual pixels. The
        // previous fixed 9-pixel search lost fast-moving accessories even at 1080p.
        // The bounded window keeps the work deterministic on low-end WARP machines.
        // A just-recovered subject can still be moving quickly across the frame.
        // Give those first few ordinary tracking frames a wider, still bounded
        // continuity window so recovery does not immediately lose the same subject.
        var recoveryRadiusMultiplier = _reacquiredFrames > 0 ? 3 : 1;
        var radius = Math.Clamp((int)Math.Round(Math.Min(
            frame.Width * Region.Width, frame.Height * Region.Height) * 0.9 * recoveryRadiusMultiplier), 12, 96);
        var coarse = Math.Max(1, radius / 8);
        var best = double.MaxValue;
        var bestScore = double.MaxValue;
        var bestDx = 0;
        var bestDy = 0;
        var bestWidth = Region.Width;
        var bestHeight = Region.Height;
        void TryMatch(int dx, int dy, float width, float height)
        {
            var left = Region.Left + (float)dx / frame.Width;
            var top = Region.Top + (float)dy / frame.Height;
            if (left < 0 || top < 0 || left + width > 1 || top + height > 1)
            {
                return;
            }
    
            var distance = Distance(frame, left, top, width, height, _reference);
            if (_returnAppearance is { } appearance)
            {
                var identityError = AppearanceDistance(frame,
                    (int)Math.Round(left * frame.Width), (int)Math.Round(top * frame.Height),
                    (int)Math.Round(width * frame.Width), (int)Math.Round(height * frame.Height), appearance);
                distance = Math.Min(distance, 4 + identityError) + Math.Max(0, identityError - 22);
            }
            // A repeated or flat pattern may match dozens of places equally well.
            // Prefer continuity at the last known position to avoid drifting across
            // an unrelated but identically-coloured surface.
            var score = distance + 0.06 * Math.Sqrt((double)dx * dx + (double)dy * dy)
                + 8 * Math.Abs(Math.Log(width / Region.Width));
            if (score < bestScore)
            {
                bestScore = score;
                best = distance;
                bestDx = dx;
                bestDy = dy;
                bestWidth = width;
                bestHeight = height;
            }
        }
    
        TryMatch(0, 0, Region.Width, Region.Height);
        for (var dy = -radius; dy <= radius; dy += coarse)
        {
            for (var dx = -radius; dx <= radius; dx += coarse)
            {
                TryMatch(dx, dy, Region.Width, Region.Height);
            }
        }
    
        var roughX = bestDx;
        var roughY = bestDy;
        for (var dy = roughY - coarse; dy <= roughY + coarse; dy++)
        {
            for (var dx = roughX - coarse; dx <= roughX + coarse; dx++)
            {
                if (Math.Abs(dx) <= radius && Math.Abs(dy) <= radius)
                {
                    TryMatch(dx, dy, Region.Width, Region.Height);
                }
            }
        }
    
        // Estimate scale independently after translation. Preserve the same centre while
        // testing nearby sizes; a fixed box gradually incorporates background as a
        // subject walks away, poisoning every subsequent template update.
        var centreX = bestDx;
        var centreY = bestDy;
        ReadOnlySpan<float> scales = stackalloc float[] { 0.85f, 0.925f, 1.075f, 1.15f };
        foreach (var scale in scales)
        {
            var width = Math.Max(4, (int)Math.Round(Region.Width * frame.Width * scale)) / (float)frame.Width;
            var height = Math.Max(4, (int)Math.Round(Region.Height * frame.Height * scale)) / (float)frame.Height;
            if (width * frame.Width < Math.Max(4, _identityWidth / 4)
                || height * frame.Height < Math.Max(4, _identityHeight / 4)) continue;
            var offsetX = centreX + (int)Math.Round((Region.Width - width) * frame.Width / 2);
            var offsetY = centreY + (int)Math.Round((Region.Height - height) * frame.Height / 2);
            for (var dy = offsetY - coarse; dy <= offsetY + coarse; dy += Math.Max(1, coarse / 2))
            {
                for (var dx = offsetX - coarse; dx <= offsetX + coarse; dx += Math.Max(1, coarse / 2))
                {
                    TryMatch(dx, dy, width, height);
                }
            }
        }

        Confidence = Math.Clamp(1 - best / 75, 0, 1);
        if (Confidence < 0.38)
        {
            Tracking = false;
            Erase = false;
            _lostFrames = 0;
            _returnCandidate = null;
            _returnConfirmations = 0;
            _reacquiredFrames = 0;
            Status = "Tracking uncertain: looking for the selected object to return.";
            return;
        }
    
        // Distant candidate matches with the same appearance are ambiguous: do not
        // jump to a nearby identical shoe/person when the selected one is lost.
        if ((Math.Abs(bestDx) > 2 || Math.Abs(bestDy) > 2) && best < 12)
        {
            var original = Distance(frame, Region.Left, Region.Top,
                Region.Width, Region.Height, _reference);
            if (original < best + 2)
            {
                Tracking = false;
                Erase = false;
                Confidence = 0;
                _lostFrames = 0;
                _returnCandidate = null;
                _returnConfirmations = 0;
                _reacquiredFrames = 0;
                Status = "Similar-looking regions: watching for a clear match. Select again if necessary.";
                return;
            }
        }
    
        Region = (Region.Left + (float)bestDx / frame.Width,
            Region.Top + (float)bestDy / frame.Height, bestWidth, bestHeight);
        // Update very slowly to accommodate lighting, without adopting a neighbouring target.
        SampleInto(frame, Region.Left, Region.Top, Region.Width, Region.Height, _samples);
        for (var i = 0; best < 22 && i < _reference.Length; i++)
        {
            _reference[i] = _reference[i] * 0.97f + _samples[i] * 0.03f;
        }
    
        Status = _reacquiredFrames > 0
            ? "Original visual texture found again. Tracking resumed."
            : "Tracking selected region.";
        if (_reacquiredFrames > 0)
        {
            _reacquiredFrames--;
        }
    }
}
