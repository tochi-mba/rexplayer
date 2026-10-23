# Compares two independent Windows UI suite runs against the same offline scene contracts.
# The files are not promoted to baselines. They remain review evidence.
param(
    [string]$FirstLane = "ci-windows",
    [string]$SecondLane = "ci-windows-repeat",
    [int]$PixelThreshold = 40,
    [double]$MaxDifferingRatio = 0.005
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationCore
$inventory = Get-Content "tests/screenshot-scenes.json" -Raw | ConvertFrom-Json
$owners = @("LibraryFixtureVisualTests", "LibraryEvidenceEdgeTests")
$scenes = @($inventory.scenes | Where-Object { $_.owner -in $owners })
if ($scenes.Count -lt 7) { throw "Expected at least seven stable offline fixture scenes." }

function Read-Picture([string]$path) {
    if (-not (Test-Path $path)) { throw "Missing screenshot: $path" }
    $stream = [System.IO.File]::OpenRead($path)
    try {
        $decoder = [System.Windows.Media.Imaging.BitmapDecoder]::Create(
            $stream,
            [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
            [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        $frame = [System.Windows.Media.Imaging.FormatConvertedBitmap]::new(
            $decoder.Frames[0],
            [System.Windows.Media.PixelFormats]::Bgra32,
            $null,
            0)
        $stride = $frame.PixelWidth * 4
        $pixels = [byte[]]::new($stride * $frame.PixelHeight)
        $frame.CopyPixels($pixels, $stride, 0)
        return @{ Width = $frame.PixelWidth; Height = $frame.PixelHeight; Pixels = $pixels }
    }
    finally {
        $stream.Dispose()
    }
}

$errors = [System.Collections.Generic.List[string]]::new()
foreach ($scene in $scenes) {
    $firstPath = "artifacts/screenshots/$FirstLane/actual/$($scene.id).png"
    $secondPath = "artifacts/screenshots/$SecondLane/actual/$($scene.id).png"
    try {
        $firstEvidence = Get-Content "artifacts/screenshots/$FirstLane/actual/$($scene.id).json" -Raw | ConvertFrom-Json
        $secondEvidence = Get-Content "artifacts/screenshots/$SecondLane/actual/$($scene.id).json" -Raw | ConvertFrom-Json
        foreach ($evidence in @($firstEvidence, $secondEvidence)) {
            if ($evidence.sceneId -ne $scene.id -or $evidence.fixture -ne $scene.fixture) {
                throw "Independent run has the wrong scene identity or fixture."
            }
            if ($evidence.readinessAchieved -ne $true -or $evidence.pixelReadinessAchieved -ne $true) {
                throw "Independent run lacks semantic or pixel readiness evidence."
            }
            if ($evidence.viewportMatchesDeclared -ne $true -or $evidence.fullyOnDesktop -ne $true) {
                throw "Independent run has a mismatched viewport or clips the virtual screen."
            }
        }
        if ($firstEvidence.commit -ne $secondEvidence.commit) {
            throw "Repeated screenshots came from different source revisions."
        }
        $first = Read-Picture $firstPath
        $second = Read-Picture $secondPath
        if ($first.Width -ne $second.Width -or $first.Height -ne $second.Height) {
            throw "Viewport size changed from $($first.Width)x$($first.Height) to $($second.Width)x$($second.Height)."
        }

        $pixels = $first.Pixels.Length / 4
        $differing = 0
        for ($i = 0; $i -lt $first.Pixels.Length; $i += 4) {
            $delta = [Math]::Max(
                [Math]::Abs([int]$first.Pixels[$i] - [int]$second.Pixels[$i]),
                [Math]::Max(
                    [Math]::Abs([int]$first.Pixels[$i + 1] - [int]$second.Pixels[$i + 1]),
                    [Math]::Abs([int]$first.Pixels[$i + 2] - [int]$second.Pixels[$i + 2])))
            if ($delta -gt $PixelThreshold) { $differing++ }
        }

        $ratio = $differing / [double]$pixels
        Write-Host "$($scene.id): $differing / $pixels pixels differ ($($ratio.ToString('P3'))) across independent process runs."
        if ($ratio -gt $MaxDifferingRatio) {
            throw "Difference exceeds $($MaxDifferingRatio.ToString('P3'))."
        }
    }
    catch {
        $errors.Add("$($scene.id): $_")
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Host "REPEATABILITY FAILURE: $_" }
    throw "$($errors.Count) offline fixture scene(s) changed between complete app launches."
}
Write-Host "All $($scenes.Count) stable offline fixtures matched across independent Windows UI runs."
