# RunMind 2.8.1 — Project Mission Control refinement

RunMind 2.8.1 strengthens the P1 Project Intelligence implementation with a more complete Project Mission Control experience. It builds on the 2.8.0 P1 capability set and the P0 reliability foundation.

## Mission Control improvements

- **Structured shared report:** Agent Core and Agent Settings consume the same versioned Mission Control result rather than a counters-only summary.
- **Freshness and completeness:** reports index age, last update, scan counts, bytes read, skipped/reused/removed files, truncation state and refresh outcome. An index older than five minutes is incrementally refreshed when possible.
- **Architecture evidence:** exposes module boundaries, entry-point candidates, import graph coverage, high-coupling examples, circular dependency paths, unresolved imports and parser coverage.
- **Operational diagnostics:** displays the detailed static diagnostic checks and supporting workspace-relative evidence.
- **Release readiness:** displays each static release criterion, suggested build/test commands without executing them, and the verification gates that remain unverified.
- **Test impact:** accepts optional explicit workspace-relative changed paths in Agent Settings and displays direct/transitive/filename-only candidates, unknown paths and unsupported file types.
- **Prioritized actions:** findings are de-duplicated, ranked by priority and presented with source and evidence.
- **Safe rendering:** dashboard content is built with DOM APIs and `textContent`; project files cannot inject HTML into the dashboard.

## Validation performed in the development environment

- Portable Node suite: **177/177 passed**.
- JavaScript/CJS/MJS syntax: **42/42 passed**; JSON: **4/4 parsed**; XML-family: **7/7 parsed**.
- Standalone, PowerToys Run, Command Palette and Appx package versions agree on **2.8.1 / 2.8.1.0**.
- Windows/.NET compilation and live acceptance remain pending.

## Safety and scope

- Mission Control is read-only and never executes commands, writes project files, installs packages, changes Git state or modifies release metadata.
- An older index is refreshed incrementally where the selected workspace permits it; refresh failure is disclosed and stale evidence remains marked stale.
- Test Impact Analysis requires explicit changed paths. Git changes are not inferred and tests are not executed.
- Language-aware project analysis remains limited to JavaScript, TypeScript, JSX/TSX, HTML and CSS. Other source files remain generic text and are not semantically analyzed.
- Static release checks do not verify Git cleanliness, actual builds/tests, live dependency vulnerabilities, signing, SBOM, packaging or publication.
- P0/P1 Windows acceptance remains required; see `docs/P0-WINDOWS-ACCEPTANCE.md` and `docs/P1-WINDOWS-ACCEPTANCE.md`.
