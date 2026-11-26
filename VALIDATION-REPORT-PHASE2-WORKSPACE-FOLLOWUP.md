# Phase 2 workspace and browser follow-up validation

Date: 2026-11-22

## Automated checks performed

- Node.js test suite: **128 passed, 0 failed** (`node --test --test-concurrency=1 tests/*.test.cjs`).
- JavaScript syntax checks passed for the changed Playwright runner, API runner, workspace path helper, workspace adapter, Agent tools, and provider schemas.
- Regression coverage includes UTF-8 BOM handling, native PowerShell stderr preference, safe selected-folder path normalization, installed Edge/Chrome launch fallback, Playwright `inspect_page`, native workspace selection idempotence, and workspace-relative development-server follow-up paths.

## Important validation limits

- This environment has Node.js 22.16.0 but does not have the .NET SDK, Windows PowerShell, or a Windows browser installation. The three Windows host projects were not compiled or launched here.
- Playwright launch behavior is covered with deterministic test doubles. A real Edge/Chrome/Chromium launch and Windows UI inspection still require validation on the user's Windows machine.
- A CDN HTTP 403 during Playwright browser installation is external to RunMind. The runner now tries installed Microsoft Edge and Chrome before bundled Chromium when `channel` is `auto`.

## Unified workspace requirement

The user requested selecting a workspace once for Agent file access and reusing it for native tools. This remains **not implemented** in this increment. Current Agent file operations use browser `FileSystemDirectoryHandle` objects; those objects intentionally do not reveal an absolute Windows path. Native process execution has a separate host-side root. Guessing the native path from the folder display name would risk authorizing the wrong directory.

The required implementation is the host-owned canonical workspace plus bounded, path-contained native workspace file RPC documented in `docs/UNIFIED-WORKSPACE-SELECTION.md`. Until that migration is complete, the two selection prompts may still occur. Native selection is now idempotent within a chat once its native root has been selected.
