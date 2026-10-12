using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class PromptedMaskTrackerTests
{
    private const int Width = 40;
    private const int Height = 30;

    /// <summary>A segmenter that answers with whatever the test queues and records the prompts.</summary>
    private sealed class ScriptedSegmenter : IPromptSegmenter
    {
        public Queue<PictureMask> Answers { get; } = new();

        public List<SegmentPrompt> Prompts { get; } = [];

        public ExecutionDevice Device => ExecutionDevice.Cpu;

        public TimeSpan LastEncode => TimeSpan.Zero;

        public TimeSpan LastDecode => TimeSpan.Zero;

        public PictureMask Segment(BgraPicture picture, PictureKey key, SegmentPrompt prompt)
        {
            Prompts.Add(prompt);
            return Answers.Dequeue();
        }

        public void Dispose()
        {
        }
    }

    private static PictureMask Square(int left, int top, int side, double score = 0.9, int width = Width, int height = Height)
    {
        var values = new byte[width * height];
        for (var y = top; y < top + side; y++)
        {
            values.AsSpan((y * width) + left, side).Fill(255);
        }

        return new PictureMask(values, width, height, score);
    }

    private static readonly byte[] Pixels = new byte[Width * Height * 4];

    private static BgraPicture Picture => new(Pixels, Width, Height, Width * 4);

    [Fact]
    public void TheSubjectIsFollowedWithABoxAroundWhereItWasAtItsLastSize()
    {
        var segmenter = new ScriptedSegmenter();
        var tracker = new PromptedMaskTracker(segmenter);
        tracker.Start(Square(10, 10, 8));
        segmenter.Answers.Enqueue(Square(12, 11, 8));

        var result = tracker.Track(Picture, new PictureKey(1, 1));

        Assert.Equal(TrackOutcome.Tracked, result.Outcome);
        Assert.Same(result.Mask, tracker.Memory);
        Assert.Equal(new PixelBox(8, 8, 12, 12), segmenter.Prompts[0].Box);
        Assert.Equal(64, segmenter.Prompts[0].ExpectedArea);
        Assert.Null(segmenter.Prompts[0].X);
    }

    [Fact]
    public void DoubtfulAnswersAreRejectedAndTheMemoryKept()
    {
        var segmenter = new ScriptedSegmenter();
        var tracker = new PromptedMaskTracker(segmenter);
        var start = Square(10, 10, 8);
        tracker.Start(start);
        segmenter.Answers.Enqueue(new PictureMask(new byte[Width * Height], Width, Height, 1));
        segmenter.Answers.Enqueue(Square(10, 10, 8, score: 0.3));
        segmenter.Answers.Enqueue(Square(5, 5, 20));
        segmenter.Answers.Enqueue(Square(10, 10, 2));

        Assert.Equal("The model found no object there.", tracker.Track(Picture, default).Reason);
        Assert.Equal("The model was not confident about the outline.", tracker.Track(Picture, default).Reason);
        Assert.Equal("The outline changed size too suddenly to be the same object.", tracker.Track(Picture, default).Reason);
        Assert.Equal(TrackOutcome.Lost, tracker.Track(Picture, default).Outcome);
        Assert.Same(start, tracker.Memory);
    }

    [Fact]
    public void AReturningSubjectMayHaveChangedSizeMore()
    {
        var segmenter = new ScriptedSegmenter();
        var tracker = new PromptedMaskTracker(segmenter);
        tracker.Start(Square(2, 2, 6));
        segmenter.Answers.Enqueue(Square(20, 10, 10));

        var result = tracker.Reacquire(Picture, default, new PixelBox(20, 10, 10, 10));

        Assert.Equal(TrackOutcome.Tracked, result.Outcome);
        Assert.Equal(new PixelBox(19, 9, 12, 12), segmenter.Prompts[0].Box);
        Assert.Equal(36, segmenter.Prompts[0].ExpectedArea);
    }

    [Fact]
    public void NothingIsSearchedWithoutASubjectOrInTheWrongPicture()
    {
        var segmenter = new ScriptedSegmenter();
        var tracker = new PromptedMaskTracker(segmenter);

        Assert.Equal("Nothing is being followed.", tracker.Track(Picture, default).Reason);
        tracker.Start(Square(2, 2, 6, width: 20, height: 20));
        Assert.Equal("The picture size changed.", tracker.Track(Picture, default).Reason);
        tracker.Start(Square(2, 2, 6));
        Assert.Equal("The search area is outside the picture.", tracker.Reacquire(Picture, default, new PixelBox(500, 500, 10, 10)).Reason);
        tracker.Reset();
        Assert.Null(tracker.Memory);
        Assert.Empty(segmenter.Prompts);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => new PromptedMaskTracker(null!));
        var tracker = new PromptedMaskTracker(new ScriptedSegmenter());
        Assert.Throws<ArgumentNullException>(() => tracker.Start(null!));
        Assert.Throws<ArgumentException>(() => tracker.Start(new PictureMask(new byte[4], 2, 2, 1)));
    }
}
