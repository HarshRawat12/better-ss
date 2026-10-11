@echo off
setlocal

rem Always run from the repository root, even when launched by double-click.
cd /d "%~dp0"

echo Closing any running Better SS instance...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$processes = @(Get-Process -Name 'BetterSS' -ErrorAction SilentlyContinue); if ($processes.Count -gt 0) { $processes | Stop-Process -Force; Start-Sleep -Milliseconds 500 }"
if errorlevel 1 (
    echo Could not close the running Better SS instance.
    pause
    exit /b 1
)

echo Starting Better SS live development...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run-BetterSS-Dev.ps1" -Background
if errorlevel 1 (
    echo Better SS live development could not be started.
    pause
    exit /b 1
)

echo Better SS live development is running. Save C# files to rebuild and relaunch it.
endlocal
exit /b 0
