using Rex.Media.Primitives;

namespace Rex.Media.Video;

/// <summary>
/// Converts decoded pictures to 8-bit BGRA on the CPU, for snapshots and for tests that compare
/// pictures. Playback converts on the GPU with the same <see cref="YuvTransform"/>: ITU-T
/// H.273's matrices (from Kr and Kb), with limited or full range. Chroma is taken from the nearest
/// sample, which is what a still image of the coded picture needs, without a display scaler's smoothing.
/// </summary>
public static class ColorConverter
{
    /// <summary>The luma weights of red and blue for a matrix, or null for RGB stored as GBR planes.</summary>
    public static (double Kr, double Kb)? Weights(ColorMatrix matrix) => matrix switch
    {
        ColorMatrix.Rgb => null,
        ColorMatrix.Bt709 => (0.2126, 0.0722),
        ColorMatrix.Bt2020NonConstant => (0.2627, 0.0593),
        _ => (0.299, 0.114),
    };

    /// <summary>A BGRA copy of <paramref name="source"/>, with its timing and aspect; the colour is resolved for its size.</summary>
    public static VideoFrame ToBgra(VideoFrame source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var output = VideoFrame.Rent(PixelFormat.Bgra32, source.Width, source.Height);
        output.Pts = source.Pts;
        output.Duration = source.Duration;
        output.Generation = source.Generation;
        output.PixelAspect = source.PixelAspect;
        output.Color = source.Color;
        if (source.Format == PixelFormat.Bgra32)
        {
            for (var y = 0; y < source.Height; y++)
            {
                source.Row(0, y).CopyTo(output.Row(0, y));
            }

            return output;
        }

        var bits = source.Format is PixelFormat.P010 or PixelFormat.I420P10 ? 10 : 8;
        var reader = new SampleReader(source);
        var transform = YuvTransform.For(source.Color.Resolve(source.Width, source.Height), bits, reader.HasChroma);
        for (var y = 0; y < source.Height; y++)
        {
            var row = output.Row(0, y);
            for (var x = 0; x < source.Width; x++)
            {
                var (r, g, b) = reader.HasChroma ? transform.Apply(reader.Luma(x, y), reader.Cb(x, y), reader.Cr(x, y)) : transform.Apply(reader.Luma(x, y), 0, 0);
                row[(x * 4) + 0] = ToByte(b);
                row[(x * 4) + 1] = ToByte(g);
                row[(x * 4) + 2] = ToByte(r);
                row[(x * 4) + 3] = 255;
            }
        }

        return output;
    }

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);

    /// <summary>Reads samples of any planar or semi-planar format as integers at their coded depth.</summary>
    private readonly ref struct SampleReader
    {
        private readonly VideoFrame _frame;
        private readonly int _shiftX;
        private readonly int _shiftY;

        public SampleReader(VideoFrame frame)
        {
            _frame = frame;
            HasChroma = frame.Format != PixelFormat.Gray8;
            _shiftX = frame.Format is PixelFormat.I444 ? 0 : 1;
            _shiftY = frame.Format is PixelFormat.I444 or PixelFormat.I422 ? 0 : 1;
        }

        public bool HasChroma { get; }

        public int Luma(int x, int y) => Read(0, x, y);

        public int Cb(int x, int y) => _frame.Format is PixelFormat.Nv12 or PixelFormat.P010
            ? Read(1, (x >> _shiftX) * 2, y >> _shiftY)
            : Read(1, x >> _shiftX, y >> _shiftY);

        public int Cr(int x, int y) => _frame.Format is PixelFormat.Nv12 or PixelFormat.P010
            ? Read(1, ((x >> _shiftX) * 2) + 1, y >> _shiftY)
            : Read(2, x >> _shiftX, y >> _shiftY);

        /// <summary>The sample at a column of a plane's row; 16-bit formats are little-endian, P010 holding its value in the top bits.</summary>
        private int Read(int plane, int column, int row)
        {
            var bytes = _frame.Row(plane, row);
            return _frame.Format switch
            {
                PixelFormat.P010 => (bytes[column * 2] | (bytes[(column * 2) + 1] << 8)) >> 6,
                PixelFormat.I420P10 => bytes[column * 2] | (bytes[(column * 2) + 1] << 8),
                _ => bytes[column],
            };
        }
    }
}
