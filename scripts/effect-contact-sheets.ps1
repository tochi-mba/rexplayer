# Renders every picture effect and look over frames of your own videos into labelled contact
# sheets, with a report of how steady each one is from one picture to the next. It runs
# EffectContactSheets in the Windows test suite, which skips itself unless these are set.
#
#   ./scripts/effect-contact-sheets.ps1 -Sources "day|D:\clip.mp4|800;night|D:\film.mkv|120"
#
# Each source is "label|file|seconds", optionally followed by "|bars" (black bars painted into the
# top and bottom, as a letterboxed recording has) or "|crop=x,y" (draw at the picture's own size
# and show the part around x,y, each 0 to 1, one screen pixel per picture pixel).
# -Only takes effect names (VideoEffect members) separated by commas; -Extremes draws each effect
# at the ends of every control. Sheets go to artifacts/effect-sheets (ignored by git): never commit
# them or the frames.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Sources,
    [string]$Out = "artifacts/effect-sheets",
    [string]$Only = "",
    [switch]$Extremes,
    [string]$Tile = "480x270",
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

dotnet build tests/Rex.Media.Windows.Tests/Rex.Media.Windows.Tests.csproj -c $Configuration -nologo
if ($LASTEXITCODE -ne 0) { throw "The Windows tests did not build." }

$env:REXPLAYER_EFFECT_SHEETS = Join-Path $root $Out
$env:REXPLAYER_EFFECT_SOURCES = $Sources
$env:REXPLAYER_EFFECT_ONLY = $Only
$env:REXPLAYER_EFFECT_EXTREMES = if ($Extremes) { "1" } else { "" }
$env:REXPLAYER_EFFECT_TILE = $Tile
$tests = Get-ChildItem "tests/Rex.Media.Windows.Tests/bin/$Configuration" -Recurse -Filter Rex.Media.Windows.Tests.exe | Select-Object -First 1
& $tests.FullName --filter-method "*DrawEveryEffectOverRealPictures*"
if ($LASTEXITCODE -ne 0) { throw "Drawing the contact sheets failed." }
Write-Host "Contact sheets and the steadiness report are in $($env:REXPLAYER_EFFECT_SHEETS)"
