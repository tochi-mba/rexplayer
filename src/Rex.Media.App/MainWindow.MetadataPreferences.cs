using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Metadata;
using Rex.Media.Settings;
using Windows.Storage.Pickers;

namespace Rex.Media.App;

/// <summary>
/// The Metadata group of Preferences: whether rexplayer may fetch posters, descriptions, episode
/// titles, genres and album art from free online catalogues. Strictly off by default, because a
/// lookup sends the names of the user's files and folders to those services; each kind has its own
/// switch, excluded folders are never looked up, and everything fetched can be cleared in one go.
/// The cache is measured off the window's thread while the dialog is open.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The cache's folder, beside the library's own store in the app's data.</summary>
    internal static string MetadataFolder => App.Roots.Metadata;

    /// <summary>The group's controls, and how the settings change on Save.</summary>
    private (UIElement[] Controls, Func<PlayerSettings, PlayerSettings> Apply) MetadataPreferences()
    {
        var m = _settings.Metadata;
        var intro = new TextBlock
        {
            Text = "rexplayer can fetch posters, descriptions, episode titles, genres, cast and album art "
                + "from free online catalogues that need no key or sign-up. A lookup sends the names of your files and folders to those "
                + "services, so each kind stays off until you turn it on. Everything fetched is kept on this "
                + "computer and works offline, and artwork found in your files or beside them always wins.",
            TextWrapping = TextWrapping.Wrap,
        };
        var tv = Check("Fetch TV show details (TVmaze)", m.TvShows);
        AutomationProperties.SetAutomationId(tv, "PreferencesMetadataTv");
        var movies = Check("Fetch movie details (Wikidata and Wikipedia)", m.Movies);
        AutomationProperties.SetAutomationId(movies, "PreferencesMetadataMovies");
        var music = Check("Fetch album art and music details (MusicBrainz and the Cover Art Archive)", m.Music);
        AutomationProperties.SetAutomationId(music, "PreferencesMetadataMusic");
        var covers = Choice("Fetched album covers", ["Fill missing covers only", "Replace existing covers too"], m.ReplaceCovers ? 1 : 0);
        AutomationProperties.SetAutomationId(covers, "PreferencesMetadataCovers");
        var coversHelp = new TextBlock
        {
            Text = "Replacing changes only what rexplayer shows; your files are never written to. Per album, right-click for Replace cover... and Restore original cover.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        };
        var credit = new TextBlock { Text = MetadataPolicy.DataCredit, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
        AutomationProperties.SetAutomationId(credit, "PreferencesMetadataCredit");

        // Folders left out: listed with a way to put each back, and a picker to leave one out.
        var excluded = m.Excluded.ToList();
        var leftOut = new StackPanel { Spacing = 2 };
        var leftOutHeading = new TextBlock { Text = "Folders never looked up:", TextWrapping = TextWrapping.Wrap };
        void ShowLeftOut()
        {
            leftOut.Children.Clear();
            leftOutHeading.Visibility = excluded.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            var i = 0;
            foreach (var folder in excluded)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(new TextBlock { Text = folder, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 });
                var back = new HyperlinkButton { Content = "Put back" };
                AutomationProperties.SetName(back, "Put back " + folder);
                AutomationProperties.SetAutomationId(back, "PreferencesMetadataPutBack-" + i++.ToString(CultureInfo.InvariantCulture));
                back.Click += (_, _) =>
                {
                    excluded.Remove(folder);
                    ShowLeftOut();
                };
                row.Children.Add(back);
                leftOut.Children.Add(row);
            }
        }

        ShowLeftOut();
        var leaveOut = new Button { Content = "Exclude a folder..." };
        AutomationProperties.SetAutomationId(leaveOut, "PreferencesMetadataExclude");
        leaveOut.Click += async (_, _) =>
        {
            var picker = Prepared(new FolderPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary });
            picker.FileTypeFilter.Add("*");
            if (await picker.PickSingleFolderAsync() is { } chosen && !excluded.Contains(chosen.Path, StringComparer.OrdinalIgnoreCase))
            {
                excluded.Add(chosen.Path);
                ShowLeftOut();
            }
        };

        // The downloads' size, measured off the window's thread, and a way to clear them.
        var cache = new MetadataCache(MetadataFolder);
        var cacheLine = new TextBlock { Text = "Measuring the downloaded metadata...", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(cacheLine, "PreferencesMetadataCacheSize");
        async Task MeasureAsync()
        {
            var text = await Task.Run(() =>
            {
                try
                {
                    return "Downloaded metadata: " + MetadataCache.Describe(cache.Size());
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return "The downloaded metadata could not be measured: " + ex.Message;
                }
            });
            cacheLine.Text = text;
        }

        Forget(MeasureAsync());
        var clear = new Button { Content = "Clear downloaded metadata" };
        AutomationProperties.SetAutomationId(clear, "PreferencesMetadataClear");
        clear.Click += async (_, _) =>
        {
            clear.IsEnabled = false;
            var problem = await Task.Run(() =>
            {
                try
                {
                    cache.Clear();
                    return null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return ex.Message;
                }
            });
            Say(problem is null ? "Downloaded metadata cleared" : "The downloaded metadata could not all be cleared: " + problem);
            await MeasureAsync();
            clear.IsEnabled = true;
        };

        UIElement[] controls = [intro, tv, movies, music, covers, coversHelp, credit, leftOutHeading, leftOut, leaveOut, cacheLine, clear];
        return (controls, settings => settings with
        {
            Metadata = new MetadataSettings
            {
                TvShows = tv.IsChecked == true,
                Movies = movies.IsChecked == true,
                Music = music.IsChecked == true,
                ReplaceCovers = covers.SelectedIndex == 1,
                Excluded = excluded,
            }.Normalize(),
        });
    }
}
