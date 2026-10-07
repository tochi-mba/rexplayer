using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Updates;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    private static readonly string ResumePath = Path.Combine(App.DataRoot, "resume.json");

    /// <summary>
    /// Offers, in the banner at the foot of the picture, to carry on where a run that did not close
    /// cleanly left off (WIN-14). Answering either way clears the marker.
    /// </summary>
    private void OfferResume(ResumePoint point)
    {
        var resume = new Button { Content = "Resume" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(resume, "ResumeButton");
        resume.Click += (_, _) =>
        {
            Notice.IsOpen = false;
            _player.Resume(point.Location, TimeSpan.FromSeconds(point.PositionSeconds));
        };
        ShowNotice("rexplayer closed unexpectedly", $"Carry on with {point.Title} at {TimeText.Format(TimeSpan.FromSeconds(point.PositionSeconds))}?", resume, InfoBarSeverity.Informational);
        Notice.Closed += (_, _) => TryClearResumeMarker();
    }

    /// <summary>Notes what is playing and where, for <see cref="OfferResume"/> after a crash.</summary>
    private void RememberWhereWeAre()
    {
        try
        {
            if (_player.IsPlaying && _player.Item is { } item)
            {
                ResumeMarker.Write(ResumePath, new ResumePoint(item.Location, _player.Title, _player.Position.TotalSeconds));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Log.Warning(LogSource, "The resume point could not be saved: " + ex.Message);
        }
    }

    private static void TryClearResumeMarker()
    {
        try
        {
            ResumeMarker.Clear(ResumePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Log.Warning(LogSource, "The resume point could not be cleared: " + ex.Message);
        }
    }

    /// <summary>
    /// Looks for a newer release (TOOL-09): on the schedule the user chose, or when they ask.
    /// Nothing is downloaded until they choose Install.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool asked)
    {
        UpdateOffer? offer;
        try
        {
            using var http = UpdateClient();
            var json = await http.GetStringAsync(UpdateCheck.LatestRelease);
            offer = UpdateCheck.Offer(json, Version);
            _settings = _settings with { LastUpdateCheck = DateTimeOffset.UtcNow };
            SaveSettings();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            App.Log.Info(LogSource, "Could not look for updates: " + ex.Message);
            if (asked)
            {
                Say("Could not reach the release page: " + ex.Message);
            }

            return;
        }

        if (offer is null)
        {
            if (asked)
            {
                Say($"rexplayer {Version} is the newest version.");
            }

            return;
        }

        var install = new Button { Content = "Install" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(install, "InstallUpdateButton");
        install.Click += async (_, _) =>
        {
            Notice.IsOpen = false;
            await InstallAsync(offer);
        };
        ShowNotice($"rexplayer {offer.Version} is available", "It installs over this one and keeps your settings.", install, InfoBarSeverity.Success);
    }

    /// <summary>
    /// Downloads the installer and its checksum, runs the installer only if they match, and closes
    /// so it can replace this copy.
    /// </summary>
    private async Task InstallAsync(UpdateOffer offer)
    {
        Say($"Downloading rexplayer {offer.Version}\u2026");
        var folder = Path.Combine(Path.GetTempPath(), "rexplayer-update");
        var installer = Path.Combine(folder, offer.InstallerName);
        try
        {
            Directory.CreateDirectory(folder);
            using var http = UpdateClient();
            http.Timeout = TimeSpan.FromMinutes(10);
            var checksum = await http.GetStringAsync(offer.Checksum);
            await using (var file = File.Create(installer))
            {
                await using var download = await http.GetStreamAsync(offer.Installer);
                await download.CopyToAsync(file);
            }

            await using (var file = File.OpenRead(installer))
            {
                if (!UpdateCheck.Verify(file, checksum, offer.InstallerName))
                {
                    App.Log.Warning(LogSource, $"The download of {offer.InstallerName} did not match its checksum.");
                    Say("The download did not match its published checksum, so nothing was installed.");
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Say("The update could not be downloaded: " + ex.Message);
            return;
        }

        App.Log.Info(LogSource, $"Installing rexplayer {offer.Version}.");
        Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true, ArgumentList = { "/SILENT", "/CLOSEAPPLICATIONS" } });
        Close();
    }

    private static HttpClient UpdateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"rexplayer/{Version}");
        return http;
    }

    private void ShowNotice(string title, string message, Button action, InfoBarSeverity severity)
    {
        Notice.Title = title;
        Notice.Message = message;
        Notice.ActionButton = action;
        Notice.Severity = severity;
        Notice.IsOpen = true;
    }

    /// <summary>What happens once the window is up: the scheduled update check and any crash to recover from.</summary>
    private void AfterGreeting(ResumePoint? crashed, bool openedSomething)
    {
        if (crashed is not null && !openedSomething)
        {
            OfferResume(crashed);
        }
        else
        {
            TryClearResumeMarker();
        }

        if (UpdateCheck.IsDue(_settings.UpdateChecks, _settings.LastUpdateCheck, DateTimeOffset.UtcNow))
        {
            _ = CheckForUpdatesAsync(asked: false);
        }
    }
}
