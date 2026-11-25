# RunMind 2.3.0 validation report

## Source baseline

- Baseline: `RunMind-2.2.7-CommandPalette-Settings-Polish.zip`
- Baseline file count: 128
- Baseline SHA-256: `e730c23ee8b587c22141f60fd962735c09e6861845d527ad3de97b337fefe91f`
- Baseline archive integrity: `unzip -t` passed; no compressed-data errors.
- Scope audit: the release preserves the existing three-host solution and shared UI. Changes are limited to project-index/workflow implementation, their shared UI/tool integration, version metadata, and release documentation. Two new shared JavaScript modules were added.

## Checks performed

- Final source bundle: 131 files, including all three hosts and the validation report.

| Check | Result | Detail |
|---|---|---|
| JavaScript syntax | Passed | `node --check` passed for all 17 JavaScript files under `Shared/ChatWindow/scripts`. |
| Project-index runtime tests | Passed | A Node test harness exercised the index module with mock File System Access handles. |
| Static project-validation tool | Passed | A Node harness verified JSON/XML/Go manifest inspection, package-script extraction, test-file discovery, and explicit no-command-execution reporting. |
| Workflow catalog runtime tests | Passed | A Node test harness exercised the built-in and custom workflow catalog. |
| JSON parsing | Passed | Both repository JSON files parsed successfully. |
| XML parsing | Passed | All three `.csproj` files and the Command Palette package manifest parsed successfully. |
| Version synchronization | Passed | PowerToys Run plugin, Standalone, Command Palette project, plugin metadata, and MSIX manifest report 2.3.0 / 2.3.0.0 consistently. |
| Shared-host integration | Passed | Both new scripts are loaded by the canonical `Shared/ChatWindow.html`; all three host projects retain their existing shared ChatWindow wildcard content rules. |
| Archive integrity | Passed | The final 2.3.0 ZIP passed `unzip -t`; its SHA-256 is provided in the adjacent `.sha256` sidecar. |

### Project-index runtime coverage

- Indexed source files and common npm, Python, Cargo, Go, NuGet, Maven, Gradle, Composer, and Bundler manifests.
- Verified dependency and framework hints, including React, Next.js, FastAPI, Spring, and Tokio.
- Verified JavaScript import and symbol extraction and symbol search.
- Verified exclusions for `.env`, secret directories, and dependency directories.
- Verified best-effort redaction of API keys, refresh tokens, client secrets, private keys, access tokens, bearer credentials, credentialed URLs, and recognized token patterns before indexed text is persisted.
- Verified unchanged-file reuse, changed-file refresh, removed-file pruning, and cancellation without replacing the previous index.

### Developer-workflow runtime coverage

- Verified all five built-in workflows are available.
- Verified custom workflow save, reload, start/instruction retrieval, and delete.
- Workflows use the existing Agent tool runtime and approval mechanisms; no separate workflow executor or general shell/process bridge was added.

## Not performed in this environment

- No Windows/.NET solution build was performed. The `dotnet` CLI is not installed in this environment, and the projects target Windows/WPF/WebView2/PowerToys.
- No live Standalone, PowerToys Run, or PowerToys Command Palette startup test was performed.
- No live WebView2 File System Access / IndexedDB quota or permission-revocation test was performed.
- No real repository build, lint, or test command was executed. The new `project_validate` tool performs bounded static manifest checks and test-file discovery only; RunMind still has no general command-execution bridge.

## Implementation limits to retain

- The index is local to the WebView profile and Workspace Context. It is not exported with `.runmind` conversations or Project Profiles.
- Indexing is explicit, not automatic. Refresh after external project changes.
- Incremental reuse compares relative path, file size, and last-modified timestamp; unusual filesystems that change content without changing those metadata values can leave a stale entry until a forced rebuild.
- Indexing is bounded to 10,000 files, 50,000 directory entries, depth 10, 256 KiB per file, and 24 MiB of newly read data per refresh. Partial scans report their stop reason.
- Secret exclusion/redaction is heuristic and best-effort, not a guarantee that arbitrary application-specific secrets will be recognized. Do not treat the index as a secure secret scanner.
- Import/symbol extraction is heuristic for JavaScript/TypeScript, Python, C#, Go, Rust, and Java/Kotlin. Other indexed languages receive language hints and text search, not compiler-grade symbol resolution.
- `project_validate` does not execute build, lint, or test commands. The feature workflows must report this limitation rather than claim unperformed validation.
