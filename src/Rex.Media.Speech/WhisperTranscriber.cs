using System.Text;
using Rex.Media.AppCore.Captions;
using Rex.Media.Codecs;
using Whisper.net;

namespace Rex.Media.Speech;

/// <summary>
/// Hears a file's speech with an open Whisper model through Whisper.net (SUB-19, ADR-0020), on
/// this computer, offline. The model gives tokens with times; tokens are joined into words (a token
/// beginning with a space begins a word), each word taking its first token's start, its last
/// token's end, and the least sure of its tokens' probabilities.
/// </summary>
public sealed class WhisperTranscriber : ISpeechTranscriber
{
    private readonly string _modelPath;
    private readonly DecoderRegistry _decoders;

    /// <summary>A transcriber on the model file at <paramref name="modelPath"/> (named <paramref name="modelName"/>), decoding sound with <paramref name="decoders"/>.</summary>
    public WhisperTranscriber(string modelPath, string modelName, DecoderRegistry decoders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentNullException.ThrowIfNull(decoders);
        (_modelPath, ModelName, _decoders) = (modelPath, modelName, decoders);
    }

    public string ModelName { get; }

    public async Task<Transcript> TranscribeAsync(string path, string? language, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        var (mono, rate) = await Task.Run(() => SpeechAudio.DecodeMono(path, _decoders, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (rate == 0 || mono.Length == 0)
        {
            return new Transcript(language ?? "", ModelName, []);
        }

        var samples = SpeechAudio.Resample(mono, rate);
        progress?.Report(0.05);
        return await HearAsync(samples, language, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The words in <paramref name="samples"/> (16 kHz mono), for a test with sound already in hand.
    /// Asynchronous end to end: the processor is released with DisposeAsync, which waits for the
    /// model to stop when hearing is cancelled part-way, so no thread is ever blocked on it.
    /// </summary>
    public async Task<Transcript> HearAsync(float[] samples, string? language, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length == 0)
        {
            // Given no samples, the native model skips its spectrogram and then reads it, which
            // ends the process: there is nothing to hear, so nothing is asked.
            return new Transcript(language ?? "", ModelName, []);
        }

        using var factory = WhisperFactory.FromPath(_modelPath);
        var builder = factory.CreateBuilder()
            .WithThreads(Math.Max(1, Environment.ProcessorCount - 1))
            .WithTokenTimestamps()
            .WithProgressHandler(percent => progress?.Report(0.05 + (0.94 * Math.Clamp(percent, 0, 100) / 100)));
        builder = string.IsNullOrEmpty(language) ? builder.WithLanguageDetection() : builder.WithLanguage(language);
        await using var processor = builder.Build();
        var words = new List<TranscriptWord>();
        var heard = language ?? "";
        await foreach (var segment in processor.ProcessAsync(samples, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            heard = segment.Language is { Length: > 0 } spoken ? spoken : heard;
            words.AddRange(Words(segment));
        }

        progress?.Report(1);
        return new Transcript(heard, ModelName, words);
    }

    /// <summary>The words of a segment from its tokens; without tokens, the segment's text spread evenly over its time.</summary>
    internal static IEnumerable<TranscriptWord> Words(SegmentData segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var tokens = segment.Tokens?.Where(token => token.Text is { Length: > 0 } text && !text.StartsWith("[_", StringComparison.Ordinal) && !text.StartsWith("<|", StringComparison.Ordinal)).ToList() ?? [];
        if (tokens.Count == 0)
        {
            var plain = segment.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var span = (segment.End - segment.Start) / Math.Max(1, plain.Length);
            return plain.Select((word, i) => new TranscriptWord(word, segment.Start + (span * i), segment.Start + (span * (i + 1)), 0.5f));
        }

        var words = new List<TranscriptWord>();
        var text = new StringBuilder();
        TimeSpan start = TimeSpan.Zero, end = TimeSpan.Zero;
        var sure = 1f;
        void End()
        {
            if (text.Length > 0 && text.ToString().Trim().Length > 0)
            {
                words.Add(new TranscriptWord(text.ToString().Trim(), start, end < start ? start : end, sure));
            }

            text.Clear();
            sure = 1f;
        }

        foreach (var token in tokens)
        {
            var piece = token.Text!;
            if (piece.StartsWith(' ') && text.Length > 0)
            {
                End();
            }

            if (text.Length == 0)
            {
                start = TimeSpan.FromMilliseconds(token.Start * 10);
            }

            end = TimeSpan.FromMilliseconds(token.End * 10);
            sure = Math.Min(sure, token.Probability);
            text.Append(piece);
        }

        End();
        return words;
    }
}
