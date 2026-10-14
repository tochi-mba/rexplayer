using System.Diagnostics;
using Rex.Media.AppCore;
using Rex.Media.Audio;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.TestKit.Damage;

namespace Rex.Media.Tests.Robustness;

/// <summary>
/// Plays a damaged file through a whole media session, as the window does, into an output that
/// counts what it is given as fast as it comes, with pictures checked by
/// <see cref="CheckedVideoDecoderFactory"/>: what a viewer would hear and see of it.
/// </summary>
internal static class DamagedSession
{
    public static async Task<DamageMeasure> PlayAsync(byte[] bytes, string extension, TimeSpan timeout)
    {
        var video = new CheckedVideoDecoderFactory();
        var sink = new CountingSink();
        var presenter = new RecordingVideoPresenter();
        var notices = 0;
        var allocated = GC.GetTotalAllocatedBytes();
        var clock = Stopwatch.StartNew();
        var session = new MediaSession(
            new EngineOptions
            {
                Demuxers = MediaRegistries.Demuxers(),
                Decoders = MediaRegistries.Decoders(video),
                AudioSinkFactory = () => sink,
                VideoPresenterFactory = () => presenter,
            },
            sessionEvent =>
            {
                if (sessionEvent is MediaDamagedEvent)
                {
                    Interlocked.Increment(ref notices);
                }
            });
        var silent = false;
        DamageMeasure Done(DamageResult result, string? error) =>
            new(result, error, silent ? 0 : sink.Seconds, presenter.Shown.Count, video.Clean, GC.GetTotalAllocatedBytes() - allocated, clock.Elapsed, bytes.Length) { Notices = Volatile.Read(ref notices) };

        try
        {
            try
            {
                await session.OpenAsync(new MemoryByteSource(bytes, "damaged" + extension)).WaitAsync(timeout);
            }
            catch (Exception ex) when (ex is MediaFormatException or NotSupportedException)
            {
                return Done(DamageResult.Refused, ex.Message);
            }
            catch (TimeoutException)
            {
                return Done(DamageResult.Hung, "Not open in time.");
            }
            catch (Exception ex)
            {
                // The session survived (the process did), but a reader's own fault reached the caller.
                return Done(DamageResult.Crashed, ex.GetType().Name + ": " + ex.Message);
            }

            try
            {
                await session.WaitForFinishAsync().WaitAsync(timeout - clock.Elapsed);
            }
            catch (TimeoutException)
            {
                return Done(DamageResult.Hung, $"Still playing after {timeout.TotalSeconds:0} seconds.");
            }

            // Pictures without sound play against silence, which is not sound salvaged from the file.
            silent = (await session.GetStatsAsync().WaitAsync(timeout)).AudioDecoder == "silence";
            return session.State == SessionState.Faulted
                ? Done(sink.Seconds > 0 ? DamageResult.Played : DamageResult.Refused, session.FailureReason)
                : Done(DamageResult.Played, null);
        }
        finally
        {
            // A hung session is left to its threads rather than waited for.
            if (clock.Elapsed < timeout)
            {
                session.Dispose();
            }
        }
    }

    /// <summary>An output that only counts the sound it is given, as fast as it comes.</summary>
    private sealed class CountingSink : IAudioSink
    {
        private long _played;
        private long _total;
        private int _rate = 1;

        public string Name => "counting";

        public long PlayedSamples => Interlocked.Read(ref _played);

        public long QueuedSamples => 0;

        public bool IsRealTime => false;

        public double Seconds => Interlocked.Read(ref _total) / (double)_rate;

        public AudioFormat Open(AudioFormat preferred)
        {
            _rate = preferred.SampleRate;
            return new AudioFormat(preferred.SampleRate, preferred.Channels, SampleFormat.F32, preferred.Layout);
        }

        public void Write(AudioFrame frame, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Add(ref _played, frame.SampleCount);
            Interlocked.Add(ref _total, frame.SampleCount);
        }

        public void Pause()
        {
        }

        public void Resume()
        {
        }

        public void Flush() => Interlocked.Exchange(ref _played, 0);

        public void Drain(CancellationToken cancellationToken)
        {
        }

        public void Dispose()
        {
        }
    }
}
