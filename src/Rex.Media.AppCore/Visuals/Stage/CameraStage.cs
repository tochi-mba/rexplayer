using Rex.Media.Video;
using Rex.Media.Vision;

namespace Rex.Media.AppCore.Visuals.Stage;

/// <summary>
/// A camera effect drawn on the graphics card (ADR-0022). <see cref="CameraStage"/> hands it the
/// stage each frame; it draws into <see cref="CameraStage.Light"/>, and the stage blooms and tones
/// that to the screen. Textures it keeps are made at the stage's size on first use and let go in
/// <see cref="Release"/>, which the stage calls when its size changes or the effect is put away.
/// </summary>
public abstract class CameraEffect
{
    /// <summary>Every program the effect draws with, so all of them can be compiled in tests.</summary>
    public abstract IReadOnlyList<StageProgram> Programs { get; }

    /// <summary>How strongly the effect's light blooms.</summary>
    public virtual float Bloom => 0.9f;

    /// <summary>The light's exposure before the tone curve.</summary>
    public virtual float Exposure => 1;

    /// <summary>Draws a frame into <see cref="CameraStage.Light"/>.</summary>
    public abstract void Draw(CameraStage stage);

    /// <summary>Lets go of the effect's own textures.</summary>
    public virtual void Release(IStageGpu gpu)
    {
    }
}

/// <summary>
/// Runs a <see cref="CameraEffect"/> on an <see cref="IStageGpu"/> (ADR-0022): uploads what changed
/// (the camera's picture when a new one came, the scene when perception finished one), fills the
/// shared constants (time, the music, where the picture sits, the palette, the hands), lets the
/// effect draw into the light, and blooms and tones the light onto the output. Uploads happen only
/// on change; every buffer is made once, so a frame allocates nothing.
/// </summary>
public sealed class CameraStage : IDisposable
{
    /// <summary>Points the camera saw, as the programs read them: two hands of 21, then the body's 33.</summary>
    public const int PointCount = (HandSense.Points * 2) + BodySense.Points;

    private const int BloomLevels = 4;

    private readonly IStageGpu _gpu;
    private readonly float[] _constants = new float[IStageGpu.MaxConstants];
    private readonly float[] _shared = new float[StageShaders.SharedRows * 4];
    private readonly float[] _points = new float[PointCount * 4];
    private readonly StageTexture[] _bloom = new StageTexture[BloomLevels];
    private readonly (float X, float Y, float Vx, float Vy, bool Seen)[] _tips = new (float, float, float, float, bool)[2];
    private float[] _flow = [];
    private CameraEffect? _drawn;
    private (int Width, int Height) _size;
    private (int Width, int Height) _pictureSize;
    private long _cameraStamp = long.MinValue;
    private long _senseStamp = long.MinValue;
    private float _beat;
    private float _drop;
    private bool _made;

    public CameraStage(IStageGpu gpu)
    {
        _gpu = gpu ?? throw new ArgumentNullException(nameof(gpu));
    }

    public IStageGpu Gpu => _gpu;

    public int Width => _gpu.Width;

    public int Height => _gpu.Height;

    /// <summary>What the effect draws into: light in floating point, brighter than white allowed.</summary>
    public StageTexture Light { get; private set; }

    /// <summary>The camera's picture, as it came (never mirrored).</summary>
    public StageTexture Camera { get; private set; }

    /// <summary>The listener's matte, 0 to 1, as the scene saw it.</summary>
    public StageTexture Matte { get; private set; }

    /// <summary>How the picture moves: x and y in matte pixels a frame, a cell per <see cref="SceneSense.FlowStep"/> matte pixels.</summary>
    public StageTexture Flow { get; private set; }

    /// <summary>The points the camera saw (x, y as shares of the scene, depth, confidence; confidence 0 for a part not seen).</summary>
    public StageTexture Points { get; private set; }

    /// <summary>The frame's seconds since the last, and the song's time.</summary>
    public float Dt { get; private set; }

    public float Seconds { get; private set; }

    /// <summary>The music as heard this frame.</summary>
    public MusicPulse? Pulse { get; private set; }

    /// <summary>The scene as last perceived.</summary>
    public SceneSense? Sense { get; private set; }

    /// <summary>
    /// The constants for a draw: the shared rows as this frame filled them, the effect's own rows
    /// (from <see cref="StageShaders.EffectRow"/>) cleared. Ask again before each draw that changes them.
    /// </summary>
    public Span<float> Constants()
    {
        Array.Clear(_constants);
        _shared.CopyTo(_constants, 0);
        return _constants;
    }

    /// <summary>A texture the effect keeps, made at the given size on first use.</summary>
    public StageTexture Keep(ref StageTexture slot, int width, int height, StageFormat format)
    {
        if (slot.Id == 0)
        {
            slot = _gpu.Texture(Math.Max(1, width), Math.Max(1, height), format);
        }

        return slot;
    }

    /// <summary>Lets go of a texture the effect kept, leaving the slot empty.</summary>
    public static void Drop(IStageGpu gpu, ref StageTexture slot)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        if (slot.Id != 0)
        {
            gpu.Release(slot);
            slot = default;
        }
    }

    /// <summary>
    /// Draws a frame of <paramref name="effect"/>: <paramref name="camera"/> is the camera's newest
    /// picture (uploaded when <paramref name="cameraStamp"/> is new), <paramref name="sense"/> the
    /// newest scene, <paramref name="palette"/> four colours, <paramref name="dt"/> the seconds since
    /// the last frame.
    /// </summary>
    public void Draw(CameraEffect effect, VisualPicture? camera, long cameraStamp, SceneSense sense, MusicPulse pulse,
        ReadOnlySpan<Rgb> palette, double seconds, double dt, bool mirror)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(sense);
        ArgumentNullException.ThrowIfNull(pulse);
        if (palette.Length != 4)
        {
            throw new ArgumentException("The stage's palette is four colours.", nameof(palette));
        }

        Prepare(effect, sense);
        if (camera is { Width: > 0, Height: > 0 } && cameraStamp != _cameraStamp)
        {
            if ((camera.Width, camera.Height) != _pictureSize)
            {
                Drop(_gpu, ref _cameraSlot);
                _pictureSize = (camera.Width, camera.Height);
            }

            Camera = Keep(ref _cameraSlot, camera.Width, camera.Height, StageFormat.Bgra8);
            _gpu.Upload(Camera, camera.Bgra, camera.Width * 4);
            _cameraStamp = cameraStamp;
        }

        if (sense.Timestamp != _senseStamp)
        {
            UploadSense(sense);
            _senseStamp = sense.Timestamp;
        }

        (Dt, Seconds, Pulse, Sense) = ((float)(double.IsFinite(dt) ? Math.Clamp(dt, 0, 0.25) : 0), (float)seconds, pulse, sense);
        Share(sense, pulse, palette, mirror);
        effect.Draw(this);
        Finish(effect);
    }

    private StageTexture _cameraSlot;
    private StageTexture _matteSlot;
    private StageTexture _flowSlot;
    private StageTexture _pointsSlot;
    private StageTexture _lightSlot;

    /// <summary>Makes the textures for the stage's size, and lets the last effect go when the effect or the size changes.</summary>
    private void Prepare(CameraEffect effect, SceneSense sense)
    {
        var size = (_gpu.Width, _gpu.Height);
        if (_made && size == _size && ReferenceEquals(effect, _drawn))
        {
            return;
        }

        _drawn?.Release(_gpu);
        if (size != _size)
        {
            Drop(_gpu, ref _lightSlot);
            for (var i = 0; i < BloomLevels; i++)
            {
                Drop(_gpu, ref _bloom[i]);
            }
        }

        _size = size;
        _drawn = effect;
        Light = Keep(ref _lightSlot, size.Width, size.Height, StageFormat.Half4);
        for (var i = 0; i < BloomLevels; i++)
        {
            Keep(ref _bloom[i], size.Width >> (i + 1), size.Height >> (i + 1), StageFormat.Half4);
        }

        if (!_made)
        {
            Matte = Keep(ref _matteSlot, sense.Width, sense.Height, StageFormat.Float1);
            Flow = Keep(ref _flowSlot, sense.FlowWidth, sense.FlowHeight, StageFormat.Float2);
            Points = Keep(ref _pointsSlot, PointCount, 1, StageFormat.Float4);
            _flow = new float[sense.FlowWidth * sense.FlowHeight * 2];
            Camera = Keep(ref _cameraSlot, 1, 1, StageFormat.Bgra8);
            _pictureSize = (1, 1);
            _made = true;
        }
    }

    private void UploadSense(SceneSense sense)
    {
        if (Matte.Width != sense.Width || Matte.Height != sense.Height)
        {
            throw new ArgumentException("The scene is a different size from the stage's matte.", nameof(sense));
        }

        _gpu.Upload(Matte, sense.Matte, sense.Width);
        for (var i = 0; i < sense.FlowX.Length; i++)
        {
            (_flow[i * 2], _flow[(i * 2) + 1]) = (sense.FlowX[i], sense.FlowY[i]);
        }

        _gpu.Upload(Flow, _flow, sense.FlowWidth * 2);
        var at = 0;
        foreach (var hand in sense.Hands)
        {
            Pack(hand.Landmarks, hand.Present, ref at);
        }

        Pack(sense.Body.Landmarks, sense.Body.Present, ref at);
        _gpu.Upload(Points, _points, PointCount * 4);
    }

    private void Pack(Landmark[] landmarks, bool present, ref int at)
    {
        foreach (var point in landmarks)
        {
            (_points[at], _points[at + 1], _points[at + 2], _points[at + 3]) = (point.X, point.Y, point.Z, present ? Math.Max(0.01f, point.Confidence) : 0);
            at += 4;
        }
    }

    /// <summary>Fills the shared rows (see <see cref="StageShaders.Prelude"/>).</summary>
    private void Share(SceneSense sense, MusicPulse pulse, ReadOnlySpan<Rgb> palette, bool mirror)
    {
        var s = _shared;
        var (width, height) = (Math.Max(1, _gpu.Width), Math.Max(1, _gpu.Height));
        (s[0], s[1], s[2], s[3]) = (Seconds, Dt, width, height);
        (s[4], s[5], s[6], s[7]) = (pulse.Bass, pulse.Mid, pulse.Treble, pulse.Punch);
        _beat = pulse.Beat ? 1 : Math.Max(0, _beat - (Dt * 4));
        _drop = pulse.Drop ? 1 : Math.Max(0, _drop - (Dt * 0.8f));
        (s[8], s[9], s[10], s[11]) = (pulse.Loudness, (float)pulse.Phase, _beat, pulse.Build);

        // The camera's picture fills the stage, cut at its longer edges: a stage point's share of
        // the picture is uv * scale + offset.
        var (pw, ph) = ((float)_pictureSize.Width, (float)_pictureSize.Height);
        var fill = Math.Max(width / pw, height / ph);
        var (scaleX, scaleY) = (width / (fill * pw), height / (fill * ph));
        (s[12], s[13], s[14], s[15]) = (scaleX, scaleY, (1 - scaleX) / 2, (1 - scaleY) / 2);
        (s[16], s[17], s[18], s[19]) = (mirror ? 1 : 0, sense.Presence, sense.Motion, _drop);
        for (var i = 0; i < 4; i++)
        {
            (s[20 + (i * 4)], s[21 + (i * 4)], s[22 + (i * 4)], s[23 + (i * 4)]) = (palette[i].R, palette[i].G, palette[i].B, 1);
        }

        for (var h = 0; h < 2; h++)
        {
            var hand = sense.Hands[h];
            var row = 36 + (h * 4);
            if (hand.Present)
            {
                var tip = hand.Landmarks[HandSense.IndexTip];
                var (x, y) = ((tip.X - s[14]) / scaleX, (tip.Y - s[15]) / scaleY);
                var last = _tips[h];
                var (vx, vy) = last.Seen && Dt > 0 ? ((x - last.X) / Dt, (y - last.Y) / Dt) : (0f, 0f);
                // Velocities are smoothed: one picture's jump must not read as a flick.
                _tips[h] = (x, y, (last.Vx * 0.5f) + (vx * 0.5f), (last.Vy * 0.5f) + (vy * 0.5f), true);
            }
            else
            {
                _tips[h] = default;
            }

            (s[row], s[row + 1], s[row + 2], s[row + 3]) = (_tips[h].X, _tips[h].Y, _tips[h].Vx, _tips[h].Vy);
            s[44 + (h * 2)] = hand.Present ? 1 : 0;
            s[45 + (h * 2)] = hand.Present ? Math.Clamp(1 - (hand.Pinch / 1.5f), 0, 1) : 0;
        }

        var body = sense.Body;
        var (bx, by) = body.Present ? body.Shoulders : (0.5f, 0.5f);
        (s[48], s[49], s[50], s[51]) = ((bx - s[14]) / scaleX, (by - s[15]) / scaleY, body.Width / scaleX, body.Present ? 1 : 0);
        s[52] = (float)width / height;
    }

    /// <summary>Blooms the light over four halvings and tones it onto the output.</summary>
    private void Finish(CameraEffect effect)
    {
        _gpu.Pass(StageShaders.Bright, _bloom[0], [Light], Constants());
        for (var i = 1; i < BloomLevels; i++)
        {
            var c = Constants();
            (c[64], c[65]) = (1f / _bloom[i - 1].Width, 1f / _bloom[i - 1].Height);
            _gpu.Pass(StageShaders.Down, _bloom[i], [_bloom[i - 1]], c);
        }

        for (var i = BloomLevels - 1; i > 0; i--)
        {
            var c = Constants();
            (c[64], c[65]) = (1f / _bloom[i].Width, 1f / _bloom[i].Height);
            _gpu.Pass(StageShaders.Up, _bloom[i - 1], [_bloom[i]], c, StageBlend.Add);
        }

        var finish = Constants();
        (finish[64], finish[65]) = (effect.Bloom, effect.Exposure);
        _gpu.Pass(StageShaders.Finish, null, [Light, _bloom[0]], finish);
    }

    /// <summary>Lets go of every texture the stage and its last effect kept.</summary>
    public void Dispose()
    {
        _drawn?.Release(_gpu);
        _drawn = null;
        foreach (var slot in new[] { _cameraSlot, _matteSlot, _flowSlot, _pointsSlot, _lightSlot })
        {
            var held = slot;
            Drop(_gpu, ref held);
        }

        for (var i = 0; i < BloomLevels; i++)
        {
            Drop(_gpu, ref _bloom[i]);
        }

        (_cameraSlot, _matteSlot, _flowSlot, _pointsSlot, _lightSlot, _made) = (default, default, default, default, default, false);
    }
}
