using System.Diagnostics;
using Rex.Media.AppCore.Visuals;
using Rex.Media.Settings;

namespace Rex.Media.Tests.AppCore;

public sealed class ProfileVisualsTemp
{
    [Fact]
    public void Profile()
    {
        if (Environment.GetEnvironmentVariable("REXPLAYER_VISUAL_DUMP") is not { Length: > 0 } folder)
        {
            return;
        }

        var lines = new List<string>();
        void Time(string what, int times, Action action)
        {
            action();
            var clock = Stopwatch.StartNew();
            for (var i = 0; i < times; i++)
            {
                action();
            }

            lines.Add($"{what}: {clock.Elapsed.TotalMilliseconds / times:0.00} ms");
        }

        var canvas = new Raster(480, 270);
        var output = new byte[480 * 270 * 4];
        canvas.Fill(0, 0, 480, 270, new Rgb(0.5f, 0.2f, 0.1f));
        Time("ToBgra plain", 50, () => canvas.ToBgra(output, 1, 0.35f));
        Time("ToBgra bloom", 50, () => { canvas.Bloom(0.4f, 0.8f, 5); canvas.ToBgra(output, 1, 0.35f); });
        Time("Feedback+Settle", 50, () => { canvas.Feedback(0.7f, 1.01, 0.002, 0, 0); canvas.Settle(); });
        Time("Clear", 50, () => canvas.Clear(Rgb.Black));
        Time("Line 96 rays", 20, () =>
        {
            for (var i = 0; i < 96; i++)
            {
                var a = i / 96.0 * Math.Tau;
                canvas.Line(240 + (50 * Math.Cos(a)), 135 + (50 * Math.Sin(a)), 240 + (130 * Math.Cos(a)), 135 + (130 * Math.Sin(a)), 4, Rgb.White, 1, 1.2);
            }
        });
        var pulse = new MusicPulse();
        var music = new SyntheticMusic(120, 4);
        var frame = 1;
        Time("Pulse", 100, () =>
        {
            var (l, r) = music.At(frame++ % 100 + 1);
            pulse.Update(l, 48_000, TimeSpan.FromSeconds(1 / 30.0));
        });
        var context = new VisualContext(canvas, pulse) { Choice = VisualizerChoice.Halo };
        Time("Prepare", 100, context.Prepare);
        Time("Number", 1000, () => context.Number("rays"));
        Time("Toggle", 1000, () => context.Toggle("spin"));
        Time("Paint", 100000, () => context.Paint(0.4));
        foreach (var choice in new[] { VisualizerChoice.Vinyl, VisualizerChoice.Halo, VisualizerChoice.Mirror, VisualizerChoice.Aurora, VisualizerChoice.Embers, VisualizerChoice.Ripples, VisualizerChoice.Strobe, VisualizerChoice.BeatEdit })
        {
            var stage = new VisualStage(480, 270, new Random(1));
            var f = 1;
            var options = new Dictionary<string, string>();
            Time("Stage " + choice, 60, () =>
            {
                var (l, r) = music.At(f++ % 100 + 1);
                stage.Draw(choice, options, l, r, 48_000, TimeSpan.FromSeconds(1 / 30.0));
            });
        }

        File.WriteAllLines(Path.Combine(folder, "profile.txt"), lines);
    }
}
