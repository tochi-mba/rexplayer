namespace Rex.Media.AppCore;

/// <summary>Work a launch starts on background threads before its window is built.</summary>
public enum LaunchWork
{
    /// <summary>The graphics device, its shaders and a swap chain for the picture.</summary>
    Pictures,

    /// <summary>Starting Windows' media framework, whose decoders open most video.</summary>
    Decoders,

    /// <summary>Activating the default audio device once, so opening it for the first sound is quick.</summary>
    AudioDevice,

    /// <summary>Reading what the player remembers (resume points, recent media, the queue).</summary>
    Memory,

    /// <summary>Reading the library's store.</summary>
    Library,
}

/// <summary>
/// What a launch starts in the background while the window's XAML loads, most needed first. The
/// window needs the memory and the library before it can finish building, so they always come; a
/// launch that opens media then wants its picture surface, its decoders and the audio device ready
/// the moment the window is, while an empty launch shows the library first and plays nothing yet.
/// The audio device is left alone when there is no real one to warm (the window tests' silent output).
/// </summary>
public static class LaunchPlan
{
    /// <summary>
    /// How long after a launch the library first looks through its folders: past the first picture
    /// and sound, which a scan of thousands of files would otherwise slow down. What the library
    /// already knows shows at once; only finding new and changed files waits.
    /// </summary>
    public static readonly TimeSpan FirstLibraryScan = TimeSpan.FromSeconds(10);

    /// <param name="opensMedia">The launch was given something to open.</param>
    /// <param name="realAudio">There is a real audio device to warm.</param>
    /// <param name="lean">
    /// A weak computer (DeviceBudget.LeanLaunch): with nothing to open, only the two stores the
    /// window needs are read ahead, so they do not share two processor cores with three warm-ups
    /// whose results may not be wanted; the rest is made when first used.
    /// </param>
    public static IReadOnlyList<LaunchWork> Order(bool opensMedia, bool realAudio, bool lean = false)
    {
        IEnumerable<LaunchWork> order = opensMedia
            ? [LaunchWork.Pictures, LaunchWork.Memory, LaunchWork.Decoders, LaunchWork.AudioDevice, LaunchWork.Library]
            : lean ? [LaunchWork.Memory, LaunchWork.Library]
            : [LaunchWork.Memory, LaunchWork.Library, LaunchWork.Pictures, LaunchWork.Decoders, LaunchWork.AudioDevice];
        return [.. order.Where(work => realAudio || work != LaunchWork.AudioDevice)];
    }
}
