# Phase 1 completion extension — validation report

Date: 2026-11-21  
Source baseline: RunMind 2.6.3  
Project-index schema: v6  
Status: **Implementation candidate; Windows build/runtime release gate still open**

## Implemented in this extension

### JavaScript, JSX, TSX and TypeScript intelligence

- Keep Acorn 8.15.0 for detailed JavaScript AST analysis of `.js`, `.mjs`, and `.cjs` files.
- Bundle TypeScript compiler API 5.8.3 offline under `Shared/ChatWindow/vendor/typescript/`, including its Apache-2.0 license. It is loaded lazily when project indexing begins, not at application startup.
- Use the TypeScript compiler parser for `.jsx`, `.ts`, `.tsx`, `.mts`, and `.cts` files, including JSX/TSX syntax and parser diagnostics with source coordinates.
- Run a bounded virtual-workspace `ts.Program` and checker over indexed JS/JSX/TS/TSX source. It resolves workspace cross-file symbols, import bindings, TSX component references, and root `tsconfig` compiler options/`paths`.
- Bound checker analysis to 500 source files and 12 MB. It does not load default library declarations or run a full semantic diagnostic/type-check pass. Extended tsconfig chains/project references are not fully evaluated.
- Preserve redacted JS/TS analysis source in the local project index for incremental reuse. Reuse prior checker results when no indexed files have changed. Project-index schema/key is v6.

### Node.js module resolution

- Extend static workspace resolution with `.js` to `.ts/.tsx/.d.ts` source substitutions and tsconfig path aliases.
- Add `node_resolve_modules`, which uses the installed Node runtime's `createRequire().resolve()` and `import.meta.resolve()` to report actual CommonJS and ESM targets, including installed `node_modules` packages and conditional exports.
- The resolver is a fixed script, requires fresh approval, requires the native execution workspace to match the project browser workspace, and does not import or execute target modules.
- The static index still does not scan `node_modules`; custom loaders, runtime-generated specifiers, and code-dependent hooks are not executed.

### Cross-file public API refactoring

- Add preview-first `rename-public-api` for direct named exports.
- Update resolved named importers and re-export sites across up to 20 files, preserving consumer-local names with aliases where needed.
- Applying uses the existing approved single/multi-file edit path, per-file recovery backups, concurrent-change checks, and best-effort rollback.
- Default exports, export-star chains, dynamic/unresolved importers, and changes spanning more than 20 files are rejected rather than guessed.

### Startup management

- Support reversible enable/disable for exact HKCU/HKLM `Run` and `RunOnce` registry values in both registry views.
- Preserve the exact command and original `REG_SZ`/`REG_EXPAND_SZ` type in the RunMind recovery registry key.
- Support inventoried `.lnk` and `.exe` entries in the user/common Startup folders. Disable copies the item to a RunMind recovery folder, verifies SHA-256, then removes the original. Restore refuses to overwrite an existing item and verifies the restored hash.
- Disabled entries remain visible in startup inventory with their recovery information.
- Every mutation requires fresh native confirmation. Other Startup-folder formats remain inventory-only.

## Validation performed in this environment

| Check | Result |
|---|---:|
| Complete Node test suite | **66/66 passed** |
| JavaScript syntax checks, including vendor bundles | **20/20 passed** |
| JSON parsing | **2/2 passed** |
| XML/project/XAML/manifest parsing | **6/6 passed** |
| C# delimiter sanity | **2 source files balanced** |
| Git repository integration workflow | **Passed** |
| Node runtime conditional-export test | **Passed** (Node v22.16.0; ESM and CommonJS targets resolved without importing them) |
| TypeScript compiler parser/checker fixtures | **Passed** (TS, TSX, JSX, cross-file symbols, tsconfig paths) |

C# delimiter sanity is only a lexical balance check. It is not C# compilation.

## Explicit release gates not performed

This environment has no .NET SDK/MSBuild, PowerShell, or Windows runtime. Therefore:

- Standalone, PowerToys Run, and PowerToys Command Palette have **not** been compiled in this environment.
- Windows registry Run/RunOnce operations, Startup-folder file moves/copies, CIM hardware queries, `driverquery`, `netstat`, Windows services/processes, and Podman/Docker operations have **not** been executed on Windows.
- The TypeScript compiler bundle has been tested under Node/VM fixtures, but its lazy WebView loading and memory/performance behavior still need Windows WebView runtime validation.
- Cross-host model-driven workflows and multi-file rollback under actual WebView file permissions have not been exercised across all three hosts.

Run from the extracted source root on Windows with Node.js, the .NET 10 SDK, and restored dependencies:

```powershell
.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64
```

Then follow `docs/PHASE1-INTEGRATED-VALIDATION.md` in Standalone, PowerToys Run, and Command Palette. Phase 1 should only be marked complete after those builds and runtime scenarios pass.

## Remaining bounded limitations

- TypeScript analysis is compiler-backed but bounded: 500 files/12 MB, no default libs, no full diagnostics/type checking, and no complete project-reference/extended-tsconfig evaluation.
- The static project index excludes `node_modules`. Actual installed package resolution is available through the approved Node runtime tool; custom loaders and runtime-generated specifiers are not executed.
- Public API rename is limited to direct named exports and statically resolved import/re-export sites, with a 20-file cap.
- Startup-folder management supports `.lnk` and `.exe` entries; other file types are inventory-only.
- Extract-function remains conservative and rejects control-flow-sensitive selections. Multi-file writes are sequential, not filesystem-atomic; recovery backups are retained, but rollback can still fail and is reported.

This is the most complete Phase 1 source candidate, not a Windows-certified release. Version remains 2.6.3 pending the three-host validation gate.
