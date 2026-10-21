using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Rex.Media.Interop.Mac.Audio.AudioToolbox;

namespace Rex.Media.Interop.Mac.Audio;

/// <summary>
/// The Mac's default audio output, as an AudioUnit that pulls interleaved 32-bit floats from a
/// callback on its own real-time thread. The unit converts to whatever the device runs at, so the
/// stream format given is the one the callback fills. Humble (ADR-0008): it sequences calls and
/// reports failures; what to play and how to count it is the caller's.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed unsafe class AudioUnitOutput : IDisposable
{
    /// <summary>Fills an interleaved float buffer; called on the device's real-time thread, so it must not block or allocate.</summary>
    public delegate void Fill(Span<float> interleaved);

    private readonly Fill _fill;
    private GCHandle _self;
    private nint _unit;
    private bool _running;

    private AudioUnitOutput(Fill fill) => _fill = fill;

    /// <summary>
    /// Opens the default output for <paramref name="channels"/> channels at <paramref name="sampleRate"/>;
    /// null when the Mac has no output device (a build machine, say).
    /// </summary>
    public static AudioUnitOutput? OpenDefault(int sampleRate, int channels, Fill fill)
    {
        ArgumentNullException.ThrowIfNull(fill);
        var description = new AudioComponentDescription { ComponentType = TypeOutput, ComponentSubType = SubTypeDefaultOutput, ComponentManufacturer = ManufacturerApple };
        var component = AudioComponentFindNext(0, ref description);
        if (component == 0)
        {
            return null;
        }

        var output = new AudioUnitOutput(fill);
        try
        {
            Check(AudioComponentInstanceNew(component, out output._unit), "Opening the default output");
            var format = new AudioStreamBasicDescription
            {
                SampleRate = sampleRate,
                FormatId = FormatLinearPcm,
                FormatFlags = FormatFlagIsFloat | FormatFlagIsPacked,
                FramesPerPacket = 1,
                ChannelsPerFrame = (uint)channels,
                BitsPerChannel = 32,
                BytesPerFrame = (uint)(4 * channels),
                BytesPerPacket = (uint)(4 * channels),
            };
            Check(AudioUnitSetProperty(output._unit, PropertyStreamFormat, ScopeInput, 0, &format, (uint)sizeof(AudioStreamBasicDescription)), "Setting the output format");
            output._self = GCHandle.Alloc(output);
            var callback = new RenderCallback { Procedure = &Render, ProcedureReference = GCHandle.ToIntPtr(output._self) };
            Check(AudioUnitSetProperty(output._unit, PropertySetRenderCallback, ScopeInput, 0, &callback, (uint)sizeof(RenderCallback)), "Setting the output callback");
            var initialized = AudioUnitInitialize(output._unit);
            if (initialized != 0)
            {
                // No device to initialise against: report it as no output rather than a failure.
                output.Dispose();
                return null;
            }

            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    public void Start()
    {
        if (!_running && _unit != 0)
        {
            Check(AudioOutputUnitStart(_unit), "Starting the output");
            _running = true;
        }
    }

    /// <summary>Stops pulling; when it returns, the callback is not running and will not run until <see cref="Start"/>.</summary>
    public void Stop()
    {
        if (_running && _unit != 0)
        {
            Check(AudioOutputUnitStop(_unit), "Stopping the output");
            _running = false;
        }
    }

    public void Dispose()
    {
        if (_unit != 0)
        {
            _ = AudioOutputUnitStop(_unit);
            _ = AudioUnitUninitialize(_unit);
            _ = AudioComponentInstanceDispose(_unit);
            _unit = 0;
            _running = false;
        }

        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    [UnmanagedCallersOnly]
    private static int Render(nint reference, uint* flags, nint timeStamp, uint bus, uint frames, AudioBufferList* buffers)
    {
        var output = (AudioUnitOutput)GCHandle.FromIntPtr(reference).Target!;
        var buffer = buffers->First;
        output._fill(new Span<float>(buffer.Data, (int)(buffer.DataByteSize / sizeof(float))));
        return 0;
    }
}
