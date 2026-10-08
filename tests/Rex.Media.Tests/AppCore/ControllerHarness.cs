using Rex.Media.AppCore.Player;
using Rex.Media.Codecs;
using Rex.Media.Codecs.Software.Pcm;
using Rex.Media.Containers;
using Rex.Media.Containers.Riff;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Settings;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// A <see cref="PlayerController"/> over real sessions with in-memory files. The controller's
/// dispatcher only queues what the sessions report; the test runs the queue (<see cref="Pump"/>),
/// standing in for the window's thread, so it decides exactly when the controller hears of
/// anything. Every session gets a fresh recording sink, and the engine's own events are kept too.
/// </summary>
internal sealed class ControllerHarness : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly Queue<Action> _pending = new();
    private readonly List<SessionEvent> _engineEvents = [];

    /// <param name="autoPlay">Whether sessions start playing as soon as they open.</param>
    /// <param name="settings">The controller's settings.</param>
    /// <param name="openSource">Opens a location; by default from <see cref="Files"/>.</param>
    /// <param name="diskFolders">Leave folder expansion and finding subtitle files to the controller's defaults, which read the disk.</param>
    /// <param name="sidecars">Subtitle files beside each item, by the item's location.</param>
    public ControllerHarness(bool autoPlay = true, PlayerSettings? settings = null, Func<string, IByteSource>? openSource = null, bool diskFolders = false, Dictionary<string, string[]>? sidecars = null)
    {
        var demuxers = new DemuxerRegistry().Add(new WavDemuxerFactory()).Add(new Rex.Media.Containers.Matroska.MatroskaDemuxerFactory());
        var decoders = new DecoderRegistry().Add(new PcmDecoderFactory());
        Controller = new PlayerController(
            listener =>
            {
                var sink = new RecordingAudioSink(channels: 1);
                lock (_gate)
                {
                    Sinks.Add(sink);
                }

                return new MediaSession(
                    new EngineOptions { Demuxers = demuxers, Decoders = decoders, AudioSinkFactory = () => sink, AutoPlay = autoPlay, PositionInterval = TimeSpan.Zero },
                    sessionEvent =>
                    {
                        lock (_gate)
                        {
                            _engineEvents.Add(sessionEvent);
                            Monitor.PulseAll(_gate);
                        }

                        listener(sessionEvent);
                    });
            },
            openSource ?? (location => Files.TryGetValue(location, out var bytes) ? new MemoryByteSource(bytes, location) : throw new FileNotFoundException($"{location} is missing.")),
            Dispatch,
            settings,
            diskFolders ? null : location => [location],
            new Random(1),
            diskFolders ? null : media => Rex.Media.Subtitles.SubtitleSidecars.Find(media, folder => folder == Path.GetDirectoryName(media) ? sidecars?.GetValueOrDefault(media) ?? [] : []),
            diskFolders ? null : path => Files.TryGetValue(path, out var bytes) ? bytes : throw new FileNotFoundException(path),
            diskFolders ? null : (path, bytes) => Files[path] = path.Contains("readonly", StringComparison.Ordinal) ? throw new UnauthorizedAccessException(path + " is read-only.") : bytes);
        Controller.Message += (_, text) => Messages.Add(text);

        // The window always listens to both.
        Controller.Changed += (_, _) => Changes++;
        Controller.PositionChanged += (_, _) => PositionChanges++;
    }

    public int Changes { get; private set; }

    public int PositionChanges { get; private set; }

    public PlayerController Controller { get; }

    /// <summary>The media the controller can open, by location.</summary>
    public Dictionary<string, byte[]> Files { get; } = [];

    public List<RecordingAudioSink> Sinks { get; } = [];

    public List<string> Messages { get; } = [];

    /// <summary>A mono 8 kHz WAV whose samples count up from <paramref name="first"/>.</summary>
    public static byte[] Count(int first, int samples) =>
        WavBuilder.Pcm(8000, 1, 16, Pcm.Int16([.. Enumerable.Range(first, samples).Select(v => v / 32768f)])).Build();

    public static float[] Expected(params (int First, int Samples)[] runs) =>
        [.. runs.SelectMany(run => Enumerable.Range(run.First, run.Samples).Select(v => v / 32768f))];

    /// <summary>Everything every session played, in the order the sessions were made.</summary>
    public float[] Played()
    {
        lock (_gate)
        {
            return [.. Sinks.SelectMany(sink => sink.Channel(0))];
        }
    }

    /// <summary>Runs what the sessions have reported so far, as the window's thread would.</summary>
    public void Pump()
    {
        while (true)
        {
            Action next;
            lock (_gate)
            {
                if (!_pending.TryDequeue(out next!))
                {
                    return;
                }
            }

            next();
        }
    }

    /// <summary>Runs reports as they arrive until <paramref name="done"/> holds.</summary>
    public void PumpUntil(Func<PlayerController, bool> done)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (true)
        {
            Pump();
            if (done(Controller))
            {
                return;
            }

            lock (_gate)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException($"The controller never got there; it is {Controller.State} on {Controller.Item?.Title}. Messages: {string.Join(" | ", Messages)}");
                }

                if (_pending.Count == 0)
                {
                    Monitor.Wait(_gate, remaining);
                }
            }
        }
    }

    /// <summary>Waits, without running anything, until the engine has reported <paramref name="count"/> events of a kind.</summary>
    public void WaitForEngine<T>(int count = 1)
        where T : SessionEvent
    {
        var deadline = DateTime.UtcNow + Patience;
        lock (_gate)
        {
            while (_engineEvents.OfType<T>().Count() < count)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException($"The engine never reported {typeof(T).Name}.");
                }

                Monitor.Wait(_gate, remaining);
            }
        }
    }

    public void Dispose() => Controller.Dispose();

    private void Dispatch(Action action)
    {
        lock (_gate)
        {
            _pending.Enqueue(action);
            Monitor.PulseAll(_gate);
        }
    }
}
