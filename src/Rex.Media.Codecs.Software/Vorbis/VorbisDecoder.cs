// Spec: Vorbis I specification, section 4.3 (audio packet decode: mode, window shape, floor curves, nonzero propagation, residue decode, inverse coupling, dot product, inverse MDCT, overlap-add), 7.2.2-7.2.4 (floor 1 packet decode and curve), 8.6.2-8.6.7 (residue 0, 1 and 2) and 10.1 (floor1_inverse_dB_table), with the channel order of section 4.3.9.
using System.Runtime.InteropServices;
using Rex.Media.Primitives;

namespace Rex.Media.Codecs.Software.Vorbis;

/// <summary>Decodes Vorbis, rexplayer's own implementation of the Vorbis I specification.</summary>
public sealed class VorbisDecoderFactory : IDecoderFactory
{
    public string Name => "rexplayer Vorbis";

    public DecoderSource Source => DecoderSource.Own;

    public int Rank => 100;

    public bool CanDecode(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.Codec == CodecId.Vorbis && track.Audio is not null;
    }

    public IAudioDecoder CreateAudio(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return CanDecode(track) ? new VorbisDecoder(track) : throw new NotSupportedException("The track is not Vorbis.");
    }
}

/// <summary>
/// Turns Vorbis packets into float samples. Each packet is one block, overlapped with the one before:
/// the first packet after opening or a seek only primes the overlap and gives no samples. A packet
/// that does not decode becomes silence of its length, so the timeline stays intact.
/// </summary>
public sealed class VorbisDecoder : IAudioDecoder
{
    // Section 10.1: floor values 0 to 255 map to amplitudes from -140 dB to 0 dB, a factor of 1.0649863 apart.
    private static readonly float[] InverseDb = [.. Enumerable.Range(0, 256).Select(i => (float)Math.Pow(1.0649863, i - 255))];

    // Section 4.3.9: Vorbis channel order for one to eight channels, as positions in WAVE order.
    private static readonly int[][] ToWaveOrder =
    [
        [0],
        [0, 1],
        [0, 2, 1],
        [0, 1, 2, 3],
        [0, 2, 1, 3, 4],
        [0, 2, 1, 4, 5, 3],
        [0, 2, 1, 5, 6, 4, 3],
        [0, 2, 1, 6, 7, 4, 5, 3],
    ];

    private readonly VorbisSetup _setup;
    private readonly int _channels;
    private readonly ChannelLayout _layout;
    private readonly int[] _order;
    private readonly VorbisBits _bits = new();
    private readonly VorbisMdct _shortMdct;
    private readonly VorbisMdct _longMdct;
    private readonly float[] _shortSlope;
    private readonly float[] _longSlope;
    private readonly float[][] _spectrum;
    private readonly float[][] _floor;
    private readonly float[][] _block;
    private readonly float[][] _previous;
    private readonly bool[] _floorUnused;
    private readonly bool[] _noResidue;
    private readonly int[] _floorY = new int[65];
    private readonly int[] _finalY = new int[65];
    private readonly bool[] _step2 = new bool[65];
    private readonly int[] _floorInt;
    private int[][] _classes = [];
    private float[] _interleaved = [];
    private int _previousSize;

    public VorbisDecoder(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        _setup = VorbisSetup.FromCodecPrivate(track.CodecPrivate);
        _channels = _setup.Channels;
        _order = _channels <= 8 ? ToWaveOrder[_channels - 1] : [.. Enumerable.Range(0, _channels)];
        _layout = _channels switch
        {
            3 => ChannelLayout.Surround,
            5 => ChannelLayout.FrontLeft | ChannelLayout.FrontRight | ChannelLayout.FrontCenter | ChannelLayout.BackLeft | ChannelLayout.BackRight,
            6 => ChannelLayout.Surround51Back,
            7 => ChannelLayout.FrontLeft | ChannelLayout.FrontRight | ChannelLayout.FrontCenter | ChannelLayout.LowFrequency | ChannelLayout.BackCenter | ChannelLayout.SideLeft | ChannelLayout.SideRight,
            8 => ChannelLayout.FrontLeft | ChannelLayout.FrontRight | ChannelLayout.FrontCenter | ChannelLayout.LowFrequency | ChannelLayout.BackLeft | ChannelLayout.BackRight | ChannelLayout.SideLeft | ChannelLayout.SideRight,
            _ => ChannelLayouts.Default(_channels),
        };
        _shortMdct = new VorbisMdct(_setup.ShortBlock);
        _longMdct = new VorbisMdct(_setup.LongBlock);
        _shortSlope = Slope(_setup.ShortBlock / 2);
        _longSlope = Slope(_setup.LongBlock / 2);
        var longHalf = _setup.LongBlock / 2;
        _spectrum = [.. Enumerable.Range(0, _channels).Select(_ => new float[longHalf])];
        _floor = [.. Enumerable.Range(0, _channels).Select(_ => new float[longHalf])];
        _block = [.. Enumerable.Range(0, _channels).Select(_ => new float[_setup.LongBlock])];
        _previous = [.. Enumerable.Range(0, _channels).Select(_ => new float[_setup.LongBlock])];
        _floorUnused = new bool[_channels];
        _noResidue = new bool[_channels];
        _floorInt = new int[longHalf];
    }

    public string Name => "rexplayer Vorbis";

    public DecoderSource Source => DecoderSource.Own;

    /// <summary>Packets replaced by silence because they did not decode.</summary>
    public int CorruptPackets { get; private set; }

    public int SampleRate => _setup.SampleRate;

    public int Channels => _channels;

    public void Decode(Packet packet, ICollection<AudioFrame> output)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(output);
        // A packet's buffer is an array from its start, read in place.
        var memory = packet.Data.Memory;
        var segment = MemoryMarshal.TryGetArray<byte>(memory, out var array) && array.Offset == 0 ? array : new ArraySegment<byte>(memory.ToArray());
        _bits.Reset(segment.Array!, segment.Count);

        // An empty packet, or a header repeated in-band, carries no audio.
        if (memory.Length == 0 || _bits.ReadFlag())
        {
            return;
        }

        var modeNumber = _bits.ReadInt(VorbisCodebook.Ilog(_setup.Modes.Length - 1));
        if (modeNumber >= _setup.Modes.Length)
        {
            CorruptPackets++;
            return;
        }

        var mode = _setup.Modes[modeNumber];
        var size = mode.LongBlock ? _setup.LongBlock : _setup.ShortBlock;
        var previousLong = mode.LongBlock && _bits.ReadFlag();
        var nextLong = mode.LongBlock && _bits.ReadFlag();
        try
        {
            DecodeBlock(_setup.Mappings[mode.Mapping], size);
        }
        catch (MediaFormatException)
        {
            CorruptPackets++;
            for (var channel = 0; channel < _channels; channel++)
            {
                Array.Clear(_spectrum[channel]);
                _floorUnused[channel] = true;
            }
        }

        Window(size, mode.LongBlock, previousLong, nextLong);
        Emit(packet, size, output);
    }

    public void Drain(ICollection<AudioFrame> output)
    {
    }

    public void Flush() => _previousSize = 0;

    public void Dispose()
    {
    }

    private static float[] Slope(int length) =>
        [.. Enumerable.Range(0, length).Select(i => (float)Math.Sin(Math.PI / 2 * Math.Pow(Math.Sin((i + 0.5) / length * Math.PI / 2), 2)))];

    private void DecodeBlock(VorbisMapping mapping, int size)
    {
        var half = size / 2;

        // Floors.
        for (var channel = 0; channel < _channels; channel++)
        {
            var floor = _setup.Floors[mapping.SubmapFloor[mapping.Mux[channel]]] ?? throw new MediaFormatException("Vorbis floor type 0 is not supported.");
            _floorUnused[channel] = !DecodeFloor(floor, half, _floor[channel]);
            _noResidue[channel] = _floorUnused[channel];
            Array.Clear(_spectrum[channel], 0, half);
        }

        // A coupled pair is decoded if either of its channels is in use.
        for (var i = 0; i < mapping.Magnitude.Length; i++)
        {
            if (!_noResidue[mapping.Magnitude[i]] || !_noResidue[mapping.Angle[i]])
            {
                _noResidue[mapping.Magnitude[i]] = false;
                _noResidue[mapping.Angle[i]] = false;
            }
        }

        // Residues, a submap at a time.
        for (var submap = 0; submap < mapping.SubmapResidue.Length; submap++)
        {
            var channels = Enumerable.Range(0, _channels).Where(channel => mapping.Mux[channel] == submap).ToArray();
            DecodeResidue(_setup.Residues[mapping.SubmapResidue[submap]], channels, half);
        }

        // Inverse coupling, last step first.
        for (var i = mapping.Magnitude.Length - 1; i >= 0; i--)
        {
            var magnitude = _spectrum[mapping.Magnitude[i]];
            var angle = _spectrum[mapping.Angle[i]];
            for (var j = 0; j < half; j++)
            {
                var m = magnitude[j];
                var a = angle[j];
                if (m > 0)
                {
                    (magnitude[j], angle[j]) = a > 0 ? (m, m - a) : (m + a, m);
                }
                else
                {
                    (magnitude[j], angle[j]) = a > 0 ? (m, m + a) : (m - a, m);
                }
            }
        }

        // The floor shapes the residue.
        for (var channel = 0; channel < _channels; channel++)
        {
            var spectrum = _spectrum[channel];
            if (_floorUnused[channel])
            {
                Array.Clear(spectrum, 0, half);
                continue;
            }

            var floor = _floor[channel];
            for (var j = 0; j < half; j++)
            {
                spectrum[j] *= floor[j];
            }
        }
    }

    /// <summary>Decodes a floor 1 curve into <paramref name="curve"/>; false when the channel is unused this packet.</summary>
    private bool DecodeFloor(VorbisFloor1 floor, int half, float[] curve)
    {
        if (!_bits.ReadFlag())
        {
            return false;
        }

        var range = floor.Multiplier switch { 1 => 256, 2 => 128, 3 => 86, _ => 64 };
        var yBits = VorbisCodebook.Ilog(range - 1);
        var y = _floorY;
        y[0] = _bits.ReadInt(yBits);
        y[1] = _bits.ReadInt(yBits);
        var offset = 2;
        foreach (var c in floor.PartitionClasses)
        {
            var dimensions = floor.ClassDimensions[c];
            var subclassBits = floor.ClassSubclasses[c];
            var mask = (1 << subclassBits) - 1;
            var value = 0;
            if (subclassBits > 0)
            {
                value = _setup.Codebooks[floor.ClassMasterbooks[c]].DecodeScalar(_bits);
                if (value < 0)
                {
                    return false;
                }
            }

            for (var j = 0; j < dimensions; j++)
            {
                var book = floor.SubclassBooks[c][value & mask];
                value >>= subclassBits;
                if (book >= 0)
                {
                    var decoded = _setup.Codebooks[book].DecodeScalar(_bits);
                    if (decoded < 0)
                    {
                        return false;
                    }

                    y[offset + j] = decoded;
                }
                else
                {
                    y[offset + j] = 0;
                }
            }

            offset += dimensions;
        }

        if (_bits.EndOfPacket)
        {
            return false;
        }

        // Step 1: each point's amplitude, predicted from its neighbours and corrected by its value.
        var x = floor.X;
        var points = x.Length;
        _finalY[0] = y[0];
        _finalY[1] = y[1];
        _step2[0] = _step2[1] = true;
        for (var i = 2; i < points; i++)
        {
            var low = floor.Low[i];
            var high = floor.High[i];
            var predicted = RenderPoint(x[low], _finalY[low], x[high], _finalY[high], x[i]);
            var value = y[i];
            var highRoom = range - predicted;
            var lowRoom = predicted;
            var room = (highRoom < lowRoom ? highRoom : lowRoom) * 2;
            if (value != 0)
            {
                _step2[low] = _step2[high] = _step2[i] = true;
                _finalY[i] = value >= room
                    ? highRoom > lowRoom ? value - lowRoom + predicted : predicted - value + highRoom - 1
                    : (value & 1) != 0 ? predicted - ((value + 1) / 2) : predicted + (value / 2);
            }
            else
            {
                _step2[i] = false;
                _finalY[i] = predicted;
            }
        }

        // Step 2: lines between the points in use, in order of X.
        var lx = 0;
        var ly = _finalY[floor.Sorted[0]] * floor.Multiplier;
        var hx = 0;
        var hy = ly;
        for (var s = 1; s < points; s++)
        {
            var i = floor.Sorted[s];
            if (_step2[i])
            {
                hy = _finalY[i] * floor.Multiplier;
                hx = x[i];
                RenderLine(lx, ly, hx, hy, half);
                lx = hx;
                ly = hy;
            }
        }

        for (var i = Math.Max(hx, 0); i < half; i++)
        {
            _floorInt[i] = hy;
        }

        for (var i = 0; i < half; i++)
        {
            curve[i] = InverseDb[Math.Clamp(_floorInt[i], 0, 255)];
        }

        return true;
    }

    private static int RenderPoint(int x0, int y0, int x1, int y1, int x)
    {
        var dy = y1 - y0;
        var adx = x1 - x0;
        var err = Math.Abs(dy) * (x - x0);
        var off = err / adx;
        return dy < 0 ? y0 - off : y0 + off;
    }

    /// <summary>
    /// Section 9.2.7: an integer line from (x0, y0) towards (x1, y1), not including x1, and not past
    /// <paramref name="limit"/>. The floor's points are distinct and drawn in order, so x1 is past x0.
    /// </summary>
    private void RenderLine(int x0, int y0, int x1, int y1, int limit)
    {
        var dy = y1 - y0;
        var adx = x1 - x0;
        var ady = Math.Abs(dy);
        var baseStep = dy / adx;
        var sy = dy < 0 ? baseStep - 1 : baseStep + 1;
        ady -= Math.Abs(baseStep) * adx;
        var yy = y0;
        var err = 0;
        if (x0 < limit)
        {
            _floorInt[x0] = yy;
        }

        for (var xx = x0 + 1; xx < x1 && xx < limit; xx++)
        {
            err += ady;
            if (err >= adx)
            {
                err -= adx;
                yy += sy;
            }
            else
            {
                yy += baseStep;
            }

            _floorInt[xx] = yy;
        }
    }

    private void DecodeResidue(VorbisResidue residue, int[] channels, int half)
    {
        if (channels.Length == 0)
        {
            return;
        }

        if (residue.Type == 2)
        {
            if (channels.All(channel => _noResidue[channel]))
            {
                return;
            }

            var total = half * channels.Length;
            if (_interleaved.Length < total)
            {
                _interleaved = new float[total];
            }

            Array.Clear(_interleaved, 0, total);
            DecodePartitions(residue, [_interleaved], [false], total, interleaved: true);
            for (var i = 0; i < half; i++)
            {
                for (var j = 0; j < channels.Length; j++)
                {
                    _spectrum[channels[j]][i] = _interleaved[(i * channels.Length) + j];
                }
            }

            return;
        }

        DecodePartitions(residue, [.. channels.Select(channel => _spectrum[channel])], [.. channels.Select(channel => _noResidue[channel])], half, interleaved: false);
    }

    /// <summary>Sections 8.6.2-8.6.4: the residue vectors, decoded partition by partition over eight passes.</summary>
    private void DecodePartitions(VorbisResidue residue, float[][] vectors, bool[] skip, int size, bool interleaved)
    {
        var begin = Math.Min(residue.Begin, size);
        var end = Math.Min(residue.End, size);
        var classbook = _setup.Codebooks[residue.Classbook];
        var perCodeword = classbook.Dimensions;
        var partitions = (end - begin) / residue.PartitionSize;
        if (partitions <= 0)
        {
            return;
        }

        if (_classes.Length < vectors.Length || _classes[0].Length < partitions + perCodeword)
        {
            _classes = [.. Enumerable.Range(0, Math.Max(vectors.Length, _channels)).Select(_ => new int[partitions + perCodeword])];
        }

        for (var pass = 0; pass < 8; pass++)
        {
            var partition = 0;
            while (partition < partitions)
            {
                if (pass == 0)
                {
                    for (var j = 0; j < vectors.Length; j++)
                    {
                        if (skip[j])
                        {
                            continue;
                        }

                        var temp = classbook.DecodeScalar(_bits);
                        if (temp < 0)
                        {
                            return;
                        }

                        for (var i = perCodeword - 1; i >= 0; i--)
                        {
                            _classes[j][i + partition] = temp % residue.Classifications;
                            temp /= residue.Classifications;
                        }
                    }
                }

                for (var i = 0; i < perCodeword && partition < partitions; i++, partition++)
                {
                    for (var j = 0; j < vectors.Length; j++)
                    {
                        if (skip[j])
                        {
                            continue;
                        }

                        var book = residue.Books[_classes[j][partition]][pass];
                        if (book < 0)
                        {
                            continue;
                        }

                        var offset = begin + (partition * residue.PartitionSize);
                        var ok = residue.Type == 0 && !interleaved
                            ? Partition0(_setup.Codebooks[book], vectors[j], offset, residue.PartitionSize)
                            : Partition1(_setup.Codebooks[book], vectors[j], offset, residue.PartitionSize);
                        if (!ok)
                        {
                            return;
                        }
                    }
                }
            }
        }
    }

    private bool Partition0(VorbisCodebook book, float[] vector, int offset, int size)
    {
        var step = size / book.Dimensions;
        for (var j = 0; j < step; j++)
        {
            var values = book.DecodeVector(_bits);
            if (values.IsEmpty)
            {
                return false;
            }

            for (var i = 0; i < values.Length; i++)
            {
                vector[offset + j + (i * step)] += values[i];
            }
        }

        return true;
    }

    private bool Partition1(VorbisCodebook book, float[] vector, int offset, int size)
    {
        var i = 0;
        while (i < size)
        {
            var values = book.DecodeVector(_bits);
            if (values.IsEmpty)
            {
                return false;
            }

            for (var k = 0; k < values.Length && i < size; k++, i++)
            {
                vector[offset + i] += values[k];
            }
        }

        return true;
    }

    /// <summary>The inverse MDCT of each channel, shaped by this block's window (section 4.3.1).</summary>
    private void Window(int size, bool longBlock, bool previousLong, bool nextLong)
    {
        var shortHalf = _setup.ShortBlock / 2;
        var leftN = longBlock && !previousLong ? shortHalf : size / 2;
        var rightN = longBlock && !nextLong ? shortHalf : size / 2;
        var leftStart = (size / 4) - (leftN / 2);
        var rightStart = (size * 3 / 4) - (rightN / 2);
        var leftSlope = leftN == _setup.LongBlock / 2 ? _longSlope : _shortSlope;
        var rightSlope = rightN == _setup.LongBlock / 2 ? _longSlope : _shortSlope;
        var mdct = longBlock ? _longMdct : _shortMdct;
        for (var channel = 0; channel < _channels; channel++)
        {
            var block = _block[channel].AsSpan(0, size);
            mdct.Inverse(_spectrum[channel].AsSpan(0, size / 2), block);
            block[..leftStart].Clear();
            for (var i = 0; i < leftN; i++)
            {
                block[leftStart + i] *= leftSlope[i];
            }

            for (var i = 0; i < rightN; i++)
            {
                block[rightStart + i] *= rightSlope[rightN - 1 - i];
            }

            block[(rightStart + rightN)..].Clear();
        }
    }

    /// <summary>Overlaps this block with the last and gives the samples between their centres (section 4.3.8).</summary>
    private void Emit(Packet packet, int size, ICollection<AudioFrame> output)
    {
        if (_previousSize > 0)
        {
            var count = (_previousSize / 4) + (size / 4);
            var frame = AudioFrame.Rent(_setup.SampleRate, _channels, count, _layout);
            frame.Pts = packet.Timestamp;
            frame.Generation = packet.Generation;
            var currentStart = (size / 4) - (_previousSize / 4);
            for (var channel = 0; channel < _channels; channel++)
            {
                var target = frame.Channel(_order[channel]);
                var previous = _previous[channel];
                var current = _block[channel];
                for (var t = 0; t < count; t++)
                {
                    var p = (_previousSize / 2) + t;
                    var c = currentStart + t;
                    target[t] = (p < _previousSize ? previous[p] : 0) + (c >= 0 ? current[c] : 0);
                }
            }

            output.Add(frame);
        }

        for (var channel = 0; channel < _channels; channel++)
        {
            _block[channel].AsSpan(0, size).CopyTo(_previous[channel]);
        }

        _previousSize = size;
    }
}
