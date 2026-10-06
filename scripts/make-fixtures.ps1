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

# MP4 and QuickTime, from FFmpeg's muxer. Audio rexplayer decodes itself gets a reference decode by
# FFmpeg (edit lists applied); video and AAC are for the Windows decoders.
$mp4 = Join-Path $root 'tests/fixtures/mp4'
New-Item -ItemType Directory -Force $mp4 | Out-Null

function Invoke-Mp4 {
    param([string]$Source, [string[]]$Options, [string]$Name, [switch]$Reference, [string[]]$Inputs = @(), [string[]]$Before = @())
    $output = Join-Path $mp4 $Name
    & $Ffmpeg -hide_banner -loglevel error -y @Before -f lavfi -i $Source @Inputs @Options -fflags +bitexact -flags:a +bitexact -flags:v +bitexact $output
    if ($LASTEXITCODE -ne 0) {
        throw "ffmpeg failed to write $output."
    }

    Write-Host "Wrote $output ($((Get-Item $output).Length) bytes)."
    if ($Reference) {
        $wav = Join-Path $mp4 "$([System.IO.Path]::GetFileNameWithoutExtension($Name)).reference.wav"
        & $Ffmpeg -hide_banner -loglevel error -y -i $output -map 0:a -c:a pcm_s24le -map_metadata -1 -fflags +bitexact -flags:a +bitexact $wav
        if ($LASTEXITCODE -ne 0) {
            throw "ffmpeg failed to decode $output."
        }

        Write-Host "Wrote $wav ($((Get-Item $wav).Length) bytes)."
    }
}

$tones = 'aevalsrc=0.5*sin(2*PI*440*t)|0.4*sin(2*PI*660*t):s=44100:d=0.2'
Invoke-Mp4 -Name 'mp3-in-mp4.mp4' -Source $tones -Options @('-c:a', 'libmp3lame', '-b:a', '128k', '-map_metadata', '-1') -Reference
Invoke-Mp4 -Name 'flac-in-mp4.mp4' -Source $tones -Options @('-c:a', 'flac', '-strict', 'experimental', '-map_metadata', '-1') -Reference
Invoke-Mp4 -Name 'pcm-s16le.mov' -Source $tones -Options @('-c:a', 'pcm_s16le', '-map_metadata', '-1') -Reference
Invoke-Mp4 -Name 'pcm-s24be.mov' -Source $tones -Options @('-c:a', 'pcm_s24be', '-map_metadata', '-1') -Reference
Invoke-Mp4 -Name 'pcm-f32le.mov' -Source 'aevalsrc=0.5*sin(2*PI*440*t):s=22050:d=0.2' -Options @('-c:a', 'pcm_f32le', '-map_metadata', '-1') -Reference
Invoke-Mp4 -Name 'alac.m4a' -Source $tones -Options @('-c:a', 'alac', '-map_metadata', '-1')

# Video with sound: a test pattern and tones, H.264 with B-frames and AAC, tagged and chaptered.
# SEI units are stripped: the encoder writes its name and web address into one, and fixtures carry
# no third-party names.
$chapters = Join-Path ([System.IO.Path]::GetTempPath()) 'rexplayer-chapters.txt'
Set-Content -Path $chapters -Encoding utf8 -Value @(
    ';FFMETADATA1', 'title=Basquiat', 'artist=Asake', 'album=Lungu Boy', 'date=2024',
    '[CHAPTER]', 'TIMEBASE=1/1000', 'START=0', 'END=200', 'title=Intro',
    '[CHAPTER]', 'TIMEBASE=1/1000', 'START=200', 'END=400', 'title=Verse')
$video = @('-f', 'lavfi', '-i', 'aevalsrc=0.3*sin(2*PI*440*t):s=48000:d=0.4', '-f', 'ffmetadata', '-i', $chapters)
$common = @('-map', '0:v', '-map', '1:a', '-map_metadata', '2', '-map_chapters', '2', '-c:v', 'libx264', '-preset', 'veryfast', '-g', '5', '-bf', '2', '-pix_fmt', 'yuv420p', '-bsf:v', 'filter_units=remove_types=6', '-c:a', 'aac', '-b:a', '64k')
Invoke-Mp4 -Name 'h264-aac.mp4' -Source 'testsrc2=size=128x72:rate=25:duration=0.4' -Inputs $video -Options ($common + @('-movflags', '+faststart'))
Invoke-Mp4 -Name 'h264-aac-fragmented.mp4' -Source 'testsrc2=size=128x72:rate=25:duration=0.4' -Inputs $video -Options ($common + @('-movflags', '+frag_keyframe+empty_moov+default_base_moof'))
Invoke-Mp4 -Name 'h264-rotated.mov' -Source 'testsrc2=size=128x72:rate=25:duration=0.2' -Before @('-display_rotation', '90', '-noautorotate') -Options @('-c:v', 'libx264', '-preset', 'veryfast', '-pix_fmt', 'yuv420p', '-bsf:v', 'filter_units=remove_types=6', '-map_metadata', '-1')

# Matroska and WebM, from FFmpeg's muxers.
$mkv = Join-Path $root 'tests/fixtures/mkv'
New-Item -ItemType Directory -Force $mkv | Out-Null
$temp = [System.IO.Path]::GetTempPath()
$subtitles = Join-Path $temp 'rexplayer-subtitles.srt'
Set-Content -Path $subtitles -Encoding utf8 -Value @('1', '00:00:00,000 --> 00:00:00,200', 'Hello', '', '2', '00:00:00,200 --> 00:00:00,400', 'World')
$cover = Join-Path $temp 'rexplayer-cover.png'
& $Ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'color=c=0xD7FF3F:s=8x8:d=0.04' -frames:v 1 -fflags +bitexact $cover
Set-Content -Path $chapters -Encoding utf8 -Value @(
    ';FFMETADATA1', 'title=Terminator', 'artist=Asake',
    '[CHAPTER]', 'TIMEBASE=1/1000', 'START=0', 'END=200', 'title=Intro',
    '[CHAPTER]', 'TIMEBASE=1/1000', 'START=200', 'END=400', 'title=Verse')

function Invoke-Mkv {
    param([string]$Name, [string[]]$Arguments, [switch]$Reference)
    $output = Join-Path $mkv $Name
    & $Ffmpeg -hide_banner -loglevel error -y @Arguments -fflags +bitexact -flags:a +bitexact -flags:v +bitexact $output
    if ($LASTEXITCODE -ne 0) {
        throw "ffmpeg failed to write $output."
    }

    Write-Host "Wrote $output ($((Get-Item $output).Length) bytes)."
    if ($Reference) {
        $index = 0
        foreach ($stream in @(& $Ffmpeg -hide_banner -i $output 2>&1 | Select-String 'Audio:')) {
            $wav = Join-Path $mkv "$([System.IO.Path]::GetFileNameWithoutExtension($Name)).audio$index.reference.wav"
            & $Ffmpeg -hide_banner -loglevel error -y -i $output -map "0:a:$index" -c:a pcm_s24le -map_metadata -1 -fflags +bitexact -flags:a +bitexact $wav
            Write-Host "Wrote $wav ($((Get-Item $wav).Length) bytes)."
            $index++
        }
    }
}

$pattern = @('-f', 'lavfi', '-i', 'testsrc2=size=128x72:rate=25:duration=0.4')
$tone48 = @('-f', 'lavfi', '-i', 'aevalsrc=0.3*sin(2*PI*440*t):s=48000:d=0.4')
Invoke-Mkv -Name 'h264-aac-subtitles.mkv' -Arguments ($pattern + $tone48 + @('-i', $subtitles, '-f', 'ffmetadata', '-i', $chapters,
    '-map', '0:v', '-map', '1:a', '-map', '2:s', '-map_metadata', '3', '-map_chapters', '3', '-attach', $cover, '-metadata:s:t', 'mimetype=image/png', '-metadata:s:t', 'filename=cover.png',
    '-c:v', 'libx264', '-preset', 'veryfast', '-g', '5', '-bf', '2', '-pix_fmt', 'yuv420p', '-bsf:v', 'filter_units=remove_types=6',
    '-c:a', 'aac', '-b:a', '64k', '-c:s', 'srt', '-metadata:s:a', 'language=yor', '-metadata:s:s', 'language=eng'))
Invoke-Mkv -Name 'flac-mp3-pcm.mkv' -Reference -Arguments (@('-f', 'lavfi', '-i', $tones, '-f', 'lavfi', '-i', $tones, '-f', 'lavfi', '-i', $tones,
    '-map', '0:a', '-map', '1:a', '-map', '2:a', '-c:a:0', 'flac', '-c:a:1', 'libmp3lame', '-b:a:1', '128k', '-c:a:2', 'pcm_s16le', '-map_metadata', '-1'))
Invoke-Mkv -Name 'vp9-opus.webm' -Arguments (@('-f', 'lavfi', '-i', 'testsrc2=size=128x72:rate=25:duration=0.4') + $tone48 + @('-c:v', 'libvpx-vp9', '-deadline', 'realtime', '-b:v', '200k', '-c:a', 'libopus', '-b:a', '48k', '-map_metadata', '-1'))
Invoke-Mkv -Name 'live-opus.webm' -Arguments ($tone48 + @('-c:a', 'libopus', '-b:a', '48k', '-live', '1', '-cluster_time_limit', '100', '-map_metadata', '-1'))
Remove-Item $subtitles, $cover, $chapters

# Raw H.264 and HEVC streams, each beside what FFmpeg's own parser reads from it, so rexplayer's
# parameter-set parsers are checked against an independent reader. SEI units are removed: the
# encoders write their names and options into them.
$video = Join-Path $root 'tests/fixtures/video'
New-Item -ItemType Directory -Force $video | Out-Null
function Invoke-Video {
    param([string]$Name, [string]$Size, [string]$Rate, [string[]]$Options)
    $output = Join-Path $video $Name
    $codec = if ($Options -contains 'libx264') { 'h264' } else { 'hevc' }
    $format = if ($Name.EndsWith('.mp4')) { 'mp4' } else { $codec }
    $sei = if ($codec -eq 'h264') { 'filter_units=remove_types=6' } else { 'filter_units=remove_types=39|40' }
    & $Ffmpeg -hide_banner -loglevel error -y -f lavfi -i "testsrc2=size=$($Size):rate=$($Rate):duration=0.1" @Options -frames:v 2 -bsf:v $sei -map_metadata -1 -fflags +bitexact -flags:v +bitexact -f $format $output
    if ($LASTEXITCODE -ne 0) {
        throw "ffmpeg failed to write $output."
    }

    $probe = Join-Path $video "$([System.IO.Path]::GetFileNameWithoutExtension($Name)).probe.json"
    $fields = 'stream=profile,level,width,height,sample_aspect_ratio,pix_fmt,color_range,color_space,color_transfer,color_primaries,field_order'
    & ($Ffmpeg -replace 'ffmpeg(\.exe)?$', 'ffprobe$1') -v error -select_streams v:0 -show_entries $fields -of json $output | Set-Content -Encoding utf8 $probe
    Write-Host "Wrote $output ($((Get-Item $output).Length) bytes) and $probe."
}

$x264 = @('-c:v', 'libx264', '-preset', 'veryfast')
Invoke-Video -Name 'h264-high-420.h264' -Size '128x72' -Rate '25' -Options ($x264 + @('-pix_fmt', 'yuv420p', '-vf', 'setsar=4/3', '-color_primaries', 'bt709', '-color_trc', 'bt709', '-colorspace', 'bt709', '-color_range', 'tv'))
Invoke-Video -Name 'h264-high422-10bit-interlaced.h264' -Size '96x64' -Rate '25' -Options ($x264 + @('-pix_fmt', 'yuv422p10le', '-flags', '+ilme+ildct', '-x264-params', 'tff=1', '-color_range', 'pc'))
Invoke-Video -Name 'h264-high444-scaling.h264' -Size '64x48' -Rate '30000/1001' -Options ($x264 + @('-pix_fmt', 'yuv444p', '-x264-params', 'cqm=jvt', '-vf', 'setsar=40/33'))
Invoke-Video -Name 'h264-baseline.h264' -Size '64x48' -Rate '24' -Options ($x264 + @('-pix_fmt', 'yuv420p', '-profile:v', 'baseline'))
Invoke-Video -Name 'h264-gray.h264' -Size '64x48' -Rate '25' -Options ($x264 + @('-pix_fmt', 'gray'))
$x265 = @('-c:v', 'libx265', '-preset', 'veryfast', '-x265-params')
Invoke-Video -Name 'hevc-main.hevc' -Size '130x74' -Rate '25' -Options ($x265 + @('log-level=error', '-pix_fmt', 'yuv420p', '-vf', 'setsar=16/11'))
Invoke-Video -Name 'hevc-main10-hdr.hevc' -Size '128x72' -Rate '50' -Options ($x265 + @('log-level=error:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc:range=full', '-pix_fmt', 'yuv420p10le'))
Invoke-Video -Name 'hevc-444-layers.hevc' -Size '64x48' -Rate '30' -Options ($x265 + @('log-level=error:temporal-layers=3:scaling-list=default:ref=3:bframes=3', '-pix_fmt', 'yuv444p'))
Invoke-Video -Name 'hevc-field.hevc' -Size '64x48' -Rate '25' -Options ($x265 + @('log-level=error:interlace=tff', '-pix_fmt', 'yuv422p'))
Invoke-Video -Name 'hevc-in-mp4.mp4' -Size '130x74' -Rate '25' -Options ($x265 + @('log-level=error', '-pix_fmt', 'yuv420p', '-vf', 'setsar=16/11', '-tag:v', 'hvc1'))

# FFmpeg's own decode of the AAC in the video fixtures, for checking the decoder Windows supplies.
foreach ($pair in @(@('mp4', 'h264-aac.mp4'), @('mkv', 'h264-aac-subtitles.mkv'))) {
    $source = Join-Path $root "tests/fixtures/$($pair[0])/$($pair[1])"
    $wav = Join-Path $root "tests/fixtures/$($pair[0])/$([System.IO.Path]::GetFileNameWithoutExtension($pair[1])).aac.reference.wav"
    & $Ffmpeg -hide_banner -loglevel error -y -i $source -map 0:a:0 -c:a pcm_s24le -map_metadata -1 -fflags +bitexact -flags:a +bitexact $wav
    if ($LASTEXITCODE -ne 0) {
        throw "ffmpeg failed to write $wav."
    }

    Write-Host "Wrote $wav ($((Get-Item $wav).Length) bytes)."
}

# FFmpeg's MD5 of every decoded picture (as NV12, in presentation order). H.264 and HEVC decoding is
# exact, so the pictures Windows' decoders give must hash the same.
foreach ($fixture in @('mp4/h264-aac.mp4', 'mkv/h264-aac-subtitles.mkv', 'video/hevc-in-mp4.mp4')) {
    $source = Join-Path $root "tests/fixtures/$fixture"
    $hashes = Join-Path $root "tests/fixtures/$([System.IO.Path]::ChangeExtension($fixture, '.nv12.framemd5'))"
    & $Ffmpeg -hide_banner -loglevel error -y -i $source -map 0:v:0 -pix_fmt nv12 -fflags +bitexact -f framemd5 $hashes
    if ($LASTEXITCODE -ne 0) {
        throw "ffmpeg failed to write $hashes."
    }

    Write-Host "Wrote $hashes."
}
