# RunMind 3.1.0 — P1 regression acceptance (carried into P3)

Run after the P0 Windows acceptance checklist. Use the same canonical workspace in all three hosts.

## Build gate

- [ ] Run `Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64` with Node.js 24+ and .NET 10.
- [ ] Confirm Standalone, PowerToys Run and Command Palette compile and package versions agree on 3.1.0; Command Palette Appx version is 3.1.0.0.

## Project index and understanding

- [ ] Select a JS/TS workspace and wait for automatic indexing.
- [ ] Confirm the index remains local to the workspace and refreshes incrementally.
- [ ] Run the Project Understanding workflow; verify entry-point candidates, module boundaries, import direction, high-coupling modules and cycles against actual source.
- [ ] Confirm Python/C#/Go/Rust/Java or other unsupported source files remain generic text and are not parsed or assigned language-specific toolchains.
- [ ] Confirm SVG files are indexed as vector assets with local references but are not parsed as executable programming language.

## Project Diagnostics

- [ ] Run Project Diagnostics on a healthy Node project.
- [ ] Introduce a temporary malformed package.json in a disposable fixture and verify an error is reported; restore it afterward.
- [ ] Use a fixture with mismatched package.json/package-lock.json name or version and verify the mismatch is reported.
- [ ] Use conflicting npm/pnpm/Yarn/Bun lockfiles and verify ambiguity/conflict is reported without running a package manager.
- [ ] Confirm test discovery and declared scripts are reported as static evidence only.
- [ ] Confirm no command, install, repair, format or file mutation occurs from static diagnostics.

## Project Mission Control

- [ ] Open Agent Settings → Project intelligence → Run Mission Control.
- [ ] Confirm status explanation, index freshness/completeness, architecture evidence, diagnostic checks, release criteria, prioritized actions and unverified gates are visible.
- [ ] Open Mission Control through Agent Mode and compare the structured result with the Settings dashboard.
- [ ] Supply two JS/TS workspace-relative changed paths in Settings and confirm Test Impact Analysis lists candidate tests with confidence labels.
- [ ] Supply an unsupported-language file path and confirm it is reported as unanalysed, not semantically analyzed.
- [ ] Confirm stale indexes refresh incrementally when possible, and failed refreshes remain clearly marked stale.
- [ ] Confirm project strings are rendered as text and cannot inject HTML into the dashboard.
- [ ] Confirm no build/test/audit/Git/release command runs automatically.

## Test Impact Analysis

- [ ] Change a module with one direct test and one test that imports a transitive dependent; supply exact workspace-relative paths.
- [ ] Confirm direct and transitive tests are selected with different confidence labels.
- [ ] Confirm filename-only candidates are low confidence and unknown paths are reported.
- [ ] Confirm no tests execute until a separate command is shown and approved.
- [ ] Add generic `*.test.py` / `*.test.cs` files to the fixture and confirm they remain generic text and are not selected by P1 test discovery or impact analysis.

## Release Readiness

- [ ] Run static Release Readiness on a fixture with package version, changelog, README, CI config, tests, lockfile and build/test scripts.
- [ ] Confirm the result distinguishes static checks from Git status, actual builds/tests, live audit, signing, SBOM, packaging and publication.
- [ ] Confirm it never creates commits/tags or publishes artifacts.

## Host consistency

- [ ] Repeat representative checks in Standalone, PowerToys Run and Command Palette.
- [ ] Confirm all hosts use the same selected workspace root and persistent project index identity.
- [ ] Confirm no second workspace picker is requested when the canonical workspace is already selected.

Record host version, Windows version, Node/.NET versions, test fixture commit/hash, screenshots and any failure transcript. Do not mark P1 Windows-accepted until all applicable checks pass.

## Mission Control detail checks

Use [`PROJECT-MISSION-CONTROL.md`](PROJECT-MISSION-CONTROL.md) as the behavior reference. Validate one fresh-index report, one stale-index refresh, one failed-refresh case where practical, and explicit changed-path impact analysis.
