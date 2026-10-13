<#
.SYNOPSIS
Measures how long rexplayer takes to start, to show its first picture and play its first sound, to
seek and to move to the next episode, from the timing lines it writes to its log.

.DESCRIPTION
rexplayer writes a "timing" line to its log at each moment of a launch and of opening media (see
Rex.Media.Diagnostics.Milestones): the window drawn, the first picture presented, the first sound
written to the device, each with the milliseconds since the process started. REXPLAYER_EXIT_AFTER
makes it exit once the moments a run waits for have come, so this script can launch it again and
again without the window tests. Commands (seek, next episode, open) go through the automation pipe.

Every run gets a scratch data folder (REXPLAYER_ROOT) and a pipe of its own (REXPLAYER_PIPE_NAME),
so it never touches the real settings, never hands its file to a player already running, and plays
with the volume at zero.

Scenarios:
  launch  start with nothing to open, until the window is interactive
  play    rexplayer.exe <file>, until its first picture and first sound
  open    start empty, then open <file> as the library or the open dialog does
  seek    play <file>, then jump a minute forward
  next    play <file>, then move to the next episode in its folder

.PARAMETER Exe
The rexplayer.exe to measure; by default the Release build's.

.PARAMETER Media
The files to play. Audio files (by extension) wait for sound only.

.PARAMETER Runs
How many times each scenario runs; the report gives the median and the worst.

.PARAMETER Seed
A data folder to copy into each run's scratch folder (settings, library and memory as a real user
has them); by default each run starts with only the settings that keep dialogs away.

.PARAMETER Cold
Copies the program folder with unbuffered I/O before every run, so its files are read from the disk
rather than from memory, the nearest an unelevated session gets to a first start after a reboot.

.PARAMETER Out
A JSON file to write every run's moments to.
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
    [string]$Out
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $Exe) {
    $Exe = Get-ChildItem -Recurse -Filter rexplayer.exe -Path (Join-Path $root 'src/Rex.Media.App/bin/Release') -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Exe -or -not (Test-Path $Exe)) { throw 'rexplayer.exe has not been built; run ./dev.ps1 build or pass -Exe.' }
$Exe = (Resolve-Path $Exe).Path
if ($Media.Count -eq 0) {
    $Media = @((Join-Path $root 'tests/fixtures/mp4/h264-aac.mp4'), (Join-Path $root 'tests/fixtures/mp3/stereo-44k-128k-cbr.mp3'))
}
$Media = @($Media | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })

$version = (Get-Item $Exe).VersionInfo.ProductVersion -replace '\+.*$', ''
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('rexplayer-measure-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$audioExtensions = @('.mp3', '.flac', '.wav', '.ogg', '.opus', '.m4a', '.aac', '.aiff', '.wma')
$timing = [regex]'\[INFO\] timing: (?<name>[\w-]+) at (?<ms>[\d.]+) ms'

function New-DataRoot {
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
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 20))
    Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $data 'resume.json'), (Join-Path $data 'resume.json.rex-backup')
    return $data
}

function Get-ProgramCopy {
    if (-not $Cold) { return $Exe }
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

function Wait-Moments([string]$data, [hashtable]$needed, [Diagnostics.Process]$process) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $moments = Read-Moments $data
        $missing = @($needed.Keys | Where-Object { @($moments | Where-Object Name -eq $_).Count -lt $needed[$_] })
        if ($missing.Count -eq 0) { return $moments }
        if ($process.HasExited) { throw "rexplayer exited before $($missing -join ', ')." }
        Start-Sleep -Milliseconds 20
    }

    throw "Timed out waiting for $($needed.Keys -join ', ')."
}

function Send-Request([string]$pipeName, [string]$json) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', $pipeName, [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(10000)
        $bytes = [Text.Encoding]::UTF8.GetBytes($json + "`n")
        $pipe.Write($bytes, 0, $bytes.Length)
        $pipe.Flush()
        $reply = [IO.StreamReader]::new($pipe).ReadLine()
        if ($reply -notmatch '"ok":true') { throw "The player refused $json`: $reply" }
    }
    finally { $pipe.Dispose() }
}

function Invoke-Run([string]$kind, [string]$file) {
    $data = New-DataRoot
    $program = Get-ProgramCopy
    $audioOnly = $file -and ($audioExtensions -contains [IO.Path]::GetExtension($file).ToLowerInvariant())
    $played = if ($audioOnly) { 'first-audio' } else { 'first-frame+first-audio' }
    $exitAfter = switch ($kind) {
        'launch' { 'interactive' }
        'play' { $played }
        'open' { $played }
        default { if ($audioOnly) { 'first-audio*2' } else { 'first-frame*2+first-audio*2' } }
    }

    $info = [Diagnostics.ProcessStartInfo]::new($program)
    $info.UseShellExecute = $false
    if ($kind -ne 'launch' -and $kind -ne 'open') { $info.ArgumentList.Add($file) }
    $pipeName = 'rexplayer-measure-' + [guid]::NewGuid().ToString('N')
    $info.Environment['REXPLAYER_ROOT'] = $data
    $info.Environment['REXPLAYER_PIPE_NAME'] = $pipeName
    $info.Environment['REXPLAYER_EXIT_AFTER'] = $exitAfter
    $asked = [DateTime]::Now
    $process = [Diagnostics.Process]::Start($info)
    try {
        $created = ($process.StartTime - $asked).TotalMilliseconds
        $firstPlayed = if ($audioOnly) { @{ 'first-audio' = 1 } } else { @{ 'first-frame' = 1; 'first-audio' = 1 } }
        switch ($kind) {
            'open' {
                Wait-Moments $data @{ 'interactive' = 1 } $process | Out-Null
                Send-Request $pipeName (@{ command = 'open'; paths = @($file) } | ConvertTo-Json -Compress)
            }
            'seek' {
                Wait-Moments $data $firstPlayed $process | Out-Null
                Start-Sleep -Milliseconds 1500
                Send-Request $pipeName '{"command":"run","id":"jump-forward-medium"}'
            }
            'next' {
                Wait-Moments $data $firstPlayed $process | Out-Null
                Start-Sleep -Milliseconds 1500
                Send-Request $pipeName '{"command":"run","id":"next-episode"}'
            }
        }

        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw "rexplayer did not reach $exitAfter within $TimeoutSeconds s." }
        $moments = Read-Moments $data
    }
    finally {
        if (-not $process.HasExited) { $process.Kill() }
        $process.Dispose()
    }

    $result = [ordered]@{ 'process-created' = $created }
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
        'seek' {
            $seek = Find-Moment $moments 'seek'
            foreach ($name in @('first-frame', 'target-frame', 'first-audio')) {
                $at = Find-Moment $moments $name 2
                if ($null -eq $at -and $name -eq 'target-frame') { $at = Find-Moment $moments $name 1 }
                if ($null -ne $at -and $null -ne $seek -and $at -ge $seek) { $result["seek>$name"] = $at - $seek }
            }
        }
        'next' {
            $start = Find-Moment $moments 'start' 2
            foreach ($name in @('open', 'opened', 'first-frame', 'first-audio')) {
                $at = Find-Moment $moments $name 2
                if ($null -ne $at -and $null -ne $start) { $result["next>$name"] = $at - $start }
            }
        }
    }

    if ($Cold) { Remove-Item -Recurse -Force -ErrorAction SilentlyContinue (Split-Path -Parent $program) }
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $data
    return [pscustomobject]$result
}

function Get-Median([double[]]$values) {
    $sorted = @($values | Sort-Object)
    $middle = [math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

$report = [ordered]@{}
try {
    foreach ($kind in $Scenario) {
        $targets = if ($kind -eq 'launch') { @($null) } else { $Media }
        foreach ($file in $targets) {
            $label = if ($file) { "$kind $(Split-Path -Leaf $file)" } else { $kind }
            $runs = [Collections.Generic.List[object]]::new()
            for ($run = 1; $run -le $Runs; $run++) {
                try {
                    $runs.Add((Invoke-Run $kind $file))
                }
                catch {
                    Write-Warning "$label, run $run`: $($_.Exception.Message)"
                }
            }

            $report[$label] = $runs
            Write-Host ''
            Write-Host "$label ($($runs.Count) of $Runs runs)"
            $names = [Collections.Generic.List[string]]::new()
            foreach ($one in $runs) { foreach ($property in $one.PSObject.Properties.Name) { if (-not $names.Contains($property)) { $names.Add($property) } } }
            foreach ($name in $names) {
                $values = @($runs | ForEach-Object { $_.PSObject.Properties[$name] } | Where-Object { $_ } | ForEach-Object { [double]$_.Value })
                if ($values.Count -eq 0) { continue }
                Write-Host ('  {0,-26} median {1,8:0.0} ms   worst {2,8:0.0} ms   ({3})' -f $name, (Get-Median $values), ($values | Measure-Object -Maximum).Maximum, (($values | ForEach-Object { $_.ToString('0') }) -join ', '))
            }
        }
    }
}
finally {
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $scratch
    if ($Out) { $report | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 $Out }
}
