using System.Globalization;
using Rex.Media.AppCore;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Commands;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.Primitives;
using Rex.Media.Speech;
using Windows.Storage.Pickers;

namespace Rex.Media.App;

/// <summary>
/// Hearing the speech of what plays (SUB-19): the model the settings name is downloaded on the
/// user's say-so, the file is heard in the background with progress over the picture, and the
/// transcript joins the subtitle tracks as "Transcript", is kept for next time, and can be saved
/// as a subtitle file. A transcript kept before is picked up as soon as the file opens.
/// </summary>
public sealed partial class MainWindow
{
    private readonly TranscriptCache _transcripts = new(TranscriptCache.DefaultRoot(App.DataRoot));
    private readonly SpeechModelStore _speechModels = new(SpeechModelStore.DefaultRoot(App.DataRoot));
    private CancellationTokenSource? _transcribing;
    private Transcript? _heard;

    /// <summary>The window's transcript commands.</summary>
    private void RunTranscriptCommand(string command)
    {
        if (command == CommandCatalog.Transcribe)
        {
            _ = TranscribeAsync();
        }
        else
        {
            _ = SaveTranscriptAsync();
        }
    }

    /// <summary>A transcript kept for the item playing, if there is one, as its track.</summary>
    private void LoadKeptTranscript(string item)
    {
        var model = SpeechModels.For(_settings.KineticCaptions.TranscriptModel);
        if (_transcripts.Load(item, model.Id) is { IsEmpty: false } kept)
        {
            TakeTranscript(kept, model.Id);
        }
    }

    private async Task TranscribeAsync()
    {
        if (_player.Item?.Location is not { } item || !File.Exists(item))
        {
            Say("Open a file to hear its speech first.");
            return;
        }

        if (_transcribing is not null)
        {
            Say("Still hearing the speech; a moment.");
            return;
        }

        var model = SpeechModels.For(_settings.KineticCaptions.TranscriptModel);
        var language = _settings.KineticCaptions.TranscriptLanguage is { Length: > 0 } code ? code : null;
        _transcribing = new CancellationTokenSource();
        var token = _transcribing.Token;
        try
        {
            if (!_speechModels.IsInstalled(model))
            {
                Say($"Downloading the {model.Name} speech model ({model.Size}) once, to {_speechModels.Root}.");
                await _speechModels.DownloadAsync(model, Progress("Downloading " + model.Name), token);
            }

            Say($"Hearing the speech with {model.Name}...");
            var transcriber = new WhisperTranscriber(_speechModels.PathOf(model), model.Id, MediaRegistries.Decoders(new MfDecoderFactory(), new WicDecoderFactory()));
            var transcript = await transcriber.TranscribeAsync(item, language, Progress("Hearing"), token);
            _transcripts.Save(item, transcript);
            if (_player.Item?.Location == item)
            {
                TakeTranscript(transcript, model.Id);
                Say(transcript.IsEmpty ? "No speech was heard." : string.Create(CultureInfo.InvariantCulture, $"Heard {transcript.Words.Count} words; the transcript is a subtitle track now."));
            }
        }
        catch (OperationCanceledException)
        {
            // Closed, or another file: nothing to say.
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidDataException or UnauthorizedAccessException or MediaFormatException or NotSupportedException)
        {
            App.Log.Warning(LogSource, "The speech could not be heard: " + ex.Message);
            Say("The speech could not be heard: " + ex.Message);
        }
        finally
        {
            _transcribing?.Dispose();
            _transcribing = null;
        }
    }

    /// <summary>Progress said over the picture at every tenth, on the window's thread.</summary>
    private IProgress<double> Progress(string what)
    {
        var lastTenth = -1;
        return new Progress<double>(share =>
        {
            var tenth = (int)(share * 10);
            if (tenth != lastTenth && share < 1)
            {
                lastTenth = tenth;
                Say(string.Create(CultureInfo.InvariantCulture, $"{what}: {share:P0}"));
            }
        });
    }

    /// <summary>The transcript becomes the item's words and a subtitle track, shown when no subtitles were.</summary>
    private void TakeTranscript(Transcript transcript, string modelId)
    {
        _heard = transcript;
        _transcript = transcript.ToTrack("Transcript (" + modelId + ")");
        _player.AddSubtitles(_transcript, show: _player.Subtitles is null);
        _toldNoWords = false;
        ApplyKineticCaptions();
    }

    private async Task SaveTranscriptAsync()
    {
        if (_heard is not { IsEmpty: false } transcript)
        {
            Say("There is no transcript to save yet; transcribe the speech first (Subtitles menu).");
            return;
        }

        var picker = Prepared(new FileSavePicker { SuggestedStartLocation = PickerLocationId.VideosLibrary, SuggestedFileName = Path.GetFileNameWithoutExtension(_player.Item?.Location ?? "transcript") });
        picker.FileTypeChoices.Add("SubRip subtitles", [".srt"]);
        picker.FileTypeChoices.Add("WebVTT subtitles, every word timed", [".vtt"]);
        picker.FileTypeChoices.Add("Transcript as JSON", [".json"]);
        if (await picker.PickSaveFileAsync() is not { } file)
        {
            return;
        }

        var text = Path.GetExtension(file.Path).ToLowerInvariant() switch
        {
            ".vtt" => transcript.ToWebVtt(),
            ".json" => transcript.ToJson(),
            _ => transcript.ToSrt(),
        };
        await File.WriteAllTextAsync(file.Path, text);
        Say("Saved the transcript to " + file.Path);
    }

    private void StopTranscribing()
    {
        _transcribing?.Cancel();
    }
}
