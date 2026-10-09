# Publishes rexplayer self-contained for x64 and wraps it two ways: a per-user installer
# (dist\rexplayer-Setup-<version>.exe) and a portable zip (dist\rexplayer-<version>-win-x64.zip),
# each with a .sha256 file the release job and the in-app updater verify.
[CmdletBinding()]
param([string]$Version = "0.0.0")

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-.][0-9A-Za-z.-]+)?$') { throw "'$Version' is not a version." }

$root = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..")
$dist = Join-Path $root "dist"
$payload = Join-Path $dist "rexplayer"
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Path $payload | Out-Null

$projects = @("src/Rex.Media.Cli/Rex.Media.Cli.csproj", "src/Rex.Media.Updater/Rex.Media.Updater.csproj")
$app = Join-Path $root "src/Rex.Media.App/Rex.Media.App.csproj"
if (Test-Path $app) { $projects += "src/Rex.Media.App/Rex.Media.App.csproj" }

foreach ($project in $projects) {
    Write-Host "Publishing $project"
    & dotnet publish (Join-Path $root $project) -c Release -r win-x64 --self-contained true -p:Version=$Version -o $payload -nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Publishing $project failed." }
}

# The window cannot start without its resource index and compiled XAML; never ship it without them.
if (Test-Path $app) {
    foreach ($needed in @("rexplayer.exe", "rexupdate.exe", "rexplayer.pri", "App.xbf", "MainWindow.xbf")) {
        if (-not (Test-Path (Join-Path $payload $needed))) { throw "The app was published without $needed, so it would not start." }
    }
}

$iscc = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue)
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 is not installed; get it from https://jrsoftware.org/isdl.php." }

& $iscc "/DAppVersion=$Version" "/DSource=$dist" "/O$dist" /Q (Join-Path $root "installer\rexplayer.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }

$zip = Join-Path $dist "rexplayer-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $payload "*") -DestinationPath $zip

foreach ($file in @((Join-Path $dist "rexplayer-Setup-$Version.exe"), $zip)) {
    $hash = (Get-FileHash -Algorithm SHA256 $file).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$file.sha256", "$hash  $(Split-Path -Leaf $file)`n", [Text.Encoding]::ASCII)
    Write-Host "Built $(Split-Path -Leaf $file) ($hash)"
}
