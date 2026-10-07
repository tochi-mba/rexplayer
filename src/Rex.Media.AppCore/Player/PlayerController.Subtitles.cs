using System.Globalization;
using Rex.Media.AppCore.Commands;
using Rex.Media.Engine;
using Rex.Media.Primitives;
using Rex.Media.Subtitles;

namespace Rex.Media.AppCore.Player;

public sealed partial class PlayerController
{
    /// <summary>What is said when subtitles are added with nothing open to show them with.</summary>
    public const string NothingToSubtitle = "Open something to play first, then add its subtitles.";

    private readonly Dictionary<(MediaInfo Item, int TrackId), SubtitleTrack> _embedded = [];
    private readonly List<SubtitleTrack> _subtitleTracks = [];
    private SubtitleTrack? _lastSubtitles;

    /// <summary>Raised when the subtitle tracks, the chosen one or the subtitle delay change.</summary>
    public event EventHandler? SubtitlesChanged;

    /// <summary>The current item's subtitle tracks: files beside it (and ones added) first, then its own.</summary>
    public IReadOnlyList<SubtitleTrack> SubtitleTracks => _subtitleTracks;

    /// <summary>The track being shown, or null when subtitles are off.</summary>
    public SubtitleTrack? Subtitles { get; private set; }

    /// <summary>A second track shown at the same time, at the top (OSD-04), or null.</summary>
    public SubtitleTrack? SecondarySubtitles { get; private set; }

    /// <summary>How much later the subtitles show than they were made for (negative: sooner).</summary>
    public TimeSpan SubtitleDelay { get; private set; }

    /// <summary>The cues to show now.</summary>
    public IReadOnlyList<SubtitleCue> SubtitlesNow => Subtitles?.At(Position, SubtitleDelay) ?? [];

    /// <summary>The cues to show at <paramref name="position"/>, for a window that keeps time between position reports.</summary>
    public IReadOnlyList<SubtitleCue> SubtitlesAt(TimeSpan position) => Subtitles?.At(position, SubtitleDelay) ?? [];

    /// <summary>The second track's cues to show at <paramref name="position"/>.</summary>
    public IReadOnlyList<SubtitleCue> SecondarySubtitlesAt(TimeSpan position) => SecondarySubtitles?.At(position, SubtitleDelay) ?? [];

    /// <summary>Reads a subtitle file and shows it with what is playing (SUB-16).</summary>
    public bool AddSubtitles(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Item is null)
        {
            Say(NothingToSubtitle);
            return false;
        }

        if (LoadSubtitles(path, Path.GetFileName(path)) is not { } track)
        {
            Say($"{Path.GetFileName(path)} is not a subtitle file rexplayer can read.");
            return false;
        }

        _subtitleTracks.Insert(0, track);
        ShowSubtitles(track);
        return true;
    }

    /// <summary>
    /// What was dropped on the window: media opens as <see cref="Open"/> would, and subtitle files
    /// join what is then playing, so a film dropped with its subtitles plays with them (SUB-16).
    /// </summary>
    public void Drop(IReadOnlyList<string> paths, bool enqueue = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var media = paths.Where(path => !SubtitleSidecars.IsSubtitle(path)).ToList();
        if (media.Count > 0)
        {
            Open(media, enqueue);
        }

        foreach (var path in paths.Where(SubtitleSidecars.IsSubtitle))
        {
            AddSubtitles(path);
        }
    }

    /// <summary>Shows <paramref name="track"/> (one of <see cref="SubtitleTracks"/>), or turns subtitles off with null.</summary>
    public void ShowSubtitles(SubtitleTrack? track)
    {
        Subtitles = track;
        if (track is not null)
        {
            _lastSubtitles = track;
        }

        if (track is not null && track == SecondarySubtitles)
        {
            SecondarySubtitles = null;
        }

        Say(track is null ? "Subtitles off" : "Subtitles: " + track.Name);
        SubtitlesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows <paramref name="track"/> as well as the main subtitles, or stops with null.</summary>
    public void ShowSecondarySubtitles(SubtitleTrack? track)
    {
        SecondarySubtitles = track;
        Say(track is null ? "Second subtitles off" : "Second subtitles: " + track.Name);
        SubtitlesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSubtitleDelay(TimeSpan delay)
    {
        SubtitleDelay = delay < -MediaSession.MaxAudioDelay ? -MediaSession.MaxAudioDelay : delay > MediaSession.MaxAudioDelay ? MediaSession.MaxAudioDelay : delay;
        Say(string.Create(CultureInfo.InvariantCulture, $"Subtitle delay {SubtitleDelay.TotalMilliseconds:0} ms"));
        SubtitlesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The subtitle commands; false for any other.</summary>
    private bool ExecuteSubtitles(string commandId)
    {
        switch (commandId)
        {
            case CommandCatalog.CycleSubtitles:
                // Off, then each track in turn, then off again.
                var index = Subtitles is null ? -1 : _subtitleTracks.IndexOf(Subtitles);
                ShowSubtitles(index + 1 < _subtitleTracks.Count ? _subtitleTracks[index + 1] : null);
                return true;
            case CommandCatalog.CycleSecondarySubtitles:
                // The same, passing over the track already shown as the main one.
                var others = _subtitleTracks.Where(track => track != Subtitles).ToList();
                var at = SecondarySubtitles is null ? -1 : others.IndexOf(SecondarySubtitles);
                ShowSecondarySubtitles(at + 1 < others.Count ? others[at + 1] : null);
                return true;
            case CommandCatalog.ToggleSubtitles:
                ShowSubtitles(Subtitles is not null ? null : _lastSubtitles is { } last && _subtitleTracks.Contains(last) ? last : _subtitleTracks.FirstOrDefault());
                return true;
            case CommandCatalog.SubtitlesEarlier or CommandCatalog.SubtitlesLater:
                SetSubtitleDelay(SubtitleDelay + TimeSpan.FromMilliseconds(commandId == CommandCatalog.SubtitlesLater ? 50 : -50));
                return true;
            case CommandCatalog.ResetSubtitleDelay:
                SetSubtitleDelay(TimeSpan.Zero);
                return true;
            default:
                return false;
        }
    }

    /// <summary>A new item: the files beside it are read now; its own tracks join once it opens.</summary>
    private void FindSubtitles(PlaylistItem item)
    {
        _subtitleTracks.Clear();
        _embedded.Clear();
        Subtitles = null;
        SecondarySubtitles = null;
        if (Uri.TryCreate(item.Location, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            return;
        }

        foreach (var sidecar in _sidecars(item.Location))
        {
            var name = Path.GetFileName(sidecar.Path) + (sidecar.Language is { } language ? $" ({language})" : "");
            if (LoadSubtitles(sidecar.Path, name) is { } track)
            {
                _subtitleTracks.Add(track);
            }
        }

        Subtitles = _subtitleTracks.FirstOrDefault();
        SubtitlesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The media's own subtitle tracks, once its details are known; one marked default is shown when no file was.</summary>
    private void AddEmbeddedSubtitles(MediaInfo info)
    {
        foreach (var track in info.Tracks.Where(track => track.Kind == MediaKind.Subtitle && SubtitlePackets.CanRead(track.Codec)))
        {
            var subtitles = Embedded(info, track.Id);
            if (!_subtitleTracks.Contains(subtitles))
            {
                _subtitleTracks.Add(subtitles);
                if (Subtitles is null && (track.IsDefault || track.IsForced))
                {
                    Subtitles = subtitles;
                }
            }
        }

        SubtitlesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The store for a track of an item, made the first time a cue or the item's details mention it.</summary>
    private SubtitleTrack Embedded(MediaInfo item, int trackId)
    {
        if (!_embedded.TryGetValue((item, trackId), out var subtitles))
        {
            var track = item.Tracks.FirstOrDefault(t => t.Id == trackId);
            var label = string.Join(" ", new[] { track?.Title, track?.Language }.Where(part => !string.IsNullOrEmpty(part)));
            subtitles = new SubtitleTrack(label.Length > 0 ? label : $"Track {trackId}");
            _embedded[(item, trackId)] = subtitles;
        }

        return subtitles;
    }

    private SubtitleTrack? LoadSubtitles(string path, string name)
    {
        try
        {
            var (text, _) = SubtitleText.Decode(_readFile(path), Settings.SubtitleCodePage);
            var track = new SubtitleTrack(name);
            track.AddRange(SubtitleFile.Parse(text).Cues);
            return track;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }
}
