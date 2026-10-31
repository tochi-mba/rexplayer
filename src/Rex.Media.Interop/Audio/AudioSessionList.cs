using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.Audio.Endpoints;
using Windows.Win32.System.Com;

namespace Rex.Media.Interop.Audio;

/// <summary>One app's sound on the default output: its process, whether it is playing now, and how loud (0 to 1).</summary>
public readonly record struct AudioSessionInfo(uint ProcessId, bool Active, float Peak);

/// <summary>
/// The apps making sound on the media default output, read from Windows' audio sessions, for the
/// visualisation's "Listen to" choice. Plumbing only (ADR-008): which to offer and in what order is
/// decided by <c>ListenChooser</c>.
/// </summary>
[SupportedOSPlatform("windows6.1")]
public static unsafe class AudioSessionList
{
    /// <summary>Every session with a process (the system's own sounds have none), or nothing when there is no output.</summary>
    public static IReadOnlyList<AudioSessionInfo> Read()
    {
        var found = new List<AudioSessionInfo>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice? device = null;
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device);
            var id = typeof(IAudioSessionManager2).GUID;
            device.Activate(&id, CLSCTX.CLSCTX_ALL, null, out var activated);
            manager = (IAudioSessionManager2)activated;
            sessions = manager.GetSessionEnumerator();
            sessions.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                sessions.GetSession(i, out var control);
                try
                {
                    if (control is not IAudioSessionControl2 detail)
                    {
                        continue;
                    }

                    detail.GetProcessId(out var process);
                    AudioSessionState state;
                    detail.GetState(&state);
                    var peak = 0f;
                    if (control is IAudioMeterInformation meter)
                    {
                        meter.GetPeakValue(out peak);
                    }

                    if (process != 0)
                    {
                        found.Add(new AudioSessionInfo(process, state == AudioSessionState.AudioSessionStateActive, peak));
                    }
                }
                catch (COMException)
                {
                    // A session can end while it is read; it is simply left out.
                }
                finally
                {
                    Marshal.ReleaseComObject(control);
                }
            }
        }
        catch (COMException)
        {
            // No output device, or the audio service is restarting: no apps to offer just now.
        }
        finally
        {
            foreach (var com in new object?[] { sessions, manager, device, enumerator })
            {
                if (com is not null)
                {
                    Marshal.ReleaseComObject(com);
                }
            }
        }

        return found;
    }
}
