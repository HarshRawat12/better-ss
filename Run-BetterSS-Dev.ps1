$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSCommandPath
$projectFile = Join-Path $projectRoot 'BetterSS\BetterSS.csproj'

if (Get-Process -Name 'BetterSS' -ErrorAction SilentlyContinue) {
    Write-Host 'Better SS is already running. Quit it from the system tray, then run this launcher again.' -ForegroundColor Yellow
    exit 1
}

Write-Host 'Starting Better SS in watch mode. Save C# files to rebuild and relaunch it.' -ForegroundColor Cyan
& dotnet watch --no-hot-reload --project $projectFile run
