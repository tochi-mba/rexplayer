using System.Security.Cryptography;
using System.Text;
using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Player;
using Rex.Media.Subtitles;

namespace Rex.Media.Tests.AppCore.Captions;

public sealed class CaptionChoreographyFrameTests(ITestOutputHelper output)
{
    private static readonly Area Area = new(0, 0, 1080, 1920);
    private static readonly SpeechFrame Plain = new(0.5f, 0.5f, true, 3, false, 0);
    private static readonly string[] Said = ["why", "do", "crazy", "creators", "talk", "like", "this", "every", "single", "time", "big!", "ok"];

    private static CaptionStyle Style(CaptionLayout layout, int variant) => CaptionStyle.Default with
    {
        Layout = layout,
        Cursor = variant == 1,
        Highlight = variant == 1,
        Dissolve = variant == 2 ? 1.5 : 0,
        Jitter = variant == 2 ? 0.05 : 0,
        PlateAlways = variant == 1,
    };

    /// <summary>Two seconds of words arriving, a cluster closing, and sixty frames a second drawn.</summary>
    private static IEnumerable<IReadOnlyList<CaptionWord>> Play(CaptionChoreography choreography)
    {
        var said = 0;
        for (var frame = 0; frame < 240; frame++)
        {
            var seconds = frame / 60.0;
            if (frame % 9 == 0 && said < Said.Length)
            {
                var stress = said == 3 ? Stress.Shout : said == 10 ? Stress.Peak : Stress.Plain;
                choreography.Reveal(new SubtitleWord(Said[said], new SubtitleRun(""), null, 0), Plain, stress, seconds);
                said++;
            }

            if (frame == 150)
            {
                choreography.Clear(seconds);
            }

            yield return choreography.Scene(seconds, Area, 60, 3);
        }
    }

    [Fact]
    public void EveryLayoutDrawsTheSameFramesItAlwaysHas()
    {
        // The frames hold sines and cosines, whose last digits differ between Windows' maths library and
        // others'; the hash was recorded on Windows, where the player runs.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The recorded frames come from Windows' maths library.");
        var text = new StringBuilder();
        foreach (var layout in Enum.GetValues<CaptionLayout>())
        {
            for (var variant = 0; variant < 3; variant++)
            {
                foreach (var scene in Play(new CaptionChoreography(Style(layout, variant))))
                {
                    text.AppendJoin('|', scene).Append('\n');
                }
            }
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        output.WriteLine(hash);
        // Recorded before the scene stopped allocating its working lists: the frames must not change.
        Assert.Equal("92714BC935921788DD0E1CAAB08A3848B1126CD425253562C2FC01B812966DFE", hash);
    }

    [Theory]
    [InlineData(CaptionLayout.Scatter)]
    [InlineData(CaptionLayout.Stack)]
    [InlineData(CaptionLayout.Confetti)]
    [InlineData(CaptionLayout.Ticker)]
    [InlineData(CaptionLayout.Stairs)]
    public void AFrameAllocatesOnlyTheWordsItDraws(CaptionLayout layout)
    {
        var choreography = new CaptionChoreography(Style(layout, 0));
        for (var i = 0; i < 8; i++)
        {
            choreography.Reveal(new SubtitleWord(Said[i], new SubtitleRun(""), null, 0), Plain, Stress.Plain, i * 0.1);
        }

        for (var warm = 0; warm < 5; warm++)
        {
            choreography.Scene(1 + (warm / 60.0), Area, 60, 3);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var words = 0;
        for (var frame = 0; frame < 60; frame++)
        {
            words = choreography.Scene(1.1 + (frame / 60.0), Area, 60, 3).Count;
        }

        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 60;
        output.WriteLine($"{layout}: {perFrame} bytes a frame for {words} words");
        Assert.Equal(8, words);
        // The eight words returned and their list, nothing else. Before, the working lists, LINQ and slot
        // records came to 1,832 to 3,112 bytes a frame by layout.
        Assert.True(perFrame <= 1400, $"{perFrame} bytes a frame");
    }
}
