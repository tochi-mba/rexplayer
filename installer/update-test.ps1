# Installs the built installer, checks rexplayer is offered in Explorer's "Open with", runs the
# installer again the way rexplayer installs an update of itself and checks that the update opens
# rexplayer again, then uninstalls and checks the offer is gone. For CI's throwaway machines only:
# it installs into a temporary folder and changes the user's file associations while it runs.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Installer)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$work = Join-Path ([IO.Path]::GetTempPath()) ("rexplayer-update-test-" + [guid]::NewGuid().ToString('N'))
$installDir = Join-Path $work 'app'
$prefix = $installDir + '\'

# The reopened player keeps its settings in the work folder and plays nothing aloud.
$env:REXPLAYER_ROOT = Join-Path $work 'data'
$env:REXPLAYER_FAKE_AUDIO = '1'

function Get-Installed {
    @(Get-Process -Name rexplayer -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
}

function Test-OpenWith {
    $null -ne (Get-ItemProperty -Path 'HKCU:\Software\Classes\.mp4\OpenWithProgids' -Name 'rexplayer.video' -ErrorAction SilentlyContinue) -and
    (Test-Path 'HKCU:\Software\Classes\Applications\rexplayer.exe\shell\open\command')
}

function Invoke-Installer([string[]]$Arguments) {
    $process = Start-Process -FilePath $Installer -ArgumentList $Arguments -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "The installer exited with $($process.ExitCode)." }
}

try {
    Invoke-Installer @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$installDir`"")
    if (-not (Test-Path (Join-Path $installDir 'rexplayer.exe'))) { throw 'rexplayer.exe was not installed.' }
    Start-Sleep -Seconds 3
    if (@(Get-Installed).Count -gt 0) { throw 'A silent install opened rexplayer; only an update should.' }
    if (-not (Test-OpenWith)) { throw 'rexplayer is not offered in "Open with" for .mp4 files.' }

    Invoke-Installer @('/SILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CLOSEAPPLICATIONS', '/relaunch=1', "/DIR=`"$installDir`"")
    $deadline = (Get-Date).AddSeconds(30)
    while (@(Get-Installed).Count -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if (@(Get-Installed).Count -eq 0) { throw 'The update did not open rexplayer again.' }
    Write-Host 'The update installed and opened rexplayer again.'

    Get-Installed | ForEach-Object { $_.Kill(); $_.WaitForExit(10000) | Out-Null }
    Start-Process -FilePath (Join-Path $installDir 'unins000.exe') -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait | Out-Null
    if (Test-OpenWith) { throw 'Uninstalling left rexplayer in "Open with".' }
    Write-Host 'Uninstalling took rexplayer out of "Open with".'
}
finally {
    Get-Installed | ForEach-Object { $_.Kill(); $_.WaitForExit(10000) | Out-Null }
    $uninstaller = Join-Path $installDir 'unins000.exe'
    if (Test-Path $uninstaller) {
        Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait | Out-Null
    }

    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
