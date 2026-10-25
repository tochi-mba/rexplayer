using System.IO;
using Rex.Media.TestKit;

namespace Rex.Media.App.Tests;

/// <summary>Deterministic audio fixtures shared by the window UI tests.</summary>
public sealed partial class AppWindowTests
{
    private string Music(int seconds, bool audiobook = false)
    {
        const int Rate = 48_000;
        var random = new Random(5);
        var samples = new float[Rate * seconds * 2];
        for (var i = 0; i < Rate * seconds; i++)
        {
            var t = (double)i / Rate;
            var (beat, since) = ((int)(t / 0.5), t % 0.5);
            var kick = (beat % 4 == 0 ? 0.85 : 0.5) * Math.Sin(Math.Tau * (50 + (90 * Math.Exp(-since * 30))) * since) * Math.Exp(-since * 12);
            var snare = beat % 2 == 1 ? 0.25 * ((random.NextDouble() * 2) - 1) * Math.Exp(-since * 25) : 0;
            var pad = 0.06 * (Math.Sin(Math.Tau * 220 * t) + Math.Sin(Math.Tau * 277.2 * t) + Math.Sin(Math.Tau * 329.6 * t));
            samples[i * 2] = samples[(i * 2) + 1] = (float)Math.Clamp(kick + snare + pad, -1, 1);
        }
        var path = Path.Combine(_media, "Terminator.wav");
        var wav = WavBuilder.Pcm(Rate, 2, 16, Pcm.Int16(samples));
        if (audiobook)
        {
            // Caption tests need speech-eligible audio rather than a song. Keep the
            // deterministic sound while marking its WAV metadata as spoken-word.
            wav.Info(("IGNR", "Audiobook"));
        }

        File.WriteAllBytes(path, wav.Build());
        return path;
    }
}
