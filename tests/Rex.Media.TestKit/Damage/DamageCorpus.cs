namespace Rex.Media.TestKit.Damage;

/// <summary>One reproducible damaged copy of a synthetic media file.</summary>
public sealed record DamagedMedia(string Format, string Kind, byte[] Bytes, string Extension);

/// <summary>
/// Deterministic damaged files for the pure engine regression lane. Media is generated at test
/// time and no private recordings or large binary fixtures enter the repository.
/// </summary>
public static class DamageCorpus
{
    public static IReadOnlyList<DamagedMedia> Make(HealthyFile healthy, int seed = 7)
    {
        ArgumentNullException.ThrowIfNull(healthy);
        var original = healthy.Bytes;
        var random = new Random(seed);
        var cases = new List<DamagedMedia>();
        if (original.Length == 0)
        {
            return [new DamagedMedia(healthy.Format, "empty", [], healthy.Extension)];
        }

        // Keep copies independent: the host may run these tests in any order.
        cases.Add(new DamagedMedia(healthy.Format, "empty", [], healthy.Extension));
        cases.Add(new DamagedMedia(healthy.Format, "truncated-tail", original[..Math.Max(1, original.Length * 3 / 4)], healthy.Extension));

        var flipped = (byte[])original.Clone();
        for (var i = 0; i < Math.Min(32, original.Length); i++)
        {
            var at = Math.Clamp(original.Length / 2 + random.Next(-256, 257), 0, original.Length - 1);
            flipped[at] ^= (byte)random.Next(1, 256);
        }

        cases.Add(new DamagedMedia(healthy.Format, "flipped-middle", flipped, healthy.Extension));

        var zeroed = (byte[])original.Clone();
        var start = original.Length / 3;
        Array.Clear(zeroed, start, Math.Min(1024, original.Length - start));
        cases.Add(new DamagedMedia(healthy.Format, "zero-run", zeroed, healthy.Extension));

        var garbage = (byte[])original.Clone();
        var atRun = original.Length / 2;
        random.NextBytes(garbage.AsSpan(atRun, Math.Min(1024, original.Length - atRun)));
        cases.Add(new DamagedMedia(healthy.Format, "garbage-run", garbage, healthy.Extension));

        var prefixed = new byte[4096 + original.Length];
        random.NextBytes(prefixed.AsSpan(0, 4096));
        original.CopyTo(prefixed, 4096);
        cases.Add(new DamagedMedia(healthy.Format, "garbage-prefix", prefixed, healthy.Extension));

        cases.Add(new DamagedMedia(healthy.Format, "wrong-extension", (byte[])original.Clone(), ".bin"));
        foreach (var (kind, spans) in healthy.Landmarks)
        {
            if (spans.Count == 0)
            {
                continue;
            }

            var (offset, length) = spans[0];
            if (length <= 0 || offset < 0 || offset >= original.Length)
            {
                continue;
            }

            var modified = (byte[])original.Clone();
            modified.AsSpan(offset, Math.Min(length, Math.Min(64, original.Length - offset))).Clear();
            cases.Add(new DamagedMedia(healthy.Format, "broken-" + kind, modified, healthy.Extension));
        }

        return cases;
    }
}
