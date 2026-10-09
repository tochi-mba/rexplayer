namespace Rex.Media.AppCore.Visuals;

/// <summary>
/// A beat edit of the camera, cut live to the music (AU-18), the way short music videos are edited:
/// the picture punches in on every beat, shakes, splits into red, green and blue, slips in glitched
/// slices, cuts between colour grades every few beats, freezes for half a beat and snaps back, leaves
/// echoes, flashes white on the big hits, and on the drop breaks into four mirrored screens. A style
/// sets which tricks it plays, the intensity how hard; with no camera, the song's cover is edited.
/// The camera's picture never leaves the computer, and is not kept.
/// </summary>
public sealed class BeatEditScene : VisualScene
{
    private const int Grain = 4096;

    private readonly float[] _grain = new float[Grain];
    private readonly int[] _sliceShift = new int[12];
    private VisualPicture? _frozen;
    private VisualPicture? _made;
    private double _punch;
    private double _shake;
    private double _flash;
    private double _sinceFlash = 10;
    private double _glitch;
    private double _split;
    private double _freeze;
    private double _tilt;
    private int _grade;
    private long _flashes;

    public override bool UsesCamera => true;

    /// <summary>How many flashes the edit has made: what its tests count.</summary>
    public long Flashes => _flashes;

    /// <summary>The colour grade showing now (see <see cref="Graded"/>).</summary>
    public int Grade => _grade;

    public override void Draw(VisualContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var canvas = context.Canvas;
        var pulse = context.Pulse;
        var tricks = Tricks.For(context.Pick("style"));
        var intensity = context.Number("intensity");
        var dt = context.Dt;
        var random = context.Random;
        var source = Source(context);
        _sinceFlash += dt;
        var beatSeconds = pulse.Tempo > 0 ? 60 / pulse.Tempo : 0.5;
        if (pulse.Beat)
        {
            _punch = Math.Max(_punch, tricks.Punch * (0.5 + (0.5 * pulse.BeatStrength)) * intensity);
            _shake = Math.Max(_shake, tricks.Shake * pulse.BeatStrength * intensity);
            var every = context.Pick("cuts") switch { 0 => 1, 2 => 4, 3 => 8, _ => 2 };
            if (pulse.Beats % every == 0)
            {
                _grade = tricks.Grades[(int)(pulse.Beats / every % tricks.Grades.Length)];
            }

            if (tricks.Slices > 0 && random.NextDouble() < tricks.Slices)
            {
                _glitch = 0.18;
                for (var i = 0; i < _sliceShift.Length; i++)
                {
                    _sliceShift[i] = random.NextDouble() < 0.4 ? (int)((random.NextDouble() - 0.5) * canvas.Width * 0.2 * intensity) : 0;
                }
            }

            if (tricks.Freeze && pulse.Beats % 8 == 0 && source is not null)
            {
                _frozen = source with { Bgra = (byte[])source.Bgra.Clone() };
                _freeze = beatSeconds / 2;
            }

            if (pulse.BeatStrength > 0.7)
            {
                Flash(context, tricks.Flash * intensity);
            }
        }

        if (pulse.Drop)
        {
            _punch = Math.Max(_punch, 1.2 * intensity);
            _tilt = (random.NextDouble() < 0.5 ? -1 : 1) * 0.07 * intensity;
            _split = tricks.Split ? beatSeconds * 4 : 0;
            Flash(context, intensity);
        }

        var zoom = (tricks.Drift ? 1.06 + (0.04 * Math.Sin(context.Seconds * 0.35)) : 1.02) * (1 + (0.24 * _punch));
        var (jx, jy) = ((random.NextDouble() - 0.5) * _shake * 0.06, (random.NextDouble() - 0.5) * _shake * 0.06);
        var split = (tricks.Rgb * intensity * (0.004 + (0.03 * _punch) + (0.012 * pulse.Bass))) + (_glitch > 0 ? 0.02 : 0);
        var shown = _freeze > 0 && _frozen is not null ? _frozen : source!;
        Render(context, shown, zoom, _tilt, jx, jy, split, tricks.Echo);

        _punch *= Math.Exp(-dt / 0.16);
        _shake *= Math.Exp(-dt / 0.12);
        _tilt *= Math.Exp(-dt / 0.6);
        _flash = Math.Max(0, _flash - (dt / 0.22));
        _glitch -= dt;
        _split -= dt;
        _freeze -= dt;
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

    private VisualPicture? _mirrored;

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
    private void Render(VisualContext context, VisualPicture source, double zoom, double tilt, double jx, double jy, double rgb, double echo)
    {
        var canvas = context.Canvas;
        var (w, h) = (canvas.Width, canvas.Height);
        var split = _split > 0;
        var (dark, bright) = (context.Paint(0.0).Times(0.12f), context.Paint(0.6));
        var bars = context.Toggle("bars") ? (int)(h * 0.1) : 0;
        var grain = context.Toggle("grain");
        if (grain)
        {
            for (var i = 0; i < Grain; i++)
            {
                _grain[i] = (float)((context.Random.NextDouble() - 0.5) * 0.07);
            }
        }

        var (cos, sin) = (Math.Cos(tilt) / zoom, Math.Sin(tilt) / zoom);
        var flash = (float)_flash;
        var keep = (float)echo;
        for (var y = 0; y < h; y++)
        {
            var slice = _glitch > 0 ? _sliceShift[y * _sliceShift.Length / h] : 0;
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
                if (grain)
                {
                    var n = _grain[(i * 7) % Grain];
                    color = new Rgb(color.R + n, color.G + n, color.B + n);
                }

                canvas.R[i] = (color.R * (1 - keep)) + (canvas.R[i] * keep) + flash;
                canvas.G[i] = (color.G * (1 - keep)) + (canvas.G[i] * keep) + flash;
                canvas.B[i] = (color.B * (1 - keep)) + (canvas.B[i] * keep) + flash;
            }
        }
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

