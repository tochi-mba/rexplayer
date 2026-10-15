using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Shell;
using Rex.Media.Engine;
using Rex.Media.Interop.Shell;
using Rex.Media.Interop.Windowing;
using Rex.Media.TestKit;
using Windows.Media.Control;

namespace Rex.Media.Windows.Tests;

/// <summary>
/// The shell adapters against the real Windows shell, on a window that is never shown: thumbnail
/// buttons, the badge and progress, Windows' media controls (read back, and driven from outside the
/// way Windows' own media flyout drives them) and the jump list under a test id of its own, so the
/// user's is never touched. Each skips where the session has no shell or media service.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class ShellIntegrationTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    /// <summary>Runs <paramref name="work"/> on a single-threaded apartment, as the app's shell thread is.</summary>
    private static void OnShellThread(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static Taskbar TaskbarOrSkip()
    {
        try
        {
            return Taskbar.Create();
        }
        catch (COMException ex)
        {
            Assert.Skip("This session has no taskbar: " + ex.Message);
            throw;
        }
    }

    private static MediaTransportControls ControlsOrSkip(nint window)
    {
        try
        {
            return MediaTransportControls.ForWindow(window);
        }
        catch (COMException ex)
        {
            Assert.Skip("This session has no media controls: " + ex.Message);
            throw;
        }
    }

    [Fact]
    [Capability("WIN-06")]
    public void EveryGlyphBecomesAnIconAtEveryScaleFromOneToTwoTimes()
    {
        foreach (var size in new[] { 16, 20, 24, 28, 32 })
        {
            foreach (var glyph in Enum.GetValues<TaskbarGlyph>())
            {
                foreach (var light in new[] { false, true })
                {
                    using var icon = TaskbarIcon.FromPixels(TaskbarGlyphs.Render(glyph, size, TaskbarGlyphs.InkFor(light)), size);
                    Assert.NotEqual(0, icon.Handle);
                }
            }
        }
    }

    [Fact]
    [Capability("WIN-06")]
    public void ThumbnailButtonsBadgeAndProgressTakeEveryViewThePolicyMakes()
    {
        using var window = VideoWindow.OpenHidden("rexplayer shell test", 320, 240);
        OnShellThread(() =>
        {
            using var taskbar = TaskbarOrSkip();
            var size = TaskbarMessages.IconSize(window.Handle);
            Assert.InRange(size, 16, 64);
            var icons = Enum.GetValues<TaskbarGlyph>().ToDictionary(glyph => glyph, glyph => TaskbarIcon.FromPixels(TaskbarGlyphs.Render(glyph, size, TaskbarGlyphs.InkFor(TaskbarMessages.TaskbarIsLight())), size));
            try
            {
                ThumbnailButton[] Buttons(TransportView view) =>
                [
                    new(1, icons[TaskbarGlyph.Previous], view.Previous.Tooltip, view.Previous.IsVisible, view.Previous.IsEnabled),
                    new(2, icons[view.ShowsPause ? TaskbarGlyph.Pause : TaskbarGlyph.Play], view.PlayPause.Tooltip, view.PlayPause.IsVisible, view.PlayPause.IsEnabled),
                    new(3, icons[TaskbarGlyph.Next], view.Next.Tooltip, view.Next.IsVisible, view.Next.IsEnabled),
                ];

                // Added once, hidden while there is nothing; every later view is an update.
                taskbar.AddButtons(window.Handle, Buttons(TransportAvailability.Nothing));
                foreach (var state in Enum.GetValues<SessionState>())
                {
                    var view = TransportAvailability.Decide(new TransportInput(state, state is SessionState.Playing or SessionState.Opening, true, TransportMedia.Audio, "Now", state == SessionState.Paused ? null : "Before", "After " + new string('x', 400), null, false));
                    taskbar.UpdateButtons(window.Handle, Buttons(view));
                    var badge = view.Badge switch
                    {
                        TransportBadge.Paused => icons[TaskbarGlyph.PausedBadge],
                        TransportBadge.Failed => icons[TaskbarGlyph.FailedBadge],
                        _ => null,
                    };
                    taskbar.SetOverlay(window.Handle, badge, badge is null ? "" : "Paused");
                }

                taskbar.UpdateButtons(window.Handle, Buttons(TransportAvailability.Nothing));
                taskbar.SetProgress(window.Handle, TaskbarProgressKind.Normal, 40);
                taskbar.SetProgress(window.Handle, TaskbarProgressKind.Paused, 140);
                taskbar.SetProgress(window.Handle, TaskbarProgressKind.None, 0);
                Assert.Throws<ArgumentException>(() => taskbar.UpdateButtons(window.Handle, []));
            }
            finally
            {
                foreach (var icon in icons.Values)
                {
                    icon.Dispose();
                }
            }
        });
    }

    [Fact]
    [Capability("WIN-05")]
    public void MediaControlsHoldExactlyWhatThePolicySaysAndEveryCard()
    {
        using var window = VideoWindow.OpenHidden("rexplayer shell test", 320, 240);
        OnShellThread(() =>
        {
            using var controls = ControlsOrSkip(window.Handle);
            controls.IsEnabled = true;
            Assert.True(controls.IsEnabled);
            foreach (var (earlier, following) in new[] { ((string?)null, (string?)null), ("Before", null), (null, "After"), ("Before", "After") })
            {
                var view = TransportAvailability.Decide(new TransportInput(SessionState.Playing, true, true, TransportMedia.Audio, "Now", earlier, following, null, false));
                controls.SetButtons(view.PlayPause.IsEnabled, view.PlayPause.IsEnabled, view.Stop.IsEnabled, view.Previous.IsEnabled, view.Next.IsEnabled);
                controls.Status = (int)view.Status;
                Assert.Equal(earlier is not null, controls.IsPreviousEnabled);
                Assert.Equal(following is not null, controls.IsNextEnabled);
                Assert.True(controls.IsPlayEnabled && controls.IsPauseEnabled && controls.IsStopEnabled);
                Assert.Equal((int)TransportStatus.Playing, controls.Status);
            }

            var png = TinyPng();
            controls.SetDisplay((int)MediaCardKind.Music, "Lonely At The Top", "Asake", "Asake", "Work of Art", 3, png);
            controls.SetDisplay((int)MediaCardKind.Video, "Episode", "The Show, season 1, episode 2", "", "", 0, []);
            controls.SetDisplay((int)MediaCardKind.Image, "Beach", "", "", "", 0, png);
            controls.SetTimeline(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(3), seekable: true);

            // A live stream: no length, nothing to seek in.
            controls.SetTimeline(TimeSpan.FromSeconds(5), TimeSpan.Zero, seekable: false);
            controls.ClearDisplay();
            controls.Status = (int)TransportStatus.Closed;
            controls.IsEnabled = false;
            Assert.False(controls.IsEnabled);
        });
    }

    [Fact]
    [Capability("WIN-05")]
    [Capability("PB-27")]
    public async Task WindowsSeesTheCardAndItsButtonsReachThePlayer()
    {
        using var window = VideoWindow.OpenHidden("rexplayer shell test", 320, 240);
        var title = "rexplayer test " + Guid.NewGuid().ToString("N")[..8];
        var pressed = new List<MediaControlButton>();
        MediaTransportControls? controls = null;
        OnShellThread(() =>
        {
            controls = ControlsOrSkip(window.Handle);
            controls.ButtonPressed += (_, button) =>
            {
                lock (pressed)
                {
                    pressed.Add(button);
                }
            };
            controls.IsEnabled = true;
            controls.SetButtons(play: true, pause: true, stop: true, previous: false, next: true);
            controls.Status = (int)TransportStatus.Playing;
            controls.SetDisplay((int)MediaCardKind.Music, title, "Asake", "Asake", "Work of Art", 1, TinyPng());
            controls.SetTimeline(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(3), seekable: true);
        });

        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            GlobalSystemMediaTransportControlsSession? ours = null;
            var deadline = DateTime.UtcNow + Patience;
            while (ours is null && DateTime.UtcNow < deadline)
            {
                foreach (var session in manager.GetSessions())
                {
                    if ((await session.TryGetMediaPropertiesAsync()).Title == title)
                    {
                        ours = session;
                    }
                }

                if (ours is null)
                {
                    await Task.Delay(200, TestContext.Current.CancellationToken);
                }
            }

            if (ours is null)
            {
                Assert.Skip("Windows' media service did not list this test's session.");
            }

            // Windows publishes artwork after the title has made the session
            // discoverable. Poll the actual media properties, not just the session
            // list, before asserting that the card contains its thumbnail.
            var properties = await ours.TryGetMediaPropertiesAsync();
            var artworkDeadline = DateTime.UtcNow + Patience;
            while (properties.Thumbnail is null && DateTime.UtcNow < artworkDeadline)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
                properties = await ours.TryGetMediaPropertiesAsync();
            }

            Assert.Equal("Asake", properties.Artist);
            Assert.Equal("Work of Art", properties.AlbumTitle);
            Assert.NotNull(properties.Thumbnail);
            var info = ours.GetPlaybackInfo();
            Assert.Equal(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing, info.PlaybackStatus);
            Assert.True(info.Controls.IsNextEnabled);
            Assert.False(info.Controls.IsPreviousEnabled);
            Assert.Equal(TimeSpan.FromMinutes(3), ours.GetTimelineProperties().EndTime);

            Assert.True(await ours.TrySkipNextAsync());
            Assert.True(await ours.TryPauseAsync());
            var arrived = DateTime.UtcNow + Patience;
            while (DateTime.UtcNow < arrived)
            {
                lock (pressed)
                {
                    if (pressed.Count >= 2)
                    {
                        break;
                    }
                }

                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            lock (pressed)
            {
                // Windows forwards independently posted system media commands asynchronously:
                // callback delivery can reverse their order even when both requests succeed.
                // Verify both commands arrive exactly once, with no unexpected buttons.
                Assert.Equal(2, pressed.Count);
                Assert.Contains(MediaControlButton.Next, pressed);
                Assert.Contains(MediaControlButton.Pause, pressed);
            }
        }
        finally
        {
            OnShellThread(() => controls?.Dispose());
        }
    }

    [Fact]
    [Capability("WIN-07")]
    public void TheJumpListIsWrittenFromThePlanAndLeftAsItWas()
    {
        const string testId = "REX.rexplayer.Tests";
        var program = Environment.ProcessPath!;
        var recent = new[] { Path.Combine(AppContext.BaseDirectory, Path.GetFileName(program)), @"C:\nowhere\gone.mp3", "https://example.com/live.m3u8" };
        OnShellThread(() =>
        {
            int? slots = null;
            try
            {
                JumpList.Publish(testId, program, "Recently played", (room, removed) =>
                {
                    slots = room;
                    Assert.Empty(removed);
                    var plan = JumpListPlan.Plan(recent, keepHistory: true, removed, room, File.Exists);
                    return ([.. plan.Recent.Select(entry => new JumpListLine(entry.Title, entry.Arguments))], [.. plan.Tasks.Select(entry => new JumpListLine(entry.Title, entry.Arguments))]);
                });
            }
            catch (COMException ex)
            {
                Assert.Skip("This session has no jump lists: " + ex.Message);
            }
            finally
            {
                try
                {
                    JumpList.Delete(testId);
                }
                catch (COMException)
                {
                    // Nothing was written.
                }
            }

            Assert.True(slots > 0);
        });
    }

    /// <summary>A real PNG for the card's thumbnail: a little rexplayer glyph.</summary>
    private static byte[] TinyPng()
    {
        var pixels = TaskbarGlyphs.Render(TaskbarGlyph.Play, 32, TaskbarGlyphs.LightInk);
        using var frame = Rex.Media.Primitives.VideoFrame.Rent(Rex.Media.Primitives.PixelFormat.Bgra32, 32, 32);
        for (var y = 0; y < 32; y++)
        {
            pixels.AsSpan(y * 128, 128).CopyTo(frame.Row(0, y));
        }

        using var output = new MemoryStream();
        Rex.Media.Video.PngWriter.Write(output, frame);
        return output.ToArray();
    }
}
