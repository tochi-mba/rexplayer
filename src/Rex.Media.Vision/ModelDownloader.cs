using System.Net;
using System.Net.Http.Headers;

namespace Rex.Media.Vision;

/// <summary>
/// Downloads a catalogue model into the <see cref="ModelStore"/>, driving its
/// <see cref="InstallStateMachine"/>. Before anything is fetched the licence is checked against the
/// permissive list and the disk is checked for room. Each file is written beside its final name with
/// a <c>.part</c> suffix, resumed with a byte range after a dropped connection, and moved into place
/// only once its SHA-256 equals the catalogue's. Nothing is ever loaded from a <c>.part</c> file.
/// </summary>
public sealed class ModelDownloader
{
    /// <summary>Room kept free beyond the download itself, so a model never fills the disk.</summary>
    public const long SpareBytes = 64L * 1024 * 1024;

    private const int BufferBytes = 1 << 16;
    private readonly HttpClient _http;
    private readonly ModelStore _store;
    private readonly Func<string, long> _freeBytes;
    private readonly RetryPolicy _retry;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;

    /// <param name="http">The client the files are fetched with.</param>
    /// <param name="store">Where the files go.</param>
    /// <param name="freeBytes">Free bytes on the drive holding a folder (the app passes a DriveInfo lookup).</param>
    /// <param name="retry">When to try again; <see cref="RetryPolicy.Default"/> when null.</param>
    /// <param name="wait">How a pause between attempts is taken; tests pass one that returns at once.</param>
    public ModelDownloader(HttpClient http, ModelStore store, Func<string, long> freeBytes, RetryPolicy? retry = null,
        Func<TimeSpan, CancellationToken, Task>? wait = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _freeBytes = freeBytes ?? throw new ArgumentNullException(nameof(freeBytes));
        _retry = retry ?? RetryPolicy.Default;
        _wait = wait ?? Task.Delay;
    }

    /// <summary>
    /// Installs <paramref name="model"/>. Failures end in <see cref="InstallPhase.Failed"/> with a
    /// reason for the user rather than an exception; cancelling returns the machine to where it was,
    /// keeps the partial file for a later resume, and rethrows the cancellation.
    /// </summary>
    public async Task InstallAsync(VisionModel model, InstallStateMachine state, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(state);
        if (!ModelCatalogue.IsPermissive(model.Licence))
        {
            throw new InvalidOperationException($"{model.Title} carries the {model.Licence} licence, which rexplayer may not download.");
        }

        state.Begin(model.Bytes);
        try
        {
            Directory.CreateDirectory(_store.Folder(model));
            var needed = _store.Remaining(model);
            var free = _freeBytes(_store.Root);
            if (free < needed + SpareBytes)
            {
                state.Fail($"There is not enough free space. The download needs {ModelDownloadText.Size(needed + SpareBytes)} and {ModelDownloadText.Size(Math.Max(0, free))} is free.");
                return;
            }

            var fetched = new List<ModelFile>();
            long before = 0;
            foreach (var file in model.Files)
            {
                if (!_store.HasVerified(model, file))
                {
                    await FetchAsync(model, file, before, state, cancellation).ConfigureAwait(false);
                    fetched.Add(file);
                }

                before += file.Bytes;
                state.Report(before);
            }

            state.Verify();
            foreach (var file in fetched)
            {
                var partial = _store.PartialPathOf(model, file);
                if (ModelStore.HashOf(partial) != file.Sha256)
                {
                    File.Delete(partial);
                    state.Fail($"{file.Name} did not match its published fingerprint, so it was deleted. Try again later.");
                    return;
                }

                File.Move(partial, _store.PathOf(model, file), overwrite: true);
            }

            _store.MarkInstalled(model);
            state.Complete();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            state.Cancel();
            throw;
        }
        catch (Exception error)
        {
            state.Fail(Describe(error));
        }
    }

    /// <summary>A failure in words for the Preferences section.</summary>
    public static string Describe(Exception error) => error switch
    {
        HttpRequestException { StatusCode: { } status } => $"The server answered {(int)status} ({status}). Try again later.",
        HttpRequestException or TaskCanceledException => "The connection failed. Check the network and try again.",
        UnauthorizedAccessException => "rexplayer may not write to its models folder.",
        InvalidDataException => "The server sent more data than the model should have. The download was stopped.",
        _ => $"The download stopped: {error.Message}",
    };

    private async Task FetchAsync(VisionModel model, ModelFile file, long before, InstallStateMachine state, CancellationToken cancellation)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await FetchOnceAsync(model, file, before, state, cancellation).ConfigureAwait(false);
                return;
            }
            catch (Exception error) when (!cancellation.IsCancellationRequested && _retry.ShouldRetry(attempt, error))
            {
                await _wait(_retry.Delay(attempt), cancellation).ConfigureAwait(false);
            }
        }
    }

    private async Task FetchOnceAsync(VisionModel model, ModelFile file, long before, InstallStateMachine state, CancellationToken cancellation)
    {
        var partial = _store.PartialPathOf(model, file);
        var have = _store.PartialBytes(model, file);
        if (have >= file.Bytes)
        {
            if (have == file.Bytes)
            {
                return;
            }

            File.Delete(partial);
            have = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, file.Url);
        if (have > 0)
        {
            request.Headers.Range = new RangeHeaderValue(have, null);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // The partial file no longer fits what the server holds: start that file again.
            File.Delete(partial);
            throw new IOException("The server could not resume the download.");
        }

        response.EnsureSuccessStatusCode();
        var append = have > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append)
        {
            have = 0;
        }

        var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            var target = new FileStream(partial, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, BufferBytes, useAsync: true);
            await using (target.ConfigureAwait(false))
            {
                var buffer = new byte[BufferBytes];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
                {
                    if (have + read > file.Bytes)
                    {
                        throw new InvalidDataException($"{file.Name} is longer than {file.Bytes} bytes.");
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                    have += read;
                    state.Report(before + have);
                }
            }
        }

        if (have != file.Bytes)
        {
            throw new IOException($"The download of {file.Name} ended early.");
        }
    }
}
