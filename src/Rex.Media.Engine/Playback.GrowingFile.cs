using Rex.Media.IO;
using Rex.Media.Primitives;

namespace Rex.Media.Engine;

public sealed partial class MediaSession
{
    private sealed partial class Playback
    {
        /// <summary>
        /// A demuxer's null normally means the end. For a local source whose declared timeline still
        /// has media left, it means the writer has not supplied the next bytes yet. Wait at the byte
        /// source instead of spinning, and let a seek or shutdown cancel the wait immediately.
        /// </summary>
        private bool WaitForGrowingFile(long generation, CancellationToken lifetime)
        {
            if (_demuxItem.Source is not IGrowingByteSource growing || !HasDeclaredShortfall(_demuxItem.Info.Duration, _demuxReached))
            {
                StopFileBuffering();
                return false;
            }

            var observed = growing.Extent;
            StartFileBuffering();
            ByteSourceChange change;
            try
            {
                change = growing.WaitForChange(observed, _session._options.GrowingFileStallTimeout, GenerationToken(generation));
            }
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
            {
                StopFileBuffering();
                return true;
            }

            StopFileBuffering();
            switch (change)
            {
                case ByteSourceChange.Grew:
                    _session._log.Info(LogSource, $"More bytes arrived for {_demuxItem.Source.Name}; playback is continuing.");
                    return true;
                case ByteSourceChange.Truncated:
                case ByteSourceChange.Replaced:
                    _demuxItem.Demuxer.Seek(_demuxReached, lifetime);
                    _session._log.Info(LogSource, $"{_demuxItem.Source.Name} changed while open; playback was repositioned.");
                    return true;
                default:
                    _session._log.Warning(LogSource, $"No more bytes arrived for {_demuxItem.Source.Name} before the download wait expired.");
                    return false;
            }
        }

        private static bool HasDeclaredShortfall(MediaTime duration, MediaTime reached)
        {
            if (!duration.IsKnown || !reached.IsKnown)
            {
                return false;
            }

            var tolerance = MediaTime.Max(MediaTime.FromSeconds(2), new MediaTime(duration.Ticks / 200));
            return reached + tolerance < duration;
        }

        private void StartFileBuffering()
        {
            if (Interlocked.Exchange(ref _waitingForFile, 1) == 0)
            {
                _session.OnBuffering(this, buffering: true);
            }
        }

        private void StopFileBuffering()
        {
            if (Interlocked.Exchange(ref _waitingForFile, 0) != 0)
            {
                _session.OnBuffering(this, buffering: false);
            }
        }
    }
}
