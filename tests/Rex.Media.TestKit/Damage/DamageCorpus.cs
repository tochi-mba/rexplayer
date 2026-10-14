namespace Rex.Media.TestKit.Damage;

/// <summary>One reproducible damaged copy of a synthetic media file.</summary>
public sealed record DamagedMedia(string Format, string Kind, byte[] Bytes, string Extension)
{
    /// <summary>The least share of the healthy file's sound this copy should still play (0: only that it fails cleanly).</summary>
    public double MinimumShare { get; init; }

    public string Name => Format + "/" + Kind;
}

/// <summary>
/// Deterministic damaged files for the pure engine regression lane: damage any file can suffer
/// (cut short, flipped bits, zeroed or garbage stretches, junk in front, two files run together,
/// the wrong extension, nothing at all) and damage aimed at each format's structure
/// (<see cref="StructuralDamage"/>). Media is generated at test time, so no private recordings or
/// large binary fixtures enter the repository.
/// </summary>
public static class DamageCorpus
{
    public static IReadOnlyList<DamagedMedia> Make(HealthyFile healthy, int seed = 7)
    {
        ArgumentNullException.ThrowIfNull(healthy);
        var original = healthy.Bytes;
        var random = new Random(seed);
        var cases = new List<DamagedMedia>();
        DamagedMedia Case(string kind, byte[] bytes, double share, string? extension = null) =>
            new(healthy.Format, kind, bytes, extension ?? healthy.Extension) { MinimumShare = share };

        // Keep copies independent: the host may run these tests in any order.
        cases.Add(Case("empty", [], 0));
        if (original.Length == 0)
        {
            return cases;
        }

        cases.Add(Case("truncated-tail", original[..Math.Max(1, original.Length * 3 / 4)], 0.6));
        cases.Add(Case("truncated-half", original[..Math.Max(1, original.Length / 2)], 0.35));
        cases.Add(Case("header-only", original[..Math.Min(original.Length, 1024)], 0));

        var flipped = (byte[])original.Clone();
        for (var i = 0; i < Math.Min(32, original.Length); i++)
        {
            var at = Math.Clamp((original.Length / 2) + random.Next(-256, 257), 0, original.Length - 1);
            flipped[at] ^= (byte)random.Next(1, 256);
        }

        cases.Add(Case("flipped-middle", flipped, 0.85));

        // Sparse flips through the whole file, one per 16 KB, past the headers (the first 5 %, and
        // at least 8 KB, which holds a Vorbis setup header): damage there is measured on its own.
        var scattered = (byte[])original.Clone();
        for (var at = Math.Max(original.Length / 20, 8192); at < original.Length; at += 16 * 1024)
        {
            scattered[Math.Min(original.Length - 1, at + random.Next(16 * 1024))] ^= (byte)random.Next(1, 256);
        }

        cases.Add(Case("flipped-throughout", scattered, 0.7));

        var zeroed = (byte[])original.Clone();
        var start = original.Length / 3;
        Array.Clear(zeroed, start, Math.Min(1024, original.Length - start));
        cases.Add(Case("zero-run", zeroed, 0.7));

        var wide = (byte[])original.Clone();
        var wideLength = original.Length / 20;
        Array.Clear(wide, original.Length / 2, wideLength);
        cases.Add(Case("zero-run-5pc", wide, 0.75));

        var garbage = (byte[])original.Clone();
        var atRun = original.Length / 2;
        random.NextBytes(garbage.AsSpan(atRun, Math.Min(1024, original.Length - atRun)));
        cases.Add(Case("garbage-run", garbage, 0.7));

        var noise = (byte[])original.Clone();
        random.NextBytes(noise.AsSpan(original.Length / 2, wideLength));
        cases.Add(Case("garbage-run-5pc", noise, 0.75));

        var prefixed = new byte[4096 + original.Length];
        random.NextBytes(prefixed.AsSpan(0, 4096));
        original.CopyTo(prefixed, 4096);
        cases.Add(Case("garbage-prefix", prefixed, 0.9));

        cases.Add(Case("concatenated", [.. original, .. original], 0.99));
        cases.Add(Case("wrong-extension", (byte[])original.Clone(), 0.99, ".bin"));
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
            cases.Add(Case("broken-" + kind, modified, 0));
        }

        cases.AddRange(StructuralDamage.For(healthy, random));
        return cases;
    }
}
