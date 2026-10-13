using Rex.Media.AppCore.Visuals;
using Rex.Media.Settings;

namespace Rex.Media.Tests.AppCore;

// TEMPORARY probe; removed before commit.
public sealed class ZzProbe
{
    [Fact]
    public void Murmuration()
    {
        var stage = new VisualStage(96, 54, new Random(42));
        var music = new SyntheticMusic(120, 3);
        var lines = new List<string>();
        byte[]? last = null;
        for (var frame = 1; frame <= music.Frames; frame++)
        {
            var (left, right) = music.At(frame);
            stage.Context.Seconds = (double)frame / SyntheticMusic.Fps;
            stage.Draw(VisualizerChoice.Murmuration, new Dictionary<string, string>(), left, right, SyntheticMusic.Rate, SyntheticMusic.FrameTime);
            long change = 0;
            if (last is not null)
            {
                for (var i = 0; i < last.Length; i++)
                {
                    change += Math.Abs(stage.Pixels[i] - last[i]);
                }
            }

            last = [.. stage.Pixels];
            if (frame % 10 == 0)
            {
                var scene = typeof(VisualStage).GetField("_scene", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(stage)!;
                var birds = ((ValueTuple<double, double, double, double>[])scene.GetType().GetField("_birds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(scene)!);
                var (minX, maxX) = (birds.Min(b => b.Item1), birds.Max(b => b.Item1));
                var speed = birds.Average(b => Math.Sqrt((b.Item3 * b.Item3) + (b.Item4 * b.Item4)));
                lines.Add($"{frame}: y {birds.Min(b => b.Item2):0}..{birds.Max(b => b.Item2):0} x {minX:0}..{maxX:0} speed {speed:0.0} change {change / (last.Length * 255.0):0.0000} maxpix {stage.Pixels.Max()} loud {stage.Pulse.Loudness:0.00} punch {stage.Pulse.Punch:0.00}");
            }
        }

        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "murmuration-probe.txt"), lines);
    }
}
