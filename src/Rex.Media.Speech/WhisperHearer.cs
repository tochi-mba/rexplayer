using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Transcription;
using Whisper.net;

namespace Rex.Media.Speech;

/// <summary>
/// A Whisper model loaded once (SUB-21, ADR-0020) and asked to hear one piece of sound after
/// another; the worker keeps one hearer, and so one loaded model, while it has work. Each piece is
/// heard on its own, with no text carried over from the last, so pieces heard out of order (from
/// the playhead, then the start) agree. Hearing is asynchronous end to end: the processor is
/// enumerated with <c>await foreach</c> and released with DisposeAsync, which waits for the native
/// model to stop when a piece is cancelled part-way. A processor that was cancelled, or that failed,
/// is released and built afresh for the next piece; one that heard cleanly is kept and built again
/// only when the threads or the language change. Every decision is the worker's.
/// </summary>
public sealed class WhisperHearer : ISpeechHearer
{
    private readonly WhisperFactory _factory;
    private WhisperProcessor? _processor;
    private (int Threads, string? Language) _built;

    /// <summary>Loads the model file at <paramref name="modelPath"/>.</summary>
    public WhisperHearer(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        _factory = WhisperFactory.FromPath(modelPath);
    }

    public async ValueTask<(IReadOnlyList<TranscriptWord> Words, string? Language)> HearAsync(float[] samples, string? language, int threads, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length == 0)
        {
            // Given no samples, the native model skips its spectrogram and then reads it, which
            // ends the process: there is nothing to hear, so nothing is asked.
            return ([], language);
        }

        if (_processor is null || _built != (threads, language))
        {
            await ReleaseProcessorAsync().ConfigureAwait(false);
            var builder = _factory.CreateBuilder().WithThreads(Math.Max(1, threads)).WithTokenTimestamps().WithNoContext();
            builder = string.IsNullOrEmpty(language) ? builder.WithLanguageDetection() : builder.WithLanguage(language);
            _processor = builder.Build();
            _built = (threads, language);
        }

        var words = new List<TranscriptWord>();
        string? heard = null;
        try
        {
            await foreach (var segment in _processor.ProcessAsync(samples, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                heard = segment.Language is { Length: > 0 } spoken ? spoken : heard;
                words.AddRange(WhisperTranscriber.Words(segment));
            }
        }
        catch
        {
            // Stopped part-way: let the processor finish stopping, and start the next piece afresh.
            await ReleaseProcessorAsync().ConfigureAwait(false);
            throw;
        }

        return (words, heard ?? language);
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseProcessorAsync().ConfigureAwait(false);
        _factory.Dispose();
    }

    private async ValueTask ReleaseProcessorAsync()
    {
        if (_processor is { } processor)
        {
            _processor = null;
            await processor.DisposeAsync().ConfigureAwait(false);
        }
    }
}
