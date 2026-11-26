# RunMind 3.3.0 — P6 Development Intelligence Build Status

## Source and portable validation

- Source baseline: RunMind 3.2.0 P5, verified against its SHA-256 sidecar before extraction and modification.
- Release: RunMind 3.3.0 P6 Development Intelligence, shared by Standalone, PowerToys Run and PowerToys Command Palette.
- JavaScript runtime in this packaging environment: Node.js 22.16.0. Target runtime remains Node.js 24+.
- Full portable regression suite: **247/247 passed** (`node --test --test-concurrency=1 tests/*.test.cjs`).
- P6 focused suite: **10/10 passed** (`node --test tests/p6-development-intelligence.test.cjs`).
- Shared JavaScript syntax: **21/21 passed** with `node --check`; test harness syntax: **22/22 passed**.
- JSON validation: **4/4 passed**. XML, MSBuild project, Appx manifest and XAML validation: **6/6 passed**.
- These portable checks ran under Node.js 22.16.0 in the packaging environment; the requested target Node.js 24+ acceptance remains outstanding.

## Release archive verification

- Archive CRC verification passed; the ZIP was extracted into a clean directory.
- Complete regression suite rerun from the extracted release: **247/247 passed**.
- P6 focused suite rerun from the extracted release: **10/10 passed**.
- Shared JavaScript syntax rerun: **21/21 passed**; test harness syntax: **22/22 passed**.
- Extracted JSON validation: **4/4 passed**; XML/MSBuild/Appx/XAML validation: **6/6 passed**.

## Windows build status

**Not yet certified on Windows.** This packaging environment is Linux, does not have the .NET 10 SDK, and cannot exercise Windows Event Log, PowerShell/CIM, WebView2, Windows services, or PowerToys host integration. Do not interpret passing portable tests as proof of successful Release x64 Windows builds.

On the target Windows machine, use Node.js 24+ and .NET 10, then run:

```powershell
.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64
```

Complete `docs/P6-WINDOWS-ACCEPTANCE.md` and carried-forward P0–P5 acceptance checklists in Standalone, PowerToys Run and PowerToys Command Palette. Do not mark RunMind 3.3.0 Windows-certified until the checks pass.

## Known limitations

- CI provider status is not fetched automatically; comparison accepts user-supplied results only.
- Flaky-test detection starts with RunMind-recorded script runs and is heuristic.
- Package-lock consistency fully resolves root npm package-lock v2/v3; other lockfiles are inventoried. npm/pnpm/Yarn audit and outdated output depends on installed manager version and advisory configuration; Bun auditing is deferred.
- Project memory is bounded, redacted, local to each host's WebView profile and keyed to the native workspace path fingerprint. It is not guaranteed to sync between the three host applications.
- Git/output limits, static index coverage and user approval remain explicit constraints.
