@echo off
setlocal EnableDelayedExpansion

rem Build the Debug DLL, bump the patch version in install\VERSION.txt,
rem then compile the Inno Setup script into install\dist\DictionaryTasker-Setup.exe.
rem
rem The patch bump means every re-run of this script produces an installer
rem with a newer version number, so Windows (Apps and Features, the wizard
rem header, the .exe metadata) always reflects that the bits have changed --
rem no more sitting at 1.0.0 while the binary silently updates.

set ROOT=%~dp0..
set DLL=%ROOT%\ClarionDctAddin\bin\Debug\ClarionDctAddin.dll
set ISS=%~dp0setup.iss
set VERFILE=%~dp0VERSION.txt

rem --- 1. make sure the DLL exists -------------------------------------------
if not exist "%DLL%" (
  echo Build output not found: %DLL%
  echo.
  echo Run "dotnet build -c Debug" first ^(or build the .sln in Visual Studio^).
  exit /b 1
)

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

echo.
echo Installer created: %~dp0dist\DictionaryTasker-Setup.exe  ^(version %VERSION%^)
exit /b 0
