using System.Globalization;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>Answers the automation pipe: the requests it takes, the description of the window's controls, the window's picture and the smoke checks of resume and the main window.</summary>
public sealed partial class MainWindow
{
    /// <summary>Answers the automation pipe (TOOL-03) as the Windows window does: open, run or status.</summary>
    public Task<AutomationReply> AnswerAsync(AutomationRequest request)
    {
        var answered = new TaskCompletionSource<AutomationReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnWindowThread(() =>
        {
            switch (request.Command)
            {
                case "open":
                    if (request.Paths.Count > 0)
                    {
                        _player.OpenHandedOver(request.Paths, request.Enqueue, request.LaunchedAt ?? DateTimeOffset.Now);
                    }

                    if (OperatingSystem.IsMacOSVersionAtLeast(14))
                    {
                        NSApplication.SharedApplication.Activate();
                    }
                    else
                    {
                        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
                    }
                    break;
                case "run" when request.Id is { } id && CommandCatalog.Find(id) is not null:
                    Run(id);
                    break;
                case "run":
                    answered.SetResult(AutomationReply.Failed($"There is no command \"{request.Id}\"."));
                    return;
                case "seek" when request.To is { } seconds && double.IsFinite(seconds) && seconds >= 0:
                    _player.Seek(TimeSpan.FromSeconds(seconds));
                    break;
                case "seek":
                    answered.SetResult(AutomationReply.Failed("seek needs a non-negative time in seconds."));
                    return;
                case "mac-queue":
                    if (!QueueSmokeAction(request.Id))
                    {
                        answered.SetResult(AutomationReply.Failed("The native queue smoke action is unavailable: " + request.Id));
                        return;
                    }

                    break;
                case "mac-library":
                    if (!LibrarySmokeAction(request.Id, request.Paths))
                    {
                        answered.SetResult(AutomationReply.Failed("The native library smoke action is unavailable: " + request.Id));
                        return;
                    }

                    break;
                case "mac-library-backup":
                    answered.SetResult(LibraryBackupSmokeAction(request.Id, request.Path));
                    return;
                case "mac-playlists":
                    if (!SavedPlaylistSmokeAction(request.Id, request.Path))
                    {
                        answered.SetResult(AutomationReply.Failed("Mac playlist action failed: " + request.Id));
                        return;
                    }

                    break;
                case "mac-shortcuts":
                    if (!ShortcutSmokeAction(request.Id))
                    {
                        answered.SetResult(AutomationReply.Failed("Mac shortcut action failed: " + request.Id));
                        return;
                    }

                    break;
                case "mac-keyboard":
                    answered.SetResult(ProbeMacKeyboardTraversal());
                    return;
                case "mac-accessibility":
                    var (controls, missing) = NameMacControls();
                    answered.SetResult(new AutomationReply
                    {
                        Ok = missing == 0,
                        Text = $"controls={controls};missing-labels={missing}",
                    });
                    return;
                case "mac-bookmarks":
                    answered.SetResult(BookmarkSmokeAction(request.Id));
                    return;
                case "mac-pitch-speed":
                    answered.SetResult(PitchSpeedSmokeAction(request.Id));
                    return;
                case "mac-resume":
                    answered.SetResult(ResumeSmokeAction(request.Id));
                    return;
                case "mac-quick-slots":
                    answered.SetResult(QuickSlotSmokeAction(request.Id));
                    return;
                case "mac-startup-guidance":
                    answered.SetResult(StartupGuidanceSmokeAction(request.Id));
                    return;
                case "mac-diagnostics":
                    answered.SetResult(DiagnosticsSmokeAction(request.Id, request.Path));
                    return;
                case "mac-window":
                    answered.SetResult(new AutomationReply
                    {
                        Ok = true,
                        Text = $"fullscreen={IsFullScreen};floating={_window.Level == NSWindowLevel.Floating};minimal={_minimalInterface};context={ContextCommandCount};title={_window.Title}",
                    });
                    return;
                case "mac-fullscreen-controller":
                    answered.SetResult(ProbeFullScreenController());
                    return;
                case "mac-main-window":
                    answered.SetResult(ProbeMainWindow());
                    return;
                case "mac-video-geometry":
                    answered.SetResult(VideoGeometrySmokeAction(request.Id));
                    return;
                case "mac-kinetic-captions":
                    answered.SetResult(MacKineticSmokeAction(request.Id));
                    return;
                case "mac-osd":
                    answered.SetResult(MacOsdSmokeAction(request.Id));
                    return;
                case "mac-speech":
                    answered.SetResult(MacSpeechSmokeAction(request.Id));
                    return;
                case "mac-loudness":
                    answered.SetResult(MacLoudnessSmokeAction(request.Id));
                    return;
                case "mac-equalizer":
                    answered.SetResult(MacEqualizerSmokeAction(request.Id));
                    return;
                case "mac-stereo":
                    answered.SetResult(MacStereoSmokeAction(request.Id));
                    return;
                case "mac-window-sizing":
                    answered.SetResult(MacWindowSizingSmokeAction(request.Id));
                    return;
                case "mac-crash-recovery":
                    answered.SetResult(MacCrashRecoverySmokeAction(request.Id));
                    return;
                case "mac-stats":
                    answered.SetResult(MacStatsSmokeAction(request.Id));
                    return;
                case "mac-drop":
                    _player.Drop(request.Paths ?? [], request.Enqueue);
                    answered.SetResult(new AutomationReply { Ok = true });
                    return;
                case "mac-subtitles":
                    answered.SetResult(SubtitleSmokeAction(request.Id));
                    return;
                case "mac-subtitle-style":
                    answered.SetResult(SubtitleStyleSmokeAction(request.Id));
                    return;
                case "mac-audio":
                    answered.SetResult(new AutomationReply
                    {
                        Ok = true,
                        Text = $"muted={_player.Muted};volume={_player.Volume.ToString("0.###", CultureInfo.InvariantCulture)}",
                    });
                    return;
                case "mac-music":
                    answered.SetResult(MusicSmokeAction(request.Id));
                    return;
                case "mac-snapshot":
                    answered.SetResult(SnapshotSmokeAction(request.Id, request.Path));
                    return;
                case "mac-updates":
                    answered.SetResult(MacUpdateSmokeAction(request.Id));
                    return;
                case "mac-preferences":
                    answered.SetResult(PreferencesSmokeAction(request.Id));
                    return;
                case "status":
                    break;
                case "ui.add-folder":
                    answered.SetResult(LibraryShown
                        ? AddLibraryFolders(request.Paths)
                        : AutomationReply.Failed("Open the library before adding watched folders."));
                    return;
                case "ui.keyboard":
                    answered.SetResult(ProbeLibraryKeyboard());
                    return;
                case "ui.verify-layout":
                    answered.SetResult(VerifyLibraryResultLayout());
                    return;
                case "ui.tree":
                    answered.SetResult(LibraryControlTree());
                    return;
                case "ui.press":
                    answered.SetResult(PressLibraryControl(request.Target));
                    return;
                case "ui.set" when request.Target == "LibraryCategory":
                    answered.SetResult(ChangeLibraryCategory(request.Value));
                    return;
                case "ui.set" when request.Target == "LibraryWidth":
                    answered.SetResult(ResizeLibraryWindow(request.Value));
                    return;
                case "ui.text" when request.Target == "LibrarySummary":
                    answered.SetResult(LibraryShown
                        ? new AutomationReply { Ok = true, Text = _librarySummary?.StringValue }
                        : AutomationReply.Failed("Open the library to read its summary."));
                    return;
                case "ui.text" when request.Target == "LibraryPage":
                    answered.SetResult(LibraryShown
                        ? new AutomationReply { Ok = true, Text = _libraryPageSummary?.StringValue }
                        : AutomationReply.Failed("Open the library to read its page."));
                    return;
                case "ui.search":
                    if (!LibraryShown || _librarySearch is null || request.Target is null)
                    {
                        answered.SetResult(AutomationReply.Failed("Open the library and supply a search query."));
                        return;
                    }

                    _librarySearch.StringValue = request.Target;
                    ChangeLibraryView();
                    answered.SetResult(new AutomationReply { Ok = true, Text = _librarySummary?.StringValue });
                    return;
                case "ui.snapshot":
                    if (string.IsNullOrWhiteSpace(request.Path))
                    {
                        answered.SetResult(AutomationReply.Failed("ui.snapshot needs a path."));
                        return;
                    }

                    try
                    {
                        CaptureWindowSnapshot(request.Path);
                        answered.SetResult(new AutomationReply { Ok = true, Text = request.Path });
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                    {
                        answered.SetResult(AutomationReply.Failed("Could not capture the window: " + ex.Message));
                    }

                    return;
                case "ui.describe":
                    answered.SetResult(new AutomationReply { Ok = true, Text = DescribeMainControls() });
                    return;
                default:
                    answered.SetResult(AutomationReply.Failed($"\"{request.Command}\" is not a request on this Mac yet; try open, run or status."));
                    return;
            }

            answered.SetResult(new AutomationReply
            {
                Ok = true,
                State = _player.State.ToString(),
                Title = request.Command == "mac-queue"
                    ? (_queueSummary?.StringValue ?? "") + " | " + (_queueShuffle?.Title ?? "") + " | " + (_queueRepeat?.Title ?? "")
                    : request.Command == "mac-library"
                        ? LibrarySmokeTitle()
                        : _player.Item is null ? null : _player.Title,
                Position = _player.Position.TotalSeconds,
                Duration = _player.Duration > TimeSpan.Zero ? _player.Duration.TotalSeconds : null,
            });
        });
        return answered.Task;
    }

    /// <summary>The controls of the bar and the welcome a person can see now, as "id:kind" in the order they show.</summary>
    private string DescribeMainControls() => string.Join(";", _describedControls
        .Where(control => !control.View.IsHiddenOrHasHiddenAncestor
            && !(LibraryShown && _welcome is not null && control.View.IsDescendantOf(_welcome)))
        .Select(control => control.Id + ":" + control.Kind));

    /// <summary>Saves the window's whole content (stage or library, Up Next, the bar) as a PNG at <paramref name="path"/>.</summary>
    private void CaptureWindowSnapshot(string path)
    {
        if (_window.ContentView is not { } content)
        {
            throw new InvalidOperationException("The window has nothing to capture.");
        }

        content.LayoutSubtreeIfNeeded();
        using var bitmap = content.BitmapImageRepForCachingDisplayInRect(content.Bounds)
            ?? throw new InvalidOperationException("AppKit could not capture the window.");
        content.CacheDisplay(content.Bounds, bitmap);
        using var png = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png)
            ?? throw new InvalidOperationException("AppKit could not encode the window's picture.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (!png.Save(NSUrl.FromFilename(Path.GetFullPath(path)), true))
        {
            throw new IOException("Could not save the window's picture.");
        }
    }

    // Opening saves where the playing item is, which would overwrite the planted point, so the
    // session ends first and the planted point is the one the reopened item finds.
    private void OfferResumeAt15(string location)
    {
        _player.Stop();
        _player.Memory.Left(location, TimeSpan.FromSeconds(15), TimeSpan.Zero);
        _player.Open([location]);
    }

    /// <summary>Exercise the native, nonmodal resume offer and its two real buttons.</summary>
    private AutomationReply ResumeSmokeAction(string? action)
    {
        if (_player.Item is not { } item)
        {
            return AutomationReply.Failed("Open media before checking resume behavior.");
        }

        switch (action)
        {
            case "offer":
                _settings = _settings with { ResumeMode = ResumeBehavior.Ask };
                _player.Settings = _settings;
                OfferResumeAt15(item.Location);
                break;
            case "accept":
                _resumeAccept?.PerformClick(_resumeAccept);
                break;
            case "start-over":
                _resumeStartOver?.PerformClick(_resumeStartOver);
                break;
            case "always":
                _settings = _settings with { ResumeMode = ResumeBehavior.Always };
                _player.Settings = _settings;
                OfferResumeAt15(item.Location);
                break;
            case "never":
                _settings = _settings with { ResumeMode = ResumeBehavior.Never };
                _player.Settings = _settings;
                OfferResumeAt15(item.Location);
                break;
            case "status":
                break;
            default:
                return AutomationReply.Failed("Unknown resume action: " + action);
        }

        ShowState();
        return new AutomationReply
        {
            Ok = true,
            Text = $"mode={_settings.ResumeMode};pending={_player.PendingResumeAt?.TotalSeconds.ToString(CultureInfo.InvariantCulture) ?? "none"};visible={_resumeBanner?.Hidden == false};remembered={_player.Memory.ResumePoint(item.Location)?.TotalSeconds.ToString(CultureInfo.InvariantCulture) ?? "none"}",
            Position = _player.Position.TotalSeconds,
        };
    }

    private AutomationReply ProbeMainWindow()
    {
        var before = _remainingTime;
        ToggleTimeDisplay();
        var toggled = _remainingTime != before;
        ShowSeekPreview(0.5);
        var preview = _seekPreview?.Hidden == false && !string.IsNullOrWhiteSpace(_seekPreviewTime?.StringValue);
        HideSeekPreview();
        var controls = new[] { _mainFullScreen, _mainRepeat, _mainShuffle, _mainMute,
            _fullScreenTracks, _fullScreenEffects, _fullScreenLeave };
        var complete = controls.All(button => button is { Enabled: true }) && toggled && preview
            && _seek.Buffered >= 0 && _seek.Chapters is not null && _mainKeyViews.Count == 17;
        return complete
            ? new AutomationReply
            {
                Ok = true,
                Text = $"transport={controls.Length};timeline=elapsed,remaining,chapters,buffer,preview;" +
                    $"volume=True;chips={_statusChips.StringValue};keys={_mainKeyViews.Count}",
            }
            : AutomationReply.Failed("The native main-window controls are incomplete.");
    }
}
