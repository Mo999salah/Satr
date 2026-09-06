#ifndef AppVersion
  #define AppVersion "0.2.0-preview.1"
#endif
[Setup]
AppId={{A16AFDC9-9A8C-46BF-B0D7-6B9260D55DF3}
AppName=Satr Preview
AppVersion={#AppVersion}
AppPublisher=Mohamad Salah
AppPublisherURL=https://mohamadsala.me/
DefaultDirName={localappdata}\Programs\Satr-Preview
DefaultGroupName=Satr Preview
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=Satr-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\Satr\Satr.ico
UninstallDisplayIcon={app}\Satr.exe
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
LicenseFile=..\LICENSE
[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Files]
Source: "..\artifacts\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Satr Preview"; Filename: "{app}\Satr.exe"; WorkingDir: "{userdocs}"
; Personal drafts in LocalAppData\Satr-Preview are intentionally retained.
