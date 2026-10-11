param([switch]$Test, [switch]$Package, [switch]$SelfContained, [switch]$BundleVideoTools, [string]$OutputDirectory = 'dist\BetterSS-local')
$ErrorActionPreference = 'Stop'
$buildRoot = $PSScriptRoot
$dotnetTool = Join-Path $buildRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnetTool)) { $dotnetTool = (Get-Command dotnet -ErrorAction Stop).Source }
$outputRoot = if ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) } else { [IO.Path]::GetFullPath((Join-Path $buildRoot $OutputDirectory)) }
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
# A fresh folder prevents a previous full build leaving video/runtime files in the small package.
$buildId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,6)
$publishRoot = Join-Path $outputRoot ('app-' + $buildId)
$releaseRoot = Join-Path $outputRoot ('releases-' + $buildId)
$publishedApp = Join-Path $publishRoot 'BetterSS.exe'
$selfContainedValue = $SelfContained.IsPresent.ToString().ToLowerInvariant()
$bundleValue = $BundleVideoTools.IsPresent.ToString().ToLowerInvariant()
if ($BundleVideoTools -and (!(Test-Path -LiteralPath (Join-Path $buildRoot 'BetterSS\Tools\ffmpeg.exe')) -or !(Test-Path -LiteralPath (Join-Path $buildRoot 'BetterSS\Tools\ffprobe.exe')))) { throw 'Run scripts/setup-ffmpeg.ps1 before requesting -BundleVideoTools.' }
& $dotnetTool publish (Join-Path $buildRoot 'BetterSS\BetterSS.csproj') -c Release -r win-x64 --self-contained $selfContainedValue "-p:BundleVideoTools=$bundleValue" -p:DebugType=embedded -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
if (!$BundleVideoTools -and ((Test-Path -LiteralPath (Join-Path $publishRoot 'ffmpeg.exe')) -or (Test-Path -LiteralPath (Join-Path $publishRoot 'ffprobe.exe')))) { throw 'Video tools were unexpectedly included in the base package.' }
if (!$SelfContained -and (Test-Path -LiteralPath (Join-Path $publishRoot 'coreclr.dll'))) { throw 'The .NET runtime was unexpectedly included in the base package.' }
if ($Test) {
    $testRun = Start-Process -FilePath $publishedApp -ArgumentList '--self-test' -WorkingDirectory $buildRoot -Wait -PassThru -WindowStyle Hidden
    if ($testRun.ExitCode -ne 0) { throw 'Self-test failed; see test-results/results.txt' }
}
if ($Package) {
    $vpkTool = Join-Path $buildRoot '.tools\vpk\vpk.exe'
    if (!(Test-Path -LiteralPath $vpkTool)) {
        & $dotnetTool tool install vpk --tool-path (Join-Path $buildRoot '.tools\vpk') --version 1.2.161
        if ($LASTEXITCODE -ne 0) { throw 'Could not install the local packaging tool.' }
    }
    [xml]$projectXml = Get-Content -LiteralPath (Join-Path $buildRoot 'BetterSS\BetterSS.csproj') -Raw
    $version = [string]$projectXml.Project.PropertyGroup.Version
    $packageArguments = @('pack','--packId','BetterSS','--packVersion',$version,'--packDir',$publishRoot,'--mainExe','BetterSS.exe','--packTitle','Better SS','--packAuthors','Harsh Rawat and contributors','--icon',(Join-Path $buildRoot 'BetterSS\Assets\better-ss.ico'),'--runtime','win-x64','--outputDir',$releaseRoot,'--delta','None')
    if (!$SelfContained) { $packageArguments += @('--framework','net8.0-x64-desktop') }
    & $vpkTool @packageArguments
    if ($LASTEXITCODE -ne 0) { throw 'Installer packaging failed' }
    Write-Host "Installer: $(Join-Path $releaseRoot 'BetterSS-win-Setup.exe')"
}
@{ Application = $publishedApp; Releases = $(if ($Package) { $releaseRoot } else { $null }); SelfContained = $SelfContained.IsPresent; BundledVideoTools = $BundleVideoTools.IsPresent } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'build-info.json') -Encoding utf8
Write-Host "Ready: $publishedApp"
