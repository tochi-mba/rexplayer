namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// A beat edit of the camera, cut live to the music (AU-18), the way short music videos are edited.
/// Nothing in it is left to chance: every move is a drum.
/// <list type="bullet">
/// <item>The kick punches the picture in, snapping in on the hit and easing out, and swings it left
/// then right on alternate kicks; red, green and blue spread apart with it and the bass.</item>
/// <item>With the tempo known the picture also pumps on every beat, like a side-chained bass.</item>
/// <item>The snare tilts the picture, slips glitched slices across it, and flashes on the big ones.</item>
/// <item>Bars of four kicks set the cuts: the colour grade changes every so many kicks, and every
/// eighth kick the picture freezes for half a beat, then snaps back.</item>
/// <item>The hi-hats glitter in the film grain.</item>
/// <item>The drop breaks the picture into four mirrored screens for a bar, with a flash and a twist.</item>
/// </list>
/// A style sets which tricks it plays, the intensity how hard. With no camera, the song's cover is
/// edited. The camera's picture never leaves the computer, and is not kept.
/// </summary>
public sealed class BeatEditScene : VisualScene
{
    private readonly int[] _sliceShift = new int[12];
    private VisualPicture? _frozen;
    private VisualPicture? _made;
    private VisualPicture? _mirrored;
    private double _flash;
    private double _sinceFlash = 10;
    private double _split;
    private double _freeze;
    private double _twist;
    private int _grade;
    private long _flashes;
    private long _frame;
    private double _subjectPanX;
    private double _subjectPanY;
    private double _subjectZoom;

    public override bool UsesCamera => true;

    /// <summary>How many flashes the edit has made: what its tests count.</summary>
    public long Flashes => _flashes;

    /// <summary>The colour grade showing now (see <see cref="Graded"/>).</summary>
    public int Grade => _grade;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var pulse = context.Pulse;
        var (kick, snare, hat) = (pulse.Kick, pulse.Snare, pulse.Hat);
        var tricks = Tricks.For(context.Pick("style"));
        var intensity = context.Number("intensity");
        var dt = context.Dt;
        var source = Source(context);
        var beatSeconds = pulse.Tempo > 0 ? 60 / pulse.Tempo : 0.5;
        _frame++;
        _sinceFlash += dt;

        if (kick.Hit)
        {
            var every = context.Pick("cuts") switch { 0 => 1, 2 => 4, 3 => 8, _ => 2 };
            if (kick.Count % every == 0)
            {
                _grade = tricks.Grades[(int)(kick.Count / every % tricks.Grades.Length)];
            }

            if (tricks.Freeze && kick.Count % 8 == 0)
            {
                _frozen = source with { Bgra = (byte[])source.Bgra.Clone() };
                _freeze = beatSeconds / 2;
            }

            // The first kick of each bar of four flashes, when it is a hard one.
            if (kick.Count % 4 == 1 && kick.Strength > 0.6f)
            {
                Flash(context, tricks.Flash * intensity);
            }
        }

        if (snare.Hit)
        {
            if (tricks.Slices > 0)
            {
                // Which slices slip, and how far, follows the count of snares: varied, never random.
                for (var i = 0; i < _sliceShift.Length; i++)
                {
                    var roll = Noise.Hash((snare.Count * 31) + i);
                    _sliceShift[i] = roll < tricks.Slices * 0.6 ? (int)((Noise.Hash((snare.Count * 57) + i) - 0.5) * context.Canvas.Width * 0.22 * intensity) : 0;
                }
            }

            if (snare.Strength > 0.8f)
            {
                Flash(context, tricks.Flash * intensity * 0.6);
            }
        }

        if (pulse.Drop)
        {
            _split = tricks.Split ? beatSeconds * 4 : 0;
            _twist = (pulse.Beats % 2 == 0 ? 1 : -1) * 0.08 * intensity;
            Flash(context, intensity);
        }

        // The kick's punch snaps in and eases out; the beat's pump swells between kicks.
        var punch = Math.Pow(kick.Level, 0.7) * tricks.Punch * intensity;
        var pump = pulse.Tempo > 0 ? Math.Pow(1 - pulse.Phase, 3) * tricks.Punch * 0.35 * intensity : 0;
        var drift = tricks.Drift ? 1.06 + (0.04 * Math.Sin(context.Seconds * Math.Tau / (beatSeconds * 16))) : 1.03;
        var zoom = drift * (1 + (0.2 * punch) + (0.05 * pump) + (0.12 * Math.Max(0, _split > 0 ? 0 : _twist * _twist * 10)));

        // When an outline is trustworthy, frame the people rather than blindly zooming into the
        // centre. A gentle damped camera follows the silhouette without shaking on every gesture.
        var follow = context.Toggle("follow") && context.Pick("source") == 0 && context.Camera is not null
            && context.Mask is { } mask ? SubjectCentre(mask, context.MaskWidth, context.MaskHeight) : null;
        var mirror = context.Toggle("mirror");
        var targetX = follow is { } subject ? ((mirror ? 1 - subject.X : subject.X) - 0.5) * 0.55 : 0;
        var targetY = follow is { } tracked ? (tracked.Y - 0.5) * 0.30 : 0;
        var targetZoom = follow is not null ? 0.07 : 0;
        var smoothing = 1 - Math.Exp(-Math.Clamp(dt, 0, 0.25) * 5.0);
        _subjectPanX += (targetX - _subjectPanX) * smoothing;
        _subjectPanY += (targetY - _subjectPanY) * smoothing;
        _subjectZoom += (targetZoom - _subjectZoom) * smoothing;

        // Swing: left on one kick, right on the next; a snare tilts it, alternately too.
        var swing = (kick.Count % 2 == 0 ? 1 : -1) * kick.Level * tricks.Shake * 0.05 * intensity;
        var tilt = ((snare.Count % 2 == 0 ? 1 : -1) * snare.Level * tricks.Shake * 0.045 * intensity) + _twist;
        var lift = -snare.Level * tricks.Shake * 0.015 * intensity;
        var rgb = tricks.Rgb * intensity * ((0.025 * kick.Level) + (0.01 * pulse.Bass) + (snare.Level > 0.3f && tricks.Slices > 0 ? 0.015 : 0));
        var shown = _freeze > 0 && _frozen is not null ? _frozen : source;
        var echo = Math.Clamp(tricks.Echo + (0.15 * pulse.Mid * tricks.Echo), 0, 0.85);
        Render(context, shown, zoom + _subjectZoom, tilt, swing + _subjectPanX, lift + _subjectPanY, rgb, echo, snare.Level > 0.25f && tricks.Slices > 0, hat.Level);

        _twist *= Math.Exp(-dt / 0.5);
        _flash = Math.Max(0, _flash - (dt / 0.2));
        _split -= dt;
        _freeze -= dt;
    }

    /// <summary>
    /// Mass centre of a sufficiently large, known foreground shape. This isn't face or person
    /// detection: when a room's outline is unreliable, don't invent a subject or snap the camera.
    /// </summary>
    public static (double X, double Y)? SubjectCentre(ReadOnlySpan<byte> mask, int width, int height)
    {
        if (width <= 0 || height <= 0 || mask.Length != width * height)
        {
            return null;
        }

        long xSum = 0, ySum = 0;
        var pixels = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (mask[(y * width) + x] == 0)
                {
                    continue;
                }

                xSum += x;
                ySum += y;
                pixels++;
            }
        }

        // Ignore sparse camera noise and full-screen lighting failures.
        if (pixels < Math.Max(4, width * height / 80) || pixels > width * height * 0.85)
        {
            return null;
        }

        return ((xSum / (double)pixels + 0.5) / width, (ySum / (double)pixels + 0.5) / height);
    }

    /// <summary>
    /// A colour grade: 0 a little punchier than life, 1 high-contrast black and white, 2 a duotone of
    /// the palette, 3 teal shadows and orange light, 4 posterised, 5 warm film; 6 is the negative.
    /// </summary>
    public static Rgb Graded(int grade, Rgb color, Rgb dark, Rgb bright)
    {
        var luma = Math.Clamp(color.Luma, 0, 1);
        switch (grade)
        {
            case 1:
                var mono = Smooth(0.12f, 0.88f, luma);
                return new Rgb(mono, mono, mono);
            case 2:
                return dark.Toward(bright, Smooth(0.05f, 0.95f, luma));
            case 3:
                var film = new Rgb(0.05f, 0.32f, 0.38f).Toward(new Rgb(1f, 0.66f, 0.36f), luma);
                return color.Toward(film, 0.55f);
            case 4:
                return new Rgb(Poster(color.R), Poster(color.G), Poster(color.B));
            case 5:
                return new Rgb(0.05f + (color.R * 1.08f), 0.04f + (color.G * 0.98f), 0.03f + (color.B * 0.8f));
            case 6:
                return new Rgb(1 - color.R, 1 - color.G, 1 - color.B);
            default:
                return new Rgb(Contrast(color.R), Contrast(color.G), Contrast(color.B));
        }
    }

    private static float Contrast(float c) => Math.Max(0, ((c - 0.5f) * 1.15f) + 0.5f);

    private static float Poster(float c) => MathF.Round(Math.Clamp(c * 1.2f, 0, 1) * 3) / 3;

    private static float Smooth(float from, float to, float x)
    {
        var t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - (2 * t));
    }

    /// <summary>A white flash, no more often than the setting allows; none at all when flashing is off.</summary>
    private void Flash(VisualContext context, double strength)
    {
        if (strength > 0 && context.Toggle("flash") && _sinceFlash >= 1 / context.Number("flashes"))
        {
            _flash = Math.Min(1, strength);
            _sinceFlash = 0;
            _flashes++;
        }
    }

    /// <summary>What is edited: the camera, else the cover, else a field of the palette's colours that moves with the spectrum.</summary>
    private VisualPicture Source(VisualContext context)
    {
        var mirror = context.Toggle("mirror");
        if (context.Pick("source") == 0 && context.Camera is { } camera)
        {
            return mirror ? Mirrored(camera) : camera;
        }

        if (context.Cover is { } cover)
        {
            return cover;
        }

        const int W = 64, H = 36;
        _made ??= new VisualPicture(new byte[W * H * 4], W, H);
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var band = context.Pulse.Bands[x * context.Pulse.Bands.Count / W];
                var color = context.Paint(((double)x / W) + (context.Seconds * 0.05)).Times(0.25f + (band * (1 - ((float)y / H))));
                var at = ((y * W) + x) * 4;
                (_made.Bgra[at], _made.Bgra[at + 1], _made.Bgra[at + 2], _made.Bgra[at + 3]) =
                    ((byte)(Math.Min(1, color.B) * 255), (byte)(Math.Min(1, color.G) * 255), (byte)(Math.Min(1, color.R) * 255), 255);
            }
        }

        return _made;
    }

    private VisualPicture Mirrored(VisualPicture picture)
    {
        if (_mirrored is null || _mirrored.Width != picture.Width || _mirrored.Height != picture.Height)
        {
            _mirrored = new VisualPicture(new byte[picture.Width * picture.Height * 4], picture.Width, picture.Height);
        }

        for (var y = 0; y < picture.Height; y++)
        {
            for (var x = 0; x < picture.Width; x++)
            {
                Array.Copy(picture.Bgra, ((y * picture.Width) + x) * 4, _mirrored.Bgra, ((y * picture.Width) + picture.Width - 1 - x) * 4, 4);
            }
        }

        return _mirrored;
    }

    /// <summary>Draws <paramref name="source"/> through the edit's camera moves, colour and effects.</summary>
    private void Render(VisualContext context, VisualPicture source, double zoom, double tilt, double jx, double jy, double rgb, double echo, bool glitched, float hats)
    {
        var canvas = context.Canvas;
        var (w, h) = (canvas.Width, canvas.Height);
        var split = _split > 0;
        var (dark, bright) = (context.Paint(0.0).Times(0.12f), context.Paint(0.6));
        var tint = context.Paint(0.15).Times(0.22f);
        // Cinematic bars that close a little on each kick.
        var bars = context.Toggle("bars") ? (int)(h * (0.09 + (0.03 * context.Pulse.Kick.Level))) : 0;
        var grain = context.Toggle("grain");

        // Film grain, a new pattern each frame, glittering harder on the hi-hats.
        var seed = _frame * 7_919;
        var grainAmount = 0.05 + (0.12 * hats);

        var (cos, sin) = (Math.Cos(tilt) / zoom, Math.Sin(tilt) / zoom);
        var flash = (float)_flash;
        var keep = (float)echo;
        Raster.Rows(h, y =>
        {
            var slice = glitched ? _sliceShift[y * _sliceShift.Length / h] : 0;
            for (var x = 0; x < w; x++)
            {
                var i = (y * w) + x;
                if (y < bars || y >= h - bars)
                {
                    canvas.R[i] = canvas.G[i] = canvas.B[i] = 0;
                    continue;
                }

                // Where in a full frame this pixel looks: four mirrored screens while split.
                double fx = (double)(x + slice) / w, fy = (double)y / h;
                if (split)
                {
                    fx = fx * 2 % 1;
                    fy = fy * 2 % 1;
                    fx = x * 2 >= w ? 1 - fx : fx;
                }

                // Centred, turned and scaled, in widths of the stage.
                var (u, v) = ((fx - 0.5) * w / w, (fy - 0.5) * h / w);
                var (su, sv) = ((u * cos) - (v * sin) + jx, (u * sin) + (v * cos) + jy);
                var (px, py) = ((w / 2.0) + (su * w), (h / 2.0) + (sv * w));
                var color = SampleSplit(source, px, py, w, h, su * rgb * w, sv * rgb * w);
                color = Graded(_grade, color, dark, bright);

                // Split-toning: the shadows lean toward the palette's deep colour.
                var shadow = Math.Max(0, 1 - (color.Luma * 2));
                color = color.Plus(tint.Times(shadow));
                if (grain)
                {
                    var n = (float)((Noise.Hash(seed + i) - 0.5) * grainAmount);
                    color = new Rgb(color.R + n, color.G + n, color.B + n);
                }

                canvas.R[i] = (color.R * (1 - keep)) + (canvas.R[i] * keep) + flash;
                canvas.G[i] = (color.G * (1 - keep)) + (canvas.G[i] * keep) + flash;
                canvas.B[i] = (color.B * (1 - keep)) + (canvas.B[i] * keep) + flash;
            }
        });
    }

    /// <summary>The source at a stage pixel, its picture scaled to fill the stage, red and blue drawn apart by (<paramref name="ox"/>, <paramref name="oy"/>).</summary>
    private static Rgb SampleSplit(VisualPicture source, double px, double py, int w, int h, double ox, double oy)
    {
        var scale = Math.Max((double)w / source.Width, (double)h / source.Height);
        var (left, top) = ((w - (source.Width * scale)) / 2, (h - (source.Height * scale)) / 2);
        var (sx, sy) = ((px - left) / scale, (py - top) / scale);
        var (dx, dy) = (ox / scale, oy / scale);
        return new Rgb(source.Sample(sx + dx, sy + dy, 2), source.Sample(sx, sy, 1), source.Sample(sx - dx, sy - dy, 0));
    }

    /// <summary>What each style plays, and how hard.</summary>
    private sealed record Tricks(double Punch, double Shake, double Rgb, double Slices, double Echo, bool Split, bool Freeze, bool Drift, double Flash, int[] Grades)
    {
        public static Tricks For(int style) => style switch
        {
            // Glitch: colour split and slices, posterised and duotone grades.
            1 => new(0.5, 0.4, 2.2, 0.7, 0.1, false, false, false, 0.5, [2, 4, 0, 2, 6]),

            // Hype: hard punches and shakes, a grade every cut, four screens on the drop.
            2 => new(1.0, 1.0, 1.0, 0.25, 0.0, true, false, false, 0.8, [0, 2, 4, 1, 3]),

            // Dreamy: slow drift and long echoes, warm and teal grades, no shaking.
            3 => new(0.35, 0.0, 0.5, 0.0, 0.6, false, false, true, 0.0, [5, 3, 0]),

            // A bit of everything.
            4 => new(0.8, 0.6, 1.2, 0.35, 0.25, true, true, true, 0.6, [0, 1, 2, 3, 4, 5]),

            // Velocity: punch-ins, freeze frames, black and white cuts, white flashes.
            _ => new(1.1, 0.3, 0.8, 0.0, 0.15, false, true, false, 1.0, [0, 1, 5, 1]),
        };
    }
}

