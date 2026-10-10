using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.Primitives;

namespace Rex.Media.App;

/// <summary>Picture style menus and independent, live-preview preset controls.</summary>
public sealed partial class MainWindow
{
    private readonly List<(RadioMenuFlyoutItem Item, VideoLook Look)> _videoLookItems = [];

    /// <summary>One-tap, accessible GPU picture looks from the Video and picture context menus.</summary>
    private void AddPictureLooks(IList<MenuFlyoutItemBase> items, string prefix)
    {
        var looks = new MenuFlyoutSubItem { Text = "Picture look" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(looks, prefix + "Menu");
        foreach (var look in Enum.GetValues<VideoLook>())
        {
            var name = VideoLooks.Name(look);
            var item = new RadioMenuFlyoutItem
            {
                Text = name,
                GroupName = prefix + "PictureLook",
                IsChecked = look == _settings.VideoLook,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, prefix + look);
            item.Click += (_, _) => ChooseVideoLook(look);
            _videoLookItems.Add((item, look));
            looks.Items.Add(item);
        }

        var customize = new MenuFlyoutItem { Text = "Customize current look..." };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(customize, prefix + "Customize");
        customize.Click += (_, _) => _ = ShowVideoStyleSettingsAsync(effect: false);
        looks.Items.Add(new MenuFlyoutSeparator());
        looks.Items.Add(customize);
        items.Add(new MenuFlyoutSeparator());
        items.Add(looks);
    }

    private readonly List<(RadioMenuFlyoutItem Item, VideoEffect Effect)> _videoEffectItems = [];

    /// <summary>Grouped real-time picture effects, independently selectable from colour looks.</summary>
    private void AddPictureEffects(IList<MenuFlyoutItemBase> items, string prefix)
    {
        var sub = new MenuFlyoutSubItem { Text = "Picture effects" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(sub, prefix + "Menu");
        var groups = new Dictionary<string, MenuFlyoutSubItem>(StringComparer.Ordinal);
        foreach (var effect in Enum.GetValues<VideoEffect>())
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = VideoEffects.Name(effect),
                GroupName = prefix + "PictureEffects",
                IsChecked = effect == _settings.VideoEffect,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, prefix + effect);
            item.Click += (_, _) => ChooseVideoEffect(effect);
            _videoEffectItems.Add((item, effect));
            var category = VideoEffects.Category(effect);
            if (category == "Essentials")
            {
                sub.Items.Add(item);
            }
            else
            {
                if (!groups.TryGetValue(category, out var group))
                {
                    group = new MenuFlyoutSubItem { Text = category };
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(group, prefix + "Group-" + category.Replace(" ", ""));
                    groups.Add(category, group);
                    sub.Items.Add(group);
                }

                group.Items.Add(item);
            }
        }

        var customize = new MenuFlyoutItem { Text = "Customize current effect..." };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(customize, prefix + "Customize");
        customize.Click += (_, _) => _ = ShowVideoStyleSettingsAsync(effect: true);
        sub.Items.Add(new MenuFlyoutSeparator());
        sub.Items.Add(customize);
        items.Add(sub);
    }

    private void ApplyVideoEffect(VideoEffect effect, int strength)
    {
        foreach (var (item, chosen) in _videoEffectItems)
        {
            item.IsChecked = chosen == effect;
        }

        var redraw = !_player.IsPlaying;
        OnPresenterThread(presenter =>
        {
            var options = VideoStyleOptions.ForEffect(effect);
            var values = _settings.VideoStyleValues;
            var intensity = values.ContainsKey(options[0].Key) ? VideoStyleOptions.Read(values, options[0]) : strength;
            presenter.SetEffect(effect, intensity, VideoStyleOptions.Read(values, options[1]));
            if (redraw)
            {
                presenter.Redraw();
            }
        });
    }

    private void ChooseVideoEffect(VideoEffect effect)
    {
        _settings = _settings with { VideoEffect = effect };
        RememberLater();
        ApplyVideoEffect(effect, _settings.VideoEffectStrength);
        Say("Picture effects: " + VideoEffects.Name(effect));
    }

    /// <summary>
    /// Per-look controls preview immediately, but Cancel returns the renderer and settings to
    /// the prior values. Reset removes only this style's keys, never another style's presets.
    /// </summary>
    private async Task ShowVideoStyleSettingsAsync(bool effect)
    {
        var options = effect ? VideoStyleOptions.ForEffect(_settings.VideoEffect) : VideoStyleOptions.ForLook(_settings.VideoLook);
        var title = effect ? VideoEffects.Name(_settings.VideoEffect) : VideoLooks.Name(_settings.VideoLook);
        var before = _settings.VideoStyleValues;
        var content = new StackPanel { Spacing = 12, MinWidth = 300 };
        content.Children.Add(new TextBlock
        {
            Text = "Changes preview live. Each preset remembers its own settings. Cancel restores your previous values.",
            TextWrapping = TextWrapping.Wrap,
        });
        var sliders = new List<(VideoStyleOption Option, NumberBox Control)>();
        foreach (var option in options)
        {
            var box = Number(option.Label, VideoStyleOptions.Read(before, option), option.Min, option.Max);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, "VideoStyle-" + option.Key);
            box.ValueChanged += (_, _) =>
            {
                if (!double.IsFinite(box.Value))
                {
                    return;
                }

                _settings = _settings with
                {
                    VideoStyleValues = VideoStyleOptions.With(_settings.VideoStyleValues, option, (int)box.Value),
                };
                if (effect)
                {
                    ApplyVideoEffect(_settings.VideoEffect, _settings.VideoEffectStrength);
                }
                else
                {
                    ApplyVideoLook(_settings.VideoLook);
                }
            };
            sliders.Add((option, box));
            content.Children.Add(box);
        }

        var reset = new Button { Content = "Reset this preset to defaults" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(reset, "VideoStyleReset");
        reset.Click += (_, _) =>
        {
            _settings = _settings with { VideoStyleValues = VideoStyleOptions.Reset(_settings.VideoStyleValues, options) };
            foreach (var (option, box) in sliders)
            {
                box.Value = option.Default;
            }
        };
        content.Children.Add(reset);

        if (!await Ask(title + " settings", content, "Save"))
        {
            _settings = _settings with { VideoStyleValues = before };
        }
        else
        {
            SaveSettings();
        }

        if (effect)
        {
            ApplyVideoEffect(_settings.VideoEffect, _settings.VideoEffectStrength);
        }
        else
        {
            ApplyVideoLook(_settings.VideoLook);
        }
    }

    /// <summary>Changes the renderer live and redraws the current picture when paused.</summary>
    private void ApplyVideoLook(VideoLook look)
    {
        foreach (var (item, selected) in _videoLookItems)
        {
            item.IsChecked = selected == look;
        }

        var redraw = !_player.IsPlaying;
        OnPresenterThread(presenter =>
        {
            var options = VideoStyleOptions.ForLook(look);
            var values = _settings.VideoStyleValues;
            presenter.SetLook(look, VideoStyleOptions.Read(values, options[0]), VideoStyleOptions.Read(values, options[1]));
            if (redraw)
            {
                presenter.Redraw();
            }
        });
    }

    private void ChooseVideoLook(VideoLook look)
    {
        _settings = _settings with { VideoLook = look };
        RememberLater();
        ApplyVideoLook(look);
        Say("Picture look: " + VideoLooks.Name(look));
    }

}
