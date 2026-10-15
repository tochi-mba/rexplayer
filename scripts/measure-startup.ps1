<#
.SYNOPSIS
Measures rexplayer as people feel it: how long it takes to start, to show its first picture and play
its first sound, to seek; what playing costs (processor, dropped pictures, jitter, memory,
allocations); how quickly the library and the panels open; and, with -LowEnd, all of it again on an
imitation of a weak computer.

.DESCRIPTION
rexplayer writes a "timing" line to its log at each moment of a launch and of opening media (see
Rex.Media.Diagnostics.Milestones): the window drawn, the first picture presented, the first sound
written to the device, each with the milliseconds since the process started. REXPLAYER_EXIT_AFTER
makes it exit once the moments a run waits for have come, so this script can launch it again and
again without the window tests. Commands (seek, next episode, open), its figures ("metrics") and
timed panels ("measure") go through the automation pipe.

Every run gets a scratch data folder (REXPLAYER_ROOT) and a pipe of its own (REXPLAYER_PIPE_NAME),
so it never touches the real settings, never hands its file to a player already running, and plays
with the volume at zero. Each scenario runs several times; the report gives the median and the worst,
and how busy the rest of the machine was just before each run.

Scenarios:
  launch   start with nothing to open, until the window is interactive (warm: files in memory)
  cold     the same from a fresh copy of the program read from the disk
  play     rexplayer.exe <file>, until its first picture and first sound
  open     start empty, then open <file> as the library or the open dialog does
  seek     play <file>, then jump a short way forward (a near seek)
  seekfar  play <file>, then seek to four fifths of the way through (a far seek)
  next     play <file>, then move to the next episode in its folder
  steady   play <file> and measure it for -SteadySeconds: processor, pictures dropped, jitter,
           memory, allocations and how responsive the window stays
  music    play <file> with a software visualisation and measure the same, and its frame cost
  ui       play <file> and time opening the library, Preferences, the Subject Lock panel, the
           picture effect picker and the effects panel
  library  start empty, scan a generated library of -LibraryFiles files, then open the library

-All makes the media with FFmpeg (once, into artifacts/measure/media) and runs every scenario with
the files each one needs; -Perf runs the performance suite (./dev.ps1 perf) under the same
conditions and adds its figures to the report.

.PARAMETER LowEnd
Imitates a weak computer for everything this script starts: two processor cores, below-normal
priority, a cap of -MemoryCapMB on each process's memory (a job object), pictures drawn in software
(REXPLAYER_GRAPHICS=software) and no decoding on the graphics card (REXPLAYER_HARDWARE_DECODING=off).

.PARAMETER Only
Runs only these of the scenarios (with -All, only these of all of them).

.PARAMETER Out
A JSON file to write every run's figures to.

.PARAMETER Report
A Markdown file to write the summary tables to.
#>
[CmdletBinding()]
param(
    [string]$Exe,
    [string[]]$Media = @(),
    [int]$Runs = 5,
    [string[]]$Scenario = @('launch', 'play', 'open', 'seek', 'next'),
    [string]$Seed,
    [switch]$Cold,
    [int]$TimeoutSeconds = 60,
    [string]$Out,
    [string]$Report,
    [switch]$All,
    [switch]$LowEnd,
    [switch]$Perf,
    [int]$SteadySeconds = 15,
    [int]$LibraryFiles = 3000,
    [int]$MemoryCapMB = 2048,
    [string]$Label,
    [string[]]$Only = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
# "launch,play" arrives as one string through pwsh -File.
$Scenario = @($Scenario | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
if (-not $Exe) {
    $Exe = Get-ChildItem -Recurse -Filter rexplayer.exe -Path (Join-Path $root 'src/Rex.Media.App/bin/Release') -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Exe -or -not (Test-Path $Exe)) { throw 'rexplayer.exe has not been built; run ./dev.ps1 build or pass -Exe.' }
$Exe = (Resolve-Path $Exe).Path
if (-not $Label) { $Label = $(if ($LowEnd) { 'low-end imitation' } else { 'this computer' }) }

$version = (Get-Item $Exe).VersionInfo.ProductVersion -replace '\+.*$', ''
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('rexplayer-measure-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$shaderCache = Join-Path $scratch 'shaders'
$audioExtensions = @('.mp3', '.flac', '.wav', '.ogg', '.opus', '.m4a', '.aac', '.aiff', '.wma')
$pictureExtensions = @('.jpg', '.jpeg', '.png', '.bmp', '.gif', '.webp', '.tiff')
$timing = [regex]'\[INFO\] timing: (?<name>[\w-]+) at (?<ms>[\d.]+) ms'

# The weak computer: set on this process, so every process it starts inherits the cores, the
# priority, the memory cap and the switches.
if ($LowEnd) {
    $self = [Diagnostics.Process]::GetCurrentProcess()
    # Two cores that are not hyperthreads of each other where the machine has them.
    $self.ProcessorAffinity = [IntPtr]$(if ([Environment]::ProcessorCount -ge 4) { 0x5 } else { 0x3 })
    $self.PriorityClass = [Diagnostics.ProcessPriorityClass]::BelowNormal
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MeasureJob
{
    [StructLayout(LayoutKind.Sequential)]
    struct Basic { public long UserTime; public long JobUserTime; public uint Flags; public UIntPtr MinWorkingSet; public UIntPtr MaxWorkingSet; public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority; public uint Scheduling; }
    [StructLayout(LayoutKind.Sequential)]
    struct Io { public ulong A, B, C, D, E, F; }
    [StructLayout(LayoutKind.Sequential)]
    struct Extended { public Basic Basic; public Io Io; public UIntPtr ProcessMemory; public UIntPtr JobMemory; public UIntPtr PeakProcess; public UIntPtr PeakJob; }
    [DllImport("kernel32", SetLastError = true)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int kind, ref Extended info, int size);
    [DllImport("kernel32", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32")] static extern IntPtr GetCurrentProcess();
    public static void CapEachProcess(long bytes)
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        var info = new Extended { Basic = new Basic { Flags = 0x100 }, ProcessMemory = (UIntPtr)(ulong)bytes };
        if (!SetInformationJobObject(job, 9, ref info, Marshal.SizeOf<Extended>()) || !AssignProcessToJobObject(job, GetCurrentProcess()))
        {
            throw new System.ComponentModel.Win32Exception();
        }
    }
}
'@
    [MeasureJob]::CapEachProcess([long]$MemoryCapMB * 1MB)
    $env:REXPLAYER_GRAPHICS = 'software'
    $env:REXPLAYER_HARDWARE_DECODING = 'off'
}

# The media each scenario plays, made once with FFmpeg and kept between runs.
function Get-MeasureMedia {
    $folder = Join-Path $root 'artifacts/measure/media'
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $tone = @('-f', 'lavfi', '-i', 'sine=frequency=330:sample_rate=48000')
    $made = [ordered]@{
        'h264-1080p.mp4' = @('-f', 'lavfi', '-i', 'testsrc2=size=1920x1080:rate=30') + $tone + @('-t', '60', '-c:v', 'libx264', '-preset', 'veryfast', '-pix_fmt', 'yuv420p', '-g', '60', '-c:a', 'aac', '-b:a', '128k')
        'h264-2160p.mp4' = @('-f', 'lavfi', '-i', 'testsrc2=size=3840x2160:rate=30') + $tone + @('-t', '30', '-c:v', 'libx264', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p', '-g', '60', '-c:a', 'aac', '-b:a', '128k')
        'hevc10-1080p.mp4' = @('-f', 'lavfi', '-i', 'testsrc2=size=1920x1080:rate=30') + $tone + @('-t', '20', '-c:v', 'hevc_qsv', '-profile:v', 'main10', '-pix_fmt', 'p010le', '-tag:v', 'hvc1', '-c:a', 'aac', '-b:a', '128k')
        'vp9-1080p.webm' = @('-f', 'lavfi', '-i', 'testsrc2=size=1920x1080:rate=30') + $tone + @('-t', '20', '-c:v', 'libvpx-vp9', '-deadline', 'realtime', '-cpu-used', '8', '-b:v', '4M', '-c:a', 'libopus')
        'music.mp3' = @('-f', 'lavfi', '-i', 'aevalsrc=0.4*sin(2*PI*(220+40*sin(t))*t)+0.6*sin(2*PI*55*t)*lt(mod(t\,0.5)\,0.06):s=44100:c=stereo', '-t', '90', '-c:a', 'libmp3lame', '-b:a', '192k')
        'music.flac' = @('-f', 'lavfi', '-i', 'aevalsrc=0.4*sin(2*PI*(220+40*sin(t))*t)+0.6*sin(2*PI*55*t)*lt(mod(t\,0.5)\,0.06):s=44100:c=stereo', '-t', '90', '-c:a', 'flac')
        'picture.jpg' = @('-f', 'lavfi', '-i', 'testsrc2=size=1920x1080:rate=1', '-frames:v', '1', '-q:v', '3')
    }
    # Without a graphics card that encodes HEVC, the software encoder makes it, slowly.
    $instead = @{ 'hevc10-1080p.mp4' = @('-f', 'lavfi', '-i', 'testsrc2=size=1920x1080:rate=30') + $tone + @('-t', '20', '-c:v', 'libx265', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p10le', '-tag:v', 'hvc1', '-x265-params', 'log-level=error', '-c:a', 'aac', '-b:a', '128k') }
    foreach ($name in $made.Keys) {
        $path = Join-Path $folder $name
        if (Test-Path $path) { continue }
        Write-Host "Making $name with FFmpeg"
        & ffmpeg -hide_banner -loglevel error -nostdin -y @($made[$name]) $path
        if ($LASTEXITCODE -ne 0 -and $instead.Contains($name)) { & ffmpeg -hide_banner -loglevel error -nostdin -y @($instead[$name]) $path }
        if ($LASTEXITCODE -ne 0) { Remove-Item -Force -ErrorAction SilentlyContinue $path; throw "FFmpeg could not make $name." }
    }

    return $folder
}

# A library of many small files in artist and album folders, copies of one fixture.
function Get-MeasureLibrary([int]$count) {
    $folder = Join-Path $root "artifacts/measure/library-$count"
    if ((Test-Path $folder) -and @(Get-ChildItem -Recurse -File $folder).Count -eq $count) { return $folder }
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $folder
    $song = Join-Path $root 'tests/fixtures/mp3/stereo-44k-128k-cbr.mp3'
    for ($i = 0; $i -lt $count; $i++) {
        $album = Join-Path $folder ("Artist {0:000}/Album {1:0000}" -f [math]::Floor($i / 100), [math]::Floor($i / 10))
        if (-not (Test-Path $album)) { New-Item -ItemType Directory -Force -Path $album | Out-Null }
        [IO.File]::Copy($song, (Join-Path $album ("{0:00} Song {1}.mp3" -f ($i % 10), $i)))
    }

    return $folder
}

$plan = [ordered]@{}
if ($All) {
    $folder = Get-MeasureMedia
    $at = { param($name) Join-Path $folder $name }
    $Scenario = @('launch', 'cold', 'play', 'seek', 'seekfar', 'steady', 'music', 'ui', 'library')
    $plan['play'] = @('h264-1080p.mp4', 'hevc10-1080p.mp4', 'vp9-1080p.webm', 'music.mp3', 'music.flac', 'picture.jpg' | ForEach-Object { & $at $_ })
    $plan['seek'] = @(& $at 'h264-1080p.mp4')
    $plan['seekfar'] = @(& $at 'h264-1080p.mp4')
    $plan['steady'] = @('h264-1080p.mp4', 'h264-2160p.mp4' | ForEach-Object { & $at $_ })
    $plan['music'] = @(& $at 'music.mp3')
    $plan['ui'] = @(& $at 'h264-1080p.mp4')
}

# -Only runs some of the scenarios, as when two builds are measured turn about.
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
if ($Only.Count -gt 0) { $Scenario = @($Scenario | Where-Object { $Only -contains $_ }) }
elseif ($Media.Count -eq 0) {
    $Media = @((Join-Path $root 'tests/fixtures/mp4/h264-aac.mp4'), (Join-Path $root 'tests/fixtures/mp3/stereo-44k-128k-cbr.mp3'))
}
$Media = @($Media | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })

function New-DataRoot([hashtable]$extra = @{}) {
    $data = Join-Path $scratch ('data-' + [guid]::NewGuid().ToString('N'))
    if ($Seed) {
        Copy-Item -Recurse -Path $Seed -Destination $data
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue (Join-Path $data 'logs')
    }
    else {
        New-Item -ItemType Directory -Path $data | Out-Null
    }

    # Over whatever was copied: no welcome, no update check, no sound, nothing restored.
    $settingsPath = Join-Path $data 'settings.json'
    $settings = if (Test-Path $settingsPath) { Get-Content -Raw $settingsPath | ConvertFrom-Json } else { [pscustomobject]@{ schema = 3 } }
    foreach ($pair in @(@('firstRunDone', $true), @('lastSeenVersion', $version), @('updateChecks', 'Off'), @('volume', 0), @('muted', $true), @('restoreQueue', $false), @('singleInstance', $true))) {
        $settings | Add-Member -Force -NotePropertyName $pair[0] -NotePropertyValue $pair[1]
    }
    foreach ($key in $extra.Keys) { $settings | Add-Member -Force -NotePropertyName $key -NotePropertyValue $extra[$key] }
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 20))
    Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $data 'resume.json'), (Join-Path $data 'resume.json.rex-backup')

    # The compiled shaders a first run kept, as anyone who has used the player before has them.
    if (Test-Path $shaderCache) { Copy-Item -Recurse -Path $shaderCache -Destination (Join-Path $data 'cache/shaders') -Force }
    return $data
}

# Keeps the shaders the first run compiled for every later run, then forgets the run's data.
function Remove-DataRoot([string]$data) {
    $compiled = Join-Path $data 'cache/shaders'
    if (-not (Test-Path $shaderCache) -and (Test-Path $compiled)) { Copy-Item -Recurse -Path $compiled -Destination $shaderCache }
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $data
}

function Get-ProgramCopy([bool]$cold) {
    if (-not $cold) { return $Exe }
    $folder = Join-Path $scratch ('program-' + [guid]::NewGuid().ToString('N'))
    & xcopy.exe (Split-Path -Parent $Exe) $folder /E /I /J /Q /Y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Copying the program folder failed.' }
    return Join-Path $folder (Split-Path -Leaf $Exe)
}

function Read-Moments([string]$data) {
    $log = Join-Path $data 'logs/rexplayer.log'
    if (-not (Test-Path $log)) { return @() }
    $stream = [IO.FileStream]::new($log, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try { $text = [IO.StreamReader]::new($stream).ReadToEnd() } finally { $stream.Dispose() }
    return @($timing.Matches($text) | ForEach-Object { [pscustomobject]@{ Name = $_.Groups['name'].Value; Ms = [double]$_.Groups['ms'].Value } })
}

function Find-Moment($moments, [string]$name, [int]$nth = 1) {
    $found = @($moments | Where-Object Name -eq $name)
    if ($found.Count -lt $nth) { return $null }
    return $found[$nth - 1].Ms
}

function Wait-Moments([string]$data, [hashtable]$needed, [Diagnostics.Process]$process, [hashtable]$memory) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        Add-ProcessMemorySample $process $memory
        $moments = Read-Moments $data
        $missing = @($needed.Keys | Where-Object { @($moments | Where-Object Name -eq $_).Count -lt $needed[$_] })
        if ($missing.Count -eq 0) {
            Add-ProcessMemorySample $process $memory
            return $moments
        }
        if ($process.HasExited) { throw "rexplayer exited before $($missing -join ', ')." }
        Start-Sleep -Milliseconds 20
    }

    throw "Timed out waiting for $($needed.Keys -join ', ')."
}

function Wait-PlayerExit([Diagnostics.Process]$process, [hashtable]$memory, [string]$exitAfter) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        Add-ProcessMemorySample $process $memory
        if ($process.WaitForExit(50)) { return }
    }

    throw "rexplayer did not reach $exitAfter within $TimeoutSeconds s."
}

# Sends one request and gives the player's reply.
function Send-Request([string]$pipeName, [string]$json) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', $pipeName, [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(10000)
        $bytes = [Text.Encoding]::UTF8.GetBytes($json + "`n")
        $pipe.Write($bytes, 0, $bytes.Length)
        $pipe.Flush()
        $reply = [IO.StreamReader]::new($pipe).ReadLine()
        if ($reply -notmatch '"ok":true') { throw "The player refused $json`: $reply" }
        return $reply | ConvertFrom-Json
    }
    finally { $pipe.Dispose() }
}

function Get-Metrics([string]$pipeName, [switch]$Start) {
    $request = if ($Start) { '{"command":"metrics","id":"start"}' } else { '{"command":"metrics"}' }
    return (Send-Request $pipeName $request).metrics
}

# Reads the OS-maintained lifetime high-water marks while a timed run is active.
function Add-ProcessMemorySample([Diagnostics.Process]$process, [hashtable]$memory) {
    if ($null -eq $memory) { return }

    try {
        if ($process.HasExited) { return }
        $process.Refresh()
        if ($process.HasExited) { return }
        $memory['peak-working-set-mb'] = [math]::Max($memory['peak-working-set-mb'], $process.PeakWorkingSet64 / 1MB)
        $memory['peak-private-mb'] = [math]::Max($memory['peak-private-mb'], $process.PeakPagedMemorySize64 / 1MB)
    }
    catch [InvalidOperationException] {
        if (-not $process.HasExited) { throw }
    }
    catch [ComponentModel.Win32Exception] {
        if (-not $process.HasExited) { throw }
    }
}

# How busy the rest of the machine is now, in percent of all its processors.
function Get-MachineBusy {
    try { return [double](Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'").PercentProcessorTime }
    catch { return -1 }
}

function Start-Player([string]$data, [string]$file, [string]$exitAfter, [bool]$cold, [string]$pipeName) {
    $info = [Diagnostics.ProcessStartInfo]::new((Get-ProgramCopy $cold))
    $info.UseShellExecute = $false
    if ($file) { $info.ArgumentList.Add($file) }
    $info.Environment['REXPLAYER_ROOT'] = $data
    $info.Environment['REXPLAYER_PIPE_NAME'] = $pipeName
    if ($exitAfter) { $info.Environment['REXPLAYER_EXIT_AFTER'] = $exitAfter }
    return [Diagnostics.Process]::Start($info)
}

function Stop-Player([Diagnostics.Process]$process, [string]$pipeName) {
    if (-not $process.HasExited) {
        try { Send-Request $pipeName '{"command":"run","id":"quit"}' | Out-Null } catch { }
        if (-not $process.WaitForExit(10000)) { $process.Kill() }
    }
    $process.Dispose()
}

# Runs that wait for the player to exit by itself once the moments come.
function Invoke-TimedRun([string]$kind, [string]$file) {
    $data = New-DataRoot
    $audioOnly = $file -and ($audioExtensions -contains [IO.Path]::GetExtension($file).ToLowerInvariant())
    $picture = $file -and ($pictureExtensions -contains [IO.Path]::GetExtension($file).ToLowerInvariant())
    $played = if ($audioOnly) { 'first-audio' } elseif ($picture) { 'first-frame' } else { 'target-frame+first-audio' }
    $exitAfter = switch ($kind) {
        { $_ -in 'launch', 'cold' } { 'interactive' }
        { $_ -in 'play', 'open' } { $played }
        default { if ($audioOnly) { 'first-audio*2' } else { 'target-frame*2+first-audio*2' } }
    }

    $pipeName = 'rexplayer-measure-' + [guid]::NewGuid().ToString('N')
    $busy = Get-MachineBusy
    $asked = [DateTime]::Now
    $process = Start-Player $data $(if ($kind -in 'launch', 'cold', 'open') { '' } else { $file }) $exitAfter ($Cold -or $kind -eq 'cold') $pipeName
    $memory = @{ 'peak-working-set-mb' = 0.0; 'peak-private-mb' = 0.0 }
    try {
        Add-ProcessMemorySample $process $memory
        $created = ($process.StartTime - $asked).TotalMilliseconds
        $firstPlayed = if ($audioOnly) { @{ 'first-audio' = 1 } } else { @{ 'first-frame' = 1; 'first-audio' = 1 } }
        switch ($kind) {
            'open' {
                Wait-Moments $data @{ 'interactive' = 1 } $process $memory | Out-Null
                Send-Request $pipeName (@{ command = 'open'; paths = @($file) } | ConvertTo-Json -Compress) | Out-Null
            }
            'seek' {
                Wait-Moments $data $firstPlayed $process $memory | Out-Null
                Start-Sleep -Milliseconds 1500
                Send-Request $pipeName '{"command":"run","id":"jump-forward-short"}' | Out-Null
            }
            'seekfar' {
                Wait-Moments $data $firstPlayed $process $memory | Out-Null
                Start-Sleep -Milliseconds 1500
                $status = Send-Request $pipeName '{"command":"status"}'
                Send-Request $pipeName (@{ command = 'seek'; to = [math]::Round($status.duration * 0.8, 1) } | ConvertTo-Json -Compress) | Out-Null
            }
            'next' {
                Wait-Moments $data $firstPlayed $process $memory | Out-Null
                Start-Sleep -Milliseconds 1500
                Send-Request $pipeName '{"command":"run","id":"next-episode"}' | Out-Null
            }
        }

        Wait-PlayerExit $process $memory $exitAfter
        $moments = Read-Moments $data
    }
    finally {
        if (-not $process.HasExited) { $process.Kill() }
        $process.Dispose()
    }

    $result = [ordered]@{ 'machine-busy-%' = $busy; 'process-created' = $created }
    $result['peak-working-set-mb'] = $memory['peak-working-set-mb']
    $result['peak-private-mb'] = $memory['peak-private-mb']
    foreach ($moment in $moments) {
        if (-not $result.Contains($moment.Name)) { $result[$moment.Name] = $moment.Ms }
    }

    # Each later step is measured from the request that started it.
    switch ($kind) {
        'open' {
            $start = Find-Moment $moments 'start'
            foreach ($name in @('open', 'demuxer', 'audio-decoder', 'video-decoder', 'opened', 'audio-device', 'first-frame', 'first-audio')) {
                if ($null -ne (Find-Moment $moments $name)) { $result["start>$name"] = (Find-Moment $moments $name) - $start }
            }
        }
        { $_ -in 'seek', 'seekfar' } {
            $seek = Find-Moment $moments 'seek'
            foreach ($name in @('first-frame', 'target-frame', 'first-audio')) {
                $at = Find-Moment $moments $name 2
                if ($null -ne $at -and $null -ne $seek -and $at -ge $seek) { $result["seek>$name"] = $at - $seek }
            }
        }
        'next' {
            $start = Find-Moment $moments 'start' 2
            foreach ($name in @('open', 'opened', 'first-frame', 'target-frame', 'first-audio')) {
                $at = Find-Moment $moments $name 2
                if ($null -ne $at -and $null -ne $start) { $result["next>$name"] = $at - $start }
            }
        }
    }

    Remove-DataRoot $data
    return [pscustomobject]$result
}

# Runs that drive a playing player through the pipe and read its figures.
function Invoke-DrivenRun([string]$kind, [string]$file) {
    $settings = if ($kind -eq 'music') { @{ visualizer = 'Aurora' } } else { @{} }
    $data = New-DataRoot $settings
    $pipeName = 'rexplayer-measure-' + [guid]::NewGuid().ToString('N')
    $busy = Get-MachineBusy
    $process = Start-Player $data $(if ($kind -eq 'library') { '' } else { $file }) '' $false $pipeName
    $result = [ordered]@{ 'machine-busy-%' = $busy }
    try {
        $audioOnly = $file -and ($audioExtensions -contains [IO.Path]::GetExtension($file).ToLowerInvariant())
        $ready = if ($kind -eq 'library') { @{ 'interactive' = 1 } } elseif ($audioOnly) { @{ 'first-audio' = 1 } } else { @{ 'first-frame' = 1 } }
        Wait-Moments $data $ready $process | Out-Null
        Start-Sleep -Seconds 3
        $before = Get-Metrics $pipeName -Start
        $since = [Diagnostics.Stopwatch]::StartNew()
        switch ($kind) {
            'library' {
                $scan = (Send-Request $pipeName (@{ command = 'measure'; id = 'library-scan'; paths = @($file) } | ConvertTo-Json -Compress)).metrics
                $result['library-scan-ms'] = $scan.'scan-ms'
                $result['library-entries'] = $scan.entries
                $result['open-library-ms'] = (Send-Request $pipeName '{"command":"measure","id":"open:library"}').metrics.'open-ms'
            }
            'ui' {
                foreach ($panel in @('library', 'preferences', 'subject-lock', 'effect-picker', 'effects')) {
                    $result["open-$panel-ms"] = (Send-Request $pipeName (@{ command = 'measure'; id = "open:$panel" } | ConvertTo-Json -Compress)).metrics.'open-ms'
                }
            }
            default { Start-Sleep -Seconds $SteadySeconds }
        }

        $after = Get-Metrics $pipeName
        $seconds = $since.Elapsed.TotalSeconds
        $result['cpu-%-of-one-core'] = ($after.'cpu-ms' - $before.'cpu-ms') / ($seconds * 10)
        $result['cpu-%-of-machine'] = $result['cpu-%-of-one-core'] / $after.processors
        $result['allocated-mb-per-s'] = ($after.'allocated-mb' - $before.'allocated-mb') / $seconds
        $result['gc-per-s'] = (($after.gc0 - $before.gc0) + ($after.gc1 - $before.gc1) + ($after.gc2 - $before.gc2)) / $seconds
        $result['working-set-mb'] = $after.'working-set-mb'
        $result['private-mb'] = $after.'private-mb'
        $result['peak-working-set-mb'] = $after.'peak-working-set-mb'
        $result['peak-private-mb'] = $after.'peak-private-mb'
        $result['managed-heap-mb'] = $after.'managed-heap-mb'
        $result['ui-blocked-ms-per-s'] = $after.'ui-blocked-ms-per-second'
        $result['ui-worst-ms'] = $after.'ui-worst-ms'
        if ($after.PSObject.Properties['frames-presented']) {
            $result['frames-presented'] = $after.'frames-presented' - $before.'frames-presented'
            $result['frames-dropped'] = $after.'frames-dropped' - $before.'frames-dropped'
            $result['decoding-on-graphics-card'] = $after.'decoding-on-graphics-card'
            if ($after.'cadence-pictures' -gt 0) {
                $result['frame-interval-jitter-ms'] = $after.'frame-interval-jitter-ms'
                $result['frame-interval-worst-ms'] = $after.'frame-interval-worst-ms'
                $result['frame-lateness-jitter-ms'] = $after.'frame-lateness-jitter-ms'
                $result['frame-lateness-worst-ms'] = $after.'frame-lateness-worst-ms'
            }
        }
        if ($after.PSObject.Properties['visual-draw-ms']) {
            $result['visual-draw-ms'] = $after.'visual-draw-ms'
            $result['visual-canvas-px'] = $after.'visual-width' * $after.'visual-height'
        }
    }
    finally {
        Stop-Player $process $pipeName
        Remove-DataRoot $data
    }

    return [pscustomobject]$result
}

function Get-Median([double[]]$values) {
    $sorted = @($values | Sort-Object)
    $middle = [math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

$collected = [ordered]@{}
$markdown = [Collections.Generic.List[string]]::new()
$machine = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim()
$markdown.Add("# rexplayer $version measured on $Label")
$markdown.Add('')
$markdown.Add("$machine, $([Environment]::ProcessorCount) logical processors visible, $(Get-Date -Format 'yyyy-MM-dd HH:mm'). Each figure is the median and the worst of the runs; times in milliseconds unless named otherwise.")
if ($LowEnd) { $markdown.Add("Imitating a weak computer: two cores, below-normal priority, $MemoryCapMB MB per process, pictures drawn in software, no decoding on the graphics card.") }
try {
    foreach ($kind in $Scenario) {
        $targets = if ($plan.Contains($kind)) { $plan[$kind] } else { $Media }
        if ($kind -in 'launch', 'cold') { $targets = @('') }
        if ($kind -eq 'library') { $targets = @((Get-MeasureLibrary $LibraryFiles)) }
        $runsHere = if ($kind -in 'steady', 'music', 'ui', 'library') { [math]::Max(1, [math]::Min($Runs, 3)) } else { $Runs }
        foreach ($file in $targets) {
            $label = if ($kind -eq 'library') { "library ($LibraryFiles files)" } elseif ($file) { "$kind $(Split-Path -Leaf $file)" } else { $kind }
            $results = [Collections.Generic.List[object]]::new()
            for ($attempt = 1; $attempt -le $runsHere; $attempt++) {
                try {
                    $results.Add($(if ($kind -in 'steady', 'music', 'ui', 'library') { Invoke-DrivenRun $kind $file } else { Invoke-TimedRun $kind $file }))
                }
                catch {
                    Write-Warning "$label, run $attempt`: $($_.Exception.Message)"
                }
            }

            $collected[$label] = $results
            Write-Host ''
            Write-Host "$label ($($results.Count) of $runsHere runs)"
            $markdown.Add('')
            $markdown.Add("## $label ($($results.Count) of $runsHere runs)")
            $markdown.Add('')
            $markdown.Add('| Measure | Median | Worst |')
            $markdown.Add('|---|---:|---:|')
            $names = [Collections.Generic.List[string]]::new()
            foreach ($one in $results) { foreach ($property in $one.PSObject.Properties.Name) { if (-not $names.Contains($property)) { $names.Add($property) } } }
            foreach ($name in $names) {
                $values = @($results | ForEach-Object { $_.PSObject.Properties[$name] } | Where-Object { $_ -and $null -ne $_.Value } | ForEach-Object { [double]$_.Value })
                if ($values.Count -eq 0) { continue }
                $median = Get-Median $values
                # Worst is the highest of a cost and the lowest of a count of good things.
                $worst = if ($name -match 'presented|entries|graphics-card|processors') { ($values | Measure-Object -Minimum).Minimum } else { ($values | Measure-Object -Maximum).Maximum }
                Write-Host ('  {0,-28} median {1,9:0.0}   worst {2,9:0.0}   ({3})' -f $name, $median, $worst, (($values | ForEach-Object { $_.ToString('0.#') }) -join ', '))
                $markdown.Add(('| {0} | {1:0.#} | {2:0.#} |' -f $name, $median, $worst))
            }
        }
    }

    if ($Perf) {
        $lane = $(if ($LowEnd) { 'lowend-' } else { 'local-' }) + [Environment]::MachineName.ToLowerInvariant()
        $env:REXPLAYER_PERF_LANE = $lane
        & dotnet test --project (Join-Path $root 'tests/Rex.Media.Windows.Tests/Rex.Media.Windows.Tests.csproj') -c Release --no-build --filter-trait Category=Perf | Out-Host
        $measured = Join-Path $root "artifacts/perf/$lane.json"
        if (Test-Path $measured) {
            $suite = Get-Content -Raw $measured | ConvertFrom-Json
            $collected['perf suite'] = $suite
            $markdown.Add('')
            $markdown.Add("## Performance suite (lane $lane, fastest pass)")
            $markdown.Add('')
            $markdown.Add('| Measure | Value |')
            $markdown.Add('|---|---:|')
            foreach ($property in $suite.PSObject.Properties) { $markdown.Add(('| {0} | {1:0.##} |' -f $property.Name, [double]$property.Value)) }
        }
    }
}
finally {
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $scratch
    if ($Out) { $collected | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 $Out }
    if ($Report) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent ([IO.Path]::GetFullPath($Report))) | Out-Null
        [IO.File]::WriteAllText([IO.Path]::GetFullPath($Report), ($markdown -join "`n") + "`n")
    }
}
