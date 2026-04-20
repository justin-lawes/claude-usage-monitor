# Install.ps1 - Installs Claude Usage Monitor for the current user.
# - Copies publish3\ to %LocalAppData%\Programs\ClaudeUsageMonitor
# - Creates a Start Menu shortcut
# - Registers auto-start (HKCU Run key)
# - Launches the app
#
# Run from the WindowsSources folder:
#   powershell -ExecutionPolicy Bypass -File .\Install.ps1

$ErrorActionPreference = 'Stop'

$AppName    = 'Claude Usage Monitor'
$AppId      = 'ClaudeUsageMonitor'
$SourceDir  = Join-Path $PSScriptRoot 'publish3'
$InstallDir = Join-Path $env:LOCALAPPDATA "Programs\$AppId"
$ExePath    = Join-Path $InstallDir 'ClaudeUsageMonitor.exe'
$StartMenu  = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$Shortcut   = Join-Path $StartMenu "$AppName.lnk"
$RunKey     = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

if (-not (Test-Path $SourceDir)) {
    throw "publish3 not found at $SourceDir. Build first:  dotnet publish ClaudeUsageMonitor.csproj -c Release -r win-x64 --self-contained false -o publish3"
}

# Stop any running instance so we can overwrite the exe.
Get-Process -Name 'ClaudeUsageMonitor' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Stopping running instance (PID $($_.Id))..."
    $_ | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

Write-Host "Installing to $InstallDir ..."
if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item -Path (Join-Path $SourceDir '*') -Destination $InstallDir -Recurse -Force

Write-Host "Creating Start Menu shortcut ..."
$wsh = New-Object -ComObject WScript.Shell
$lnk = $wsh.CreateShortcut($Shortcut)
$lnk.TargetPath       = $ExePath
$lnk.WorkingDirectory = $InstallDir
$lnk.IconLocation     = $ExePath
$lnk.Description      = 'Monitor Claude AI usage limits in real time'
$lnk.Save()

Write-Host "Registering auto-start ..."
New-ItemProperty -Path $RunKey -Name $AppId -Value "`"$ExePath`"" -PropertyType String -Force | Out-Null

Write-Host "Launching $AppName ..."
Start-Process -FilePath $ExePath -WorkingDirectory $InstallDir

Write-Host ""
Write-Host "Done. Look for the icon in the system tray (click the ^ near the clock if hidden)."
Write-Host "To uninstall: run Uninstall.ps1"
