# Build script for Claude Usage Monitor (Windows)
# Requires .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0

Write-Host "=== Building Claude Usage Monitor (Windows) ===" -ForegroundColor Cyan

# Restore packages
Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore
if ($LASTEXITCODE -ne 0) { Write-Host "Restore failed!" -ForegroundColor Red; exit 1 }

# Build release
Write-Host "Building release..." -ForegroundColor Yellow
dotnet build -c Release
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed!" -ForegroundColor Red; exit 1 }

# Publish self-contained
Write-Host "Publishing self-contained executable..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained -o .\publish
if ($LASTEXITCODE -ne 0) { Write-Host "Publish failed!" -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "=== Build complete ===" -ForegroundColor Green
Write-Host "Output: .\publish\ClaudeUsageMonitor.exe"
Write-Host ""
Write-Host "To install: copy the publish folder to your desired location"
Write-Host "To run at startup: enable 'Launch at login' in Settings"
