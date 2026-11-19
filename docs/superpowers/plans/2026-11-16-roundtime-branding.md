# Roundtime branding implementation plan

> **For agentic workers:** Use superpowers:executing-plans for this approved naming and icon update.

**Goal:** Rename the user-facing app to Roundtime and supply a matching icon in the app, window, taskbar, and File Explorer.

**Architecture:** Preserve internal namespaces and the existing DailyTimer settings directory for compatibility. Change the visible name, application metadata, executable name, and portable package. Use one authored vector mark to create a multi-resolution Windows ICO embedded both as the executable's native icon and as the WPF window icon; the header shows the same mark.

**Tech stack:** Existing C#/WPF/.NET Framework 4.8; native offline icon authoring with WPF geometry.

**Spec:** User selected Roundtime on 2026-11-16, accepting the proposed navy-and-lime timing dial with one separated segment. Keep all meeting behavior and saved preferences.

## Constraints and review focus

- Preserve `%LOCALAPPDATA%\DailyTimer\settings.json` and the internal source/namespace names; no data migration or deletion.
- Deliver `artifacts/Roundtime/Roundtime.exe` and `artifacts/Roundtime-Windows.zip` with their required neighboring files.
- ICO frames: 16, 20, 24, 32, 40, 48, 64, 128, and 256px; transparent outer corners, solid navy tile, lime dial, light hands.
- Both offline PowerShell and SDK project builds must embed the icon and keep embedded XAML identifiers consistent.
- Verify that WPF assets load, the native executable exposes the authored icon, and the portable package includes every runtime dependency.

## Tasks

- [x] Author the vector mark and reproducible icon renderer; inspect the rendered mark.
- [x] Update the visible name, assembly metadata, window/header icon, build paths, SDK project, and manifest.
- [x] Update README and portable packaging; keep prior artifacts intact.
- [x] Run existing behavioral/layout checks and verify branding resources/native icon in the rebuilt executable.
- [x] Inspect the running Roundtime window, review the changes, and generate the new portable package.

## Decisions

- User approval covers the selected name and proposed icon direction; implement without another approval gate.
- No .git directory exists here, so no worktree or commit steps apply.
- Retaining the old settings directory preserves existing user data without migration complexity. The product name changes independently of internal implementation names.

## Verification

- `build.ps1 -Test`: 30 behavior checks and both WPF layout checks pass.
- Runtime branding check: matching window/product identity; frozen vector header asset; high-resolution window icon; all nine embedded ICO frames; native Windows associated icon contains the authored dial.
- Visual inspection: Roundtime title, header name, header mark, and title-bar icon render in the running native app.
- Independent read-only review reran branding, behavior, and layout checks and found no concrete regressions. The SDK build configuration was reviewed for consistency; actual SDK execution remains unavailable in this environment.
- `package.ps1`: four required files packaged in Roundtime-Windows.zip, and the packaged executable's SHA-256 matches the verified build.
