using System.Globalization;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Commands;
using Windows.Storage.Pickers;

namespace Rex.Media.App;

/// <summary>
/// Hearing the speech of what plays (SUB-19, SUB-21): Transcribe hears it from where it plays,
/// ahead of the playhead, with the model the settings name (downloaded once on the user's say-so);
/// the transcript joins the subtitle tracks as "Transcript", fills in piece by piece, is kept for
/// next time, and can be saved as a subtitle file. The hearing itself is
/// MainWindow.LibraryTranscription's worker.
/// </summary>
public sealed partial class MainWindow
{
    private readonly TranscriptCache _transcripts = new(TranscriptCache.DefaultRoot(App.DataRoot));
    private readonly SpeechModelStore _speechModels = new(SpeechModelStore.DefaultRoot(App.DataRoot));
    private Transcript? _heard;

    /// <summary>The window's transcript commands.</summary>
    private void RunTranscriptCommand(string command)
    {
        if (command == CommandCatalog.Transcribe)
        {
            Forget(TranscribeWhatPlaysAsync());
        }
        else
        {
            Forget(SaveTranscriptAsync());
        }
    }

    /// <summary>Progress said over the picture at every tenth, on the window's thread.</summary>
    private IProgress<double> Progress(string what)
    {
        var lastTenth = -1;
        return new Progress<double>(share =>
        {
            _hearing = share < 1 ? share : null;
            ShowTaskbarProgress();
            var tenth = (int)(share * 10);
            if (tenth != lastTenth && share < 1)
            {
                lastTenth = tenth;
                Say(string.Create(CultureInfo.InvariantCulture, $"{what}: {share:P0}"));
            }
        });
    }

    private async Task SaveTranscriptAsync()
    {
        var item = _player.Item?.Location;
        var transcript = _heard ?? (item is null ? null : _worker?.Snapshot(item));
        if (transcript is not { IsEmpty: false })
        {
            Say("There is no transcript to save yet; transcribe the speech first (Subtitles menu).");
            return;
        }

        var picker = Prepared(new FileSavePicker { SuggestedStartLocation = PickerLocationId.VideosLibrary, SuggestedFileName = Path.GetFileNameWithoutExtension(item ?? "transcript") });
        picker.FileTypeChoices.Add("SubRip subtitles", [".srt"]);
        picker.FileTypeChoices.Add("WebVTT subtitles, every word timed", [".vtt"]);
        picker.FileTypeChoices.Add("Transcript as JSON", [".json"]);
        if (await picker.PickSaveFileAsync() is not { } file)
        {
            return;
        }

        try
        {
            Say("Saving the transcript…");
            // Some transcripts have hundreds of thousands of words. Serialize
            // and write away from the window thread, then atomically publish
            // only the complete file: a full disk must not leave a corrupt SRT.
            await Task.Run(() => TranscriptFileExport.Write(file.Path, transcript));
            if (!_closed && !_closing)
            {
                Say("Saved the transcript to " + file.Path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (!_closed && !_closing)
            {
                Say("The transcript could not be saved: " + ex.Message);
            }
        }
    }
}
