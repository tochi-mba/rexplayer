using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Rex.Media.Interop.Mac.Audio.AudioToolbox;

namespace Rex.Media.Interop.Mac.Audio;

/// <summary>
/// One of AudioToolbox's decoders (AAC or Opus), as an AudioConverter from compressed packets to
/// interleaved 32-bit floats (AudioToolbox/AudioConverter.h, CoreAudioBaseTypes.h). Humble
/// (ADR-0008): it hands one packet over per call and returns what came out; which format, how it is
/// configured, times and what to do with the frames are the caller's.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed unsafe class CompressedAudioConverter : IDisposable
{
    /// <summary>kAudioFormatMPEG4AAC, 'aac '.</summary>
    public const uint FormatAac = 0x61616320;

    /// <summary>kAudioFormatOpus, 'opus'.</summary>
    public const uint FormatOpus = 0x6F707573;

    private const uint PropertyMagicCookie = 0x646D6763; // 'dmgc'

    /// <summary>What the input call-back says when it has no packet left for this call: not an error, just "no more now".</summary>
    private const int NoMoreInput = 0x6E6F7065; // 'nope'

    private readonly int _channels;
    private GCHandle _self;
    private nint _converter;
    private AudioStreamPacketDescription* _description;
    private byte* _packet;
    private int _packetLength;
    private bool _packetGiven;
    private uint _packetFrames;

    private CompressedAudioConverter(int channels) => _channels = channels;

    /// <summary>Output channels, as the caller asked for them.</summary>
    public int Channels => _channels;

    /// <summary>
    /// A decoder for <paramref name="format"/> (<see cref="FormatAac"/> or <see cref="FormatOpus"/>)
    /// coded at <paramref name="codedRate"/> with <paramref name="framesPerPacket"/> samples a packet
    /// (zero where packets vary), giving <paramref name="outputRate"/> and <paramref name="channels"/>,
    /// configured by <paramref name="cookie"/> (AAC's ES_Descriptor; Opus's identification header).
    /// </summary>
    public static CompressedAudioConverter Open(uint format, int codedRate, int outputRate, int channels, int framesPerPacket, ReadOnlySpan<byte> cookie)
    {
        var converter = new CompressedAudioConverter(channels);
        try
        {
            var source = new AudioStreamBasicDescription { SampleRate = codedRate, FormatId = format, FramesPerPacket = (uint)framesPerPacket, ChannelsPerFrame = (uint)channels };
            var target = new AudioStreamBasicDescription
            {
                SampleRate = outputRate,
                FormatId = FormatLinearPcm,
                FormatFlags = FormatFlagIsFloat | FormatFlagIsPacked,
                FramesPerPacket = 1,
                ChannelsPerFrame = (uint)channels,
                BitsPerChannel = 32,
                BytesPerFrame = (uint)(4 * channels),
                BytesPerPacket = (uint)(4 * channels),
            };
            Check(AudioConverterNew(&source, &target, out converter._converter), "Opening the AudioToolbox decoder");
            fixed (byte* data = cookie)
            {
                Check(AudioConverterSetProperty(converter._converter, PropertyMagicCookie, (uint)cookie.Length, data), "Configuring the AudioToolbox decoder");
            }

            converter._description = (AudioStreamPacketDescription*)NativeMemory.AllocZeroed((nuint)sizeof(AudioStreamPacketDescription));
            converter._self = GCHandle.Alloc(converter);
            return converter;
        }
        catch
        {
            converter.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Decodes one packet into <paramref name="interleaved"/>, which must hold a packet's samples
    /// for every channel; returns how many frames came out. <paramref name="framesInPacket"/> is the
    /// packet's own sample count for a format whose packets vary (Opus), zero otherwise.
    /// </summary>
    public int Decode(ReadOnlySpan<byte> packet, Span<float> interleaved, int framesInPacket = 0)
    {
        fixed (byte* data = packet)
        fixed (float* output = interleaved)
        {
            _packet = data;
            _packetLength = packet.Length;
            _packetGiven = false;
            _packetFrames = (uint)Math.Max(framesInPacket, 0);
            var buffers = new AudioBufferList
            {
                NumberBuffers = 1,
                First = new AudioBuffer { NumberChannels = (uint)_channels, DataByteSize = (uint)(interleaved.Length * sizeof(float)), Data = output },
            };
            var frames = (uint)(interleaved.Length / _channels);
            var status = AudioConverterFillComplexBuffer(_converter, &Supply, GCHandle.ToIntPtr(_self), ref frames, &buffers, null);
            _packet = null;
            if (status != 0 && status != NoMoreInput)
            {
                throw new IOException($"Decoding a packet failed with OSStatus {status}.");
            }

            return (int)frames;
        }
    }

    /// <summary>Forgets what the decoder holds, for a seek.</summary>
    public void Reset() => Check(AudioConverterReset(_converter), "Resetting the AudioToolbox decoder");

    public void Dispose()
    {
        if (_converter != 0)
        {
            _ = AudioConverterDispose(_converter);
            _converter = 0;
        }

        if (_description is not null)
        {
            NativeMemory.Free(_description);
            _description = null;
        }

        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    /// <summary>The converter asking for input: the one packet of this call, once, then "no more now".</summary>
    [UnmanagedCallersOnly]
    private static int Supply(nint converterReference, uint* packets, AudioBufferList* data, AudioStreamPacketDescription** description, nint reference)
    {
        var converter = (CompressedAudioConverter)GCHandle.FromIntPtr(reference).Target!;
        if (converter._packetGiven || converter._packet is null)
        {
            *packets = 0;
            return NoMoreInput;
        }

        converter._packetGiven = true;
        *packets = 1;
        data->NumberBuffers = 1;
        data->First.NumberChannels = (uint)converter._channels;
        data->First.DataByteSize = (uint)converter._packetLength;
        data->First.Data = converter._packet;
        *converter._description = new AudioStreamPacketDescription { StartOffset = 0, VariableFramesInPacket = converter._packetFrames, DataByteSize = (uint)converter._packetLength };
        if (description is not null)
        {
            *description = converter._description;
        }

        return 0;
    }
}
