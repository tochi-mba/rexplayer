using System.Runtime.InteropServices;
using Rex.Media.AppCore.Player;
using Windows.ApplicationModel.DataTransfer;

namespace Rex.Media.App;

/// <summary>Plays what the clipboard holds: files copied in Explorer, or a path copied as text.</summary>
public sealed partial class MainWindow
{
    /// <summary>How many times the clipboard is read before it is taken to hold nothing.</summary>
    private const int ClipboardReads = 5;

    /// <summary>The pause between two reads of a clipboard another program holds open.</summary>
    private static readonly TimeSpan ClipboardRetry = TimeSpan.FromMilliseconds(100);

    /// <summary>Plays files copied in Explorer, or a path copied as text, and says so when there is neither.</summary>
    private async Task PasteAsync()
    {
        var content = await ReadClipboardAsync();
        IReadOnlyList<string> supported;
        int skipped;
        if (content.Contains(StandardDataFormats.StorageItems))
        {
            (supported, skipped) = MediaFiles.FilterClipboardPaths(PathsOf(await content.GetStorageItemsAsync()), Directory.Exists);
        }
        else if (content.Contains(StandardDataFormats.Text))
        {
            (supported, skipped) = MediaFiles.FilterClipboardText(await content.GetTextAsync(), Directory.Exists);
        }
        else
        {
            (supported, skipped) = ([], 0);
        }

        if (supported.Count == 0)
        {
            Say("The clipboard has no media rexplayer can open.");
            return;
        }

        if (skipped > 0)
        {
            Say($"Skipped {skipped} clipboard item{(skipped == 1 ? "" : "s")} that rexplayer cannot open.");
        }

        _player.Open(supported);
    }

    /// <summary>
    /// The clipboard's content. Just after something is copied, another program (clipboard history, a
    /// remote desktop) may hold the clipboard open for a moment; read then, it fails or looks empty,
    /// and a paste did nothing at all. A few reads a tenth of a second apart outlast that. This runs
    /// only when the person pastes.
    /// </summary>
    private static async Task<DataPackageView> ReadClipboardAsync()
    {
        for (var read = 1; ; read++)
        {
            try
            {
                var content = Clipboard.GetContent();
                if (content.AvailableFormats.Count > 0 || read == ClipboardReads)
                {
                    return content;
                }
            }
            catch (COMException) when (read < ClipboardReads)
            {
                // Held open by another program; read again below.
            }

            await Task.Delay(ClipboardRetry);
        }
    }
}
