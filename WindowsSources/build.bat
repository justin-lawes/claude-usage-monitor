@echo off
REM Build script for Claude Usage Monitor (Windows)
REM Requires .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0

echo === Building Claude Usage Monitor (Windows) ===

REM Restore packages
echo Restoring NuGet packages...
dotnet restore

REM Build release
echo Building release...
dotnet build -c Release

REM Publish self-contained
echo Publishing self-contained executable...
dotnet publish -c Release -r win-x64 --self-contained -o .\publish

echo.
echo === Build complete ===
echo Output: .\publish\ClaudeUsageMonitor.exe
echo.
echo To install: copy the publish folder to your desired location
echo To run at startup: enable "Launch at login" in Settings
pause
