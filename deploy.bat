@echo off
setlocal
set DEST=C:\clarion12\bin\Addins\Misc\ClarionDctAddin
set SRC=%~dp0ClarionDctAddin\bin\Release

if not exist "%SRC%\ClarionDctAddin.dll" goto :nobuild
if not exist "%DEST%" mkdir "%DEST%"

copy /Y "%SRC%\ClarionDctAddin.dll"   "%DEST%\" || goto :copyfail
copy /Y "%SRC%\ClarionDctAddin.addin" "%DEST%\" || goto :copyfail

echo.
echo Deployed to %DEST%
echo Start Clarion 12 and look for the Dict Tools menu next to View.
exit /b 0

:copyfail
echo.
echo Copy failed. Is Clarion 12 running? Close the IDE and try again.
exit /b 1

:nobuild
echo Build output not found: %SRC%\ClarionDctAddin.dll
echo Build the solution first - dotnet build -c Release
exit /b 1
