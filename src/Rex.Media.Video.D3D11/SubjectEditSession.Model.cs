using Rex.Media.Primitives;

namespace Rex.Media.Video.D3D11;

/// <summary>
/// The appearance model behind Subject Lock: what the selected thing looks like, what the rest of
/// this video looks like, and how strongly an arbitrary patch resembles one rather than the other.
///
/// Absolute colour differences cannot answer that question. The same jacket is a different set of
/// RGB values when the person walks into shade, turns towards a window, or is lit by a passing car,
/// so a fixed "close enough" distance either rejects the subject constantly or accepts the wall
/// behind it. Three properties make the comparison survive those changes:
///
/// * Structure is compared by normalized cross-correlation over mean-subtracted, unit-length luma.
///   Any uniform change of brightness and contrast cancels out exactly, so the measure reports how
///   the patch is shaped, not how brightly it happens to be lit.
/// * Colour is compared as distributions of chromaticity, which divides out intensity, so a red
///   coat stays red in shade. A small coarse-luma term keeps a white shirt from matching a black
///   one, since both are chromatically neutral.
/// * Resemblance is judged relative to the rest of the picture rather than against a fixed number.
///   A patch counts as the subject when it looks more like remembered views of the subject than
///   like remembered views of everything else, following the relative-similarity rule of
///   Kalal, Mikolajczyk and Matas, "Tracking-Learning-Detection" (IEEE TPAMI 34(7), 2012).
///
/// None of this is identity. The model compares surfaces, and two people dressed alike are two
/// patches that look the same; such cases are reported as ambiguous instead of guessed.
/// </summary>
public sealed partial class SubjectEditSession
{
    private const int ChromaBins = 8;
    private const int ColourLength = ChromaBins * ChromaBins + 8;

    /// <summary>Structure and colour of one view of a patch, independent of how it was lit.</summary>
    private sealed class Patch
    {
        public float[] Structure { get; } = new float[Samples * Samples];
        public float[] Colour { get; } = new float[ColourLength];

        /// <summary>Luma standard deviation before normalizing; below <see cref="FlatDeviation"/> the patch carries no structure.</summary>
        public float Deviation { get; set; }

        /// <summary>Where the whole picture's colours sat when this view was taken, in chromaticity bins.</summary>
        public float Red { get; set; }

        public float Green { get; set; }

        /// <summary>The view's <see cref="Neutrality"/>, worked out once; not a number until then.</summary>
        public double Neutral { get; set; } = double.NaN;

        public bool IsFlat => Deviation < FlatDeviation;
    }

    private const float FlatDeviation = 3.5f;

    // A bounded memory of how the subject has looked, and of what else this video contains.
    // The first positive view is the user's own selection and is never forgotten: it anchors the
    // model so that slow drift through accepted updates cannot migrate the lock onto something
    // else. Later views are replaced oldest-first, except for that anchor.
    private const double Lookalike = 0.72;
    private const int MaxPositive = 12;
    private const int MaxNegative = 24;
    private readonly List<Patch> _positive = new(MaxPositive);
    private readonly List<Patch> _negative = new(MaxNegative);
    private readonly Patch _probe = new();

    // How much each of the 16 by 16 cells counts as the subject: chosen once, at selection, and
    // shared by every view so that views stay comparable. All 1 until then.
    private readonly float[] _weights = Uniform();
    private float _weightTotal = Samples * Samples;

    private static float[] Uniform()
    {
        var weights = new float[Samples * Samples];
        Array.Fill(weights, 1f);
        return weights;
    }

    // Cells that look like the surroundings still count this much, because the edge between the
    // subject and its background is part of what makes it recognisable.
    private const float BackgroundWeight = 0.2f;
    private const int WeightLevels = 4;
    private const int WeightBins = WeightLevels * WeightLevels * WeightLevels;

    /// <summary>
    /// Weigh each cell of the selected rectangle by how much it looks like the subject rather than
    /// what surrounds it. A rectangle drawn around a person also holds some wall, and a rectangle
    /// around a plain jacket next to a speckled pillar holds mostly pillar texture: left equal,
    /// the pillar's texture is what the tracker would learn and follow. Each cell's colour is
    /// compared with a ring around the rectangle, and colours the ring also shows count for less,
    /// the background-weighted histogram of Comaniciu, Ramesh and Meer ("Kernel-Based Object
    /// Tracking", IEEE TPAMI 25(5), 2003).
    /// </summary>
    private void Weigh(VideoFrame frame, (int Left, int Top, int Right, int Bottom) box)
    {
        var width = Math.Max(2, box.Right - box.Left);
        var height = Math.Max(2, box.Bottom - box.Top);
        Span<int> columns = stackalloc int[Samples * 2];
        Span<int> rows = stackalloc int[Samples * 2];
        Taps(frame, box.Left, box.Top, width, height, columns, rows);
        Span<int> bins = stackalloc int[Samples * Samples];
        Span<float> inside = stackalloc float[WeightBins];
        Span<float> around = stackalloc float[WeightBins];
        inside.Clear();
        around.Clear();
        var i = 0;
        for (var y = 0; y < Samples; y++)
        {
            var upper = frame.Row(0, rows[y]);
            var lower = frame.Row(0, rows[Samples + y]);
            for (var x = 0; x < Samples; x++)
            {
                var near = columns[x];
                var far = columns[Samples + x];
                var bin = WeightBin((upper[near] + upper[far] + lower[near] + lower[far]) / 4,
                    (upper[near + 1] + upper[far + 1] + lower[near + 1] + lower[far + 1]) / 4,
                    (upper[near + 2] + upper[far + 2] + lower[near + 2] + lower[far + 2]) / 4);
                bins[i++] = bin;
                inside[bin]++;
            }
        }

        var marginX = Math.Max(4, width / 3);
        var marginY = Math.Max(4, height / 3);
        var (outerLeft, outerTop) = (Math.Max(0, box.Left - marginX), Math.Max(0, box.Top - marginY));
        var (outerRight, outerBottom) = (Math.Min(frame.Width, box.Right + marginX), Math.Min(frame.Height, box.Bottom + marginY));
        var stepX = Math.Max(1, (outerRight - outerLeft) / 48);
        var stepY = Math.Max(1, (outerBottom - outerTop) / 48);
        var counted = 0;
        for (var y = outerTop; y < outerBottom; y += stepY)
        {
            var row = frame.Row(0, y);
            var within = y >= box.Top && y < box.Bottom;
            for (var x = outerLeft; x < outerRight; x += stepX)
            {
                if (within && x >= box.Left && x < box.Right) continue;
                var at = x * 4;
                around[WeightBin(row[at], row[at + 1], row[at + 2])]++;
                counted++;
            }
        }

        // A rectangle that fills the picture has no surroundings to compare with.
        if (counted < 64) return;
        var total = 0f;
        for (i = 0; i < bins.Length; i++)
        {
            var share = inside[bins[i]] / bins.Length;
            var likeness = share / (share + (around[bins[i]] / counted));
            var weight = BackgroundWeight + ((1 - BackgroundWeight) * likeness);
            _weights[i] = weight;
            total += weight;
        }

        _weightTotal = total;
    }

    /// <summary>A coarse colour cube of four levels a channel, enough to tell a jacket from a wall.</summary>
    private static int WeightBin(int b, int g, int r) =>
        ((b * WeightLevels / 256) * WeightLevels * WeightLevels) + ((g * WeightLevels / 256) * WeightLevels) + (r * WeightLevels / 256);

    private void ForgetWeights()
    {
        Array.Fill(_weights, 1f);
        _weightTotal = Samples * Samples;
    }
    private readonly Patch _spare = new();

    /// <summary>Resemblance to the subject, to everything else, and the two combined as a ratio.</summary>
    private readonly record struct Resemblance(double ToSubject, double ToOthers)
    {
        /// <summary>
        /// The relative similarity of Tracking-Learning-Detection: how much of the patch's total
        /// resemblance belongs to the subject rather than to the remembered background. Half means
        /// the patch is equally like both, so it decides nothing.
        /// </summary>
        public double Relative => ToSubject + ToOthers <= 0 ? 0 : ToSubject / (ToSubject + ToOthers);
    }

    private Resemblance Compare(Patch candidate)
    {
        var subject = 0d;
        foreach (var view in _positive)
        {
            var similarity = Similarity(candidate, view);
            if (similarity > subject) subject = similarity;
        }

        var others = 0d;
        foreach (var view in _negative)
        {
            var similarity = Similarity(candidate, view);
            if (similarity > others) others = similarity;
        }

        return new Resemblance(subject, others);
    }

    /// <summary>
    /// How alike two views are, from 0 to 1. Structure dominates because it is the part that
    /// survives lighting exactly; colour distributions carry the rest, because they survive
    /// the rotations and deformations that destroy point-for-point structural agreement.
    /// </summary>
    private static double Similarity(Patch a, Patch b)
    {
        // Two flat patches agree on nothing structural: fall back to colour alone rather than
        // pretending that two featureless areas correlate.
        var structure = a.IsFlat != b.IsFlat ? 0
            : a.IsFlat ? 0.5 : (Correlation(a.Structure, b.Structure) + 1) / 2;
        var chroma = Chroma(a, b);
        var luma = 0d;
        for (var i = ChromaBins * ChromaBins; i < ColourLength; i++) luma += Math.Min(a.Colour[i], b.Colour[i]);
        // Agreement on colour is evidence only when the remembered view has a colour to agree
        // on. A grey car, a black box, a shadowed doorway or any monochrome footage puts every
        // patch in the same neutral bins, and treating that as agreement hands nearly half the
        // score to anything at all; what colour cannot tell apart, shape has to.
        // Remembered views are compared many times a frame and never change, so how neutral each
        // one is gets worked out once rather than at every comparison.
        if (double.IsNaN(b.Neutral)) b.Neutral = Neutrality(b);
        var weight = 0.31 * (1 - b.Neutral);
        return (0.86 - weight) * structure + weight * chroma + 0.14 * luma;
    }

    /// <summary>
    /// How much of a view's colour sits where grey sits: the share of its pixels in the bins
    /// around equal red, green and blue, plus those too dark to have a colour at all.
    /// </summary>
    private static double Neutrality(Patch patch)
    {
        var neutral = 0d;
        var coloured = 0d;
        for (var i = 0; i < ChromaBins; i++)
        {
            for (var j = 0; j < ChromaBins; j++)
            {
                var count = patch.Colour[i * ChromaBins + j];
                coloured += count;
                var away = Math.Max(Math.Abs(i - GreyBin), Math.Abs(j - GreyBin));
                if (away == 0) neutral += count;
                else if (away == 1) neutral += count * 0.5;
            }
        }

        // Pixels below the chromaticity floor were never counted; they are dark, so neutral.
        return Math.Clamp(neutral + (1 - coloured), 0, 1);
    }

    // Where equal red, green and blue land: r * 8 / (3r * 3 / 5 + 1) is 4.4 for any bright r.
    private const int GreyBin = 4;

    // How far a patch's colours may be assumed to have been moved by the light falling on it,
    // in chromaticity bins, and what assuming it costs. Beyond this a red coat could be read as
    // a green one; within it, a face under a shop sign is still the same face.
    private const int MaxCast = 2;
    private const double CastPenalty = 0.05;

    /// <summary>
    /// How much two patches' colours agree, allowing for the light having changed between them.
    ///
    /// A brake light, a neon sign, the green cast under trees, a room lit by a different lamp:
    /// these move every colour the same way, leaving the spread of colours intact while putting
    /// each of them in a different bin. Comparing bin for bin reports no agreement at all, and a
    /// tracker that believes it loses the subject to every change of light.
    ///
    /// What is allowed for is the change in the light, measured across the whole picture, not any
    /// difference between these two patches. That distinction is the whole safety of it: letting
    /// each candidate explain its own colours away as a trick of the light is how a search ends
    /// up confidently matching a brown coat to a blue one. A shift every surface in the picture
    /// underwent is evidence about the light; a shift only this patch underwent is evidence that
    /// it is something else. Agreement needing no shift always wins a tie, and a shift is paid
    /// for in proportion to its size.
    /// </summary>
    private static double Chroma(Patch a, Patch b)
    {
        var direct = Overlap(a, b, 0, 0);
        var red = Math.Clamp((int)Math.Round(b.Red - a.Red), -MaxCast, MaxCast);
        var green = Math.Clamp((int)Math.Round(b.Green - a.Green), -MaxCast, MaxCast);
        if (red == 0 && green == 0) return direct;
        return Math.Max(direct, Overlap(a, b, red, green) - CastPenalty * (Math.Abs(red) + Math.Abs(green)));

        static double Overlap(Patch a, Patch b, int red, int green)
        {
            var total = 0d;
            for (var i = Math.Max(0, -red); i < Math.Min(ChromaBins, ChromaBins - red); i++)
            {
                for (var j = Math.Max(0, -green); j < Math.Min(ChromaBins, ChromaBins - green); j++)
                {
                    total += Math.Min(a.Colour[i * ChromaBins + j], b.Colour[(i + red) * ChromaBins + j + green]);
                }
            }

            return total;
        }
    }

    // Where the colours of the picture being read sit, in chromaticity bins. Every view is
    // stamped with this, so two views can be compared knowing how the light differed between them.
    private float _lightRed;
    private float _lightGreen;

    /// <summary>
    /// Read the colour of the light in this picture, from a coarse grid over the whole of it.
    /// Sparse on purpose: this is one reading of an entire frame, and a few thousand pixels
    /// settle where the picture's colours sit as well as all of them would.
    /// </summary>
    private void MeasureLight(VideoFrame frame)
    {
        Span<float> histogram = stackalloc float[ChromaBins * ChromaBins];
        histogram.Clear();
        var down = Math.Max(1, frame.Height / 48);
        var across = Math.Max(1, frame.Width / 48);
        var points = ((frame.Height + down - 1) / down) * ((frame.Width + across - 1) / across);
        if (_glance.Length != points)
        {
            // A different grid is a different picture size: nothing earlier compares with it.
            _glance = new byte[points];
            _glanceNext = new byte[points];
            _glanceKnown = false;
        }

        var glance = _glanceNext;
        var i = 0;
        for (var y = 0; y < frame.Height; y += down)
        {
            var row = frame.Row(0, y);
            for (var x = 0; x < frame.Width; x += across)
            {
                var at = x * 4;
                Accumulate(histogram, row[at], row[at + 1], row[at + 2]);
                glance[i++] = (byte)(((row[at] * 29) + (row[at + 1] * 150) + (row[at + 2] * 77)) >> 8);
            }
        }

        Locate(histogram, out _lightRed, out _lightGreen);
        var changed = _glanceKnown ? Changed(frame, down, across) : 0;
        WatchForCut(frame, changed);
        (_glance, _glanceNext) = (_glanceNext, _glance);
        _glanceKnown = true;
    }

    // The previous picture's luma on the same coarse grid.
    private byte[] _glance = [];
    private byte[] _glanceNext = [];
    private bool _glanceKnown;

    /// <summary>
    /// The share of the picture around the subject that changed since the previous picture, from
    /// 0 to 1, once any change of overall brightness is allowed for: the pixel-difference measure
    /// of shot-boundary detection compared in Zhang, Kankanhalli and Smoliar, "Automatic
    /// partitioning of full-motion video" (Multimedia Systems 1(1), 1993), counted over a sparse
    /// grid with a per-point threshold. The subject's own box, with room around it, is left out.
    /// </summary>
    private double Changed(VideoFrame frame, int down, int across)
    {
        var (left, top, right, bottom) = Bounds(frame.Width, frame.Height);
        var spareX = (right - left) / 4;
        var spareY = (bottom - top) / 4;
        (left, top, right, bottom) = (left - spareX, top - spareY, right + spareX, bottom + spareY);
        var shift = 0;
        var counted = 0;
        for (int y = 0, i = 0; y < frame.Height; y += down)
        {
            for (var x = 0; x < frame.Width; x += across, i++)
            {
                if (Locked && x >= left && x < right && y >= top && y < bottom) continue;
                shift += _glanceNext[i] - _glance[i];
                counted++;
            }
        }

        // Too little of the picture is left outside a subject that fills it to say anything.
        if (counted < 64) return 0;
        shift /= counted;
        var changed = 0;
        for (int y = 0, i = 0; y < frame.Height; y += down)
        {
            for (var x = 0; x < frame.Width; x += across, i++)
            {
                if (Locked && x >= left && x < right && y >= top && y < bottom) continue;
                if (Math.Abs(_glanceNext[i] - _glance[i] - shift) > ChangedLuma) changed++;
            }
        }

        return (double)changed / counted;
    }

    // How far a grid point's luma must move, beyond the picture's overall shift, to count as changed.
    private const int ChangedLuma = 28;

    // Both bars were measured on two minutes each of two series: 5,878 pictures and 52 cuts, the
    // borderline ones checked by eye, replayed by SubjectShotChangeFootageTests. After aligning the
    // camera's motion, ordinary pictures leave a mean grey difference under 8 and cuts from about
    // 18, while an arm sweeping close past the lens leaves up to 19. Removal's own bar (24) is set
    // for a different purpose and misses cuts between two angles of one room. What separates the
    // sweeping arm is how much of the picture changed: cuts change from 30 % of the points outside
    // the subject, the arm at most 18 %, other motion at most 22 %. A wrong cut here costs only a
    // re-detection a moment later.
    private const float CutResidual = 16;
    private const double CutChange = 0.25;

    // The camera's motion between pictures, measured only to notice a cut from one shot to another.
    private readonly SubjectRemovalMotion _shots = new();

    // Whether a cut has happened since the re-detection search last looked: a sighting from before
    // a cut and one from after it are not two sightings of one thing.
    private bool _shotChanged;

    /// <summary>
    /// Whether this picture starts a new shot. Two measures must agree, each covering the other's
    /// blind spot. The pictures are aligned with the camera-motion estimate of
    /// <see cref="SubjectRemovalMotion"/> (Bergen, Anandan, Hanna and Hingorani 1992), leaving the
    /// subject's box and a margin out, and the difference must survive that alignment, so a pan or
    /// a zoom is not a cut; and much of the picture must have changed once its overall brightness
    /// is allowed for, so a light coming on is not one. A picture's histogram was tried and
    /// rejected: two angles of one room, the commonest cut in a conversation, share it.
    /// </summary>
    internal bool IsCut { get; private set; }

    /// <summary>The two measured conditions behind the most recent shot decision.</summary>
    internal (float Residual, double ChangedShare, bool MotionCut) CutEvidence { get; private set; }

    private void WatchForCut(VideoFrame frame, double changed)
    {
        var box = Bounds(frame.Width, frame.Height);
        var spareX = (box.Right - box.Left) / 4;
        var spareY = (box.Bottom - box.Top) / 4;
        var exclude = Locked
            ? (box.Left - spareX, box.Top - spareY, box.Right + spareX, box.Bottom + spareY)
            : (0, 0, 0, 0);
        // Only the shot boundary is wanted, not a fine estimate of the motion, so the detailed
        // refinement is given a token area and costs next to nothing.
        _shots.Estimate(frame, exclude, (0, 0, 2, 2));
        CutEvidence = (_shots.Residual, changed, _shots.Cut);
        IsCut = _shots.Residual > CutResidual && changed >= CutChange;
        _shotChanged |= IsCut;
    }

    /// <summary>Where a chromaticity histogram's weight sits, in bins, along each axis.</summary>
    private static void Locate(ReadOnlySpan<float> histogram, out float red, out float green)
    {
        var weight = 0f;
        var alongRed = 0f;
        var alongGreen = 0f;
        for (var i = 0; i < ChromaBins; i++)
        {
            for (var j = 0; j < ChromaBins; j++)
            {
                var count = histogram[i * ChromaBins + j];
                weight += count;
                alongRed += count * i;
                alongGreen += count * j;
            }
        }

        red = weight > 0 ? alongRed / weight : 0;
        green = weight > 0 ? alongGreen / weight : 0;
    }

    /// <summary>Read a rectangle of the picture into <paramref name="patch"/> as structure and colour.</summary>
    private void Describe(VideoFrame frame, int left, int top, int width, int height, Patch patch)
    {
        Span<int> columns = stackalloc int[Samples * 2];
        Span<int> rows = stackalloc int[Samples * 2];
        Taps(frame, left, top, width, height, columns, rows);
        Span<float> luma = stackalloc float[Samples * Samples];
        var colour = patch.Colour;
        Array.Clear(colour);
        var i = 0;
        for (var y = 0; y < Samples; y++)
        {
            var upper = frame.Row(0, rows[y]);
            var lower = frame.Row(0, rows[Samples + y]);
            for (var x = 0; x < Samples; x++)
            {
                var near = columns[x];
                var far = columns[Samples + x];
                var b = (upper[near] + upper[far] + lower[near] + lower[far]) / 4;
                var g = (upper[near + 1] + upper[far + 1] + lower[near + 1] + lower[far + 1]) / 4;
                var r = (upper[near + 2] + upper[far + 2] + lower[near + 2] + lower[far + 2]) / 4;
                var value = ((b * 29) + (g * 150) + (r * 77)) / 256f;
                var share = _weights[i];
                luma[i++] = value;
                Accumulate(colour, b, g, r, share);
                colour[(ChromaBins * ChromaBins) + Math.Clamp((b + g + r) / 96, 0, 7)] += share;
            }
        }

        Normalize(luma, _weights, patch);
        patch.Red = _lightRed;
        patch.Green = _lightGreen;
        var weight = 1f / _weightTotal;
        for (var bin = 0; bin < ColourLength; bin++) colour[bin] *= weight;
    }

    // Each of the 16 by 16 cells stands for a block of the rectangle, and reading one pixel from
    // the middle of that block misrepresents it: a striped shirt sampled one pixel to the left can
    // invert, so the match score jumps about between neighbouring positions and a coarse-to-fine
    // search climbs the wrong peak. Averaging four spread taps per cell keeps the score surface
    // smooth enough to descend, at a quarter of the cost of averaging the block outright.
    //
    // Where the taps fall depends only on the rectangle, so it is worked out once per reading, 32
    // columns and 32 rows, rather than once per tap: the same readings at an eighth of the
    // divisions. Columns are byte offsets into a row.
    private static void Taps(VideoFrame frame, int left, int top, int width, int height, Span<int> columns, Span<int> rows)
    {
        for (var i = 0; i < Samples; i++)
        {
            columns[i] = Math.Clamp(left + (((i * 4) + 1) * width / (Samples * 4)), 0, frame.Width - 1) * 4;
            columns[Samples + i] = Math.Clamp(left + (((i * 4) + 3) * width / (Samples * 4)), 0, frame.Width - 1) * 4;
            rows[i] = Math.Clamp(top + (((i * 4) + 1) * height / (Samples * 4)), 0, frame.Height - 1);
            rows[Samples + i] = Math.Clamp(top + (((i * 4) + 3) * height / (Samples * 4)), 0, frame.Height - 1);
        }
    }

    /// <summary>
    /// Where the taps fall inside a rectangle of a given size, relative to its corner: the same
    /// for every position a search tries at that size, so a search works them out once.
    /// </summary>
    private static void RelativeTaps(int width, int height, Span<int> columns, Span<int> rows)
    {
        for (var i = 0; i < Samples; i++)
        {
            columns[i] = ((i * 4) + 1) * width / (Samples * 4) * 4;
            columns[Samples + i] = ((i * 4) + 3) * width / (Samples * 4) * 4;
            rows[i] = ((i * 4) + 1) * height / (Samples * 4);
            rows[Samples + i] = ((i * 4) + 3) * height / (Samples * 4);
        }
    }

    /// <summary>
    /// Structure only, for the hundreds of positions a search rejects. Building colour
    /// distributions for every one of those costs two divisions per pixel and decides nothing:
    /// the places worth considering are found by shape first and judged on colour afterwards.
    /// </summary>
    private void DescribeStructure(VideoFrame frame, int left, int top, int width, int height, Patch patch)
    {
        Span<int> columns = stackalloc int[Samples * 2];
        Span<int> rows = stackalloc int[Samples * 2];
        Taps(frame, left, top, width, height, columns, rows);
        ReadStructure(frame, columns, rows, 0, 0, patch);
    }

    /// <summary>
    /// Structure at taps worked out in advance, moved by (<paramref name="left"/>,
    /// <paramref name="top"/>). The taps must stay inside the picture; a search that moves a
    /// rectangle about inside the picture keeps them there.
    /// </summary>
    private void ReadStructure(VideoFrame frame, ReadOnlySpan<int> columns, ReadOnlySpan<int> rows,
        int left, int top, Patch patch)
    {
        Span<float> luma = stackalloc float[Samples * Samples];
        var i = 0;
        var across = left * 4;
        for (var y = 0; y < Samples; y++)
        {
            var upper = frame.Row(0, top + rows[y]);
            var lower = frame.Row(0, top + rows[Samples + y]);
            for (var x = 0; x < Samples; x++)
            {
                var near = across + columns[x];
                var far = across + columns[Samples + x];
                var value = (((upper[near] + upper[far] + lower[near] + lower[far]) * 29)
                    + ((upper[near + 1] + upper[far + 1] + lower[near + 1] + lower[far + 1]) * 150)
                    + ((upper[near + 2] + upper[far + 2] + lower[near + 2] + lower[far + 2]) * 77)) / 1024f;
                luma[i++] = value;
            }
        }

        Normalize(luma, _weights, patch);
    }

    /// <summary>
    /// Chromaticity divides each channel by the pixel's own total, so the bin a pixel lands in
    /// depends on its colour and not on how much light reached it. Near-black pixels have no
    /// reliable chromaticity, so they are counted only in the coarse luma bins.
    /// </summary>
    private static void Accumulate(Span<float> chroma, int b, int g, int r) => Accumulate(chroma, b, g, r, 1);

    private static void Accumulate(Span<float> chroma, int b, int g, int r, float amount)
    {
        var sum = b + g + r;
        // Near-black pixels have no reliable chromaticity: dividing by a total close to zero
        // turns sensor noise into a confident colour, so they are left to the luma bins.
        if (sum < 36) return;
        // Real surfaces cluster near a third of the total in each channel; spreading the
        // bins over the lower three fifths keeps that cluster from collapsing into one bin.
        var red = Math.Clamp(r * ChromaBins / (sum * 3 / 5 + 1), 0, ChromaBins - 1);
        var green = Math.Clamp(g * ChromaBins / (sum * 3 / 5 + 1), 0, ChromaBins - 1);
        chroma[red * ChromaBins + green] += amount;
    }

    /// <summary>
    /// Centre the luma on its weighted mean and scale each cell by the square root of its weight,
    /// so the dot product of two unit-length patches is their weighted correlation. With every
    /// weight 1 this is the plain correlation.
    /// </summary>
    private static void Normalize(ReadOnlySpan<float> luma, ReadOnlySpan<float> weights, Patch patch)
    {
        var weighted = 0f;
        var mass = 0f;
        for (var i = 0; i < luma.Length; i++)
        {
            weighted += weights[i] * luma[i];
            mass += weights[i];
        }

        var mean = weighted / mass;
        var square = 0f;
        var spread = 0f;
        var structure = patch.Structure;
        for (var i = 0; i < luma.Length; i++)
        {
            var centred = luma[i] - mean;
            spread += weights[i] * centred * centred;
            centred *= MathF.Sqrt(weights[i]);
            structure[i] = centred;
            square += centred * centred;
        }

        var deviation = MathF.Sqrt(spread / mass);
        patch.Deviation = deviation;
        patch.Neutral = double.NaN;
        // Unit length makes the dot product of two patches their correlation directly, so a
        // candidate is compared with every remembered view at the cost of one multiply each.
        var scale = square > 0 ? 1f / MathF.Sqrt(square) : 0f;
        for (var i = 0; i < structure.Length; i++) structure[i] *= scale;
    }

    /// <summary>
    /// Whether a patch has enough to go on. A rectangle of clear sky or a blank wall matches
    /// thousands of unrelated places equally well, and claiming a lock on one would be a
    /// promise the tracker cannot keep. Either visible structure or a colour the patch does
    /// not share with everything around it is enough.
    /// </summary>
    private static bool IsDistinctive(Patch patch)
    {
        if (!patch.IsFlat) return true;
        // A smooth but strongly coloured patch still carries evidence; a smooth neutral one,
        // whose colour sits in a single near-grey bin, does not.
        var largest = 0f;
        var reliableColour = 0f;
        for (var i = 0; i < ChromaBins * ChromaBins; i++)
        {
            largest = Math.Max(largest, patch.Colour[i]);
            reliableColour += patch.Colour[i];
        }
        // An empty histogram means the patch was too dark to measure, not that it
        // contains a distinctive spread of colours.
        return reliableColour >= 0.25f && largest < 0.9f;
    }

    private static Patch Copy(Patch source)
    {
        var copy = new Patch { Deviation = source.Deviation, Red = source.Red, Green = source.Green, Neutral = source.Neutral };
        source.Structure.CopyTo(copy.Structure, 0);
        source.Colour.CopyTo(copy.Colour, 0);
        return copy;
    }

    /// <summary>Remember another view of the subject, keeping the user's original selection.</summary>
    private void RememberSubject(Patch view)
    {
        // A view that the model already explains teaches it nothing and would only crowd out
        // the genuinely new viewpoints this memory exists to hold.
        if (_positive.Count > 0 && Compare(view).ToSubject > 0.94) return;
        if (_positive.Count >= MaxPositive) _positive.RemoveAt(1);
        _positive.Add(Copy(view));
    }

    /// <summary>
    /// Remember a view of something that is not the subject. Only a view taken while the user's
    /// own selection was on screen is <paramref name="trusted"/>: at that moment everything
    /// outside the box is certainly not the subject, look-alikes included, and those are the
    /// most valuable background views there are. Later, the box can sit a little off the subject
    /// while still reporting confidence, and from there the subject itself is "nearby and
    /// similar". Learned as background once, it is rejected on every frame after, which is how a
    /// lock becomes permanently unrecoverable. So an untrusted view that looks much like the
    /// original selection is not learned; the rival check at recovery reports such a thing as
    /// ambiguous rather than guessing.
    /// </summary>
    private void RememberOther(Patch view, bool trusted = false)
    {
        if (view.IsFlat) return;
        if (!trusted && _positive.Count > 0 && Similarity(view, _positive[0]) >= Lookalike) return;
        if (_negative.Count > 0 && Compare(view).ToOthers > 0.94) return;
        if (_negative.Count >= MaxNegative) _negative.RemoveAt(0);
        _negative.Add(Copy(view));
    }

    /// <summary>
    /// Learn what the rest of this picture looks like at the moment of selection. Without these
    /// the model can only ask "is this like the subject?", which every roughly similar surface
    /// answers yes to; with them it can ask the far more useful "is this more like the subject
    /// than like anything else here?". Patches are taken only from areas clear of the selection,
    /// so parts of the subject are never learned as background.
    /// </summary>
    private void LearnSurroundings(VideoFrame frame, (int Left, int Top, int Right, int Bottom) box)
    {
        var width = Math.Max(8, box.Right - box.Left);
        var height = Math.Max(8, box.Bottom - box.Top);
        var stepX = Math.Max(8, width / 2);
        var stepY = Math.Max(8, height / 2);
        for (var top = 0; top + height <= frame.Height; top += stepY)
        {
            for (var left = 0; left + width <= frame.Width; left += stepX)
            {
                // Any overlap at all disqualifies a sample: a patch holding the subject's sleeve
                // would teach the model that the subject is background.
                if (left < box.Right && left + width > box.Left
                    && top < box.Bottom && top + height > box.Top) continue;
                Describe(frame, left, top, width, height, _probe);
                RememberOther(_probe, trusted: true);
                if (_negative.Count >= MaxNegative) return;
            }
        }
    }
}
