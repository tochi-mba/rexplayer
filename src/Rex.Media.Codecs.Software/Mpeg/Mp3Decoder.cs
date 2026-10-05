// Spec: ISO/IEC 11172-3 clause 2.4.3.4 (Layer III decoding: bit reservoir, scalefactors, requantization, ms and intensity stereo, reordering, alias reduction, hybrid synthesis); ISO/IEC 13818-3 clause 2.4.3.4 (lower sampling frequencies: scalefactor partitions and intensity stereo).
using Rex.Media.Codecs.Mpeg;
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Software.Mpeg;

/// <summary>Decodes MPEG audio Layer III (MP3) at every MPEG-1, MPEG-2 and MPEG 2.5 sampling rate.</summary>
public sealed class Mp3DecoderFactory : IDecoderFactory
{
    public string Name => "rexplayer MP3";

    public DecoderSource Source => DecoderSource.Own;

    public int Rank => 100;

    public bool CanDecode(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.Codec == CodecId.Mp3 && track.Audio is not null;
    }

    public IAudioDecoder CreateAudio(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!CanDecode(track))
        {
            throw new NotSupportedException("The track is not MP3.");
        }

        return new Mp3Decoder(track);
    }
}

/// <summary>
/// rexplayer's own Layer III decoder. A frame's audio data may start in earlier frames (the bit
/// reservoir), so after a seek the first frame or two decode as silence until the reservoir fills;
/// the demuxer seeks a few frames early so a precise seek never hears that. A damaged frame
/// becomes silence of a frame's length.
/// </summary>
public sealed class Mp3Decoder : IAudioDecoder
{
    private const int KeptMainData = 1024;

    private static readonly int[] Slen1 = [0, 0, 0, 0, 3, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4];
    private static readonly int[] Slen2 = [0, 1, 2, 3, 0, 1, 2, 3, 1, 2, 3, 1, 2, 3, 2, 3];

    // Scalefactor counts per partition for the lower sampling frequencies: [table][long, short, mixed][partition].
    private static readonly int[][][] PartitionSizes =
    [
        [[6, 5, 5, 5], [9, 9, 9, 9], [6, 9, 9, 9]],
        [[6, 5, 7, 3], [9, 9, 12, 6], [6, 9, 12, 6]],
        [[11, 10, 0, 0], [18, 18, 0, 0], [15, 18, 0, 0]],
        [[7, 7, 7, 0], [12, 12, 12, 0], [6, 15, 12, 0]],
        [[6, 6, 6, 3], [12, 9, 9, 6], [6, 12, 9, 6]],
        [[8, 8, 5, 0], [15, 12, 9, 0], [6, 18, 9, 0]],
    ];

    private static readonly double[] PowerFourThirds = [.. Enumerable.Range(0, 8207).Select(v => Math.Pow(v, 4.0 / 3.0))];
    private static readonly float[] AliasCs = [.. Layer3Tables.AliasCoefficients.Select(c => (float)(1 / Math.Sqrt(1 + (c * c))))];
    private static readonly float[] AliasCa = [.. Layer3Tables.AliasCoefficients.Select(c => (float)(c / Math.Sqrt(1 + (c * c))))];
    private static readonly float InverseRoot2 = (float)Math.Sqrt(0.5);

    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly ChannelLayout _layout;
    private readonly byte[] _mainData = new byte[KeptMainData + 4096];
    private readonly Layer3SideInfo _side = new();
    private readonly int[][] _quantized = [new int[576], new int[576]];
    private readonly float[][] _spectrum = [new float[576], new float[576]];
    private readonly int[] _nonzero = new int[2];
    private readonly int[][] _scalefacLong = [new int[22], new int[22]];
    private readonly int[][] _scalefacShort = [new int[39], new int[39]];
    private readonly int[] _limitLong = new int[22];
    private readonly int[] _limitShort = new int[39];
    private readonly float[][] _overlap = [new float[576], new float[576]];
    private readonly float[] _time = new float[576];
    private readonly float[] _reorder = new float[576];
    private readonly float[] _block = new float[36];
    private readonly float[] _slot = new float[32];
    private readonly SynthesisFilter[] _filters = [new(), new()];
    private int _mainLength;
    private int _intensityScale;

    public Mp3Decoder(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var audio = track.Audio ?? throw new NotSupportedException("The track has no audio parameters.");
        if (audio.Channels is not (1 or 2))
        {
            throw new NotSupportedException("MP3 carries one or two channels.");
        }

        _sampleRate = audio.SampleRate;
        _channels = audio.Channels;
        _layout = ChannelLayouts.Default(_channels);
    }

    public string Name => "rexplayer MP3";

    public DecoderSource Source => DecoderSource.Own;

    /// <summary>Frames replaced by silence because they were damaged.</summary>
    public int CorruptFrames { get; private set; }

    public void Decode(Packet packet, ICollection<AudioFrame> output)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(output);
        var data = packet.Data.Span;
        if (!MpegAudioHeader.TryParse(data, out var header) || header.Layer != 3)
        {
            CorruptFrames++;
            return;
        }

        var frame = AudioFrame.Rent(_sampleRate, _channels, header.SamplesPerFrame, _layout);
        frame.Pts = packet.Timestamp;
        frame.Generation = packet.Generation;
        try
        {
            DecodeFrame(data, header, frame);
        }
        catch (MediaFormatException)
        {
            CorruptFrames++;
            for (var channel = 0; channel < _channels; channel++)
            {
                frame.Channel(channel).Clear();
            }
        }

        output.Add(frame);
    }

    public void Drain(ICollection<AudioFrame> output)
    {
    }

    /// <summary>Forgets the reservoir and the filter histories, as a seek requires.</summary>
    public void Flush()
    {
        _mainLength = 0;
        foreach (var overlap in _overlap)
        {
            Array.Clear(overlap);
        }

        foreach (var filter in _filters)
        {
            filter.Reset();
        }
    }

    public void Dispose()
    {
    }

    private void DecodeFrame(ReadOnlySpan<byte> data, MpegAudioHeader header, AudioFrame frame)
    {
        if (header.Channels != _channels || header.SampleRate != _sampleRate)
        {
            throw new MediaFormatException("The MP3 frame's format differs from the stream's.");
        }

        var sideEnd = header.PayloadOffset + header.SideInfoLength;
        if (data.Length < sideEnd)
        {
            throw new MediaFormatException("The MP3 frame is shorter than its side information.");
        }

        _side.Read(data[header.PayloadOffset..sideEnd], header);
        var previous = AppendMainData(data[sideEnd..]);
        if (_side.MainDataBegin > previous)
        {
            // The frame's audio starts in data from before a seek or a cut: nothing to decode yet.
            for (var channel = 0; channel < _channels; channel++)
            {
                frame.Channel(channel).Clear();
            }

            return;
        }

        var start = previous - _side.MainDataBegin;
        var reader = new BitReader(_mainData.AsSpan(start, _mainLength - start));
        var granules = header.IsLowSampleRate ? 1 : 2;
        for (var gr = 0; gr < granules; gr++)
        {
            for (var ch = 0; ch < _channels; ch++)
            {
                var granule = _side.Granules[gr][ch];
                var part2Start = reader.BitPosition;
                if (header.IsLowSampleRate)
                {
                    ReadLowRateScalefactors(ref reader, granule, ch, header);
                }
                else
                {
                    ReadScalefactors(ref reader, granule, ch, gr);
                }

                var end = part2Start + granule.Part23Length;
                if (reader.BitPosition > end)
                {
                    throw new MediaFormatException("A granule's scalefactors run past its bits.");
                }

                _nonzero[ch] = Layer3Huffman.ReadSpectrum(ref reader, granule, end, _quantized[ch]);
                reader.Seek(end);
                Requantize(ch, granule, header);
            }

            if (_channels == 2 && header.Mode == MpegChannelMode.JointStereo)
            {
                JointStereo(gr, header);
            }

            for (var ch = 0; ch < _channels; ch++)
            {
                var granule = _side.Granules[gr][ch];
                Reorder(ch, granule, header);
                ReduceAliasing(ch, granule);
                Synthesize(ch, granule, frame.Channel(ch).Slice(gr * 576, 576));
            }
        }
    }

    /// <summary>Adds a frame's main data to the reservoir; returns where it starts, after any compaction.</summary>
    private int AppendMainData(ReadOnlySpan<byte> bytes)
    {
        if (_mainLength + bytes.Length > _mainData.Length)
        {
            var keep = Math.Min(_mainLength, KeptMainData);
            Array.Copy(_mainData, _mainLength - keep, _mainData, 0, keep);
            _mainLength = keep;
        }

        var start = _mainLength;
        bytes[..Math.Min(bytes.Length, _mainData.Length - _mainLength)].CopyTo(_mainData.AsSpan(_mainLength));
        _mainLength = Math.Min(_mainLength + bytes.Length, _mainData.Length);
        return start;
    }

    private void ReadScalefactors(ref BitReader reader, GranuleInfo granule, int ch, int gr)
    {
        var slen1 = Slen1[granule.ScalefacCompress];
        var slen2 = Slen2[granule.ScalefacCompress];
        var scalefacLong = _scalefacLong[ch];
        var scalefacShort = _scalefacShort[ch];
        if (granule.IsShort)
        {
            var firstShort = 0;
            if (granule.MixedBlock)
            {
                for (var sfb = 0; sfb < 8; sfb++)
                {
                    scalefacLong[sfb] = (int)reader.ReadBits(slen1);
                }

                firstShort = 3;
            }

            for (var sfb = firstShort; sfb < 12; sfb++)
            {
                var bits = sfb < 6 ? slen1 : slen2;
                for (var window = 0; window < 3; window++)
                {
                    scalefacShort[(sfb * 3) + window] = (int)reader.ReadBits(bits);
                }
            }

            scalefacShort.AsSpan(36).Clear();
            return;
        }

        // Four groups of long bands; in the second granule a group may reuse the first's values.
        ReadOnlySpan<int> groupStarts = [0, 6, 11, 16, 21];
        for (var group = 0; group < 4; group++)
        {
            if (gr == 1 && _side.Scfsi[ch][group])
            {
                continue;
            }

            var bits = group < 2 ? slen1 : slen2;
            for (var sfb = groupStarts[group]; sfb < groupStarts[group + 1]; sfb++)
            {
                scalefacLong[sfb] = (int)reader.ReadBits(bits);
            }
        }

        scalefacLong[21] = 0;
    }

    private void ReadLowRateScalefactors(ref BitReader reader, GranuleInfo granule, int ch, MpegAudioHeader header)
    {
        var compress = granule.ScalefacCompress;
        var intensityChannel = ch == 1 && header.Mode == MpegChannelMode.JointStereo && (header.ModeExtension & 1) != 0;
        Span<int> slen = stackalloc int[4];
        int table;
        var preflag = false;
        if (intensityChannel)
        {
            _intensityScale = compress & 1;
            var c = compress >> 1;
            (table, slen[0], slen[1], slen[2], slen[3]) = c switch
            {
                < 180 => (3, c / 36, c % 36 / 6, c % 36 % 6, 0),
                < 244 => (4, (c - 180) % 64 >> 4, (c - 180) % 16 >> 2, (c - 180) % 4, 0),
                _ => (5, (c - 244) / 3, (c - 244) % 3, 0, 0),
            };
        }
        else if (compress < 400)
        {
            (table, slen[0], slen[1], slen[2], slen[3]) = (0, (compress >> 4) / 5, (compress >> 4) % 5, (compress % 16) >> 2, compress % 4);
        }
        else if (compress < 500)
        {
            var c = compress - 400;
            (table, slen[0], slen[1], slen[2], slen[3]) = (1, (c >> 2) / 5, (c >> 2) % 5, c % 4, 0);
        }
        else
        {
            var c = compress - 500;
            (table, slen[0], slen[1], slen[2], slen[3]) = (2, c / 3, c % 3, 0, 0);
            preflag = true;
        }

        granule.Preflag = preflag;
        var blockIndex = granule.IsShort ? (granule.MixedBlock ? 2 : 1) : 0;
        var sizes = PartitionSizes[table][blockIndex];
        var scalefacLong = _scalefacLong[ch];
        var scalefacShort = _scalefacShort[ch];
        Array.Clear(scalefacLong);
        Array.Clear(scalefacShort);
        var slot = 0;
        for (var partition = 0; partition < 4; partition++)
        {
            var bits = slen[partition];

            // An intensity position equal to the largest value its field can hold is "illegal":
            // that band is not intensity coded. A field of no bits has no such value.
            var limit = bits == 0 ? -1 : (1 << bits) - 1;
            for (var n = 0; n < sizes[partition]; n++, slot++)
            {
                var value = (int)reader.ReadBits(bits);
                if (blockIndex == 0 || (blockIndex == 2 && slot < 6))
                {
                    scalefacLong[slot] = value;
                    _limitLong[slot] = limit;
                }
                else
                {
                    var shortSlot = blockIndex == 2 ? slot - 6 + 9 : slot;
                    scalefacShort[shortSlot] = value;
                    _limitShort[shortSlot] = limit;
                }
            }
        }
    }

    private void Requantize(int ch, GranuleInfo granule, MpegAudioHeader header)
    {
        var quantized = _quantized[ch];
        var spectrum = _spectrum[ch];
        var longBands = Layer3Tables.LongBands[header.SampleRateIndex];
        var shortBands = Layer3Tables.ShortBands[header.SampleRateIndex];
        var multiplier = granule.ScalefacScale ? 4 : 2;
        var gain = granule.GlobalGain - 210;
        Array.Clear(spectrum);
        var limit = _nonzero[ch];
        var firstShort = 0;
        if (!granule.IsShort || granule.MixedBlock)
        {
            var lastLong = granule.IsShort ? 36 : 576;
            for (var sfb = 0; sfb < 22 && longBands[sfb] < Math.Min(lastLong, limit); sfb++)
            {
                var exponent = gain - (multiplier * (_scalefacLong[ch][sfb] + (granule.Preflag ? Layer3Tables.Pretab[sfb] : 0)));
                Scale(quantized, spectrum, longBands[sfb], Math.Min(longBands[sfb + 1], lastLong), exponent);
            }

            if (!granule.IsShort)
            {
                return;
            }

            firstShort = 3;
        }

        for (var sfb = firstShort; sfb < 13 && shortBands[sfb] * 3 < limit; sfb++)
        {
            var width = shortBands[sfb + 1] - shortBands[sfb];
            for (var window = 0; window < 3; window++)
            {
                var exponent = gain - (8 * granule.SubblockGain[window]) - (multiplier * _scalefacShort[ch][(sfb * 3) + window]);
                var start = (shortBands[sfb] * 3) + (window * width);
                Scale(quantized, spectrum, start, start + width, exponent);
            }
        }
    }

    /// <summary>xr = sign(is) * |is|^(4/3) * 2^(exponent / 4).</summary>
    private static void Scale(int[] quantized, float[] spectrum, int start, int end, int exponent)
    {
        var step = Math.Pow(2, exponent / 4.0);
        for (var i = start; i < end; i++)
        {
            var value = quantized[i];
            if (value != 0)
            {
                var magnitude = PowerFourThirds[Math.Min(Math.Abs(value), PowerFourThirds.Length - 1)] * step;
                spectrum[i] = (float)(value < 0 ? -magnitude : magnitude);
            }
        }
    }

    private void JointStereo(int gr, MpegAudioHeader header)
    {
        var midSide = (header.ModeExtension & 2) != 0;
        var intensity = (header.ModeExtension & 1) != 0;
        var left = _spectrum[0];
        var right = _spectrum[1];
        var granule = _side.Granules[gr][1];
        if (!intensity)
        {
            if (midSide)
            {
                MidSide(left, right, 0, Math.Max(_nonzero[0], _nonzero[1]));
            }

            return;
        }

        var longBands = Layer3Tables.LongBands[header.SampleRateIndex];
        var shortBands = Layer3Tables.ShortBands[header.SampleRateIndex];
        if (!granule.IsShort)
        {
            // Bands from the first one where the right channel is all zero up are intensity coded.
            var first = 0;
            while (first < 22 && longBands[first] < _nonzero[1])
            {
                first++;
            }

            for (var sfb = 0; sfb < 22; sfb++)
            {
                var source = Math.Min(sfb, 20);
                Band(sfb >= first, _scalefacLong[1][source], _limitLong[source], longBands[sfb], longBands[sfb + 1]);
            }

            return;
        }

        var firstShort = granule.MixedBlock ? 3 : 0;
        if (granule.MixedBlock && midSide)
        {
            MidSide(left, right, 0, 36);
        }

        for (var window = 0; window < 3; window++)
        {
            var first = 13;
            while (first > firstShort && IsSilent(right, shortBands, first - 1, window))
            {
                first--;
            }

            for (var sfb = firstShort; sfb < 13; sfb++)
            {
                var width = shortBands[sfb + 1] - shortBands[sfb];
                var start = (shortBands[sfb] * 3) + (window * width);
                var source = (Math.Min(sfb, 11) * 3) + window;
                Band(sfb >= first, _scalefacShort[1][source], _limitShort[source], start, start + width);
            }
        }

        void Band(bool coded, int position, int limit, int start, int end)
        {
            var illegal = header.IsLowSampleRate ? position == limit : position == 7;
            if (!coded || illegal)
            {
                if (midSide)
                {
                    MidSide(left, right, start, end);
                }

                return;
            }

            var (leftGain, rightGain) = IntensityGains(position, header.IsLowSampleRate);
            for (var i = start; i < end; i++)
            {
                var value = left[i];
                left[i] = value * leftGain;
                right[i] = value * rightGain;
            }
        }
    }

    private (float Left, float Right) IntensityGains(int position, bool lowRate)
    {
        if (!lowRate)
        {
            if (position == 6)
            {
                return (1f, 0f);
            }

            var ratio = Math.Tan(position * Math.PI / 12);
            return ((float)(ratio / (1 + ratio)), (float)(1 / (1 + ratio)));
        }

        if (position == 0)
        {
            return (1f, 1f);
        }

        var step = _intensityScale == 1 ? Math.Sqrt(0.5) : Math.Pow(2, -0.25);
        return position % 2 == 1
            ? ((float)Math.Pow(step, (position + 1) / 2), 1f)
            : (1f, (float)Math.Pow(step, position / 2));
    }

    private static bool IsSilent(float[] spectrum, short[] shortBands, int sfb, int window)
    {
        var width = shortBands[sfb + 1] - shortBands[sfb];
        var start = (shortBands[sfb] * 3) + (window * width);
        for (var i = start; i < start + width; i++)
        {
            if (spectrum[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void MidSide(float[] left, float[] right, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            var mid = left[i];
            var side = right[i];
            left[i] = (mid + side) * InverseRoot2;
            right[i] = (mid - side) * InverseRoot2;
        }
    }

    /// <summary>Short blocks arrive band by band, window by window; the IMDCT wants each line's three windows together.</summary>
    private void Reorder(int ch, GranuleInfo granule, MpegAudioHeader header)
    {
        if (!granule.IsShort)
        {
            return;
        }

        var spectrum = _spectrum[ch];
        var shortBands = Layer3Tables.ShortBands[header.SampleRateIndex];
        var firstShort = granule.MixedBlock ? 3 : 0;
        for (var sfb = firstShort; sfb < 13; sfb++)
        {
            var start = shortBands[sfb] * 3;
            var width = shortBands[sfb + 1] - shortBands[sfb];
            for (var window = 0; window < 3; window++)
            {
                for (var k = 0; k < width; k++)
                {
                    _reorder[start + (3 * k) + window] = spectrum[start + (window * width) + k];
                }
            }
        }

        var from = shortBands[firstShort] * 3;
        _reorder.AsSpan(from, 576 - from).CopyTo(spectrum.AsSpan(from));
    }

    private void ReduceAliasing(int ch, GranuleInfo granule)
    {
        if (granule.IsShort && !granule.MixedBlock)
        {
            return;
        }

        var spectrum = _spectrum[ch];
        var boundaries = granule.IsShort ? 1 : 31;
        for (var sb = 1; sb <= boundaries; sb++)
        {
            for (var i = 0; i < 8; i++)
            {
                var lower = (18 * sb) - 1 - i;
                var upper = (18 * sb) + i;
                var below = spectrum[lower];
                var above = spectrum[upper];
                spectrum[lower] = (below * AliasCs[i]) - (above * AliasCa[i]);
                spectrum[upper] = (above * AliasCs[i]) + (below * AliasCa[i]);
            }
        }
    }

    /// <summary>IMDCT and overlap-add per subband, frequency inversion, then the polyphase filterbank.</summary>
    private void Synthesize(int ch, GranuleInfo granule, Span<float> output)
    {
        var spectrum = _spectrum[ch];
        var overlap = _overlap[ch];
        for (var sb = 0; sb < 32; sb++)
        {
            var blockType = granule.WindowSwitching && !(granule.MixedBlock && sb < 2) ? granule.BlockType : 0;
            var lines = spectrum.AsSpan(sb * 18, 18);
            if (blockType == 2)
            {
                Imdct.Short(lines, _block);
            }
            else
            {
                Imdct.Long(lines, blockType, _block);
            }

            for (var i = 0; i < 18; i++)
            {
                var sample = _block[i] + overlap[(sb * 18) + i];
                overlap[(sb * 18) + i] = _block[i + 18];
                _time[(sb * 18) + i] = sb % 2 == 1 && i % 2 == 1 ? -sample : sample;
            }
        }

        var filter = _filters[ch];
        for (var t = 0; t < 18; t++)
        {
            for (var sb = 0; sb < 32; sb++)
            {
                _slot[sb] = _time[(sb * 18) + t];
            }

            filter.Process(_slot, output.Slice(t * 32, 32));
        }
    }
}
