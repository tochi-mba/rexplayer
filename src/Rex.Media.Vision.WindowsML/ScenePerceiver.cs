using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;

namespace Rex.Media.Vision.WindowsML;

/// <summary>
/// The camera pack (ADR-0022) on Windows ML: fills a <see cref="SceneSense"/> from a camera picture.
/// <see cref="PerceptionPlan"/> says which models run on each picture; every decision and every
/// tensor's layout is pure code in <c>Rex.Media.Vision</c>, and this class only fills tensors, runs
/// sessions and times them. The hand-written perceiver always runs first, so whatever a model does
/// not run for (or fails at) still has an answer. One call at a time; nothing allocated per picture
/// beyond what the runtime's own outputs need.
/// </summary>
public sealed class ScenePerceiver : IScenePerceiver, IDisposable
{
    private const float PalmThreshold = 0.5f;
    private const float PersonThreshold = 0.5f;
    private const float Overlap = 0.3f;
    private const float HandKeep = 0.6f;
    private const float BodyKeep = 0.5f;

    /// <summary>Processor threads a camera model may use: it runs on every picture, beside the sound and the window.</summary>
    private const int Threads = 2;

    private readonly InferenceSession _matte;
    private readonly InferenceSession _palm;
    private readonly InferenceSession _hand;
    private readonly InferenceSession _person;
    private readonly InferenceSession _pose;
    private readonly RunOptions _run = new();
    private readonly HandWrittenPerceiver _fallback;
    private readonly DetectorDecoder _palms = new(AnchorGrid.Palm, 7, 2);
    private readonly DetectorDecoder _people = new(AnchorGrid.Person, 4, 1);
    private readonly float[] _matteInput = new float[3 * SceneDecoding.MatteSide * SceneDecoding.MatteSide];
    private readonly float[] _palmInput = new float[3 * 192 * 192];
    private readonly float[] _handInput = new float[3 * SceneDecoding.HandSide * SceneDecoding.HandSide];
    private readonly float[] _personInput = new float[3 * 224 * 224];
    private readonly float[] _poseInput = new float[3 * SceneDecoding.PoseSide * SceneDecoding.PoseSide];
    private readonly CropFrame?[] _handWindows = new CropFrame?[2];
    private readonly LandmarkSmoother[] _handSmoothers = [new(HandSense.Points), new(HandSense.Points)];
    private readonly LandmarkSmoother _bodySmoother = new(BodySense.Points);
    private readonly HandSense[] _found = [new(), new()];
    private CropFrame? _bodyWindow;
    private long _lastTimestamp;

    private ScenePerceiver(InferenceSession[] sessions, ExecutionDevice device, int width, int height)
    {
        (_matte, _palm, _hand, _person, _pose) = (sessions[0], sessions[1], sessions[2], sessions[3], sessions[4]);
        Device = device;
        _fallback = new HandWrittenPerceiver(width, height);
    }

    public ExecutionDevice Device { get; }

    public PerceptionPlan Plan { get; } = new();

    /// <summary>
    /// Opens the camera pack, or returns null when it is not installed at the catalogue's version:
    /// the caller then uses <see cref="HandWrittenPerceiver"/> alone.
    /// </summary>
    public static ScenePerceiver? Open(ModelStore store, ExecutionDevice? device = null, int width = SceneSense.DefaultWidth, int height = SceneSense.DefaultHeight)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (ModelCatalogue.CameraPack.Any(model => store.Inspect(model) != InstallPhase.Installed))
        {
            return null;
        }

        var wanted = device ?? OnnxSessions.Preferred();
        var sessions = new List<InferenceSession>();
        try
        {
            var used = wanted;
            foreach (var model in ModelCatalogue.CameraPack)
            {
                var (session, ran) = OnnxSessions.Open(store.PathOf(model, model.Files[0]), wanted, Threads);
                sessions.Add(session);
                used = ran;
            }

            return new ScenePerceiver([.. sessions], used, width, height);
        }
        catch
        {
            sessions.ForEach(session => session.Dispose());
            throw;
        }
    }

    /// <summary>Fills <paramref name="scene"/> from <paramref name="picture"/>, mirrored when the camera is shown as a mirror.</summary>
    public void Perceive(BgraPicture picture, bool mirror, SceneSense scene, long timestamp)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var dt = _lastTimestamp == 0 ? 0 : (float)((timestamp - _lastTimestamp) / (double)Stopwatch.Frequency);
        _lastTimestamp = timestamp;
        // The flow is always rexplayer's own; the matte and the hands too unless the models run.
        _fallback.Perceive(picture, mirror, scene, timestamp, motionOnly: Plan.Level != PerceptionLevel.HandWritten);
        if (Plan.Level == PerceptionLevel.HandWritten)
        {
            return;
        }

        scene.Source = SenseSource.Model;
        var letterbox = CropFrame.Around(picture.Width, picture.Height);
        var followed = (_handWindows[0] is null ? 0 : 1) + (_handWindows[1] is null ? 0 : 1);
        Plan.Next(followed, _bodyWindow is not null);

        if (Plan.RunMatte)
        {
            var started = Stopwatch.GetTimestamp();
            PictureTensor.Fill(picture, letterbox, SceneDecoding.MatteSide, TensorLayout.Nchw, 0.5f, 2, _matteInput);
            using (var input = OrtValue.CreateTensorValueFromMemory(_matteInput, [1, 3, SceneDecoding.MatteSide, SceneDecoding.MatteSide]))
            using (var output = _matte.Run(_run, _matte.InputNames, [input], _matte.OutputNames))
            {
                SceneDecoding.Matte(output[0].GetTensorDataAsSpan<float>(), letterbox, picture.Width, picture.Height, mirror, scene.Width, scene.Height, scene.Matte);
            }

            Plan.MatteBudget.Record(Stopwatch.GetElapsedTime(started));
        }

        if (Plan.RunPalm)
        {
            var started = Stopwatch.GetTimestamp();
            FindPalms(picture, letterbox);
            Plan.PalmBudget.Record(Stopwatch.GetElapsedTime(started));
        }

        if (Plan.RunHands)
        {
            var started = Stopwatch.GetTimestamp();
            FollowHands(picture, mirror, scene, dt);
            Plan.HandBudget.Record(Stopwatch.GetElapsedTime(started));
        }
        else
        {
            // Not run this picture: keep the hands the models last saw rather than the guessed ones.
            for (var i = 0; i < 2; i++)
            {
                if (_found[i].Present)
                {
                    _found[i].CopyTo(scene.Hands[i]);
                }
            }
        }

        if (Plan.RunPerson && _bodyWindow is null)
        {
            var started = Stopwatch.GetTimestamp();
            FindPerson(picture, letterbox);
            Plan.PersonBudget.Record(Stopwatch.GetElapsedTime(started));
        }

        if (Plan.RunPose && _bodyWindow is { } window)
        {
            var started = Stopwatch.GetTimestamp();
            FollowBody(picture, window, mirror, scene, dt);
            Plan.PoseBudget.Record(Stopwatch.GetElapsedTime(started));
        }
    }

    private void FindPalms(BgraPicture picture, CropFrame letterbox)
    {
        PictureTensor.Fill(picture, letterbox, 192, TensorLayout.Nhwc, 0, 1, _palmInput);
        using var input = OrtValue.CreateTensorValueFromMemory(_palmInput, [1, 192, 192, 3]);
        using var output = _palm.Run(_run, _palm.InputNames, [input], _palm.OutputNames);
        _palms.Decode(output[0].GetTensorDataAsSpan<float>(), output[1].GetTensorDataAsSpan<float>(), PalmThreshold, Overlap);
        for (var d = 0; d < _palms.Count; d++)
        {
            var window = CropPlacement.Hand(_palms.Detections[d], letterbox);
            // A palm already followed keeps its window; a new one takes a free slot.
            if (_handWindows.Any(known => known is { } k && Near(k, window)))
            {
                continue;
            }

            var free = Array.IndexOf(_handWindows, null);
            if (free >= 0)
            {
                _handWindows[free] = window;
            }
        }
    }

    private void FollowHands(BgraPicture picture, bool mirror, SceneSense scene, float dt)
    {
        for (var slot = 0; slot < 2; slot++)
        {
            _found[slot].Clear();
            if (_handWindows[slot] is not { } window)
            {
                continue;
            }

            PictureTensor.Fill(picture, window, SceneDecoding.HandSide, TensorLayout.Nhwc, 0, 1, _handInput);
            using var input = OrtValue.CreateTensorValueFromMemory(_handInput, [1, SceneDecoding.HandSide, SceneDecoding.HandSide, 3]);
            using var output = _hand.Run(_run, _hand.InputNames, [input], _hand.OutputNames);
            var confidence = output[1].GetTensorDataAsSpan<float>()[0];
            if (confidence < HandKeep)
            {
                _handWindows[slot] = null;
                continue;
            }

            SceneDecoding.Hand(output[0].GetTensorDataAsSpan<float>(), confidence, output[2].GetTensorDataAsSpan<float>()[0], window, picture.Width, picture.Height, mirror, _found[slot]);
            // The next window follows this hand's own points, so the detector need not run again.
            _handWindows[slot] = FollowWindow(_found[slot], picture.Width, picture.Height, mirror);
        }

        // Slots hold hands in the order found; the scene holds the left hand first.
        for (var side = 0; side < 2; side++)
        {
            var hand = scene.Hands[side];
            var match = _found[0].Present && _found[0].IsLeft == (side == 0) ? _found[0]
                : _found[1].Present && _found[1].IsLeft == (side == 0) ? _found[1]
                : null;
            if (match is not null)
            {
                match.CopyTo(hand);
                hand.IsLeft = side == 0;
            }
            else if (_found[0].Present || _found[1].Present)
            {
                hand.Clear();
            }

            _handSmoothers[side].Smooth(hand.Landmarks, hand.Present, dt);
        }
    }

    private void FindPerson(BgraPicture picture, CropFrame letterbox)
    {
        PictureTensor.Fill(picture, letterbox, 224, TensorLayout.Nchw, 0.5f, 2, _personInput);
        using var input = OrtValue.CreateTensorValueFromMemory(_personInput, [1, 3, 224, 224]);
        using var output = _person.Run(_run, _person.InputNames, [input], _person.OutputNames);
        // The converted file lists its scores before its boxes.
        var (scores, boxes) = output[0].GetTensorDataAsSpan<float>().Length == AnchorGrid.Person.Count
            ? (output[0], output[1])
            : (output[1], output[0]);
        _people.Decode(boxes.GetTensorDataAsSpan<float>(), scores.GetTensorDataAsSpan<float>(), PersonThreshold, Overlap);
        if (_people.Count > 0)
        {
            _bodyWindow = CropPlacement.Body(_people.Detections[0], letterbox);
        }
    }

    private void FollowBody(BgraPicture picture, CropFrame window, bool mirror, SceneSense scene, float dt)
    {
        PictureTensor.Fill(picture, window, SceneDecoding.PoseSide, TensorLayout.Nhwc, 0, 1, _poseInput);
        using var input = OrtValue.CreateTensorValueFromMemory(_poseInput, [1, SceneDecoding.PoseSide, SceneDecoding.PoseSide, 3]);
        using var output = _pose.Run(_run, _pose.InputNames, [input], _pose.OutputNames);
        var confidence = output[1].GetTensorDataAsSpan<float>()[0];
        if (confidence < BodyKeep)
        {
            _bodyWindow = null;
            scene.Body.Clear();
            _bodySmoother.Smooth(scene.Body.Landmarks, false, dt);
            return;
        }

        SceneDecoding.Body(output[0].GetTensorDataAsSpan<float>(), confidence, window, picture.Width, picture.Height, mirror, scene.Body);
        _bodySmoother.Smooth(scene.Body.Landmarks, true, dt);
        var (hipX, hipY) = Unmirror(Mid(scene.Body.Landmarks[BodySense.LeftHip], scene.Body.Landmarks[BodySense.RightHip]), picture, mirror);
        var (shoulderX, shoulderY) = Unmirror(scene.Body.Shoulders, picture, mirror);
        var reach = MathF.Sqrt(((shoulderX - hipX) * (shoulderX - hipX)) + ((shoulderY - hipY) * (shoulderY - hipY)));
        _bodyWindow = CropFrame.Upright(hipX, hipY, Math.Max(1, 2.6f * reach), hipX, hipY, shoulderX, shoulderY);
    }

    /// <summary>The window for the next picture around a hand just read: its points' box, upright along wrist to middle finger, a little larger.</summary>
    private static CropFrame FollowWindow(HandSense hand, int width, int height, bool mirror)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var point in hand.Landmarks)
        {
            var x = (mirror ? 1 - point.X : point.X) * width;
            var y = point.Y * height;
            (minX, minY, maxX, maxY) = (Math.Min(minX, x), Math.Min(minY, y), Math.Max(maxX, x), Math.Max(maxY, y));
        }

        var wrist = hand.Landmarks[HandSense.Wrist];
        var middle = hand.Landmarks[HandSense.MiddleBase];
        var (wx, mx) = mirror ? (1 - wrist.X, 1 - middle.X) : (wrist.X, middle.X);
        return CropFrame.Upright((minX + maxX) / 2, (minY + maxY) / 2, Math.Max(maxX - minX, maxY - minY) * 1.9f,
            wx * width, wrist.Y * height, mx * width, middle.Y * height);
    }

    private static bool Near(CropFrame a, CropFrame b)
    {
        var distance = MathF.Sqrt(((a.CentreX - b.CentreX) * (a.CentreX - b.CentreX)) + ((a.CentreY - b.CentreY) * (a.CentreY - b.CentreY)));
        return distance < Math.Max(a.Side, b.Side) / 2;
    }

    private static (float X, float Y) Mid(Landmark a, Landmark b) => ((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static (float X, float Y) Unmirror((float X, float Y) share, BgraPicture picture, bool mirror) =>
        ((mirror ? 1 - share.X : share.X) * picture.Width, share.Y * picture.Height);

    public void Dispose()
    {
        _matte.Dispose();
        _palm.Dispose();
        _hand.Dispose();
        _person.Dispose();
        _pose.Dispose();
        _run.Dispose();
    }
}
