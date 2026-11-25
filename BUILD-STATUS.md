# RunMind 2.3.0 — Project intelligence and reusable developer workflows

## Release bundle status

- Source baseline: RunMind 2.2.7 Command Palette Settings Polish (128 entries).
- Shared project intelligence and workflow catalog added to `Shared/ChatWindow/scripts/agent/`.
- Project settings now expose explicit index build/refresh/clear and reusable developer workflow controls.
- Agent tools now include persistent project indexing, indexed project search/overview, bounded static project validation, and reusable workflow catalog/start operations.
- Standalone, PowerToys Run, and PowerToys Command Palette remain in the same solution and consume the shared WebView resources through their existing wildcard/content rules.
- Project/host metadata synchronized to 2.3.0; Command Palette package version synchronized to 2.3.0.0.

## Validation performed in this environment

See `VALIDATION-REPORT.md` for exact static checks and results. No Windows/.NET build or live WebView2 test is claimed here.

## Target-machine validation still required

- Build `RunMind.sln` with the .NET 10 Windows targeting pack and the repository's existing package dependencies.
- Launch Standalone, PowerToys Run, and Command Palette and verify shared UI/script loading.
- Grant a workspace and build/refresh its index; verify secret/dependency exclusions, incremental reuse, cancellation, and IndexedDB quota behavior.
- Search paths, source text, imports, and symbols for representative JavaScript/TypeScript, Python, C#, Go, Rust, Java/Kotlin projects.
- Save, run, and delete a custom workflow; confirm all file/Git mutations still pass through existing approval cards.

