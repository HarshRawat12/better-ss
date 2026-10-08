param([switch]$Background, [switch]$Stop, [switch]$Walkthrough, [switch]$Child)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSCommandPath
$projectFile = Join-Path $projectRoot 'BetterSS\BetterSS.csproj'
$stateFolder = Join-Path $projectRoot '.validation\live'
$stateFile = Join-Path $stateFolder 'process.json'
New-Item -ItemType Directory -Force -Path $stateFolder | Out-Null

function Get-LiveWatcher {
    if (!(Test-Path -LiteralPath $stateFile)) { return $null }
    try {
        $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        $process = Get-Process -Id $state.ProcessId -ErrorAction Stop
        $details = Get-CimInstance Win32_Process -Filter "ProcessId=$($state.ProcessId)"
        if ($process.StartTime.ToUniversalTime().Ticks.ToString() -ne $state.StartTicks -or $details.CommandLine.IndexOf($PSCommandPath, [StringComparison]::OrdinalIgnoreCase) -lt 0 -or !$details.CommandLine.Contains('-Child')) { return $null }
        return $process
    } catch { return $null }
}

$existing = Get-LiveWatcher
if ($Stop) {
    if ($existing) {
        & taskkill.exe /PID $existing.Id /T /F | Out-Null
        Write-Host 'Stopped the Better SS development watcher and its app.'
    } else { Write-Host 'Better SS live development is not running.' }
    if (Test-Path -LiteralPath $stateFile) { Remove-Item -LiteralPath $stateFile }
    exit 0
}
if (!$Child -and $existing) {
    if (Get-Process -Name 'BetterSS' -ErrorAction SilentlyContinue) {
        Write-Host "Better SS live development is already running (watcher $($existing.Id))."
        exit 0
    }
    # Quit from the tray leaves dotnet-watch waiting for a file change.
    # Opening this launcher again should bring the app back immediately.
    & taskkill.exe /PID $existing.Id /T /F | Out-Null
    Remove-Item -LiteralPath $stateFile
}
if (Get-Process -Name 'BetterSS' -ErrorAction SilentlyContinue) {
    throw 'Another Better SS instance is running. Quit it from the tray before opening live development.'
}
if ($Background) {
    $shellExecutable = (Get-Process -Id $PID).Path
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'), '-Child')
    if ($Walkthrough) { $arguments += '-Walkthrough' }
    $process = Start-Process -FilePath $shellExecutable -ArgumentList $arguments -WorkingDirectory $projectRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $stateFolder 'stdout.log') -RedirectStandardError (Join-Path $stateFolder 'stderr.log') -PassThru
    @{ ProcessId = $process.Id; StartTicks = $process.StartTime.ToUniversalTime().Ticks.ToString(); Project = $projectFile } | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding utf8
    Write-Host "Better SS live development started. Source saves rebuild and restart it. Logs: $stateFolder"
    exit 0
}
$dotnetTool = (Get-Command dotnet -ErrorAction Stop).Source
$env:DOTNET_WATCH_SUPPRESS_EMOJIS = 'true'
$appArguments = @('--dev')
if ($Walkthrough) { $appArguments += '--walkthrough' }
Write-Host 'Starting Better SS live development. Save C# files to rebuild and relaunch.'
Write-Host 'This app uses a separate dev-settings.json profile and does not require installation.'
& $dotnetTool watch --no-hot-reload --no-launch-profile --project $projectFile run -- @appArguments
exit $LASTEXITCODE

