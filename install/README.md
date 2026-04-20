# Installer

A small [Inno Setup 6](https://jrsoftware.org/isinfo.php) script that packages the
add-in DLL + manifest into a single `DictionaryTasker-Setup.exe` —
so a non-technical Clarion user can install without knowing about `deploy.bat`
or `C:\clarionXX\bin\Addins\Misc\...`. The installer targets **Clarion 10,
11, 11.1, and 12** (tested on all four) — tick any combination of checkboxes
on the "Clarion installation(s)" page.

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

1. Show a **checkbox page** with one entry per supported Clarion — **Clarion 12**,
   **Clarion 11.1**, **Clarion 11**, **Clarion 10**. The same DLL works in all
   four (targets .NET 4.0, which every supported Clarion IDE loads).
2. Auto-detect each install by scanning:
   - `C:\clarion12` / `C:\clarion11.1` / `C:\clarion11` / `C:\clarion10`
   - `%ProgramFiles%\SoftVelocity\Clarion XX`
   - `%ProgramFiles(x86)%\SoftVelocity\Clarion XX`

   Detected versions show their path in the checkbox label and come
   pre-ticked. Undetected versions are listed too — tick them and the wizard
   shows a follow-up page to enter the path manually.
3. Warn to close every targeted Clarion IDE before continuing — the IDE holds
   a lock on its add-in DLLs while running.
4. Copy `ClarionDctAddin.dll` + `ClarionDctAddin.addin` into
   `<clarion-root>\bin\Addins\Misc\ClarionDctAddin\` for every ticked version.
5. Register itself under Windows "Apps & features" so you can uninstall later
   (the uninstaller cleans every folder the installer wrote to).

## Code signing

The installer is signed with a **self-signed code signing certificate**
(subject `CN=Roberto Renz (Dictionary Tasker)`, RSA-3072, SHA-256,
RFC3161-timestamped by DigiCert). The signature proves the bits haven't
been tampered with since leaving the build machine, but Windows
SmartScreen will still warn end-users because the cert isn't issued by a
CA it already trusts.

### End-user: trusting the cert (optional)

If you want SmartScreen to stop complaining, install the public cert
(`DictionaryTasker-SelfSigned-Public.cer`, shipped in this folder) into
**Trusted Root Certification Authorities** and **Trusted Publishers** on
the target machine:

```powershell
# Run as administrator
Import-Certificate -FilePath DictionaryTasker-SelfSigned-Public.cer -CertStoreLocation Cert:\LocalMachine\Root
Import-Certificate -FilePath DictionaryTasker-SelfSigned-Public.cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher
```

Only do this if you trust the author -- self-signed trust is a manual
decision, not a CA vouching for anyone.

### Maintainer: rotating the cert

The cert expires 2029-04-20. To regenerate (or to create one from
scratch on a new build machine):

```powershell
New-SelfSignedCertificate `
  -Subject 'CN=Roberto Renz (Dictionary Tasker)' `
  -Type CodeSigningCert `
  -KeySpec Signature `
  -KeyUsage DigitalSignature `
  -KeyAlgorithm RSA -KeyLength 3072 `
  -CertStoreLocation Cert:\CurrentUser\My `
  -NotAfter (Get-Date).AddYears(3) `
  -HashAlgorithm SHA256 `
  -FriendlyName 'Dictionary Tasker code signing'
```

Then re-export the public half:

```powershell
$cert = Get-ChildItem Cert:\CurrentUser\My | ? { $_.Subject -eq 'CN=Roberto Renz (Dictionary Tasker)' }
Export-Certificate -Cert $cert -FilePath install\DictionaryTasker-SelfSigned-Public.cer -Type CERT
```

`build-installer.bat` auto-signs using whichever code-signing cert in
`CurrentUser\My` matches the configured subject (`CERT_SUBJECT` env var,
defaults to the one above). If no cert is found, the build succeeds but
skips signing -- it logs a warning and carries on.

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
