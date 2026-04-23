@echo off
setlocal EnableDelayedExpansion

rem Build the Release DLL, bump the patch version in install\VERSION.txt,
rem then compile the Inno Setup script into install\dist\DictionaryTasker-Setup.exe.
rem
rem The patch bump means every re-run of this script produces an installer
rem with a newer version number, so Windows (Apps and Features, the wizard
rem header, the .exe metadata) always reflects that the bits have changed --
rem no more sitting at 1.0.0 while the binary silently updates.

set ROOT=%~dp0..
set CSPROJ=%ROOT%\ClarionDctAddin\ClarionDctAddin.csproj
set DLL=%ROOT%\ClarionDctAddin\bin\Release\ClarionDctAddin.dll
set ISS=%~dp0setup.iss
set VERFILE=%~dp0VERSION.txt

rem --- 1. compile a fresh Release build --------------------------------------
rem Previous versions of this script only CHECKED for a DLL sitting in
rem bin\Release; if the dev cycle had last compiled Debug (or the Release
rem output was stale) the installer shipped outdated bits. Compile explicitly
rem so the installer always bundles what HEAD produces.
rem
rem We also wipe bin\ and obj\ first and pass --no-incremental. Reason: the
rem csproj auto-computes <Version> from DateTime.Now in the
rem SetVersionFromBuildTime MSBuild target. MSBuild's incremental logic can
rem still reuse an existing DLL/AssemblyInfo.cs combo if it believes the
rem inputs haven't changed, which left the installer packaging a DLL with
rem last-minute's version. A clean + non-incremental build guarantees the
rem DLL baked into the installer carries the NOW time in its AssemblyVersion.
set BINDIR=%ROOT%\ClarionDctAddin\bin
set OBJDIR=%ROOT%\ClarionDctAddin\obj
if exist "%BINDIR%" (
  echo Cleaning %BINDIR%
  rmdir /s /q "%BINDIR%"
)
if exist "%OBJDIR%" (
  echo Cleaning %OBJDIR%
  rmdir /s /q "%OBJDIR%"
)

echo Building Release DLL...
dotnet build -c Release --no-incremental "%CSPROJ%"
if errorlevel 1 (
  echo.
  echo dotnet build failed -- aborting installer build.
  exit /b 1
)

if not exist "%DLL%" (
  echo Build output missing after compile: %DLL%
  echo Check the build log above for errors.
  exit /b 1
)

rem Surface the freshly-built DLL's AssemblyVersion so it's obvious in the
rem install log WHICH version of the add-in ended up in the installer. If
rem this number doesn't move between consecutive installer builds, something
rem short-circuited the rebuild and the installer is shipping stale bits.
for /f "usebackq delims=" %%d in (`powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$v = [System.Reflection.AssemblyName]::GetAssemblyName('%DLL%').Version;" ^
    "Write-Output ('{0}.{1}.{2}.{3}' -f $v.Major,$v.Minor,$v.Build,$v.Revision)"`) do (
  set ASM_VERSION=%%d
)
echo Built DLL assembly version: %ASM_VERSION%

rem --- 2. bump the patch in VERSION.txt (creating it if missing) -------------
if not exist "%VERFILE%" (
  echo 1.0.0> "%VERFILE%"
)

rem Use PowerShell for the version math -- cmd.exe string handling is painful
rem and PowerShell ships with every supported Windows. The inline script
rem reads the file, parses major.minor.patch, increments patch, writes it
rem back with no trailing newline so future reads stay clean, and echoes
rem the new version for the FOR loop to capture.
for /f "usebackq delims=" %%v in (`powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$raw = (Get-Content -LiteralPath '%VERFILE%' -Raw).Trim();" ^
    "$parts = $raw.Split('.');" ^
    "if ($parts.Length -lt 3) { $parts = @('1','0','0') }" ^
    "$parts[2] = [string]([int]$parts[2] + 1);" ^
    "$new = $parts -join '.';" ^
    "Set-Content -LiteralPath '%VERFILE%' -Value $new -NoNewline;" ^
    "Write-Output $new"`) do (
  set VERSION=%%v
)

if "%VERSION%"=="" (
  echo Version bump failed -- VERSION.txt may be unreadable.
  exit /b 4
)

echo Installer version is now %VERSION% ^(written to %VERFILE%^)

rem --- 3. locate ISCC.exe ----------------------------------------------------
set ISCC=
for %%P in (
  "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
  "%ProgramFiles%\Inno Setup 6\ISCC.exe"
  "%ProgramFiles(x86)%\Inno Setup 5\ISCC.exe"
  "%ProgramFiles%\Inno Setup 5\ISCC.exe"
) do (
  if exist %%P (
    set "ISCC=%%~P"
    goto :found
  )
)

echo Could not find Inno Setup's ISCC.exe.
echo Install Inno Setup 6 from https://jrsoftware.org/isinfo.php and retry.
exit /b 2

:found
echo Using %ISCC%
"%ISCC%" /DAppVersion=%VERSION% "%ISS%"
if errorlevel 1 (
  echo.
  echo Installer build failed.
  exit /b 3
)

set BUILT=%~dp0dist\DictionaryTasker-Setup.exe

rem --- 4. sign the installer (best-effort) -----------------------------------
rem Looks for a self-signed code-signing cert in CurrentUser\My with the
rem expected subject. If absent, skipping the sign step is fine -- the
rem unsigned installer still works, just with a louder SmartScreen warning.
rem Override either value on the command line to use a different cert:
rem   set CERT_SUBJECT=Your Name          (just the CN value, no "CN=" prefix)
rem   set CERT_THUMBPRINT=abcd1234...
if "%CERT_SUBJECT%"=="" set CERT_SUBJECT=Roberto Renz (Dictionary Tasker)
if "%TIMESTAMP_URL%"=="" set TIMESTAMP_URL=http://timestamp.digicert.com

set SIGNTOOL=
for %%P in (
  "%ProgramFiles(x86)%\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
  "%ProgramFiles(x86)%\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
  "%ProgramFiles(x86)%\Windows Kits\10\App Certification Kit\signtool.exe"
) do (
  if exist %%P (
    set "SIGNTOOL=%%~P"
    goto :signtool_found
  )
)

echo.
echo signtool.exe not found -- installer was built but not signed.
echo Install a Windows 10/11 SDK to enable signing.
goto :done

:signtool_found
echo.
echo Signing with %SIGNTOOL%
if not "%CERT_THUMBPRINT%"=="" (
  "%SIGNTOOL%" sign /sha1 %CERT_THUMBPRINT% /fd sha256 /tr %TIMESTAMP_URL% /td sha256 /d "Dictionary Tasker" /du "https://github.com/robertorenz/clarionDictionaryTasker" "%BUILT%"
) else (
  "%SIGNTOOL%" sign /n "%CERT_SUBJECT%" /fd sha256 /tr %TIMESTAMP_URL% /td sha256 /d "Dictionary Tasker" /du "https://github.com/robertorenz/clarionDictionaryTasker" "%BUILT%"
)
if errorlevel 1 (
  echo.
  echo Signing failed -- installer is built but unsigned.
  echo Check that a code-signing cert with subject "%CERT_SUBJECT%" exists in CurrentUser\My.
)

:done
echo.
echo Installer created: %BUILT%  ^(version %VERSION%^)
exit /b 0
