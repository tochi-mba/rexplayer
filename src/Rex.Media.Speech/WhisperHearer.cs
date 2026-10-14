using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Transcription;
using Whisper.net;

namespace Rex.Media.Speech;

/// <summary>
/// A Whisper model loaded once (SUB-21, ADR-0020) and asked to hear one piece of sound after
/// another. Each piece is heard on its own, with no text carried over from the last, so pieces
/// heard out of order (from the playhead, then the start) agree. The processor is built again only
/// when the threads or the language asked for change. Every decision is the worker's.
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

    public (IReadOnlyList<TranscriptWord> Words, string? Language) Hear(float[] samples, string? language, int threads, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (_processor is null || _built != (threads, language))
        {
            _processor?.Dispose();
            var builder = _factory.CreateBuilder().WithThreads(Math.Max(1, threads)).WithTokenTimestamps().WithNoContext();
            builder = string.IsNullOrEmpty(language) ? builder.WithLanguageDetection() : builder.WithLanguage(language);
            _processor = builder.Build();
            _built = (threads, language);
        }

        var words = new List<TranscriptWord>();
        string? heard = null;
        foreach (var segment in _processor.ProcessAsync(samples, cancellationToken).ToBlockingEnumerable(cancellationToken))
        {
            heard = segment.Language is { Length: > 0 } spoken ? spoken : heard;
            words.AddRange(WhisperTranscriber.Words(segment));
        }

        return (words, heard ?? language);
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory.Dispose();
    }
}
