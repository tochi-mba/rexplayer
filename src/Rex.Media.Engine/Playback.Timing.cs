using Rex.Media.Diagnostics;

namespace Rex.Media.Engine;

public sealed partial class MediaSession
{
    /// <summary>
    /// The moments the user notices, written to the log as <see cref="TimingMarks"/>: how long after
    /// an open (or a seek) was asked for the first sound went to the device and the first picture was
    /// shown. Each is marked once; after that a frame costs a field read and a flag test.
    /// </summary>
    private sealed partial class Playback
    {
        private FirstMoments? _openMoments;
        private FirstMoments? _seekMoments;

        /// <summary>Times this playback's open, which began when <paramref name="marks"/> did.</summary>
        public void TimeOpen(TimingMarks marks) => Volatile.Write(ref _openMoments, new FirstMoments(marks, generation: -1));

        /// <summary>Times the seek that starts <paramref name="generation"/>.</summary>
        private void TimeSeek(long generation) =>
            Volatile.Write(ref _seekMoments, new FirstMoments(new TimingMarks(_session._log, "seek", _session._options.Time), generation));

        private void SoundReached(long generation)
        {
            Volatile.Read(ref _openMoments)?.Sound(generation);
            Volatile.Read(ref _seekMoments)?.Sound(generation);
        }

        private void PictureReached(long generation)
        {
            Volatile.Read(ref _openMoments)?.Picture(generation);
            Volatile.Read(ref _seekMoments)?.Picture(generation);
        }

        /// <summary>The first sound and picture of one open or seek; a generation of -1 takes any.</summary>
        private sealed class FirstMoments(TimingMarks marks, long generation)
        {
            private volatile bool _sound;
            private volatile bool _picture;

            public void Sound(long reached)
            {
                if (!_sound && (generation < 0 || reached == generation))
                {
                    _sound = true;
                    marks.Mark("first-sound");
                }
            }

            public void Picture(long reached)
            {
                if (!_picture && (generation < 0 || reached == generation))
                {
                    _picture = true;
                    marks.Mark("first-picture");
                }
            }
        }
    }
}
