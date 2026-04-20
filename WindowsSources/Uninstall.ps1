# Uninstall.ps1 - Removes Claude Usage Monitor for the current user.
#
# Run:  powershell -ExecutionPolicy Bypass -File .\Uninstall.ps1

$ErrorActionPreference = 'Continue'

$AppName    = 'Claude Usage Monitor'
$AppId      = 'ClaudeUsageMonitor'
$InstallDir = Join-Path $env:LOCALAPPDATA "Programs\$AppId"
$StartMenu  = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$Shortcut   = Join-Path $StartMenu "$AppName.lnk"
$RunKey     = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Get-Process -Name 'ClaudeUsageMonitor' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Stopping running instance (PID $($_.Id))..."
    $_ | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

if (Test-Path $Shortcut)   { Remove-Item $Shortcut   -Force; Write-Host "Removed Start Menu shortcut." }
Remove-ItemProperty -Path $RunKey -Name $AppId -ErrorAction SilentlyContinue
Write-Host "Removed auto-start entry."

if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force; Write-Host "Removed $InstallDir." }

Write-Host ""
Write-Host "Uninstalled. (Settings in %LocalAppData%\ClaudeUsageMonitor were kept. Delete that folder to fully reset.)"
