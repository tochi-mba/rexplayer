[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$InstallDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    $installRoot = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
    if (-not [IO.Directory]::Exists($installRoot)) { exit 0 }
    $prefix = $installRoot + '\'

    function Get-InstalledProcesses {
        Get-Process -Name rexplayer, rexplay -ErrorAction SilentlyContinue | Where-Object {
            # Match the executable location, never just the name: a development build running from a
            # checkout must survive an installed-app upgrade.
            $exe = $_.Path
            $exe -and $exe.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
        }
    }

    foreach ($process in @(Get-InstalledProcesses)) {
        try {
            if (-not $process.HasExited) { $process.CloseMainWindow() | Out-Null }
            if (-not $process.WaitForExit(5000)) { $process.Kill() }
            if (-not $process.WaitForExit(10000)) { throw 'Process did not exit.' }
        }
        catch {
            if (-not $process.HasExited) { throw }
        }
        finally { $process.Dispose() }
    }

    if (@(Get-InstalledProcesses).Count -gt 0) { throw 'An installed process restarted during shutdown.' }
    exit 0
}
catch {
    Write-Error -ErrorAction Continue ("Could not prepare rexplayer for upgrade: " + $_.Exception.Message)
    exit 1
}
