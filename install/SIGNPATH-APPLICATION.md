# SignPath.io application -- Dictionary Tasker

Goal: replace the self-signed cert with a **publicly-trusted code signing
certificate** via SignPath's free OSS program. Once approved, Windows
SmartScreen stops warning end-users and the "Unknown Publisher" label
goes away -- no more "run anyway" click-through.

## Eligibility (quick check)

- [x] Public GitHub repository -- https://github.com/robertorenz/clarionDictionaryTasker
- [x] OSI-approved license -- MIT (`LICENSE`)
- [x] Real commit history (not a stub project)
- [x] README explains what the project does
- [ ] At least a few GitHub stars / activity -- nice-to-have, not mandatory

Looks approvable. They reject projects that are clearly personal-only or
that haven't shipped anything; a working installer with a changelog and
docs puts you well past that bar.

## Application steps (you do these)

1. **Sign up.** Go to https://about.signpath.io/ → click **Get Started for
   Open Source**. Sign in with your GitHub account (the one that owns
   `robertorenz/clarionDictionaryTasker`).

2. **Create an organization.** Name it something like `Roberto Renz` or
   `Dictionary Tasker`. This is the billing/ownership container; for OSS
   it's free.

3. **Apply for Open Source Sponsorship.** In the org dashboard, find the
   "Open Source" or "Apply for sponsorship" link. Fill in:
   - **Project name:** Dictionary Tasker
   - **Repository URL:** https://github.com/robertorenz/clarionDictionaryTasker
   - **License:** MIT
   - **Description:** Clarion IDE add-in that adds dictionary maintenance,
     linting, comparison, SQL DDL generation, and batch editing tools to
     Clarion 10, 11, 11.1, and 12. Distributed as a signed Inno Setup
     installer.
   - **Why signing matters:** End-users are Clarion developers installing
     a DLL into their IDE. Unsigned installers trip SmartScreen and make
     non-technical users hesitate; a trusted signature removes friction
     and lets the IDE's add-in loader verify provenance.

4. **Wait for review.** Typically a few business days. They sanity-check
   the repo (real project, OSI license, no malware indicators).

5. **Once approved:**
   - Create a **Project** in SignPath tied to this repo.
   - Create a **Signing Policy** (pick "release-signing" -- used for
     tagged releases).
   - Generate a **CI Token** -- this is what the GitHub Actions workflow
     will authenticate with. Save it as a repo secret named
     `SIGNPATH_API_TOKEN`.
   - Note the **Organization ID**, **Project slug**, and **Signing
     Policy slug** -- these go into the workflow file.

## What I'll wire up once you're approved

When you have the org/project slugs + API token in hand, I'll add:

- `.github/workflows/sign-release.yml` -- on push of a `v*` tag, it
  builds the installer, uploads the artifact, calls SignPath's
  `submit-signing-request` action, and re-downloads the signed exe
  ready for the GitHub Release.
- `install/build-installer.bat` changes so local builds still use the
  self-signed cert (dev loop stays fast) but CI release builds use
  SignPath.

Until then, the self-signed path handles the "signed, tamper-evident"
requirement -- just without the pre-trusted chain.

## Timeline / fallback

- Application review: few days to ~2 weeks
- If rejected or too slow: Certum Open Source Code Signing (~USD $25/yr)
  is the cheapest alternative with a real CA chain; no application needed.
