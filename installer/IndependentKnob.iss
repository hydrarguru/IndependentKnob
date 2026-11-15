#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{6B1F3C52-7D1E-4A57-9C0B-2E4A8D5F1A93}
AppName=IndependentKnob
AppVersion={#AppVersion}
AppPublisher=hydrarguru
DefaultDirName={autopf}\IndependentKnob
DefaultGroupName=IndependentKnob
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\Assets\icon.ico
UninstallDisplayIcon={app}\IndependentKnob.exe
LicenseFile=..\LICENSE
OutputDir=..\publish
OutputBaseFilename=IndependentKnob-Setup
Compression=lzma2
SolidCompression=yes

[Files]
Source: "..\publish\IndependentKnob.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\IndependentKnob"; Filename: "{app}\IndependentKnob.exe"
Name: "{autodesktop}\IndependentKnob"; Filename: "{app}\IndependentKnob.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Run]
Filename: "{app}\IndependentKnob.exe"; Description: "Start IndependentKnob"; Flags: nowait postinstall skipifsilent
