[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$toolsDirectory = Join-Path $projectRoot 'BetterSS\Tools'
$ffmpegTarget = Join-Path $toolsDirectory 'ffmpeg.exe'
$ffprobeTarget = Join-Path $toolsDirectory 'ffprobe.exe'

if (!$Force -and (Test-Path -LiteralPath $ffmpegTarget) -and (Test-Path -LiteralPath $ffprobeTarget)) {
    Write-Host 'FFmpeg is already installed for Better SS. Use -Force to refresh it.'
    exit 0
}

$downloadBase = 'https://www.gyan.dev/ffmpeg/builds'
$archiveUrl = "$downloadBase/ffmpeg-release-essentials.zip"
$hashUrl = "$archiveUrl.sha256"
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('better-ss-ffmpeg-' + [guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $temporaryDirectory 'ffmpeg-release-essentials.zip'
$extractPath = Join-Path $temporaryDirectory 'extracted'

try {
    New-Item -ItemType Directory -Path $temporaryDirectory, $extractPath, $toolsDirectory -Force | Out-Null
    Write-Host 'Downloading the current FFmpeg release essentials build...'
    Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath
    $hashText = (Invoke-WebRequest -Uri $hashUrl).Content
    $expectedHash = [regex]::Match($hashText, '(?i)\b[0-9a-f]{64}\b').Value
    if (!$expectedHash) { throw 'The FFmpeg checksum response did not contain a SHA-256 hash.' }
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) { throw 'The downloaded FFmpeg archive failed SHA-256 verification.' }

    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath -Force
    $ffmpegSource = Get-ChildItem -Path $extractPath -Filter ffmpeg.exe -File -Recurse | Select-Object -First 1
    $ffprobeSource = Get-ChildItem -Path $extractPath -Filter ffprobe.exe -File -Recurse | Select-Object -First 1
    if (!$ffmpegSource -or !$ffprobeSource) { throw 'The FFmpeg archive did not contain ffmpeg.exe and ffprobe.exe.' }

    Copy-Item -LiteralPath $ffmpegSource.FullName -Destination $ffmpegTarget -Force
    Copy-Item -LiteralPath $ffprobeSource.FullName -Destination $ffprobeTarget -Force
    Write-Host "FFmpeg tools are ready in $toolsDirectory"
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
