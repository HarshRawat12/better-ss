param([switch]$Test, [string]$OutputDirectory = 'dist\BetterSS-release')
$ErrorActionPreference = 'Stop'
$buildRoot = $PSScriptRoot
$dotnetTool = Join-Path $buildRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnetTool)) { $dotnetTool = (Get-Command dotnet -ErrorAction Stop).Source }
$ffmpeg = Join-Path $buildRoot 'BetterSS\Tools\ffmpeg.exe'
$ffprobe = Join-Path $buildRoot 'BetterSS\Tools\ffprobe.exe'
if (!(Test-Path -LiteralPath $ffmpeg) -or !(Test-Path -LiteralPath $ffprobe)) {
    Write-Warning 'FFmpeg tools are missing. Run .\scripts\setup-ffmpeg.ps1 to enable recording and video editing.'
}
$publishRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { [System.IO.Path]::GetFullPath((Join-Path $buildRoot $OutputDirectory)) }
$publishedApp = Join-Path $publishRoot 'BetterSS.exe'
if (Get-Process BetterSS -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $publishedApp }) { throw 'The output executable is running. Quit it first or choose another -OutputDirectory.' }
& $dotnetTool publish (Join-Path $buildRoot 'BetterSS\BetterSS.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
if ($Test) {
    $testRun = Start-Process -FilePath $publishedApp -ArgumentList '--self-test' -WorkingDirectory $buildRoot -Wait -PassThru -WindowStyle Hidden
    if ($testRun.ExitCode -ne 0) { throw 'Self-test failed; see test-results/results.txt' }
}
Write-Host "Ready: $publishedApp"
