using System.Runtime.Versioning;
using Rex.Media.Primitives;
using Rex.Media.Video;
using Rex.Media.Video.D3D11;
using Rex.Media.Vision;
using static Rex.Media.TestKit.SubjectScenes;

namespace Rex.Media.Windows.Tests;

/// <summary>
/// Subject Lock with the optional models (ADR-0019), played by fakes that answer when the test lets
/// them: a paused click is outlined by the segmenter off the presenting thread, a paused removal
/// is filled by the inpainter, and every refusal, failure, resume or new click leaves the
/// hand-written methods in charge. The real models run in Rex.Media.Vision.Windows.Tests.
/// </summary>
[SupportedOSPlatform("windows8.0")]
public sealed class SubjectModelTests
{
    private const int Width = 640;
    private const int Height = 360;
    private static readonly Thing Red = new(280, 130, 60, 84, 50, 60, 200);

    /// <summary>A segmenter that answers with the red object's outline, once the test releases it.</summary>
    private sealed class FakeSegmenter(double score = 0.9, bool fails = false) : IPromptSegmenter
    {
        public ManualResetEventSlim Release { get; } = new(true);

        public int Calls { get; private set; }

        public SegmentPrompt? LastPrompt { get; private set; }

        public ExecutionDevice Device => ExecutionDevice.Cpu;

        public TimeSpan LastEncode => TimeSpan.Zero;

        public TimeSpan LastDecode => TimeSpan.Zero;

        public PictureMask Segment(BgraPicture picture, PictureKey key, SegmentPrompt prompt)
        {
            Calls++;
            LastPrompt = prompt;
            Assert.True(Release.Wait(TimeSpan.FromSeconds(20)));
            if (fails)
            {
                throw new InvalidOperationException("The model could not run.");
            }

            var values = new byte[picture.Width * picture.Height];
            for (var y = Red.Top; y < Red.Top + Red.Height; y++)
            {
                values.AsSpan(y * picture.Width + Red.Left, Red.Width).Fill(255);
            }

            return new PictureMask(values, picture.Width, picture.Height, score);
        }

        public void Dispose()
        {
        }
    }

    /// <summary>An inpainter that paints the hole one grey, once the test releases it.</summary>
    private sealed class FakeInpainter : IInpainter
    {
        public ManualResetEventSlim Release { get; } = new(true);

        public int Calls { get; private set; }

        public ExecutionDevice Device => ExecutionDevice.Cpu;

        public TimeSpan LastFill => TimeSpan.Zero;

        public void Fill(BgraPicture picture, PictureMask hole, Span<byte> output)
        {
            Calls++;
            Assert.True(Release.Wait(TimeSpan.FromSeconds(20)));
            picture.Pixels.CopyTo(output);
            for (var i = 0; i < hole.Values.Length; i++)
            {
                if (hole.Values[i] != 0)
                {
                    output.Slice(i * 4, 3).Fill(0x24);
                }
            }
        }

        public void Dispose()
        {
        }
    }

    private static VideoFrame Picture(double seconds = 1)
    {
        var frame = Draw(Width, Height, true, Red);
        frame.Pts = MediaTime.FromSeconds(seconds);
        return frame;
    }

    private static void AwaitAnswer(SubjectEditSession session) =>
        Assert.True(SpinWait.SpinUntil(() => session.HasModelAnswer, TimeSpan.FromSeconds(20)), session.Status);

    [Fact]
    public void AClickOnAPausedPictureIsOutlinedByTheSegmenterWhileThePictureStaysAsItIs()
    {
        using var picture = Picture();
        var segmenter = new FakeSegmenter();
        segmenter.Release.Reset();
        using var session = new SubjectEditSession();
        session.UseModels(segmenter, null, null);

        Assert.True(session.SelectAround(300f / Width, 160f / Height, paused: true));
        Assert.Null(session.Process(picture));
        Assert.Equal(SubjectEditSession.FindingStatus, session.Status);
        Assert.True(session.Locked);
        Assert.False(session.Tracking);
        // Redrawing while it works changes nothing and asks nothing more.
        Assert.Null(session.Process(picture, advanceTracking: false));
        Assert.False(session.HasModelAnswer);

        segmenter.Release.Set();
        AwaitAnswer(session);
        Assert.Null(session.Process(picture, advanceTracking: false));
        Assert.True(session.Tracking, session.Status);
        Assert.Equal(MaskVerdict.Accepted, session.LastModelVerdict);
        Assert.Equal(1, segmenter.Calls);
        Assert.Equal((300, 160), (segmenter.LastPrompt!.Value.X, segmenter.LastPrompt.Value.Y));
        Assert.Equal(Red.Left / (float)Width, session.Region.Left, 3);
        Assert.Equal(Red.Width / (float)Width, session.Region.Width, 3);

        // The outline is the removal's shape: the object goes, the wall beside it stays.
        session.SetErase(true);
        using var preview = session.Process(picture, advanceTracking: false);
        Assert.NotNull(preview);
        Assert.NotEqual(picture.Row(0, 170)[310 * 4 + 2], preview.Row(0, 170)[310 * 4 + 2]);
        Assert.Equal(picture.Row(0, 170)[250 * 4 + 2], preview.Row(0, 170)[250 * 4 + 2]);
    }

    [Theory]
    [InlineData(0.3, false)]
    [InlineData(0.9, true)]
    public void AnUnsureOrFailedOutlineLeavesTheClickToTheHandWrittenChoice(double score, bool fails)
    {
        using var picture = Picture();
        var segmenter = new FakeSegmenter(score, fails);
        using var session = new SubjectEditSession();
        session.UseModels(segmenter, null, null);

        session.SelectAround(300f / Width, 160f / Height, paused: true);
        Assert.Null(session.Process(picture));
        AwaitAnswer(session);
        session.Process(picture, advanceTracking: false);

        Assert.True(session.Tracking, session.Status);
        Assert.Equal(fails ? MaskVerdict.Empty : MaskVerdict.NotConfident, session.LastModelVerdict);
        Assert.True(Overlap((session.Region.Left * Width, session.Region.Top * Height, session.Region.Width * Width, session.Region.Height * Height),
            (Red.Left, Red.Top, Red.Width, Red.Height)) > 0.6, session.Region.ToString());
    }

    [Fact]
    public void APlayingPictureNeverWaitsForAModel()
    {
        using var picture = Picture();
        var segmenter = new FakeSegmenter();
        using var session = new SubjectEditSession();
        session.UseModels(segmenter, null, null);

        session.SelectAround(300f / Width, 160f / Height, paused: false);
        session.Process(picture);

        Assert.True(session.Tracking, session.Status);
        Assert.Equal(0, segmenter.Calls);
        Assert.Null(session.LastModelVerdict);
    }

    [Fact]
    public void ResumingOrClickingAgainDropsTheModelsWork()
    {
        var segmenter = new FakeSegmenter();
        segmenter.Release.Reset();
        using var session = new SubjectEditSession();
        session.UseModels(segmenter, null, null);
        using var paused = Picture(1);
        using var next = Picture(1.04);

        // Playback resumed: the next picture is settled at once by the hand-written choice.
        session.SelectAround(300f / Width, 160f / Height, paused: true);
        session.Process(paused);
        Assert.Equal(SubjectEditSession.FindingStatus, session.Status);
        session.Process(next);
        Assert.True(session.Tracking, session.Status);
        Assert.Null(session.LastModelVerdict);

        // A new click forgets the first one's work; its late answer is never used.
        session.SelectAround(300f / Width, 160f / Height, paused: true);
        session.Process(paused);
        session.SelectAround(0.9f, 0.9f, paused: false);
        segmenter.Release.Set();
        Assert.False(SpinWait.SpinUntil(() => session.HasModelAnswer, TimeSpan.FromMilliseconds(300)));

        // Taking the models away also drops their work.
        segmenter.Release.Reset();
        session.SelectAround(300f / Width, 160f / Height, paused: true);
        session.Process(paused);
        session.UseModels(null, null, null);
        segmenter.Release.Set();
        Assert.False(SpinWait.SpinUntil(() => session.HasModelAnswer, TimeSpan.FromMilliseconds(300)));
        session.Process(paused, advanceTracking: false);
        Assert.True(session.Tracking, session.Status);
    }

    [Fact]
    public void APausedRemovalIsFilledByTheInpainterOnceAndKept()
    {
        using var picture = Picture();
        var inpainter = new FakeInpainter();
        inpainter.Release.Reset();
        using var session = new SubjectEditSession();
        session.UseModels(null, inpainter, null);
        session.Select(Red.Left / (float)Width, Red.Top / (float)Height, Red.Width / (float)Width, Red.Height / (float)Height);
        Assert.Null(session.Process(picture));
        Assert.True(session.Tracking, session.Status);
        session.SetErase(true);

        // The hand-written fill shows while the model works.
        using (var first = session.Process(picture, advanceTracking: false))
        {
            Assert.NotNull(first);
            Assert.Equal(RemovalFillSource.ModelWorking, session.FillSource);
            Assert.StartsWith("Filling with the inpainting model", session.Status, StringComparison.Ordinal);
            Assert.NotEqual(0x24, first.Row(0, 170)[310 * 4]);
        }

        inpainter.Release.Set();
        AwaitAnswer(session);
        using (var filled = session.Process(picture, advanceTracking: false))
        {
            Assert.Equal(RemovalFillSource.Model, session.FillSource);
            Assert.StartsWith("Model fill", session.Status, StringComparison.Ordinal);
            Assert.Equal(0x24, filled!.Row(0, 170)[310 * 4]);
        }

        // Redrawn again, the kept fill is shown without asking the model again.
        using (var again = session.Process(picture, advanceTracking: false))
        {
            Assert.Equal(0x24, again!.Row(0, 170)[310 * 4]);
        }

        Assert.Equal(1, inpainter.Calls);

        // A playing picture is never filled by the model.
        using var later = Picture(1.04);
        using var playing = session.Process(later);
        Assert.Equal(RemovalFillSource.Synthesised, session.FillSource);
        Assert.Equal(1, inpainter.Calls);
    }
}
