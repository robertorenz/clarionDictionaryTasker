; Dictionary Tasker -- Inno Setup script
; Produces a single DictionaryTasker-Setup.exe that installs the add-in
; DLL + manifest into any combination of Clarion 10 / 11 / 11.1 / 12
; folders. The same DLL targets .NET Framework 4.0, which every one of
; those IDEs loads, so installing to multiple versions from a single run
; is supported and tested.
;
; Build with Inno Setup 6:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" install\setup.iss
; or run install\build-installer.bat from the repo root (which also
; bumps install\VERSION.txt's patch number and passes it in via
; /DAppVersion=x.y.z).

#define AppName        "Dictionary Tasker"
; AppVersion is normally passed on the command line by build-installer.bat
; (which bumps the patch in VERSION.txt each run). The #ifndef guard here
; lets ISCC.exe be invoked directly on the .iss file without the build
; script -- useful for one-offs -- in which case the fallback below is used.
#ifndef AppVersion
  #define AppVersion "1.0.1"
#endif
#define AppPublisher   "Roberto Renz"
#define AppExeName     "ClarionDctAddin.dll"
#define AppManifest    "ClarionDctAddin.addin"
#define AddinSubPath   "bin\Addins\Misc\ClarionDctAddin"
#define RepoRoot       "..\"

[Setup]
AppId={{9F8A1B5D-4E7A-4D49-9B2E-9CF6A9A9D031}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/robertorenz/clarionDictionaryTasker
DefaultDirName={autopf}\{#AppName}
; We don't actually install into DefaultDirName -- files go to the Clarion
; bin folder(s) picked on the wizard's target-selection page.
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir=dist
; Filename stays version-less so the tracked installer in git is always
; the current one. The version lives inside the .exe metadata (shown on
; the wizard header, in Apps & features, in file Properties) and in
; install/VERSION.txt.
OutputBaseFilename=DictionaryTasker-Setup
Compression=lzma2
SolidCompression=yes
; Writing into C:\clarionXX generally needs admin. If the user installed
; Clarion to a user-writable path they can still pick that path.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline dialog
WizardStyle=modern
UninstallDisplayName={#AppName} (Clarion add-in)
UninstallDisplayIcon={app}\{#AppExeName}
UsePreviousAppDir=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Payload goes into every selected Clarion's addin folder. Each entry is
; gated by a Check: function so we only copy where the user asked.
Source: "{#RepoRoot}ClarionDctAddin\bin\Debug\{#AppExeName}";   DestDir: "{code:GetC12AddinFolder}"; Flags: ignoreversion; Check: InstallForC12
Source: "{#RepoRoot}ClarionDctAddin\{#AppManifest}";             DestDir: "{code:GetC12AddinFolder}"; Flags: ignoreversion; Check: InstallForC12
Source: "{#RepoRoot}ClarionDctAddin\bin\Debug\{#AppExeName}";   DestDir: "{code:GetC111AddinFolder}"; Flags: ignoreversion; Check: InstallForC111
Source: "{#RepoRoot}ClarionDctAddin\{#AppManifest}";             DestDir: "{code:GetC111AddinFolder}"; Flags: ignoreversion; Check: InstallForC111
Source: "{#RepoRoot}ClarionDctAddin\bin\Debug\{#AppExeName}";   DestDir: "{code:GetC11AddinFolder}"; Flags: ignoreversion; Check: InstallForC11
Source: "{#RepoRoot}ClarionDctAddin\{#AppManifest}";             DestDir: "{code:GetC11AddinFolder}"; Flags: ignoreversion; Check: InstallForC11
Source: "{#RepoRoot}ClarionDctAddin\bin\Debug\{#AppExeName}";   DestDir: "{code:GetC10AddinFolder}"; Flags: ignoreversion; Check: InstallForC10
Source: "{#RepoRoot}ClarionDctAddin\{#AppManifest}";             DestDir: "{code:GetC10AddinFolder}"; Flags: ignoreversion; Check: InstallForC10
; Stub file for Inno's uninstaller anchor -- lands in whichever folder
; ends up being {app} (see CurStepChanged below).
Source: "{#RepoRoot}install\readme-installed.txt";               DestDir: "{app}"; Flags: ignoreversion

[UninstallDelete]
Type: files;      Name: "{code:GetC12AddinFolder}\{#AppExeName}";  Check: InstallForC12
Type: files;      Name: "{code:GetC12AddinFolder}\{#AppManifest}"; Check: InstallForC12
Type: dirifempty; Name: "{code:GetC12AddinFolder}";                 Check: InstallForC12
Type: files;      Name: "{code:GetC111AddinFolder}\{#AppExeName}"; Check: InstallForC111
Type: files;      Name: "{code:GetC111AddinFolder}\{#AppManifest}"; Check: InstallForC111
Type: dirifempty; Name: "{code:GetC111AddinFolder}";                Check: InstallForC111
Type: files;      Name: "{code:GetC11AddinFolder}\{#AppExeName}";  Check: InstallForC11
Type: files;      Name: "{code:GetC11AddinFolder}\{#AppManifest}"; Check: InstallForC11
Type: dirifempty; Name: "{code:GetC11AddinFolder}";                 Check: InstallForC11
Type: files;      Name: "{code:GetC10AddinFolder}\{#AppExeName}";  Check: InstallForC10
Type: files;      Name: "{code:GetC10AddinFolder}\{#AppManifest}"; Check: InstallForC10
Type: dirifempty; Name: "{code:GetC10AddinFolder}";                 Check: InstallForC10

[Code]
const
  ClarionSubPath = '\bin\Addins\Misc\ClarionDctAddin';
  // Field indexes into PathsPage.Values[] -- keep in sync with the Add()
  // calls in InitializeWizard below.
  IDX_C12  = 0;
  IDX_C111 = 1;
  IDX_C11  = 2;
  IDX_C10  = 3;

var
  PathsPage: TInputDirWizardPage;

function FirstExisting(Candidates: TArrayOfString; Fallback: String): String;
var
  I: Integer;
begin
  for I := 0 to GetArrayLength(Candidates) - 1 do
  begin
    if DirExists(Candidates[I] + '\bin') then
    begin
      Result := Candidates[I];
      Exit;
    end;
  end;
  Result := Fallback;
end;

// Return the detected path if we find a matching install on disk, else
// empty string -- so the wizard pre-fills only versions that are actually
// installed, and leaves the rest blank for the user to skip.
function DetectOrEmpty(Candidates: TArrayOfString): String;
var
  Found: String;
begin
  Found := FirstExisting(Candidates, '');
  if (Length(Found) > 0) and DirExists(Found + '\bin') then
    Result := Found
  else
    Result := '';
end;

function DetectClarion12: String;
var
  C: TArrayOfString;
begin
  SetArrayLength(C, 4);
  C[0] := ExpandConstant('{sd}\clarion12');
  C[1] := ExpandConstant('{pf}\SoftVelocity\Clarion 12');
  C[2] := ExpandConstant('{pf32}\SoftVelocity\Clarion 12');
  C[3] := 'C:\clarion12';
  Result := DetectOrEmpty(C);
end;

function DetectClarion111: String;
var
  C: TArrayOfString;
begin
  SetArrayLength(C, 4);
  C[0] := ExpandConstant('{sd}\clarion11.1');
  C[1] := ExpandConstant('{pf}\SoftVelocity\Clarion 11.1');
  C[2] := ExpandConstant('{pf32}\SoftVelocity\Clarion 11.1');
  C[3] := 'C:\clarion11.1';
  Result := DetectOrEmpty(C);
end;

function DetectClarion11: String;
var
  C: TArrayOfString;
begin
  SetArrayLength(C, 4);
  C[0] := ExpandConstant('{sd}\clarion11');
  C[1] := ExpandConstant('{pf}\SoftVelocity\Clarion 11');
  C[2] := ExpandConstant('{pf32}\SoftVelocity\Clarion 11');
  C[3] := 'C:\clarion11';
  Result := DetectOrEmpty(C);
end;

function DetectClarion10: String;
var
  C: TArrayOfString;
begin
  SetArrayLength(C, 4);
  C[0] := ExpandConstant('{sd}\clarion10');
  C[1] := ExpandConstant('{pf}\SoftVelocity\Clarion 10');
  C[2] := ExpandConstant('{pf32}\SoftVelocity\Clarion 10');
  C[3] := 'C:\clarion10';
  Result := DetectOrEmpty(C);
end;

// A target counts as "selected" iff the user left a path in and that
// path has a real bin\ subfolder. Blank = skip that version.
function IsValidTarget(Idx: Integer): Boolean;
var
  Path: String;
begin
  Path := PathsPage.Values[Idx];
  Result := (Length(Trim(Path)) > 0) and DirExists(Trim(Path) + '\bin');
end;

function InstallForC12:  Boolean; begin Result := IsValidTarget(IDX_C12);  end;
function InstallForC111: Boolean; begin Result := IsValidTarget(IDX_C111); end;
function InstallForC11:  Boolean; begin Result := IsValidTarget(IDX_C11);  end;
function InstallForC10:  Boolean; begin Result := IsValidTarget(IDX_C10);  end;

function GetC12AddinFolder(Param: String):  String; begin Result := Trim(PathsPage.Values[IDX_C12])  + ClarionSubPath; end;
function GetC111AddinFolder(Param: String): String; begin Result := Trim(PathsPage.Values[IDX_C111]) + ClarionSubPath; end;
function GetC11AddinFolder(Param: String):  String; begin Result := Trim(PathsPage.Values[IDX_C11])  + ClarionSubPath; end;
function GetC10AddinFolder(Param: String):  String; begin Result := Trim(PathsPage.Values[IDX_C10])  + ClarionSubPath; end;

procedure InitializeWizard;
begin
  PathsPage := CreateInputDirPage(
    wpWelcome,
    'Clarion installation(s)',
    'Where are the Clarion IDEs installed on this machine?',
    'Dictionary Tasker works with Clarion 10, 11, 11.1, and 12 -- the same ' +
    'DLL targets .NET Framework 4.0, which every version loads. Fill in the ' +
    'top-level folder for each Clarion you want to install into; leave a line ' +
    'blank to skip that version. Installed versions are pre-filled automatically.' + #13#10 + #13#10 +
    'Important: close every Clarion IDE you''re targeting before clicking Next -- ' +
    'the IDE holds a lock on its add-in DLLs while running.',
    False,
    'ClarionFolders');
  PathsPage.Add('Clarion 12:');
  PathsPage.Add('Clarion 11.1:');
  PathsPage.Add('Clarion 11:');
  PathsPage.Add('Clarion 10:');

  PathsPage.Values[IDX_C12]  := DetectClarion12;
  PathsPage.Values[IDX_C111] := DetectClarion111;
  PathsPage.Values[IDX_C11]  := DetectClarion11;
  PathsPage.Values[IDX_C10]  := DetectClarion10;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  AnyValid: Boolean;
  Path: String;
  I: Integer;
  Labels: TArrayOfString;
begin
  Result := True;
  if CurPageID <> PathsPage.ID then
    Exit;

  SetArrayLength(Labels, 4);
  Labels[IDX_C12]  := 'Clarion 12';
  Labels[IDX_C111] := 'Clarion 11.1';
  Labels[IDX_C11]  := 'Clarion 11';
  Labels[IDX_C10]  := 'Clarion 10';

  AnyValid := False;
  for I := 0 to 3 do
  begin
    Path := Trim(PathsPage.Values[I]);
    if Length(Path) = 0 then
      Continue;
    if not DirExists(Path + '\bin') then
    begin
      MsgBox('No "bin" subfolder was found under the ' + Labels[I] + ' path:' + #13#10 +
             Path + #13#10 + #13#10 +
             'Pick the top-level ' + Labels[I] + ' folder (the one that ' +
             'contains "bin" directly below it), or clear the line to skip ' +
             'installing for ' + Labels[I] + '.',
             mbError, MB_OK);
      Result := False;
      Exit;
    end;
    AnyValid := True;
  end;

  if not AnyValid then
  begin
    MsgBox('Fill in the path for at least one Clarion version -- no targets were selected.',
           mbError, MB_OK);
    Result := False;
  end;
end;

// {app} anchors the uninstaller. Use whichever selected version comes
// first (C12 > C11.1 > C11 > C10) so the readme + uninstall hook land
// in a stable, preferred folder.
function GetPrimaryAddinFolder: String;
begin
  if InstallForC12 then
    Result := GetC12AddinFolder('')
  else if InstallForC111 then
    Result := GetC111AddinFolder('')
  else if InstallForC11 then
    Result := GetC11AddinFolder('')
  else
    Result := GetC10AddinFolder('');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    // Redirect {app} to the primary addin folder so [Files] that use {app}
    // (and the uninstaller registration) both land in the right place.
    WizardForm.DirEdit.Text := GetPrimaryAddinFolder;
  end;
end;
