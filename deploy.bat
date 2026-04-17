@echo off
setlocal
set DEST=C:\clarion12\bin\Addins\Misc\ClarionDctAddin
set SRC=%~dp0ClarionDctAddin\bin\Debug

if not exist "%SRC%\ClarionDctAddin.dll" goto :nobuild
if not exist "%DEST%" mkdir "%DEST%"

copy /Y "%SRC%\ClarionDctAddin.dll"   "%DEST%\" || exit /b 1
copy /Y "%SRC%\ClarionDctAddin.addin" "%DEST%\" || exit /b 1

echo.
echo Deployed to %DEST%
echo Start Clarion 12 and look for the Dict Tools menu next to View.
exit /b 0

:nobuild
echo Build output not found: %SRC%\ClarionDctAddin.dll
echo Build the solution first - dotnet build -c Debug
exit /b 1
