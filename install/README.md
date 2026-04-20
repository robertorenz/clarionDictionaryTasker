# Installer

A small [Inno Setup 6](https://jrsoftware.org/isinfo.php) script that packages the
add-in DLL + manifest into a single `DictionaryTasker-Setup.exe` —
so a non-technical Clarion user can install without knowing about `deploy.bat`
or `C:\clarionXX\bin\Addins\Misc\...`. The installer targets **Clarion 10,
11, 11.1, and 12** (tested on all four) — pick any combination on the
"Clarion installation(s)" page; leave a line blank to skip a version.

## Build the installer

1. Install Inno Setup 6 from [jrsoftware.org](https://jrsoftware.org/isinfo.php).
   The `ISCC.exe` compiler needs to be on disk; no path setup required — the build
   batch auto-discovers it in the standard install locations.
2. Build the add-in first:
   ```
   dotnet build -c Debug
   ```
   (Debug is fine; we don't distribute symbols.)
3. From the repo root:
   ```
   install\build-installer.bat
   ```
4. Output lands in `install\dist\DictionaryTasker-Setup.exe`. The patch version tracked in `install\VERSION.txt` is auto-bumped on each run of the build script, so every rebuild produces a newer-versioned installer (currently `1.0.x`). The filename stays constant so the tracked installer in git is always the latest build.

## Run the installer

Double-click `DictionaryTasker-Setup.exe`. The wizard will:

1. Show a single page with four path entries — **Clarion 12**, **Clarion 11.1**,
   **Clarion 11**, **Clarion 10**. The same DLL works in all four (targets
   .NET 4.0, which every supported Clarion IDE loads).
2. Auto-detect and pre-fill each path by scanning:
   - `C:\clarion12` / `C:\clarion11.1` / `C:\clarion11` / `C:\clarion10`
   - `%ProgramFiles%\SoftVelocity\Clarion XX`
   - `%ProgramFiles(x86)%\SoftVelocity\Clarion XX`

   Versions that aren't installed come up blank. Leaving a line blank skips
   that version; at least one must be filled.
3. Warn to close every targeted Clarion IDE before continuing — the IDE holds
   a lock on its add-in DLLs while running.
4. Copy `ClarionDctAddin.dll` + `ClarionDctAddin.addin` into
   `<clarion-root>\bin\Addins\Misc\ClarionDctAddin\` for every non-blank version.
5. Register itself under Windows "Apps & features" so you can uninstall later
   (the uninstaller cleans every folder the installer wrote to).

## Uninstall

Windows settings → Apps → **Dictionary Tasker (Clarion add-in)** → Uninstall.
Removes the two files from every Clarion bin subfolder the installer wrote to.

User preferences at `%LOCALAPPDATA%\ClarionDctAddin\settings.txt` are NOT
removed — delete that file manually if you want a fully clean slate.

## Files in this folder

| File | Purpose |
| --- | --- |
| `setup.iss` | Inno Setup script (language, pages, Files section, custom code). Accepts `/DAppVersion=x.y.z` from the command line; falls back to a literal if invoked directly. |
| `build-installer.bat` | Runs `ISCC.exe setup.iss` with a couple of sanity checks first; also bumps the patch in `VERSION.txt` each run. |
| `VERSION.txt` | Single-line source of truth for the installer's patch number (`1.0.x`). The build script increments it before compiling. |
| `readme-installed.txt` | Ships inside the installer; ends up in the Clarion addin folder next to the DLL, explaining what's there. |
| `dist/` | Compiler output — `DictionaryTasker-Setup.exe` (always the same filename) lands here. Git-ignored except the latest build, which is force-tracked. |
