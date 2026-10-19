using Rex.Media.AppCore.Player;
using Rex.Media.Subtitles;

namespace Rex.Media.AppCore.Captions;

/// <summary>
/// Whether a kinetic caption frame needs drawing (OSD-11). Every movement of the words is worked
/// out from the playing position, so while playback is paused a frame at the same position, in the
/// same place, at the same size, with the same cue and the same picture read, is the frame already
/// on screen: it is skipped rather than worked out and laid out again on every display refresh.
/// </summary>
public sealed class CaptionFrameGate
{
    private bool _drawn;
    private TimeSpan _position;
    private Area _area;
    private double _fontSize;
    private SubtitleCue? _cue;
    private int _cues;
    private double _pictures;

    /// <summary>What is on screen was cleared or restyled: the next frame is drawn.</summary>
    public void Reset() => _drawn = false;

    /// <summary>
    /// True when the frame must be drawn: always while <paramref name="playing"/>; when paused, only
    /// when something it is drawn from differs from the last frame drawn. <paramref name="picturesReadTo"/>
    /// is how far the picture has been read, so words placed before it was known move clear of it.
    /// </summary>
    public bool Draws(bool playing, TimeSpan position, Area area, double fontSize, IReadOnlyList<SubtitleCue> cues, double picturesReadTo)
    {
        ArgumentNullException.ThrowIfNull(cues);
        var cue = cues.Count > 0 ? cues[0] : null;
        if (!playing && _drawn && position == _position && area == _area && fontSize.Equals(_fontSize)
            && ReferenceEquals(cue, _cue) && cues.Count == _cues && picturesReadTo.Equals(_pictures))
        {
            return false;
        }

        (_drawn, _position, _area, _fontSize, _cue, _cues, _pictures) = (true, position, area, fontSize, cue, cues.Count, picturesReadTo);
        return true;
    }
}
