using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Rex.Media.Primitives;
using Rex.Media.Video;
using Rex.Media.Video.D3D11;
using Rex.Media.Vision.WindowsML;

namespace Rex.Media.Vision.Windows.Tests;

/// <summary>
/// Subject Lock with the real downloaded models (skipped cleanly when they are absent): a paused
/// click outlined by the segmenter and its removal filled by the inpainter, through the session the
/// app uses, on movie frames from <c>REXPLAYER_VISION_FRAMES</c> (a <c>clicks.txt</c> of "file x y"
/// lines). The hand-written and the model previews are saved side by side to judge by eye, and every
/// wait is noted in timings.txt.
/// </summary>
[SupportedOSPlatform("windows8.0")]
public sealed class SubjectLockModelTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(3);

    [Fact]
    public void APausedClickIsOutlinedAndItsRemovalFilledByTheModels()
    {
        VisionFixtures.RequireInstalled(ModelCatalogue.EfficientSamTiny);
        VisionFixtures.RequireInstalled(ModelCatalogue.MiGan);
        if (VisionFixtures.Frames is not { } folder)
        {
            Assert.Skip("Set REXPLAYER_VISION_FRAMES to a folder of snapshots with a clicks.txt.");
            return;
        }

        using var segmenter = new SharedSegmenter(PromptSegmenter.Open(VisionFixtures.Store)!);
        using var inpainter = new SharedInpainter(Inpainter.Open(VisionFixtures.Store)!);
        var index = 0;
        foreach (var (file, x, y) in VisionFixtures.Clicks(folder, "clicks.txt"))
        {
            index++;
            var picture = VisionFixtures.Load(file);
            using var frame = VideoFrame.Rent(PixelFormat.Bgra32, picture.Width, picture.Height);
            for (var row = 0; row < picture.Height; row++)
            {
                picture.Pixels.AsSpan(row * picture.Stride, picture.Stride).CopyTo(frame.Row(0, row));
            }

            frame.Pts = MediaTime.FromSeconds(index);
            using var session = new SubjectEditSession();
            session.UseModels(segmenter, inpainter, new PromptedMaskTracker(segmenter));
            session.SelectAround(x / (float)picture.Width, y / (float)picture.Height, paused: true);

            var watch = Stopwatch.StartNew();
            Assert.Null(session.Process(frame));
            Assert.Equal(SubjectEditSession.FindingStatus, session.Status);
            Assert.True(SpinWait.SpinUntil(() => session.HasModelAnswer, Patience), "The segmenter did not answer.");
            var outlined = watch.Elapsed;
            session.Process(frame, advanceTracking: false);
            var name = $"{Path.GetFileNameWithoutExtension(file)}-{index}";
            VisionFixtures.Note(string.Create(CultureInfo.InvariantCulture,
                $"session {name}: outlined in {outlined.TotalMilliseconds:0} ms, verdict {session.LastModelVerdict}, tracking {session.Tracking}, region {session.Region}"));
            if (!session.Tracking)
            {
                continue;
            }

            session.SetErase(true);
            using (var handWritten = session.Process(frame, advanceTracking: false))
            {
                Assert.Equal(RemovalFillSource.ModelWorking, session.FillSource);
                VisionFixtures.Save(Copy(handWritten!), name + "-removal-hand.png");
            }

            watch.Restart();
            Assert.True(SpinWait.SpinUntil(() => session.HasModelAnswer, Patience), "The inpainter did not answer.");
            var filled = watch.Elapsed;
            using (var model = session.Process(frame, advanceTracking: false))
            {
                Assert.Equal(RemovalFillSource.Model, session.FillSource);
                VisionFixtures.Save(Copy(model!), name + "-removal-model.png");
            }

            VisionFixtures.Note(string.Create(CultureInfo.InvariantCulture,
                $"session {name}: filled in {filled.TotalMilliseconds:0} ms; {session.Status}"));
        }
    }

    private static Picture Copy(VideoFrame frame)
    {
        var pixels = new byte[frame.Width * frame.Height * 4];
        for (var row = 0; row < frame.Height; row++)
        {
            frame.Row(0, row)[..(frame.Width * 4)].CopyTo(pixels.AsSpan(row * frame.Width * 4));
        }

        return new Picture(pixels, frame.Width, frame.Height);
    }
}
