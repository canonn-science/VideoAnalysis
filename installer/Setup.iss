#ifndef MyAppVersion
#define MyAppVersion "0.0.0-dev"
#endif

[Setup]
AppId={{CFC2672E-E79F-4601-902B-1C5822235D0A}}
AppName=Rotation Analysis Lab
AppVersion={#MyAppVersion}
AppPublisher=Canonn
AppPublisherURL=https://github.com/canonn-science/RotationAnalysis
DefaultDirName={autopf}\RotationAnalysisLab
DefaultGroupName=Rotation Analysis Lab
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=RotationAnalysisLab-{#MyAppVersion}-win-x64-Setup
SetupIconFile=..\src\RotationAnalysis.App\Assets\canonn.ico
UninstallDisplayIcon={app}\RotationAnalysis.App.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Rotation Analysis Lab"; Filename: "{app}\RotationAnalysis.App.exe"
Name: "{commondesktop}\Rotation Analysis Lab"; Filename: "{app}\RotationAnalysis.App.exe"; Tasks: desktopicon
Name: "{group}\Uninstall Rotation Analysis Lab"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\RotationAnalysis.App.exe"; Description: "Launch Rotation Analysis Lab"; Flags: nowait postinstall skipifsilent
