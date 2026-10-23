using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>
/// A local podcast's spoken-word transport: resume, large skip targets, speed, sleep timer
/// and actual embedded chapter marks. RSS and download controls are intentionally absent.
/// </summary>
public sealed partial class MainWindow
{
    private async Task ShowPodcastControlsAsync(LibraryEntry episode)
    {
        var panel = new StackPanel { Spacing = 12, MaxWidth = 600 };
        panel.Children.Add(new TextBlock
        {
            Text = episode.Title,
            FontSize = 20,
            TextWrapping = TextWrapping.Wrap,
        });
        var resumeAt = _player.LeftAt(episode.Path);
        var detail = new TextBlock
        {
            Text = string.Join(" · ", new[]
            {
                "Local file",
                episode.Duration is { } duration ? TimeText.Format(duration, duration) : "Duration unknown",
                resumeAt is { } left ? "Stopped at " + TimeText.Format(left, episode.Duration) : null,
            }.Where(text => text is not null)),
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(detail, "PodcastControlsDetail");
        panel.Children.Add(detail);

        var transport = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var play = new Button { Content = resumeAt is null ? "Play" : "Resume" };
        var back = new Button { Content = "Back 15 s" };
        var forward = new Button { Content = "Forward 30 s" };
        AutomationProperties.SetAutomationId(play, "PodcastControlsPlay");
        AutomationProperties.SetAutomationId(back, "PodcastControlsBack");
        AutomationProperties.SetAutomationId(forward, "PodcastControlsForward");
        transport.Children.Add(play);
        transport.Children.Add(back);
        transport.Children.Add(forward);
        panel.Children.Add(transport);

        var status = new TextBlock
        {
            Text = "Speed affects playback until changed again. This view has no feed or download controls.",
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(status, "PodcastControlsStatus");
        panel.Children.Add(status);
        play.Click += (_, _) => Play([episode], 0, resume: true);

        void Skip(TimeSpan by)
        {
            if (!_player.CanSeek || !LibraryViews.SameLocation(_player.Item?.Location, episode.Path))
            {
                status.Text = "Play this local episode first to skip within it.";
                return;
            }

            var position = _player.Position;
            var target = position + by;
            var limit = _player.Duration;
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            if (limit > TimeSpan.Zero && target > limit) target = limit;
            _player.Seek(target);
        }

        back.Click += (_, _) => Skip(TimeSpan.FromSeconds(-15));
        forward.Click += (_, _) => Skip(TimeSpan.FromSeconds(30));

        var speed = new Slider
        {
            Minimum = 0.5,
            Maximum = 2.5,
            StepFrequency = 0.25,
            Value = Math.Clamp(_player.Speed, 0.5, 2.5),
        };
        AutomationProperties.SetAutomationId(speed, "PodcastControlsSpeed");
        AutomationProperties.SetName(speed, "Podcast playback speed");
        speed.ValueChanged += (_, e) => _player.SetSpeed(e.NewValue);
        panel.Children.Add(new TextBlock { Text = "Playback speed (0.5–2.5×)" });
        panel.Children.Add(speed);

        var timer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var sleep30 = new Button { Content = "Sleep in 30 min" };
        var sleepEnd = new Button { Content = "Sleep at episode end" };
        AutomationProperties.SetAutomationId(sleep30, "PodcastControlsSleep30");
        AutomationProperties.SetAutomationId(sleepEnd, "PodcastControlsSleepEnd");
        sleep30.Click += (_, _) => _player.SetSleepTimer(TimeSpan.FromMinutes(30));
        sleepEnd.Click += (_, _) => _player.SetSleepAtEndOfItem();
        timer.Children.Add(sleep30);
        timer.Children.Add(sleepEnd);
        panel.Children.Add(timer);

        if (episode.Chapters is { Count: > 0 } chapters)
        {
            panel.Children.Add(new TextBlock { Text = "Embedded episode chapters" });
            var list = new StackPanel { Spacing = 6 };
            foreach (var (index, chapter) in chapters.Take(50).Index())
            {
                var at = chapter.Start;
                var button = new Button
                {
                    Content = $"{TimeText.Format(at, episode.Duration)} · {chapter.Title}",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                AutomationProperties.SetAutomationId(button, "PodcastChapter-" + index);
                button.Click += (_, _) =>
                {
                    if (LibraryViews.SameLocation(_player.Item?.Location, episode.Path) && _player.CanSeek)
                    {
                        _player.Seek(at);
                    }
                    else if (!episode.Downloading && episode.Problem is null && File.Exists(episode.Path))
                    {
                        _player.PlayFromLibrary([episode], 0, at);
                    }
                };
                list.Children.Add(button);
            }

            panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 180 });
            if (chapters.Count > 50)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "First 50 chapter markers shown. All chapters remain in the media file.",
                    TextWrapping = TextWrapping.Wrap,
                });
            }
        }

        var dialog = new ContentDialog
        {
            Title = "Podcast playback",
            Content = panel,
            CloseButtonText = "Done",
            XamlRoot = Content.XamlRoot,
        };
        await ShowDialogAsync(dialog);
    }
}
