# Measures how well the visualisations' beat lands on real music at several frame rates (AU-18).
#
#   ./scripts/measure-beats.ps1 -Music <folder> [-Rates 24,30,60,144] [-Out <folder>]
#
# For each audio file under -Music: rexplay decodes it to a WAV capture, rexplay pulse hears it at
# each frame rate, and scripts/beat-reference.cs (an offline reference: spectral-flux onsets after
# Bello et al. 2005, an autocorrelation tempo weighted as in Ellis 2007, and Ellis's dynamic
# programming beats) scores the beats against its own. Files are named by index only, so the
# output never shows what someone's music is called. Run it in the background: it takes minutes.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Music,
    [int[]]$Rates = @(24, 30, 60, 144),
    [string]$Out = (Join-Path ([System.IO.Path]::GetTempPath()) "rexplayer-beats")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exe = Get-ChildItem -Recurse -Filter rexplay.exe -Path (Join-Path $root "src/Rex.Media.Cli/bin/Release") |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe) { throw "Build rexplay first: dotnet build src/Rex.Media.Cli -c Release" }
New-Item -ItemType Directory -Force $Out | Out-Null
# Built outside the repository, so the product's analyzers and build settings do not apply to a
# measuring tool.
$reference = Join-Path $Out "reference"
$source = Join-Path $Out "reference-source"
New-Item -ItemType Directory -Force $source | Out-Null
Copy-Item (Join-Path $root "scripts/beat-reference.cs") $source -Force
& dotnet build (Join-Path $source "beat-reference.cs") -c Release -o $reference | Out-Null
if ($LASTEXITCODE -ne 0) { throw "The reference did not build." }

$files = @(Get-ChildItem -Recurse -File $Music | Where-Object { $_.Extension -in ".flac", ".mp3", ".m4a", ".wav", ".ogg", ".opus" } | Sort-Object FullName)
for ($i = 0; $i -lt $files.Count; $i++) {
    $capture = Join-Path $Out "$i.wav"
    if (-not (Test-Path $capture)) {
        & $exe.FullName play $files[$i].FullName --aout "wav:$capture" --json | Out-Null
    }

    $heard = @()
    foreach ($rate in $Rates) {
        $json = Join-Path $Out "$i-$rate.json"
        & $exe.FullName pulse $files[$i].FullName --fps $rate --json | Set-Content -Encoding utf8 $json
        $heard += $json
    }

    foreach ($line in (& (Join-Path $reference "beat-reference.exe") $capture @heard)) {
        Write-Output "song $i $line"
    }
}
