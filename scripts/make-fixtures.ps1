<#
.SYNOPSIS
Regenerates the committed media fixtures that come from an independent encoder.

.DESCRIPTION
rexplayer's decoders are tested two ways: against files the test kit builds (every coding path,
on demand) and against files from an encoder rexplayer did not write, so a misreading of the
specification shared by our encoder and our decoder cannot hide. This script makes the second kind
with the FFmpeg command line, a black-box tool needed only here, never to build or test.

Every signal is a formula, so running the script again rewrites the same audio. The tests do not
depend on the exact bytes: each FLAC fixture carries its encoder's MD5 of the source samples, and
the test compares rexplayer's decoded output against that.

.PARAMETER Ffmpeg
The ffmpeg executable to use; by default the one on PATH.
#>
param(
    [string]$Ffmpeg = 'ffmpeg'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$flac = Join-Path $root 'tests/fixtures/flac'
New-Item -ItemType Directory -Force $flac | Out-Null

function Invoke-Encoder {
    param([string]$Source, [string[]]$Options, [string]$Output)
    & $Ffmpeg -hide_banner -loglevel error -y -f lavfi -i $Source @Options -map_metadata -1 -fflags +bitexact -flags:a +bitexact $Output
    if ($LASTEXITCODE -ne 0) {
        throw "ffmpeg failed to write $Output."
    }

    Write-Host "Wrote $Output ($((Get-Item $Output).Length) bytes)."
}

# Two tones with a little deterministic noise, so the encoder needs real prediction and residuals.
Invoke-Encoder `
    -Source 'aevalsrc=0.5*sin(2*PI*440*t)+0.02*(random(0)-0.5)|0.4*sin(2*PI*660*t)+0.02*(random(1)-0.5):s=44100:d=0.6' `
    -Options @('-c:a', 'flac', '-sample_fmt', 's16', '-compression_level', '8') `
    -Output (Join-Path $flac 'stereo-16bit-44k-lpc.flac')

# A rising sweep at 24 bits with the highest LPC orders the encoder will use.
Invoke-Encoder `
    -Source 'aevalsrc=0.7*sin(2*PI*(200+2000*t)*t):s=96000:d=0.25' `
    -Options @('-c:a', 'flac', '-sample_fmt', 's32', '-bits_per_raw_sample', '24', '-compression_level', '12') `
    -Output (Join-Path $flac 'mono-24bit-96k-sweep.flac')

# Six channels, each its own tone, to check the speaker order end to end.
Invoke-Encoder `
    -Source 'aevalsrc=0.3*sin(2*PI*300*t)|0.3*sin(2*PI*400*t)|0.3*sin(2*PI*500*t)|0.3*sin(2*PI*60*t)|0.3*sin(2*PI*700*t)|0.3*sin(2*PI*800*t):s=48000:d=0.2:c=5.1' `
    -Options @('-c:a', 'flac', '-sample_fmt', 's16', '-compression_level', '5') `
    -Output (Join-Path $flac 'surround-51-16bit-48k.flac')

# Forced mid/side decorrelation with only the fixed predictors.
Invoke-Encoder `
    -Source 'aevalsrc=0.6*sin(2*PI*220*t)|0.55*sin(2*PI*220*t+0.3):s=22050:d=0.5' `
    -Options @('-c:a', 'flac', '-sample_fmt', 's16', '-compression_level', '0', '-ch_mode', 'mid_side') `
    -Output (Join-Path $flac 'stereo-16bit-22k-midside-fixed.flac')
