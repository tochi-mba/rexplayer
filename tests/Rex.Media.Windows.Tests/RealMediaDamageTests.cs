using System.Diagnostics;
using Rex.Media.Audio;
using Rex.Media.AppCore;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.Engine;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.TestKit.Damage;

namespace Rex.Media.Windows.Tests;

/// <summary>
/// Damaged copies of real videos, played through Windows' own decoders: no crash, no hang, and as much
/// played as the damage leaves. The videos are the owner's own, so the test runs only where
/// REXPLAYER_DAMAGE_MEDIA names a folder of them and skips elsewhere. The first megabytes of one MKV
/// and one MP4 are copied into a temporary folder, damaged there, and deleted afterwards; nothing about
/// them is kept or written down but their kind and the results.
/// </summary>
public sealed class RealMediaDamageTests
{
    private const int CopiedBytes = 24 << 20;
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(3);

    private static readonly string? Media = Environment.GetEnvironmentVariable("REXPLAYER_DAMAGE_MEDIA") is { Length: > 0 } chosen ? chosen : null;

    [Theory]
    [InlineData(".mkv")]
    [InlineData(".mp4")]
    public async Task DamagedCopiesOfRealVideosPlayAsFarAsTheyCanBeRead(string extension)
    {
        var original = Media is not null && Directory.Exists(Media)
            ? Directory.EnumerateFiles(Media, "*" + extension, SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault()
            : null;
        if (original is null)
        {
            Assert.Skip($"Set REXPLAYER_DAMAGE_MEDIA to a folder of videos with a {extension} file.");
        }

        var folder = Directory.CreateTempSubdirectory("rexplayer-damage-").FullName;
        try
        {
            byte[] start;
            await using (var stream = File.OpenRead(original))
            {
                start = new byte[(int)Math.Min(CopiedBytes, stream.Length)];
                await stream.ReadExactlyAsync(start, TestContext.Current.CancellationToken);
            }

            // The copy is the file's first megabytes: a file cut short, which is damage in itself.
            var healthy = new HealthyFile(extension.TrimStart('.'), extension, start, 0, 0);
            var whole = await PlayAsync(folder, "copy" + extension, start);
            Assert.True(whole.Result is DamageResult.Played, $"The undamaged copy did not play: {whole.Error}");
            var failures = new List<string>();
            foreach (var damaged in DamageCorpus.Make(healthy).Where(item => item.Kind is not ("empty" or "header-only")))
            {
                var played = await PlayAsync(folder, damaged.Kind + damaged.Extension, damaged.Bytes);
                var share = whole.AudioSeconds > 0 ? played.AudioSeconds / whole.AudioSeconds : 0;
                var pictures = whole.Pictures > 0 ? played.Pictures / (double)whole.Pictures : 0;
                TestContext.Current.SendDiagnosticMessage($"{extension} {damaged.Kind}: {played.Result}, sound {share:P0}, pictures {pictures:P0}, {played.Elapsed.TotalSeconds:0.0} s");
                if (played.Result is DamageResult.Crashed or DamageResult.Hung)
                {
                    failures.Add($"{damaged.Kind}: {played.Result} ({played.Error})");
                }
                else if (Math.Max(share, pictures) < damaged.MinimumShare)
                {
                    failures.Add($"{damaged.Kind}: played {Math.Max(share, pictures):P0}, at least {damaged.MinimumShare:P0} can be read.");
                }
            }

            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task<DamageMeasure> PlayAsync(string folder, string name, byte[] bytes)
    {
        var path = Path.Combine(folder, name);
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        var sink = new CountingSink();
        var presenter = new RecordingVideoPresenter();
        var clock = Stopwatch.StartNew();
        var session = new MediaSession(new EngineOptions
        {
            Demuxers = MediaRegistries.Demuxers(),
            Decoders = MediaRegistries.Decoders(new MfDecoderFactory()),
            AudioSinkFactory = () => sink,
            VideoPresenterFactory = () => presenter,
        });
        DamageMeasure Done(DamageResult result, string? error) => new(result, error, sink.Seconds, presenter.Shown.Count, 0, 0, clock.Elapsed, bytes.Length);
        try
        {
            try
            {
                await session.OpenAsync(new FileByteSource(path)).WaitAsync(Deadline, TestContext.Current.CancellationToken);
            }
            catch (Exception ex) when (ex is MediaFormatException or NotSupportedException)
            {
                return Done(DamageResult.Refused, ex.Message);
            }

            await session.WaitForFinishAsync().WaitAsync(Deadline, TestContext.Current.CancellationToken);
            return Done(session.State == SessionState.Faulted && sink.Seconds == 0 ? DamageResult.Refused : DamageResult.Played, session.FailureReason);
        }
        catch (TimeoutException)
        {
            return Done(DamageResult.Hung, "Not finished in time.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Done(DamageResult.Crashed, ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            if (clock.Elapsed < Deadline)
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
