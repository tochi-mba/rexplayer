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

# MP3 from LAME through FFmpeg, each with a reference decode by FFmpeg's own decoder as 24-bit PCM,
# already trimmed of the encoder delay and padding the LAME tag declares.
$mp3 = Join-Path $root 'tests/fixtures/mp3'
New-Item -ItemType Directory -Force $mp3 | Out-Null

function Invoke-Mp3 {
    param([string]$Source, [string[]]$Options, [string]$Name)
    $output = Join-Path $mp3 "$Name.mp3"
    Invoke-Encoder -Source $Source -Options (@('-c:a', 'libmp3lame') + $Options) -Output $output
    $reference = Join-Path $mp3 "$Name.reference.wav"
    & $Ffmpeg -hide_banner -loglevel error -y -i $output -c:a pcm_s24le -map_metadata -1 -fflags +bitexact -flags:a +bitexact $reference
    if ($LASTEXITCODE -ne 0) {
        throw "ffmpeg failed to decode $output."
    }

    Write-Host "Wrote $reference ($((Get-Item $reference).Length) bytes)."
}

# MPEG-1, constant bitrate, joint stereo: two tones with a little noise.
Invoke-Mp3 -Name 'stereo-44k-128k-cbr' `
    -Source 'aevalsrc=0.5*sin(2*PI*440*t)+0.02*(random(0)-0.5)|0.4*sin(2*PI*660*t)+0.02*(random(1)-0.5):s=44100:d=0.4' `
    -Options @('-b:a', '128k')

# Sharp bursts every 100 ms, so the encoder switches to short blocks around each one.
Invoke-Mp3 -Name 'stereo-44k-vbr-bursts' `
    -Source "aevalsrc='0.7*sin(2*PI*1500*t)*lt(mod(t,0.1),0.01)+0.05*sin(2*PI*220*t)|0.6*(random(0)-0.5)*lt(mod(t+0.05,0.1),0.008)':s=44100:d=0.4" `
    -Options @('-q:a', '2')

# Mono at 48 kHz, variable bitrate.
Invoke-Mp3 -Name 'mono-48k-vbr' `
    -Source 'aevalsrc=0.6*sin(2*PI*(300+3000*t)*t):s=48000:d=0.3' `
    -Options @('-q:a', '5')

# MPEG-2 (lower sampling frequencies): one granule per frame and 9-bit scalefactor compression.
Invoke-Mp3 -Name 'stereo-22k-64k-mpeg2' `
    -Source 'aevalsrc=0.5*sin(2*PI*330*t)+0.03*(random(0)-0.5)|0.5*sin(2*PI*495*t):s=22050:d=0.4' `
    -Options @('-b:a', '64k')

# MPEG 2.5 at 8 kHz.
Invoke-Mp3 -Name 'mono-8k-16k-mpeg25' `
    -Source 'aevalsrc=0.5*sin(2*PI*400*t)+0.3*sin(2*PI*1100*t):s=8000:d=0.5' `
    -Options @('-b:a', '16k')
