using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

/// <summary>The optional model is admitted only when its measured cost and its mask are suitable.</summary>
public sealed class SubjectModelPolicyTests
{
    private static PictureMask Rectangle(int pictureWidth, int pictureHeight,
        int width, int height, double score = 1)
    {
        var values = new byte[pictureWidth * pictureHeight];
        for (var y = 0; y < height; y++)
        {
            values.AsSpan(y * pictureWidth, width).Fill(255);
        }

        return new PictureMask(values, pictureWidth, pictureHeight, score);
    }

    [Fact]
    public void PausedPictureUsesOnlyAvailableModelsThatAreNotTooSlow()
    {
        var budget = new FrameBudget();
        Assert.True(SubjectModelPolicy.UseOnPicture(true, true, budget));
        Assert.False(SubjectModelPolicy.UseOnPicture(false, true, budget));
        Assert.False(SubjectModelPolicy.UseOnPicture(true, false, budget));
        Assert.Throws<ArgumentNullException>(() => SubjectModelPolicy.UseOnPicture(true, true, null!));
        budget.Record(TimeSpan.FromMilliseconds(10));
        budget.Record(TimeSpan.FromMilliseconds(300));
        Assert.True(SubjectModelPolicy.UseOnPicture(true, true, budget));
        budget.Record(TimeSpan.FromSeconds(31));
        Assert.False(SubjectModelPolicy.UseOnPicture(true, true, budget));
    }

    [Fact]
    public void AnInteractiveSecondOpinionHasToAnswerWithinItsMeasuredBudget()
    {
        var budget = new FrameBudget();
        Assert.False(SubjectModelPolicy.SecondOpinion(true, budget, 30));
        budget.Record(TimeSpan.FromMilliseconds(1));
        budget.Record(TimeSpan.FromMilliseconds(20));
        Assert.True(SubjectModelPolicy.SecondOpinion(true, budget, 30));
        Assert.False(SubjectModelPolicy.SecondOpinion(false, budget, 30));
        budget.Record(TimeSpan.FromSeconds(5));
        Assert.False(SubjectModelPolicy.SecondOpinion(true, budget, 30));
        Assert.Throws<ArgumentNullException>(() => SubjectModelPolicy.SecondOpinion(true, null!, 30));
    }

    [Fact]
    public void ModelMasksAreCheckedForConfidenceScaleAndBounds()
    {
        Assert.Throws<ArgumentNullException>(() => SubjectModelPolicy.Judge(null!));
        Assert.Equal(MaskVerdict.Empty, SubjectModelPolicy.Judge(Rectangle(100, 100, 0, 0)));
        Assert.Equal(MaskVerdict.NotConfident, SubjectModelPolicy.Judge(Rectangle(100, 100, 30, 30, 0.2)));
        Assert.Equal(MaskVerdict.TooSmall, SubjectModelPolicy.Judge(Rectangle(100, 100, 10, 20)));
        Assert.Equal(MaskVerdict.TooSmall, SubjectModelPolicy.Judge(Rectangle(100, 100, 10, 45)));
        Assert.Equal(MaskVerdict.TooSmall, SubjectModelPolicy.Judge(Rectangle(2000, 2000, 20, 25)));
        Assert.Equal(MaskVerdict.TooLarge, SubjectModelPolicy.Judge(Rectangle(100, 100, 80, 80)));
        Assert.Equal(MaskVerdict.Accepted, SubjectModelPolicy.Judge(Rectangle(100, 100, 30, 30)));
    }

    private sealed class FakeSegmenter : IPromptSegmenter
    {
        public bool Disposed { get; private set; }
        public ExecutionDevice Device => ExecutionDevice.Cpu;
        public TimeSpan LastEncode => TimeSpan.FromMilliseconds(17);
        public TimeSpan LastDecode => TimeSpan.FromMilliseconds(8);
        public PictureMask Segment(BgraPicture picture, PictureKey key, SegmentPrompt prompt) =>
            Rectangle(picture.Width, picture.Height, 30, 30);
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeInpainter : IInpainter
    {
        public bool Disposed { get; private set; }
        public ExecutionDevice Device => ExecutionDevice.Gpu;
        public TimeSpan LastFill => TimeSpan.FromMilliseconds(5);
        public void Fill(BgraPicture picture, PictureMask hole, Span<byte> output) => output.Fill(42);
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void SharingAModelSerializesItsUseAndRejectsRequestsAfterDisposal()
    {
        Assert.Throws<ArgumentNullException>(() => new SharedSegmenter(null!));
        Assert.Throws<ArgumentNullException>(() => new SharedInpainter(null!));
        var pixels = new byte[100 * 100 * 4];
        var mask = Rectangle(100, 100, 30, 30);
        var segmenter = new FakeSegmenter();
        var shared = new SharedSegmenter(segmenter);
        Assert.Equal(ExecutionDevice.Cpu, shared.Device);
        Assert.Equal(TimeSpan.FromMilliseconds(17), shared.LastEncode);
        Assert.Equal(TimeSpan.FromMilliseconds(8), shared.LastDecode);
        Assert.Equal(mask.Area, shared.Segment(new BgraPicture(pixels, 100, 100, 400),
            new PictureKey(1, 100), SegmentPrompt.At(15, 15)).Area);
        shared.Dispose();
        shared.Dispose();
        Assert.True(segmenter.Disposed);
        Assert.Throws<ObjectDisposedException>(() =>
            shared.Segment(new BgraPicture(pixels, 100, 100, 400),
                new PictureKey(1, 100), SegmentPrompt.At(15, 15)));

        var inpainter = new FakeInpainter();
        var fill = new SharedInpainter(inpainter);
        Assert.Equal(ExecutionDevice.Gpu, fill.Device);
        Assert.Equal(TimeSpan.FromMilliseconds(5), fill.LastFill);
        var output = new byte[pixels.Length];
        fill.Fill(new BgraPicture(pixels, 100, 100, 400), mask, output);
        Assert.All(output, value => Assert.Equal(42, value));
        fill.Dispose();
        fill.Dispose();
        Assert.True(inpainter.Disposed);
        Assert.Throws<ObjectDisposedException>(() =>
            fill.Fill(new BgraPicture(pixels, 100, 100, 400), mask, new byte[pixels.Length]));
    }
}
