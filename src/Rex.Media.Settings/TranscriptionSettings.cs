namespace Rex.Media.Settings;

/// <summary>Which of the library's files are transcribed in the background (SUB-20).</summary>
public enum TranscriptionScope
{
    /// <summary>Every video in every folder the library watches, less the folders left out.</summary>
    WholeLibrary,

    /// <summary>Only the videos in the folders chosen, less the folders left out.</summary>
    ChosenFolders,
}

/// <summary>
/// Transcribing ahead of time (SUB-20, SUB-21): the library's speech heard in the background, at
/// low priority and off by default, and what plays heard from where it plays, so its subtitles
/// are ready before they are needed. The model and the language are the ones the kinetic caption
/// settings name (<see cref="KineticCaptionSettings.TranscriptModel"/>).
/// </summary>
public sealed record TranscriptionSettings
{
    /// <summary>Transcribe the library in the background.</summary>
    public bool Library { get; init; }

    public TranscriptionScope Scope { get; init; } = TranscriptionScope.WholeLibrary;

    /// <summary>The folders transcribed when <see cref="Scope"/> is <see cref="TranscriptionScope.ChosenFolders"/>.</summary>
    public IReadOnlyList<string> Folders { get; init; } = [];

    /// <summary>Folders never transcribed in the background, whatever holds them.</summary>
    public IReadOnlyList<string> Excluded { get; init; } = [];

    /// <summary>Songs are transcribed too; otherwise only videos are, as music is mostly sung, not spoken.</summary>
    public bool IncludeMusic { get; init; }

    /// <summary>Whatever plays is transcribed from where it plays, even outside the library's scope.</summary>
    public bool WhatPlays { get; init; }

    /// <summary>The library goes on being transcribed while something plays; otherwise it waits, so playback has the processor.</summary>
    public bool WhilePlaying { get; init; }

    /// <summary>The library goes on being transcribed on battery power; otherwise it waits for the charger.</summary>
    public bool OnBattery { get; init; }

    /// <summary>Opt-in waiting for enough generated subtitles to avoid rebuffering; playback can be manually resumed at any time.</summary>
    public bool HoldForSubtitles { get; init; }

    /// <summary>Keep a model-labelled SubRip copy beside media as its transcript changes.</summary>
    public bool WriteSrtBesideMedia { get; init; }

    /// <summary>Opt in to deleting rexplayer's model-labelled SRT copies when sidecar export is switched off.</summary>
    public bool CleanSrtWhenDisabled { get; init; }

    /// <summary>The user paused the background transcription; it stays paused until they resume it.</summary>
    public bool Paused { get; init; }

    /// <summary>A copy with every value usable: folder lists without blanks or repeats, the scope a known one.</summary>
    public TranscriptionSettings Normalize() => this with
    {
        Scope = Enum.IsDefined(Scope) ? Scope : TranscriptionScope.WholeLibrary,
        Folders = Clean(Folders),
        Excluded = Clean(Excluded),
    };

    private static string[] Clean(IReadOnlyList<string>? folders) =>
        [.. (folders ?? []).Where(folder => !string.IsNullOrWhiteSpace(folder)).Select(folder => folder.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)];
}
