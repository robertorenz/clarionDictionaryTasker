@echo off
setlocal

rem Build the Release DLL, then compile the Inno Setup script into a single
rem DictionaryTasker-Setup-<version>.exe under install\dist\.

set ROOT=%~dp0..
set DLL=%ROOT%\ClarionDctAddin\bin\Debug\ClarionDctAddin.dll
set ISS=%~dp0setup.iss

rem --- 1. make sure the DLL exists -------------------------------------------
if not exist "%DLL%" (
  echo Build output not found: %DLL%
  echo.
  echo Run "dotnet build -c Debug" first ^(or build the .sln in Visual Studio^).
  exit /b 1
)

rem --- 2. locate ISCC.exe -----------------------------------------------------
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
"%ISCC%" "%ISS%"
if errorlevel 1 (
  echo.
  echo Installer build failed.
  exit /b 3
)

echo.
echo Installer created: %~dp0dist
exit /b 0
