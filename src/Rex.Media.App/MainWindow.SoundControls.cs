using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;
using Rex.Media.Settings;

namespace Rex.Media.App;

/// <summary>
/// Compact controls for local short sounds. Repeat-one is scoped to this panel, so it never
/// leaks into the next song, audiobook or podcast after the panel closes.
/// </summary>
public sealed partial class MainWindow
{
    private async Task ShowSoundControlsAsync(LibraryEntry sound)
    {
        var originalRepeat = _player.Playlist.Repeat;
        var panel = new StackPanel { Spacing = 12, MinWidth = 330 };
        var name = new TextBlock { Text = sound.Title, FontSize = 20, TextWrapping = TextWrapping.Wrap };
        var duration = new TextBlock
        {
            Text = sound.Duration is { } length && length >= TimeSpan.Zero
                ? "Duration " + TimeText.Format(length, length) : "Duration unknown",
        };
        AutomationProperties.SetAutomationId(duration, "SoundDuration");
        panel.Children.Add(name);
        panel.Children.Add(duration);

        var replay = new Button { Content = "Replay sound" };
        AutomationProperties.SetAutomationId(replay, "SoundReplay");
        replay.Click += (_, _) => Play([sound], 0);
        panel.Children.Add(replay);

        var repeat = new CheckBox { Content = "Repeat this sound while controls are open" };
        AutomationProperties.SetAutomationId(repeat, "SoundRepeat");
        repeat.Checked += (_, _) =>
        {
            if (!LibraryViews.SameLocation(_player.Item?.Location, sound.Path))
            {
                Play([sound], 0);
            }

            _player.Playlist.Repeat = RepeatMode.One;
        };
        repeat.Unchecked += (_, _) => _player.Playlist.Repeat = originalRepeat;
        panel.Children.Add(repeat);

        var volumeLabel = new TextBlock { Text = "Playback volume" };
        var volume = new Slider
        {
            Minimum = 0,
            Maximum = _settings.MaxVolumePercent,
            StepFrequency = 1,
            Value = _player.Volume * 100,
        };
        AutomationProperties.SetAutomationId(volume, "SoundVolume");
        AutomationProperties.SetName(volume, "Playback volume percent");
        volume.ValueChanged += (_, args) => _player.SetVolume(args.NewValue / 100);
        panel.Children.Add(volumeLabel);
        panel.Children.Add(volume);

        var dialog = new ContentDialog
        {
            Title = "Quick sound",
            Content = panel,
            CloseButtonText = "Done",
            XamlRoot = Content.XamlRoot,
        };
        try
        {
            await ShowDialogAsync(dialog);
        }
        finally
        {
            if (repeat.IsChecked == true)
            {
                _player.Playlist.Repeat = originalRepeat;
            }
        }
    }
}
