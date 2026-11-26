> **Archive:** Historical development record. This file describes the source state at the time it was written; use the current README, build guides, feature matrix, and feature-specific documentation for present behavior.

# RunMind 3.3.0 — P6 Development Intelligence

RunMind 3.3.0 extends the shared Agent Core with Git-aware change review, build/test and CI-context analysis, dependency/environment diagnostics, Agent-owned change receipts, and bounded local project continuity. It is available through Standalone, PowerToys Run and PowerToys Command Palette. P0–P5 are retained.

## What's new

- **Git-aware change review:** add a bounded native Git review snapshot containing branch, porcelain status, staged and unstaged diffs, untracked path names, unmerged index entries and whitespace checks. `project_development_review` combines this evidence with the current project index, Change Impact Explorer, Test Impact Analysis and HTML/CSS/SVG asset consumers. Untracked file names are included but their contents are inspected separately; a truncated diff is marked.
- **Build & Test Intelligence:** inspect package manifests and scripts, test-file discovery, likely JS/TS tooling declarations and common GitHub Actions, GitLab CI, Azure Pipelines and CircleCI configuration. Approved runs through `project_run_script` execute only an exact declared package script with an identified npm/pnpm/Yarn/Bun executable. Redacted summaries of test/build/quality outcomes and captured Node.js/package-manager/OS/PowerShell versions feed local recurrence and possible environment-sensitive failure analysis.
- **Flaky/repeated failure signals:** group recorded runs by kind and normalized command; surface commands with both passing and failing runs, and failure signatures observed at least twice. This is heuristic history, not a statistical flakiness verdict.
- **CI comparison:** compare local run history with explicitly supplied CI result records. CI YAML is discovered statically; RunMind does not query a remote CI provider or invent remote status.
- **Dependency & environment intelligence:** compare package.json declarations to npm package-lock resolution, detect competing lockfile/package-manager signals, and expose approved, read-only package-manager vulnerability and outdated-package checks for npm, pnpm and modern Yarn. A fixed PowerShell probe can inspect Node.js, npm, pnpm, Yarn, Bun and Git versions against project declarations.
- **Agent-owned change receipts:** after Agent create/edit/multi-edit/delete/restore operations, record the workspace-relative path, operation, before/after SHA-256 where available, backup path and pre-write Git baseline. `project_agent_change_audit` displays bounded before/after line changes, checks whether the current file still matches RunMind's recorded write, and separates other new Git changes from paths already present at baseline.
- **Cross-session project continuity:** remember explicitly approved decisions, bounded redacted run summaries and interrupted Agent checkpoints. Checkpoints carry bounded operation outcome previews and may be restored only for the same native workspace fingerprint and the same host WebView profile. Resume remains explicit; uncertain operations must be inspected before retrying.
- **Built-in workflows:** add Git Change Review, Build & Test Intelligence, Dependency & Environment Health, Agent Change Review & Recovery, and Development Session Continuity.

## Safety and operating boundaries

- Git review is read-only. RunMind does not stage, commit, stash, reset, clean, merge or push as part of these workflows.
- Script execution, dependency audits/outdated checks, environment probes, remembering decisions and backup restoration require fresh approval. Package scripts execute project code with the current Windows user's permissions and may write files or use the network.
- Audit/outdated checks query configured package registries/advisory sources; they do not install, update, repair or rewrite dependencies.
- Automatic Agent backups remain under `.runmind-backups`. Restore checks the current destination and requires its existing explicit approval. A backup is evidence/recovery material, never permission to revert.
- Memory is local to the host's WebView profile, keyed by SHA-256 of the selected native workspace path. It is bounded to 60 decisions, 120 run summaries, 20 checkpoints, 100 owned-change receipts and 320 KB serialized data. It stores redacted summaries and metadata, not source contents, full logs, secrets or credentials. Standalone, PowerToys Run and Command Palette may have separate WebView profiles and therefore separate memories.
- No automatic CI-provider connector, package installation, dependency update, Git mutation, background test runner or autonomous rollback is introduced.
- Project-specific language-aware analysis/build/test/debug support remains JavaScript, TypeScript, JSX/TSX, HTML and CSS, with SVG treated as an asset format. No new programming-language support is added.

## Validation status

Portable Node regression tests and source checks are recorded in `BUILD-STATUS.md`. This source release is not Windows-certified until the P0–P6 acceptance checklists pass on the target Windows installation with Node.js 24+ and .NET 10.
