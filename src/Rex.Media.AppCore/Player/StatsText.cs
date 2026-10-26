using System.Globalization;
using Rex.Media.Codecs;
using Rex.Media.Engine;
using Rex.Media.Primitives;

namespace Rex.Media.AppCore.Player;

/// <summary>
/// The statistics overlay (VID-31): which decoder is doing the work and where, the picture's size,
/// pictures shown and dropped, and how fast the file is being read. The engine's counters are
/// cumulative, so the read rate is worked out from two snapshots taken a known time apart.
/// </summary>
public static class StatsText
{
    public static string Describe(SessionStats stats, MediaInfo? info, int? audioTrack, SessionStats? earlier = null, TimeSpan interval = default, string osSoftwareName = "Windows, in software")
    {
        ArgumentNullException.ThrowIfNull(stats);
        var lines = new List<string>();
        if (info?.FirstTrack(MediaKind.Video) is { Video: { } video } videoTrack)
        {
            var where = stats.VideoDecoderSource switch
            {
                DecoderSource.OsHardware => "graphics card",
                DecoderSource.OsSoftware => osSoftwareName,
                DecoderSource.Own => "rexplayer",
                _ => "not decoding",
            };
            lines.Add(Invariant($"Video     {videoTrack.Codec.DisplayName()} {video.Width}\u00D7{video.Height}{Rate(video.FrameRate)}"));
            if (videoTrack.BitRate is { } videoBits) lines[^1] += Invariant($", {videoBits / 1000.0:0} kb/s");
            lines.Add($"Decoder   {stats.VideoDecoder ?? "none"} ({where})");
            lines.Add(Invariant($"Pictures  {stats.VideoFramesPresented} shown, {stats.VideoFramesDropped} dropped"));
            if (stats.Cadence.Pictures > 0)
            {
                lines.Add(Invariant($"Late      {stats.Cadence.LatenessMeanMs:0.0} ms mean, {stats.Cadence.LatenessWorstMs:0.0} ms worst"));
            }

            var color = video.Color.Resolve(video.Width, video.Height);
            lines.Add($"Colour    {color.Matrix}, {color.Transfer}, {color.Primaries}, " +
                (color.FullRange ? "full range" : "limited range"));
        }

        if (info?.Tracks.FirstOrDefault(track => track.Id == audioTrack) is { Audio: { } audio } audioInfo)
        {
            lines.Add(Invariant($"Audio     {audioInfo.Codec.DisplayName()} {audio.SampleRate} Hz, {audio.Channels} ch"));
            if (audioInfo.BitRate is { } audioBits) lines[^1] += Invariant($", {audioBits / 1000.0:0} kb/s");
            lines.Add($"Decoder   {stats.AudioDecoder ?? "none"}");
        }
        else if (audioTrack is not null)
        {
            lines.Add("Audio     none (silence)");
        }

        var read = Invariant($"Read      {stats.BytesRead / (1024.0 * 1024):0.0} MB");
        if (earlier is not null && interval > TimeSpan.Zero)
        {
            var bits = (stats.BytesRead - earlier.BytesRead) * 8 / interval.TotalSeconds;
            read += Invariant($", {bits / 1000:0} kb/s");
        }

        lines.Add(read);
        lines.Add(Invariant($"Buffers   audio {stats.AudioPacketsBuffered}, video {stats.VideoPacketsBuffered} packets"));
        if (stats.AudioVideoOffsetMilliseconds is { } offset)
        {
            lines.Add(Invariant($"A/V       {offset:+0.0;-0.0;0.0} ms (picture minus sound)"));
        }
        if (stats.CorruptPackets > 0)
        {
            lines.Add(Invariant($"Damaged   {stats.CorruptPackets} packet(s) skipped"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Rate(Rational? frameRate) =>
        frameRate is { Numerator: > 0 } rate ? Invariant($", {rate.Value:0.###} fps") : "";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
