# RunMind 2.7.0 — P0 Foundation Build Status

## Release scope

P0 consolidates unified workspace authority, durable development-server ownership/recovery, Agent tool selection/argument/final-response reliability, Playwright CORS policy classification, and the previously implemented Phase 2 developer tools. Project-language support remains JavaScript, TypeScript, JSX/TSX, HTML and CSS; Node package managers are npm, pnpm, Yarn and Bun.

## Validation performed in this environment

- Node.js: v22.16.0.
- Full serial portable Node test suite: **158 passed, 0 failed**.
- JavaScript syntax checks: shared providers, Agent tools/runtime/MCP/project index, Playwright runner and browser fixture.
- XML parsing: all three host `.csproj` files and Command Palette package manifest.
- JSON parsing: PowerToys Run plugin metadata and fixture manifests.
- Release ZIP integrity and SHA-256 verification.

## Windows validation not performed here

The current build environment does not have the .NET SDK or Windows runtime. Therefore this report does not claim a C# compile, WebView2 startup test, Windows process/registry behavior test, or actual Chromium/Edge run for the 2.7.0 package.

On Windows with Node.js 24 and .NET 10, run from the repository root:

```powershell
.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64
```

Then execute every manual acceptance test in `docs/P0-FOUNDATION-ACCEPTANCE.md`. P0 is source-complete and portable-test-verified, but Windows-certified only after this gate passes on the target machine across all three hosts.

## Known limitations

- After a RunMind host restart, recovered server sessions can restore metadata only; historical stdout/stderr cannot be reattached to the original process pipes and is reported unavailable.
- Server recovery requires the same conversation context and workspace selection identity. A process from a different/unknown identity is not adopted or terminated.
- Language-aware project functionality is intentionally limited to JS/TS/JSX/HTML/CSS. Other file formats may be searched as generic text, but no other language-specific parsing or build/test toolchains are included.
