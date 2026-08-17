#define MyAppName "BarTender 标签打印工具"
#define MyAppPublisher "tall-1997"
#define MyAppExeName "BarTenderPrinter.exe"
#ifndef MyAppVersion
#define MyAppVersion GetVersionNumbersString("..\publish\BarTenderPrinter.exe")
#endif

[Setup]
AppId={{AA293069-3471-49F7-A52A-7976253617BC}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\BarTenderPrinter
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\BarTenderPrinter\icon.ico
WizardStyle=modern dynamic
Compression=lzma2
SolidCompression=yes
OutputDir=..\installer-output
OutputBaseFilename=BarTenderPrinter-Setup-v{#MyAppVersion}-win-x64
CloseApplications=yes
RestartApplications=no
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "*MobileMes*,*HASP*,*Sentinel*,*SafeNet*,*Hardlock*,*Dongle*,sync-profile.dat,*.btpsync,sync.db,sync-incoming\*,*\sync-incoming\*,template-cache\*,*\template-cache\*,sync-staging\*,*\sync-staging\*,direct-sync-certificates\*,*\direct-sync-certificates\*,*sync-diagnostic*,*sync_diagnostic*,*syncdiagnostic*,*diagnostic-sync*,*diagnostic_sync*,*.pfx,*.pfx.dat,*.p12,*.pem,*.key,*.cer,*.crt,*.der,*.p7b,*.p7c,*.snk,*.jks,*.keystore,*.db,*.sqlite,*.sqlite3,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.btw\shell\OpenWithBarTenderPrinter"; ValueType: string; ValueName: ""; ValueData: "使用 BarTenderPrinter 打开"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.btw\shell\OpenWithBarTenderPrinter"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.btw\shell\OpenWithBarTenderPrinter\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent
