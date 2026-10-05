# The one entry point for working on rexplayer. CI runs these same tasks, so a green "check" here
# means the same thing as a green build on GitHub.
#
#   ./dev.ps1 help                  what every task does
#   ./dev.ps1 build                 build everything (warnings are errors)
#   ./dev.ps1 test                  the pure suite with coverage (fast; run long suites in CI)
#   ./dev.ps1 gate                  fail unless every file in tests/coverage-required.txt is fully covered
#   ./dev.ps1 check                 build, format check, test and gate: the pre-push check
#   ./dev.ps1 format                fix formatting
#   ./dev.ps1 smoke                 run the published command line the way CI does
#   ./dev.ps1 lexicon <text-file>   check a file of commit messages for words this repository never uses
#   ./dev.ps1 package [-Version v]  build the installer and the portable zip into dist/
#   ./dev.ps1 site                  check the website
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Task = "help",
    [Parameter(Position = 1)]
    [string]$Argument,
    [string]$Configuration = "Release",
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

function Invoke-Build {
    Invoke-Checked dotnet @("build", "rexplayer.slnx", "-c", $Configuration, "-nologo")
}

function Invoke-Test {
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue (Join-Path $root "TestResults")
    Invoke-Checked dotnet @(
        "test", "--project", "tests/Rex.Media.Tests/Rex.Media.Tests.csproj", "-c", $Configuration, "--no-build", "--",
        "--coverage", "--coverage-output-format", "cobertura", "--coverage-output", "coverage.cobertura.xml",
        "--coverage-settings", "tests/coverage.settings.xml")
}

# Every listed file must have no uncovered line. Branch coverage is reported, not gated: switch
# expressions compile to branches no input can reach, and an exemption list for those would be noise.
function Invoke-Gate {
    $report = Get-ChildItem -Recurse -Filter coverage.cobertura.xml -Path $root | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $report) { throw "There is no coverage report; run ./dev.ps1 test first." }
    [xml]$coverage = Get-Content $report.FullName -Raw
    $required = Get-Content (Join-Path $root "tests/coverage-required.txt") |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith("#") }
    $classes = @($coverage.coverage.packages.package.classes.class)
    $failures = [System.Collections.Generic.List[string]]::new()
    foreach ($name in $required) {
        $matching = @($classes | Where-Object { ([string]$_.filename).Replace('\', '/').EndsWith("/" + $name) })
        if ($matching.Count -eq 0) {
            $failures.Add("${name}: absent from the coverage report")
            continue
        }

        foreach ($class in $matching) {
            foreach ($line in @($class.lines.line | Where-Object { $_ -and [int]$_.hits -eq 0 })) {
                $failures.Add("${name}:$($line.number) is not covered")
            }
        }
    }

    $branchRate = [double]$coverage.coverage.'branch-rate'
    Write-Host ("Branch coverage across measured code: {0:P1}" -f $branchRate)
    if ($failures.Count -gt 0) {
        $failures | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }
        throw "$($failures.Count) required line(s) are not covered."
    }

    Write-Host "All $($required.Count) required files have every line covered."
}

function Invoke-Smoke {
    $exe = Get-ChildItem -Recurse -Filter rexplay.exe -Path (Join-Path $root "dist"), (Join-Path $root "src/Rex.Media.Cli/bin/$Configuration") -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $exe) { throw "rexplay.exe has not been built." }
    Write-Host "Smoke testing $($exe.FullName)"

    $lines = @(& $exe.FullName agent capabilities | Where-Object { $_ -ne "" })
    if ($LASTEXITCODE -ne 0) { throw "rexplay agent capabilities failed." }
    if ($lines.Count -ne 1) { throw "Machine mode must print exactly one line; it printed $($lines.Count)." }
    $document = $lines[0] | ConvertFrom-Json
    if (-not $document.ok -or $document.protocolVersion -ne 1) { throw "The machine protocol contract is broken." }

    $fixture = Join-Path $root "tests/fixtures/smoke/tone.wav"
    $capture = Join-Path ([System.IO.Path]::GetTempPath()) ("rexplay-smoke-" + [guid]::NewGuid().ToString("N") + ".wav")
    try {
        $played = (& $exe.FullName play $fixture --aout "wav:$capture" --json) | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or -not $played.ok -or -not $played.data.finished) { throw "rexplay play did not finish the smoke fixture." }
        if ($played.data.stats.audioSamplesPlayed -ne 8000) { throw "The smoke fixture should play 8000 samples; it played $($played.data.stats.audioSamplesPlayed)." }
        $expected = (Get-Item $fixture).Length
        $actual = (Get-Item $capture).Length
        # The fixture is 16-bit mono; the capture stores 32-bit floats: twice the data, the same header.
        if ($actual -ne (($expected - 44) * 2 + 44)) { throw "The capture is $actual bytes; it should be $((($expected - 44) * 2) + 44)." }
    }
    finally {
        Remove-Item -Force -ErrorAction SilentlyContinue $capture
    }

    Write-Host "The command line keeps its contract and plays the smoke fixture exactly."
}

# Words this repository never contains, built from character codes so this file passes its own check.
function Get-BannedWords {
    return @(
        -join ([char[]](118, 108, 99)),
        -join ([char[]](118, 105, 100, 101, 111, 108, 97, 110))
    )
}

function Invoke-Lexicon([string]$Path) {
    if (-not $Path -or -not (Test-Path $Path)) { throw "Name a text file to check, such as the PR's commit messages." }
    $text = Get-Content $Path -Raw
    foreach ($word in Get-BannedWords) {
        if ($text -and $text.IndexOf($word, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "The text names something this repository never names ($($word.Length) letters). Reword it."
        }
    }

    Write-Host "The text passes the lexicon check."
}

function Invoke-Package {
    $packageVersion = if ($Version) { $Version } else { ([xml](Get-Content (Join-Path $root "Directory.Build.props") -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
    Invoke-Checked powershell.exe @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $root "installer/build.ps1"), "-Version", $packageVersion)
}

switch ($Task) {
    "help" { Get-Content $MyInvocation.MyCommand.Path | Select-Object -Skip 2 -First 13 | ForEach-Object { $_ -replace '^# ?', '' } }
    "build" { Invoke-Build }
    "test" { Invoke-Test }
    "gate" { Invoke-Gate }
    "format" { Invoke-Checked dotnet @("format", "rexplayer.slnx", "--no-restore") }
    "check" {
        Invoke-Build
        Invoke-Checked dotnet @("format", "rexplayer.slnx", "--verify-no-changes", "--no-restore")
        Invoke-Test
        Invoke-Gate
    }
    "smoke" { Invoke-Smoke }
    "lexicon" { Invoke-Lexicon $Argument }
    "package" { Invoke-Package }
    "site" {
        Invoke-Checked python @((Join-Path $root "scripts/check_site.py"))
        Invoke-Checked python @("-m", "unittest", "discover", "-s", (Join-Path $root "tests/site"), "-p", "test_*.py")
    }
    default { throw "'$Task' is not a task. Run ./dev.ps1 help." }
}
