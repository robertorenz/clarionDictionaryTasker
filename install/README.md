# Installer

A small [Inno Setup 6](https://jrsoftware.org/isinfo.php) script that packages the
add-in DLL + manifest into a single `DictionaryTasker-Setup.exe` —
so a non-technical Clarion user can install without knowing about `deploy.bat`
or `C:\clarionXX\bin\Addins\Misc\...`. The installer targets **Clarion 12
and/or Clarion 11.1** — pick one or both on the Install targets page.

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

1. Offer checkboxes for **Clarion 12** and/or **Clarion 11.1** — the same DLL
   works in both (it targets .NET 4.0, which both IDEs load). Each ticked
   version gets its own "where is it installed?" page.
2. Auto-detect the install root:
   - **C12** default `C:\clarion12`; also scans `%ProgramFiles%\SoftVelocity\Clarion 12`.
   - **C11.1** default `C:\clarion11.1`; also scans `%ProgramFiles%\SoftVelocity\Clarion 11.1`
     and the `clarion11` variants.
3. Warn to close every targeted Clarion IDE before continuing — the IDE holds
   a lock on its add-in DLLs while running.
4. Copy `ClarionDctAddin.dll` + `ClarionDctAddin.addin` into
   `<clarion-root>\bin\Addins\Misc\ClarionDctAddin\` for each selected version.
5. Register itself under Windows "Apps & features" so you can uninstall later.

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
