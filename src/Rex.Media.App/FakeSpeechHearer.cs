using System.Globalization;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Transcription;

namespace Rex.Media.App;

/// <summary>
/// A stand-in for the speech model when REXPLAYER_FAKE_SPEECH names a number of seconds: each piece
/// takes that long and hears one word at its start. The window tests use it to drive the caption
/// wait, its overlay and its release on machines with no model, the way REXPLAYER_FAKE_AUDIO stands
/// in for a sound card. Nothing is downloaded and no model file is needed while it is set.
/// </summary>
internal sealed class FakeSpeechHearer(TimeSpan perPiece) : ISpeechHearer
{
    /// <summary>How long each piece takes, or null when the real model is used.</summary>
    public static TimeSpan? PerPiece { get; } =
        double.TryParse(Environment.GetEnvironmentVariable("REXPLAYER_FAKE_SPEECH"), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0
            ? TimeSpan.FromSeconds(seconds)
            : null;

    public async ValueTask<(IReadOnlyList<TranscriptWord> Words, string? Language)> HearAsync(float[] samples, string? language, int threads, CancellationToken cancellationToken)
    {
        await Task.Delay(perPiece, cancellationToken).ConfigureAwait(false);
        return ([new TranscriptWord("Asake", TimeSpan.Zero, TimeSpan.FromSeconds(1))], language ?? "en");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
