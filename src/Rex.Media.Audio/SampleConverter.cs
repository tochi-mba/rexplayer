using System.Buffers.Binary;
using Rex.Media.Primitives;

namespace Rex.Media.Audio;

/// <summary>
/// The one conversion out of float: planes to interleaved bytes in the sink's storage format, with
/// clamping so a sample above full scale saturates rather than wrapping around.
/// </summary>
public static class SampleConverter
{
    public static void Interleave(AudioFrame frame, SampleFormat storage, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var bytesPerSample = storage switch
        {
            SampleFormat.S16 => 2,
            SampleFormat.S24 => 3,
            SampleFormat.S32 or SampleFormat.F32 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(storage), storage, "Sinks take 16, 24 or 32-bit integers or 32-bit floats."),
        };
        var blockAlign = bytesPerSample * frame.Channels;
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, frame.SampleCount * blockAlign, nameof(destination));
        for (var channel = 0; channel < frame.Channels; channel++)
        {
            var plane = frame.Channel(channel);
            for (var i = 0; i < plane.Length; i++)
            {
                var target = destination.Slice((i * blockAlign) + (channel * bytesPerSample), bytesPerSample);
                Write(plane[i], storage, target);
            }
        }
    }

    public static short ToInt16(float sample) => (short)Math.Clamp(MathF.Round(sample * 32768f), short.MinValue, short.MaxValue);

    public static int ToInt24(float sample) => (int)Math.Clamp(MathF.Round(sample * 8388608f), -8388608f, 8388607f);

    public static int ToInt32(float sample) => (int)Math.Clamp(Math.Round(sample * 2147483648.0), int.MinValue, int.MaxValue);

    private static void Write(float sample, SampleFormat storage, Span<byte> target)
    {
        switch (storage)
        {
            case SampleFormat.S16:
                BinaryPrimitives.WriteInt16LittleEndian(target, ToInt16(sample));
                break;
            case SampleFormat.S24:
                var value = ToInt24(sample);
                target[0] = (byte)value;
                target[1] = (byte)(value >> 8);
                target[2] = (byte)(value >> 16);
                break;
            case SampleFormat.S32:
                BinaryPrimitives.WriteInt32LittleEndian(target, ToInt32(sample));
                break;
            default:
                BinaryPrimitives.WriteSingleLittleEndian(target, sample);
                break;
        }
    }
}
