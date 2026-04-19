; Dictionary Tasker — Inno Setup script
; Produces a single DictionaryTasker-Setup-<version>.exe that installs the
; add-in DLL + manifest into Clarion 12's Addins folder. The user is asked
; for the Clarion 12 root directory; default is C:\clarion12.
;
; Build with Inno Setup 6:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" install\setup.iss
; or just run install\build-installer.bat from the repo root.

#define AppName        "Dictionary Tasker"
#define AppVersion     "1.0.0"
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
; We don't actually install into DefaultDirName — files go to the Clarion bin
; folder picked on the wizard's Clarion-location page. DisableDirPage hides
; the default "choose install folder" step which would be confusing here.
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir=dist
OutputBaseFilename=DictionaryTasker-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
; Writing into C:\clarion12 generally needs admin. If someone installed Clarion
; to a user-writable path they can still pick that path on the wizard.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline dialog
WizardStyle=modern
UninstallDisplayName={#AppName} (Clarion 12 add-in)
UninstallDisplayIcon={app}\{#AppExeName}
; Note for the uninstaller: we store the final addin folder in the app root.
UsePreviousAppDir=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Payload — the two files deploy.bat ships.
Source: "{#RepoRoot}ClarionDctAddin\bin\Debug\{#AppExeName}";   DestDir: "{code:GetAddinFolder}"; Flags: ignoreversion
Source: "{#RepoRoot}ClarionDctAddin\{#AppManifest}";             DestDir: "{code:GetAddinFolder}"; Flags: ignoreversion
; Copy a stub file to {app} so Inno's uninstaller has something to hook into.
Source: "{#RepoRoot}install\readme-installed.txt";               DestDir: "{app}"; Flags: ignoreversion

[UninstallDelete]
Type: files; Name: "{code:GetAddinFolder}\{#AppExeName}"
Type: files; Name: "{code:GetAddinFolder}\{#AppManifest}"
Type: dirifempty; Name: "{code:GetAddinFolder}"

[Code]
const
  ClarionSubPath = '\bin\Addins\Misc\ClarionDctAddin';
  DefaultClarion = 'C:\clarion12';

var
  ClarionPage: TInputDirWizardPage;

function DetectClarion: String;
var
  Candidates: TArrayOfString;
  I: Integer;
begin
  SetArrayLength(Candidates, 4);
  Candidates[0] := ExpandConstant('{sd}\clarion12');
  Candidates[1] := ExpandConstant('{pf}\SoftVelocity\Clarion 12');
  Candidates[2] := ExpandConstant('{pf32}\SoftVelocity\Clarion 12');
  Candidates[3] := DefaultClarion;
  for I := 0 to GetArrayLength(Candidates) - 1 do
  begin
    if DirExists(Candidates[I] + '\bin') then
    begin
      Result := Candidates[I];
      Exit;
    end;
  end;
  Result := DefaultClarion;
end;

procedure InitializeWizard;
begin
  ClarionPage := CreateInputDirPage(
    wpWelcome,
    'Clarion 12 location',
    'Where is Clarion 12 installed on this machine?',
    'The add-in files will be copied to the Addins\Misc\ClarionDctAddin subfolder of the' + #13#10 +
    'Clarion 12 bin directory. Pick the top-level Clarion 12 folder (the one that contains' + #13#10 +
    '"bin" directly below it).' + #13#10 + #13#10 +
    'Important: close Clarion 12 before clicking Next — the IDE holds a lock on its add-in DLLs while running.',
    False,
    'ClarionFolder');
  ClarionPage.Add('');
  ClarionPage.Values[0] := DetectClarion();
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ClarionRoot: String;
  BinPath: String;
begin
  Result := True;
  if CurPageID = ClarionPage.ID then
  begin
    ClarionRoot := ClarionPage.Values[0];
    BinPath := ClarionRoot + '\bin';
    if not DirExists(BinPath) then
    begin
      MsgBox('No "bin" subfolder was found under' + #13#10 + ClarionRoot + #13#10 + #13#10 +
             'Pick the top-level Clarion 12 folder (the one that contains "bin" directly below it).',
             mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;
end;

function GetClarionRoot(Param: String): String;
begin
  Result := ClarionPage.Values[0];
end;

function GetAddinFolder(Param: String): String;
begin
  Result := ClarionPage.Values[0] + ClarionSubPath;
end;

// Override the install folder so the uninstaller entry points at the addin dir.
// {app} ends up being the add-in's own subfolder, which is also where our
// stub readme-installed.txt lands.
function GetDefaultDir(Param: String): String;
begin
  Result := GetAddinFolder('');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    // Redirect {app} to the addin folder so [Files] that use {app} (and the
    // uninstaller registration) both land in the right place.
    WizardForm.DirEdit.Text := GetAddinFolder('');
  end;
end;
