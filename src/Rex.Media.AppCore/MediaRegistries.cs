using Rex.Media.Codecs;
using Rex.Media.Codecs.Software.Flac;
using Rex.Media.Codecs.Software.Mpeg;
using Rex.Media.Codecs.Software.Pcm;
using Rex.Media.Containers;
using Rex.Media.Containers.Flac;
using Rex.Media.Containers.Matroska;
using Rex.Media.Containers.Mp4;
using Rex.Media.Containers.Mpeg;
using Rex.Media.Containers.Riff;

namespace Rex.Media.AppCore;

/// <summary>
/// The demuxers and decoders rexplayer ships, assembled in one place so the app, the command line
/// and the tests all play media the same way. A host adds the Windows-only decoders (Media
/// Foundation) through the extra decoders argument.
/// </summary>
public static class MediaRegistries
{
    public static DemuxerRegistry Demuxers() => new DemuxerRegistry()
        .Add(new WavDemuxerFactory())
        .Add(new AiffDemuxerFactory())
        .Add(new FlacDemuxerFactory())
        .Add(new MpegAudioDemuxerFactory())
        .Add(new Mp4DemuxerFactory())
        .Add(new MatroskaDemuxerFactory())
        .Add(new Rex.Media.Containers.Ogg.OggDemuxerFactory());

    public static DecoderRegistry Decoders(params IDecoderFactory[] extraDecoders)
    {
        ArgumentNullException.ThrowIfNull(extraDecoders);
        var registry = new DecoderRegistry().Add(new PcmDecoderFactory()).Add(new FlacDecoderFactory()).Add(new Mp3DecoderFactory()).Add(new Rex.Media.Codecs.Software.Vorbis.VorbisDecoderFactory());
        foreach (var factory in extraDecoders)
        {
            registry.Add(factory);
        }

        return registry;
    }
}
