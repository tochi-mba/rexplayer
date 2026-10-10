using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Player;
using Rex.Media.AppCore.Updates;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    private static readonly string ResumePath = Path.Combine(App.DataRoot, "resume.json");
    private bool _installingUpdate;

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
        _player.RememberPosition();
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
    /// Downloads the installer and its checksum, and hands the verified file to the updater. The
    /// updater waits for this process to close cleanly before starting installation, so neither the
    /// shell nor the installer blocks this window or has to force it down.
    /// </summary>
    private async Task InstallAsync(UpdateOffer offer)
    {
        if (_installingUpdate || _closed)
        {
            return;
        }

        _installingUpdate = true;
        try
        {
            await DownloadAndInstallAsync(offer);
        }
        finally
        {
            _installingUpdate = false;
        }
    }

    private async Task DownloadAndInstallAsync(UpdateOffer offer)
    {
        Say($"Downloading rexplayer {offer.Version}\u2026");
        var folder = Path.Combine(Path.GetTempPath(), "rexplayer-update", Guid.NewGuid().ToString("N"));
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

            var verified = await Task.Run(() =>
            {
                using var file = File.OpenRead(installer);
                return UpdateCheck.Verify(file, checksum, offer.InstallerName);
            });
            if (!verified)
            {
                App.Log.Warning(LogSource, $"The download of {offer.InstallerName} did not match its checksum.");
                Say("The download did not match its published checksum, so nothing was installed.");
                return;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Say("The update could not be downloaded: " + ex.Message);
            return;
        }

        if (_closed)
        {
            return;
        }

        Say($"Starting the rexplayer {offer.Version} update\u2026");
        try
        {
            var updater = Path.Combine(AppContext.BaseDirectory, "rexupdate.exe");
            var stagedUpdater = Path.Combine(folder, "rexupdate.exe");
            var handoffLog = Path.Combine(App.DataRoot, "logs", "update.log");
            var parent = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await Task.Run(() =>
            {
                File.Copy(updater, stagedUpdater, overwrite: true);
                using var process = Process.Start(new ProcessStartInfo(stagedUpdater)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    ArgumentList = { parent, installer, handoffLog },
                });
                if (process is null)
                {
                    throw new InvalidOperationException("Windows did not start the update hand-off.");
                }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            App.Log.Warning(LogSource, "The update hand-off could not start: " + ex.Message);
            Say("The installer is ready, but the update could not start: " + ex.Message);
            return;
        }

        App.Log.Info(LogSource, $"Handed rexplayer {offer.Version} to the updater; closing cleanly.");
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
