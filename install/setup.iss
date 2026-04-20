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
  // Slot indexes -- keep in sync with the Add() order in InitializeWizard.
  IDX_C12  = 0;
  IDX_C111 = 1;
  IDX_C11  = 2;
  IDX_C10  = 3;

var
  // Page 1: checkbox list -- user picks which Clarion versions to target.
  SelectPage: TInputOptionWizardPage;
  // Page 2: path editor -- only shown when a ticked version wasn't auto-
  // detected (or the user wants to adjust a detected path via the Back button).
  PathsPage: TInputDirWizardPage;
  // Auto-detection results cached at wizard start so the checkbox labels
  // and the path page pre-fills stay consistent.
  DetectedC12, DetectedC111, DetectedC11, DetectedC10: String;

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

function DetectedPathFor(Idx: Integer): String;
begin
  case Idx of
    IDX_C12:  Result := DetectedC12;
    IDX_C111: Result := DetectedC111;
    IDX_C11:  Result := DetectedC11;
  else
    Result := DetectedC10;
  end;
end;

function LabelFor(Idx: Integer): String;
begin
  case Idx of
    IDX_C12:  Result := 'Clarion 12';
    IDX_C111: Result := 'Clarion 11.1';
    IDX_C11:  Result := 'Clarion 11';
  else
    Result := 'Clarion 10';
  end;
end;

function OptionCaption(Idx: Integer): String;
var
  Detected: String;
begin
  Detected := DetectedPathFor(Idx);
  if Length(Detected) > 0 then
    Result := LabelFor(Idx) + '   (detected: ' + Detected + ')'
  else
    Result := LabelFor(Idx) + '   (not detected -- tick to specify a path)';
end;

function IsSelected(Idx: Integer): Boolean;
begin
  Result := SelectPage.Values[Idx];
end;

function AnySelected: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 0 to 3 do
    if IsSelected(I) then
    begin
      Result := True;
      Exit;
    end;
end;

// The path page only needs to show up if at least one ticked version
// wasn't auto-detected -- in the common case (ticked == detected) we can
// skip straight to install.
function NeedPathsPage: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 0 to 3 do
    if IsSelected(I) and (Length(DetectedPathFor(I)) = 0) then
    begin
      Result := True;
      Exit;
    end;
end;

procedure SetRowVisible(Idx: Integer; Visible: Boolean);
begin
  PathsPage.PromptLabels[Idx].Visible := Visible;
  PathsPage.Edits[Idx].Visible        := Visible;
  PathsPage.Buttons[Idx].Visible      := Visible;
end;

function ValidPath(Path: String): Boolean;
begin
  Path := Trim(Path);
  Result := (Length(Path) > 0) and DirExists(Path + '\bin');
end;

function InstallForC12:  Boolean; begin Result := IsSelected(IDX_C12)  and ValidPath(PathsPage.Values[IDX_C12]);  end;
function InstallForC111: Boolean; begin Result := IsSelected(IDX_C111) and ValidPath(PathsPage.Values[IDX_C111]); end;
function InstallForC11:  Boolean; begin Result := IsSelected(IDX_C11)  and ValidPath(PathsPage.Values[IDX_C11]);  end;
function InstallForC10:  Boolean; begin Result := IsSelected(IDX_C10)  and ValidPath(PathsPage.Values[IDX_C10]);  end;

function GetC12AddinFolder(Param: String):  String; begin Result := Trim(PathsPage.Values[IDX_C12])  + ClarionSubPath; end;
function GetC111AddinFolder(Param: String): String; begin Result := Trim(PathsPage.Values[IDX_C111]) + ClarionSubPath; end;
function GetC11AddinFolder(Param: String):  String; begin Result := Trim(PathsPage.Values[IDX_C11])  + ClarionSubPath; end;
function GetC10AddinFolder(Param: String):  String; begin Result := Trim(PathsPage.Values[IDX_C10])  + ClarionSubPath; end;

procedure InitializeWizard;
begin
  DetectedC12  := DetectClarion12;
  DetectedC111 := DetectClarion111;
  DetectedC11  := DetectClarion11;
  DetectedC10  := DetectClarion10;

  SelectPage := CreateInputOptionPage(
    wpWelcome,
    'Clarion installation(s)',
    'Pick one or more Clarion IDEs to install Dictionary Tasker into',
    'Tick every Clarion installation you want to target. The same add-in ' +
    'DLL works in all four versions (targets .NET Framework 4.0). Detected ' +
    'installs are pre-ticked for you.' + #13#10 + #13#10 +
    'Important: close every Clarion IDE you''re targeting before clicking ' +
    'Next -- the IDE holds a lock on its add-in DLLs while running.',
    False, False);
  SelectPage.Add(OptionCaption(IDX_C12));
  SelectPage.Add(OptionCaption(IDX_C111));
  SelectPage.Add(OptionCaption(IDX_C11));
  SelectPage.Add(OptionCaption(IDX_C10));

  SelectPage.Values[IDX_C12]  := Length(DetectedC12)  > 0;
  SelectPage.Values[IDX_C111] := Length(DetectedC111) > 0;
  SelectPage.Values[IDX_C11]  := Length(DetectedC11)  > 0;
  SelectPage.Values[IDX_C10]  := Length(DetectedC10)  > 0;

  PathsPage := CreateInputDirPage(
    SelectPage.ID,
    'Clarion installation paths',
    'Confirm the install folder for each selected Clarion version',
    'Enter the top-level folder for each selected Clarion -- the one with ' +
    '"bin" directly below it.',
    False,
    'ClarionFolders');
  PathsPage.Add('Clarion 12:');
  PathsPage.Add('Clarion 11.1:');
  PathsPage.Add('Clarion 11:');
  PathsPage.Add('Clarion 10:');

  // Pre-fill with detected paths -- used even when the path page is skipped.
  PathsPage.Values[IDX_C12]  := DetectedC12;
  PathsPage.Values[IDX_C111] := DetectedC111;
  PathsPage.Values[IDX_C11]  := DetectedC11;
  PathsPage.Values[IDX_C10]  := DetectedC10;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if PageID = PathsPage.ID then
    Result := not NeedPathsPage;
end;

procedure CurPageChanged(CurPageID: Integer);
var
  I: Integer;
begin
  if CurPageID = PathsPage.ID then
  begin
    for I := 0 to 3 do
    begin
      if IsSelected(I) then
      begin
        SetRowVisible(I, True);
        // Restore the detected path if the row was blanked on a prior visit.
        if Length(Trim(PathsPage.Values[I])) = 0 then
          PathsPage.Values[I] := DetectedPathFor(I);
      end
      else
      begin
        SetRowVisible(I, False);
        // Inno's built-in validator on TInputDirWizardPage rejects blank rows
        // with "You must enter a full path with drive letter". We never use
        // this value (InstallForC* gates on IsSelected first), but it has to
        // LOOK like a full path to get past Next. Sentinel we can recognise.
        PathsPage.Values[I] := 'C:\';
      end;
    end;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  I: Integer;
  P: String;
begin
  Result := True;

  if CurPageID = SelectPage.ID then
  begin
    if not AnySelected then
    begin
      MsgBox('Tick at least one Clarion version to continue.', mbError, MB_OK);
      Result := False;
    end;
    Exit;
  end;

  if CurPageID = PathsPage.ID then
  begin
    for I := 0 to 3 do
    begin
      if not IsSelected(I) then
        Continue;
      P := Trim(PathsPage.Values[I]);
      if Length(P) = 0 then
      begin
        MsgBox('Enter the ' + LabelFor(I) + ' install folder to continue, ' +
               'or go back and untick ' + LabelFor(I) + '.',
               mbError, MB_OK);
        Result := False;
        Exit;
      end;
      if not DirExists(P + '\bin') then
      begin
        MsgBox('No "bin" subfolder was found under the ' + LabelFor(I) + ' path:' + #13#10 +
               P + #13#10 + #13#10 +
               'Pick the top-level ' + LabelFor(I) + ' folder (the one that ' +
               'contains "bin" directly below it), or go back and untick ' +
               LabelFor(I) + '.',
               mbError, MB_OK);
        Result := False;
        Exit;
      end;
    end;
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
