namespace Rex.Media.Audio;

/// <summary>Which of Windows' default outputs a notification is about.</summary>
public enum EndpointRole
{
    Console,
    Multimedia,
    Communications,
}

/// <summary>
/// When the sound moves to another output device, decided apart from the device plumbing (ADR-008).
/// rexplayer plays to Windows' default output for media; when that default changes to a different
/// device, or the device in use is lost, the sound moves to the new default, keeping its place.
/// </summary>
public static class DeviceFollowPolicy
{
    /// <summary>The stream was lost because its device went away (unplugged, disabled, or its driver restarted).</summary>
    public const int DeviceInvalidated = unchecked((int)0x88890004);

    /// <summary>The audio service was restarted and every stream has to be made again.</summary>
    public const int ServiceNotRunning = unchecked((int)0x88890010);

    /// <summary>The stream's resources were taken back, as after a device's format changes.</summary>
    public const int ResourcesInvalidated = unchecked((int)0x88890026);

    /// <summary>How long to wait between attempts to open a device while none is there.</summary>
    public static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Whether a default-device notification moves the sound: only for playback (not recording), only
    /// for the default rexplayer plays to (media), and only when it names a device other than the one in use.
    /// </summary>
    public static bool ShouldMove(bool playback, EndpointRole role, string? newDevice, string? currentDevice) =>
        playback && role == EndpointRole.Multimedia && !string.Equals(newDevice, currentDevice, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a failure means the device is gone and the sound should move (rather than a fault to report).</summary>
    public static bool IsDeviceLost(int hresult) => hresult is DeviceInvalidated or ServiceNotRunning or ResourcesInvalidated;

    /// <summary>
    /// The samples heard so far across every device: those carried from devices left behind plus
    /// those heard on the device in use, so the clock never steps back when the sound moves.
    /// </summary>
    public static long Heard(long carried, long onDevice) => Math.Max(0, carried) + Math.Max(0, onDevice);

    /// <summary>Whether enough time has passed since the last failed attempt to try opening a device again.</summary>
    public static bool ShouldRetry(TimeSpan sinceLastAttempt) => sinceLastAttempt >= Retry;
}
