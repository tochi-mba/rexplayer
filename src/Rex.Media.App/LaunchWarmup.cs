using System.Runtime.InteropServices;
using Rex.Media.AppCore;
using Rex.Media.Interop.Audio;
using Rex.Media.Interop.Graphics;
using Rex.Media.Interop.MediaFoundation;
using Rex.Media.Library;
using Rex.Media.Video.D3D11;

namespace Rex.Media.App;

/// <summary>
/// The slow parts of a launch, started on background threads before the window is built so they
/// run while its XAML loads (in the order <see cref="LaunchPlan"/> gives): the graphics device and
/// its picture surface, Windows' media framework, the audio device, and the two stores. The window
/// takes each result when it needs it, waiting only if it is not ready yet; what fails here is
/// simply made again the ordinary way.
/// </summary>
internal sealed class LaunchWarmup
{
    private Task<(D3D11Presenter Presenter, nint SwapChain)>? _pictures;
    private Task<RexStore>? _memory;
    private Task<MediaLibrary>? _library;

    private LaunchWarmup()
    {
    }

    public static LaunchWarmup Start(bool opensMedia, bool realAudio)
    {
        var warmup = new LaunchWarmup();
        foreach (var work in LaunchPlan.Order(opensMedia, realAudio))
        {
            switch (work)
            {
                case LaunchWork.Pictures when OperatingSystem.IsWindowsVersionAtLeast(10):
                    warmup._pictures = Task.Run(() =>
                    {
                        // The shaders every renderer starts with come side by side, then a surface of
                        // one pixel: the window resizes it once it knows its own size.
                        D3D11VideoRenderer.PrepareShaders();
                        var presenter = D3D11Presenter.ForComposition(1, 1, out var swapChain);
                        return (presenter, swapChain);
                    });
                    break;
                case LaunchWork.Decoders when OperatingSystem.IsWindowsVersionAtLeast(6, 1):
                    Quietly("Media Foundation", () => _ = MfTransform.IsAvailable);
                    break;
                case LaunchWork.AudioDevice when OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000):
                    Quietly("the audio device", () => WasapiRenderClient.OpenDefault(TimeSpan.FromMilliseconds(200))?.Dispose());
                    break;
                case LaunchWork.Memory:
                    warmup._memory = Task.Run(MainWindow.OpenStore);
                    break;
                case LaunchWork.Library:
                    warmup._library = Task.Run(() => new MediaLibrary(MainWindow.OpenLibraryStore()));
                    break;
            }
        }

        return warmup;
    }

    /// <summary>The store of what the player remembers, read in the background; null when it was not started.</summary>
    public RexStore? TakeMemory() => Take(ref _memory);

    /// <summary>The library, read in the background; null when it was not started.</summary>
    public MediaLibrary? TakeLibrary() => Take(ref _library);

    /// <summary>
    /// The presenter made in the background and its swap chain (one reference, for the caller to
    /// release); null when it was not started or could not be made, so the caller makes its own.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0")]
    public (D3D11Presenter Presenter, nint SwapChain)? TakePictures()
    {
        var pictures = Interlocked.Exchange(ref _pictures, null);
        try
        {
            return pictures?.GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or AggregateException)
        {
            App.Log.Warning("app", "The picture surface made at launch failed, so it is made again: " + ex.Message);
            return null;
        }
    }

    private static T? Take<T>(ref Task<T>? task)
        where T : class => Interlocked.Exchange(ref task, null)?.GetAwaiter().GetResult();

    /// <summary>Warms something up off the window's thread; a failure is only noted, since the real use reports it.</summary>
    private static void Quietly(string what, Action warm) => _ = Task.Run(() =>
    {
        try
        {
            warm();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or IOException)
        {
            App.Log.Debug("app", $"Warming up {what} at launch failed: {ex.Message}");
        }
    });
}
