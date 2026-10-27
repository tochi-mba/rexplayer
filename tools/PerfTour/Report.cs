using System.Text;

namespace Rex.PerfTour;

/// <summary>
/// The tour's findings as Markdown: every step's action-to-settled latency, memory around each
/// library view, the slowest steps, and everything over the thresholds. Nothing the library holds
/// (titles, file names, artists) ever reaches the report; steps are named generically.
/// </summary>
public static class Report
{
    /// <summary>An interaction slower than this is flagged.</summary>
    public const double InteractionMs = 200;

    /// <summary>Opening a page slower than this is flagged.</summary>
    public const double PageOpenMs = 500;

    /// <summary>A view that keeps this much working set after being left is flagged.</summary>
    public const double LeakMb = 50;

    public static string Write(TourResult tour, string label, int pid)
    {
        ArgumentNullException.ThrowIfNull(tour);
        var text = new StringBuilder();
        Line(text, $"# Performance tour: {label}");
        Line(text, "");
        Line(text, $"Attached to rexplayer (PID {pid}) on {DateTime.Now:yyyy-MM-dd HH:mm}. " +
            (tour.PipeUsed ? "Samples come from the player's own metrics." : "Samples come from outside the process."));
        foreach (var note in tour.Notes)
        {
            Line(text, $"- {note}");
        }

        Line(text, "");
        Overall(text, tour);
        StepTable(text, tour);
        Slowest(text, tour);
        Sections(text, tour);
        Flags(text, tour);
        return text.ToString();
    }

    private static void Overall(StringBuilder text, TourResult tour)
    {
        if (tour.StartSample is not { } start || tour.EndSample is not { } end)
        {
            return;
        }

        Line(text, "## Before and after");
        Line(text, "");
        Line(text, "| Figure | Start | End | Change |");
        Line(text, "|---|---:|---:|---:|");
        Row(text, "Working set (MB)", start.WorkingSetMb, end.WorkingSetMb);
        Row(text, "Private bytes (MB)", start.PrivateMb, end.PrivateMb);
        MaybeRow(text, "Managed heap (MB)", start.HeapMb, end.HeapMb);
        MaybeRow(text, "CPU time (ms)", start.CpuMs, end.CpuMs);
        MaybeRow(text, "Gen 2 collections", start.Gc2, end.Gc2);
        MaybeRow(text, "Frames dropped", start.FramesDropped, end.FramesDropped);
        MaybeRow(text, "Threads", start.Threads, end.Threads);
        if (end.UiWorstMs is { } worst)
        {
            Line(text, Invariant($"| Worst window-thread stall (ms) | | {worst:0.0} | |"));
        }

        if (end.UiBlockedMs is { } blocked)
        {
            Line(text, Invariant($"| Window thread blocked during tour (ms) | | {blocked:0.0} | |"));
        }

        Line(text, "");
    }

    private static void StepTable(StringBuilder text, TourResult tour)
    {
        Line(text, "## Every step");
        Line(text, "");
        Line(text, "| Step | Latency (ms) | Working set (MB) | Heap (MB) | Over? |");
        Line(text, "|---|---:|---:|---:|---|");
        foreach (var step in tour.Steps)
        {
            var over = !step.Ok ? "failed: " + step.Note
                : step.Ms > Threshold(step.Name) ? Invariant($"over {Threshold(step.Name):0} ms") : "";
            Line(text, Invariant($"| {step.Name} | {step.Ms:0} | {step.After?.WorkingSetMb:0} | {step.After?.HeapMb:0} | {over} |"));
        }

        Line(text, "");
    }

    private static void Slowest(StringBuilder text, TourResult tour)
    {
        Line(text, "## Slowest steps");
        Line(text, "");
        foreach (var step in tour.Steps.Where(step => step.Ok).OrderByDescending(step => step.Ms).Take(10))
        {
            Line(text, Invariant($"1. {step.Name}: {step.Ms:0} ms"));
        }

        Line(text, "");
    }

    private static void Sections(StringBuilder text, TourResult tour)
    {
        if (tour.Sections.Count == 0)
        {
            return;
        }

        Line(text, "## Memory around each view");
        Line(text, "");
        Line(text, "Working set and managed heap before opening the view, and after returning to Home: a view whose cost stays after leaving holds on to something.");
        Line(text, "");
        Line(text, "| View | WS before (MB) | WS after leaving (MB) | Kept (MB) | Heap before (MB) | Heap after (MB) |");
        Line(text, "|---|---:|---:|---:|---:|---:|");
        foreach (var section in tour.Sections)
        {
            var kept = section.AfterLeft.WorkingSetMb - section.Before.WorkingSetMb;
            Line(text, Invariant($"| {section.Name} | {section.Before.WorkingSetMb:0} | {section.AfterLeft.WorkingSetMb:0} | {kept:+0;-0;0} | {section.Before.HeapMb:0} | {section.AfterLeft.HeapMb:0} |"));
        }

        Line(text, "");
    }

    private static void Flags(StringBuilder text, TourResult tour)
    {
        Line(text, "## Over the thresholds");
        Line(text, "");
        var any = false;
        foreach (var step in tour.Steps.Where(step => step.Ok && step.Ms > Threshold(step.Name)))
        {
            Line(text, Invariant($"- {step.Name} took {step.Ms:0} ms (threshold {Threshold(step.Name):0} ms)."));
            any = true;
        }

        foreach (var section in tour.Sections.Where(section => section.AfterLeft.WorkingSetMb - section.Before.WorkingSetMb > LeakMb))
        {
            Line(text, Invariant($"- The {section.Name} view kept {section.AfterLeft.WorkingSetMb - section.Before.WorkingSetMb:0} MB of working set after being left."));
            any = true;
        }

        foreach (var step in tour.Steps.Where(step => !step.Ok))
        {
            Line(text, $"- {step.Name} did not settle: {step.Note}");
            any = true;
        }

        if (!any)
        {
            Line(text, "Nothing crossed a threshold.");
        }

        Line(text, "");
    }

    /// <summary>Opening a page or a view gets the page budget; everything else the interaction budget.</summary>
    public static double Threshold(string stepName)
    {
        ArgumentNullException.ThrowIfNull(stepName);
        return stepName.StartsWith("open", StringComparison.Ordinal) || stepName.StartsWith("go back", StringComparison.Ordinal)
            || stepName.StartsWith("return", StringComparison.Ordinal)
                ? PageOpenMs
                : InteractionMs;
    }

    private static void Row(StringBuilder text, string name, double start, double end) =>
        Line(text, Invariant($"| {name} | {start:0} | {end:0} | {end - start:+0;-0;0} |"));

    private static void MaybeRow(StringBuilder text, string name, double? start, double? end)
    {
        if (start is { } from && end is { } to)
        {
            Row(text, name, from, to);
        }
    }

    private static void Line(StringBuilder text, string line) => text.Append(line).Append('\n');

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
