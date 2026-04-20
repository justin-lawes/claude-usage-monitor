; Installer.iss - Inno Setup script for Claude Usage Monitor.
; Build with:  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" Installer.iss
; Produces:    Output\ClaudeUsageMonitorSetup.exe
;
; Prereq: dotnet publish ClaudeUsageMonitor.csproj -c Release -r win-x64 --self-contained false -o publish3

#define AppName     "Claude Usage Monitor"
#define AppId       "ClaudeUsageMonitor"
#define AppVersion  "1.0.0"
#define AppPublisher "Justin Lawes"
#define AppExe      "ClaudeUsageMonitor.exe"

[Setup]
AppId={{B4F1D0F2-8E2F-4F4D-9B9B-CLAUDEUSAGEMON}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={userappdata}\..\Local\Programs\{#AppId}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=ClaudeUsageMonitorSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}

[Files]
Source: "publish3\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked
Name: "startupicon"; Description: "Launch at Windows startup"; GroupDescription: "Startup:"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "{#AppId}"; ValueData: """{app}\{#AppExe}"""; \
    Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "KillApp"
