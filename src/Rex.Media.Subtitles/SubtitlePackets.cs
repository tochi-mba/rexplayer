// Spec: Subtitles carried in containers: Matroska codec mappings (IETF RFC 9559 and the Matroska "Subtitles" codec specifications: S_TEXT/UTF8 blocks hold the cue text; S_TEXT/ASS and S_TEXT/SSA blocks hold "ReadOrder, Layer, Style, Name, MarginL, MarginR, MarginV, Effect, Text" with the script header in CodecPrivate; S_TEXT/WEBVTT blocks hold the cue text) and 3GPP TS 26.245 section 5.17 (a timed-text sample: a 16-bit length, the UTF-8 text, then modifier boxes).
using System.Buffers.Binary;
using System.Text;
using Rex.Media.Primitives;

namespace Rex.Media.Subtitles;

/// <summary>
/// Turns the subtitle packets a demuxer reads into cues, for a track of a known codec. Each packet
/// carries one cue, timed by the packet itself.
/// </summary>
public sealed class SubtitlePackets
{
    private static readonly string[] AssBlockFormat = ["readorder", "layer", "style", "name", "marginl", "marginr", "marginv", "effect", "text"];

    private readonly CodecId _codec;
    private readonly Dictionary<string, AssStyle> _styles;

    public SubtitlePackets(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        _codec = track.Codec;
        _styles = _codec is CodecId.Ass or CodecId.Ssa ? Ass.Styles(Encoding.UTF8.GetString(track.CodecPrivate)) : [];
    }

    /// <summary>Whether rexplayer can read subtitles of this codec from packets.</summary>
    public static bool CanRead(CodecId codec) => codec is CodecId.SubRip or CodecId.Ass or CodecId.Ssa or CodecId.WebVtt or CodecId.MovText or CodecId.PlainText;

    /// <summary>The cue in a packet, or null for an empty one (a sample that clears the screen) or one that cannot be read.</summary>
    public SubtitleCue? Read(Packet packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var start = packet.Timestamp.IsKnown ? packet.Timestamp.ToTimeSpan() : TimeSpan.Zero;
        var end = start + (packet.Duration > MediaTime.Zero ? packet.Duration.ToTimeSpan() : TimeSpan.FromSeconds(3));
        var data = packet.Data.Span;
        switch (_codec)
        {
            case CodecId.MovText:
                if (data.Length < 2 || BinaryPrimitives.ReadUInt16BigEndian(data) is var length && length > data.Length - 2)
                {
                    return null;
                }

                return SubtitleFile.Cue(start, end, Escape(Encoding.UTF8.GetString(data.Slice(2, length))));
            case CodecId.Ass or CodecId.Ssa:
                var fields = Encoding.UTF8.GetString(data).Split(',', AssBlockFormat.Length);
                return fields.Length < AssBlockFormat.Length ? null : Ass.Styled(start, end, fields[^1], _styles.GetValueOrDefault(fields[2].Trim()));
            default:
                return SubtitleFile.Cue(start, end, Encoding.UTF8.GetString(data));
        }
    }

    /// <summary>MP4 timed text is plain, so anything that looks like markup is shown as written.</summary>
    private static string Escape(string text) => text.Replace("<", "\u2039", StringComparison.Ordinal).Replace("{\\", "{", StringComparison.Ordinal);
}
