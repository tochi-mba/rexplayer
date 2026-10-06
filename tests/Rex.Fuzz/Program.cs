using Rex.Media.TestKit;
using SharpFuzz;

// libFuzzer's driver starts this once per target, with the target named in REXPLAYER_FUZZ_TARGET;
// the targets themselves live in the test kit so the pure suite replays the same code (fuzz.yml).
var target = Environment.GetEnvironmentVariable("REXPLAYER_FUZZ_TARGET") ?? throw new InvalidOperationException("Set REXPLAYER_FUZZ_TARGET to one of: " + string.Join(", ", FuzzTargets.All.Keys));
if (!FuzzTargets.All.ContainsKey(target))
{
    throw new InvalidOperationException($"'{target}' is not a target. The targets are: " + string.Join(", ", FuzzTargets.All.Keys));
}

Fuzzer.LibFuzzer.Run(input => FuzzTargets.Run(target, input.ToArray()));
