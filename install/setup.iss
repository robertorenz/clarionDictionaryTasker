; Dictionary Tasker — Inno Setup script
; Produces a single DictionaryTasker-Setup-<version>.exe that installs the
; add-in DLL + manifest into a Clarion Addins folder. The wizard lets the
; user pick Clarion 12, Clarion 11.1, or both — the add-in is bytecode-
; compatible across both IDE versions, so installing to both is fine.
;
; Build with Inno Setup 6:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" install\setup.iss
; or just run install\build-installer.bat from the repo root.

#define AppName        "Dictionary Tasker"
; AppVersion is normally passed on the command line by build-installer.bat
; (which bumps the patch in VERSION.txt each run). The #ifndef guard here
; lets ISCC.exe be invoked directly on the .iss file without the build
; script — useful for one-offs — in which case the fallback below is used.
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
; We don't actually install into DefaultDirName — files go to the Clarion
; bin folder(s) picked on the wizard's target-selection pages.
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
; Writing into C:\clarion12 / C:\clarion11 generally needs admin. If the
; user installed Clarion to a user-writable path they can still pick that
; path on the wizard.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline dialog
WizardStyle=modern
UninstallDisplayName={#AppName} (Clarion add-in)
UninstallDisplayIcon={app}\{#AppExeName}
UsePreviousAppDir=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Payload goes into every selected Clarion's addin folder. Each entry
; is gated by a Check: function so we only copy where the user asked.
Source: "{#RepoRoot}ClarionDctAddin\bin\Debug\{#AppExeName}";   DestDir: "{code:GetC12AddinFolder}"; Flags: ignoreversion; Check: InstallForC12
Source: "{#RepoRoot}ClarionDctAddin\{#AppManifest}";             DestDir: "{code:GetC12AddinFolder}"; Flags: ignoreversion; Check: InstallForC12
Source: "{#RepoRoot}ClarionDctAddin\bin\Debug\{#AppExeName}";   DestDir: "{code:GetC11AddinFolder}"; Flags: ignoreversion; Check: InstallForC11
Source: "{#RepoRoot}ClarionDctAddin\{#AppManifest}";             DestDir: "{code:GetC11AddinFolder}"; Flags: ignoreversion; Check: InstallForC11
; Stub file for Inno's uninstaller anchor — lands in whichever folder
; ends up being {app} (see CurStepChanged below).
Source: "{#RepoRoot}install\readme-installed.txt";               DestDir: "{app}"; Flags: ignoreversion

[UninstallDelete]
Type: files;      Name: "{code:GetC12AddinFolder}\{#AppExeName}"; Check: InstallForC12
Type: files;      Name: "{code:GetC12AddinFolder}\{#AppManifest}"; Check: InstallForC12
Type: dirifempty; Name: "{code:GetC12AddinFolder}";                Check: InstallForC12
Type: files;      Name: "{code:GetC11AddinFolder}\{#AppExeName}"; Check: InstallForC11
Type: files;      Name: "{code:GetC11AddinFolder}\{#AppManifest}"; Check: InstallForC11
Type: dirifempty; Name: "{code:GetC11AddinFolder}";                Check: InstallForC11

[Code]
const
  ClarionSubPath   = '\bin\Addins\Misc\ClarionDctAddin';
  DefaultClarion12 = 'C:\clarion12';
  DefaultClarion11 = 'C:\clarion11.1';

var
  TargetsPage: TInputOptionWizardPage;   // checkboxes: install for C12? C11.1?
  C12Page:     TInputDirWizardPage;      // Clarion 12 path, skipped if unchecked
  C11Page:     TInputDirWizardPage;      // Clarion 11.1 path, skipped if unchecked

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

function DetectClarion12: String;
var
  C: TArrayOfString;
begin
  SetArrayLength(C, 4);
  C[0] := ExpandConstant('{sd}\clarion12');
  C[1] := ExpandConstant('{pf}\SoftVelocity\Clarion 12');
  C[2] := ExpandConstant('{pf32}\SoftVelocity\Clarion 12');
  C[3] := DefaultClarion12;
  Result := FirstExisting(C, DefaultClarion12);
end;

function DetectClarion11: String;
var
  C: TArrayOfString;
begin
  SetArrayLength(C, 6);
  C[0] := ExpandConstant('{sd}\clarion11.1');
  C[1] := ExpandConstant('{sd}\clarion11');
  C[2] := ExpandConstant('{pf}\SoftVelocity\Clarion 11.1');
  C[3] := ExpandConstant('{pf32}\SoftVelocity\Clarion 11.1');
  C[4] := ExpandConstant('{pf}\SoftVelocity\Clarion 11');
  C[5] := ExpandConstant('{pf32}\SoftVelocity\Clarion 11');
  Result := FirstExisting(C, DefaultClarion11);
end;

function InstallForC12(): Boolean;
begin
  Result := TargetsPage.Values[0];
end;

function InstallForC11(): Boolean;
begin
  Result := TargetsPage.Values[1];
end;

procedure InitializeWizard;
begin
  TargetsPage := CreateInputOptionPage(
    wpWelcome,
    'Install targets',
    'Which Clarion IDE(s) do you want to install Dictionary Tasker into?',
    'The add-in works with both Clarion 12 and Clarion 11.1. Tick one or ' +
    'both — you''ll be asked for the install folder on the next page(s).' + #13#10 + #13#10 +
    'Important: close every Clarion IDE you''re targeting before clicking Next — ' +
    'the IDE holds a lock on its add-in DLLs while running.',
    False,   { Exclusive = False → checkboxes, multi-select allowed }
    False);  { ListBox  = False → render as checkboxes, not a listbox }
  TargetsPage.Add('Clarion 12');
  TargetsPage.Add('Clarion 11.1');
  { Default: pre-tick whichever version we can actually find. If neither
    is detected, pre-tick C12 so the user at least has a starting path. }
  TargetsPage.Values[0] := DirExists(DetectClarion12 + '\bin');
  TargetsPage.Values[1] := DirExists(DetectClarion11 + '\bin');
  if (not TargetsPage.Values[0]) and (not TargetsPage.Values[1]) then
    TargetsPage.Values[0] := True;

  C12Page := CreateInputDirPage(
    TargetsPage.ID,
    'Clarion 12 location',
    'Where is Clarion 12 installed on this machine?',
    'Pick the top-level Clarion 12 folder (the one that contains "bin" ' +
    'directly below it).',
    False,
    'Clarion12Folder');
  C12Page.Add('');
  C12Page.Values[0] := DetectClarion12;

  C11Page := CreateInputDirPage(
    C12Page.ID,
    'Clarion 11.1 location',
    'Where is Clarion 11.1 installed on this machine?',
    'Pick the top-level Clarion 11.1 folder (the one that contains "bin" ' +
    'directly below it).',
    False,
    'Clarion11Folder');
  C11Page.Add('');
  C11Page.Values[0] := DetectClarion11;
end;

{ Skip the per-version path page if that version isn't ticked. }
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (PageID = C12Page.ID) and (not TargetsPage.Values[0]) then
    Result := True;
  if (PageID = C11Page.ID) and (not TargetsPage.Values[1]) then
    Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Root: String;
begin
  Result := True;
  if CurPageID = TargetsPage.ID then
  begin
    if (not TargetsPage.Values[0]) and (not TargetsPage.Values[1]) then
    begin
      MsgBox('Pick at least one Clarion version to install into.',
             mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end
  else if CurPageID = C12Page.ID then
  begin
    Root := C12Page.Values[0];
    if not DirExists(Root + '\bin') then
    begin
      MsgBox('No "bin" subfolder was found under' + #13#10 + Root + #13#10 + #13#10 +
             'Pick the top-level Clarion 12 folder (the one that contains "bin" directly below it).',
             mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end
  else if CurPageID = C11Page.ID then
  begin
    Root := C11Page.Values[0];
    if not DirExists(Root + '\bin') then
    begin
      MsgBox('No "bin" subfolder was found under' + #13#10 + Root + #13#10 + #13#10 +
             'Pick the top-level Clarion 11.1 folder (the one that contains "bin" directly below it).',
             mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;
end;

function GetC12AddinFolder(Param: String): String;
begin
  Result := C12Page.Values[0] + ClarionSubPath;
end;

function GetC11AddinFolder(Param: String): String;
begin
  Result := C11Page.Values[0] + ClarionSubPath;
end;

// {app} anchors the uninstaller. Use whichever selected version comes
// first (C12 preferred) so the readme + uninstall hook land there.
function GetPrimaryAddinFolder: String;
begin
  if InstallForC12 then
    Result := GetC12AddinFolder('')
  else
    Result := GetC11AddinFolder('');
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
