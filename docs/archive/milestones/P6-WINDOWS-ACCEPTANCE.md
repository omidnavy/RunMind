> **Archive:** Historical development record. This file describes the source state at the time it was written; use the current README, build guides, feature matrix, and feature-specific documentation for present behavior.

# P6 Windows Acceptance — RunMind 3.3.0

Run on Windows 11 with Node.js 24+ and .NET 10. Complete independently in RunMind Standalone, PowerToys Run and PowerToys Command Palette. Use a disposable project Git repository first; never validate restore/recovery against the only copy of important work.

## Release and host integration

- [ ] Verify the ZIP SHA-256 against its sidecar before extracting.
- [ ] Run `Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64`.
- [ ] Confirm Standalone, PowerToys Run and Command Palette package versions are `3.3.0`; Command Palette Appx identity/package version is `3.3.0.0`.
- [ ] Confirm the shared Development Intelligence tools/workflows appear in all three hosts.
- [ ] Confirm native workspace selection is canonical and the Git repository selector refers to that same workspace.

## Git review

- [ ] Prepare a fixture with staged changes, unstaged changes, an untracked file, a rename and a merge conflict.
- [ ] Confirm `git_review_snapshot` reports branch, separate staged/unstaged paths, untracked paths, conflict entries, whitespace check and truncation accurately.
- [ ] Confirm untracked file contents are read separately before RunMind explains them.
- [ ] Run Git Change Review and verify direct/transitive module impact, affected tests and HTML/CSS/SVG asset consumers against actual fixture content.
- [ ] Decline `gitRead` approval and confirm review/baseline capture does not access Git data; existing file edit must remain safe and ownership audit must disclose missing baseline evidence.
- [ ] Confirm none of the read-only workflows stage, commit, stash, reset, clean, merge or push.

## Build/test and CI evidence

- [ ] Use a fixture package.json with npm/pnpm/Yarn as applicable and declared test/build/quality scripts.
- [ ] Confirm Build & Test Intelligence discovers scripts, package manager, Node engine, tests and known CI configuration.
- [ ] Approve a test run via `project_run_script`; verify exact script, package path, timeout and output/result.
- [ ] Repeat a failing script twice with the same signature, then pass it; confirm repeated-failure and possible-flakiness signals.
- [ ] Confirm script execution is cancelled when approval is declined and no command is run.
- [ ] Supply a known CI result record and confirm local/CI match/difference/unknown is handled accurately. Verify RunMind never claims to have queried CI.

## Dependency and environment checks

- [ ] Confirm static inspection identifies package-manager/lockfile conflicts and supported package-lock version mismatches.
- [ ] Decline dependency audit approval and confirm no package-manager audit/outdated process runs.
- [ ] Approve npm/pnpm/Yarn audit/outdated checks where available; verify registry connectivity, advisory output and outdated results. Confirm no install/update/lockfile modification occurs.
- [ ] Decline environment probe approval and confirm no probe runs; approve it and verify Node.js, npm, pnpm, Yarn, Bun and Git versions where installed.
- [ ] Confirm declared Node engine/packageManager values are compared with observed versions, without labelling a version compatible solely because it is installed.

## Agent change review and safe recovery

- [ ] In a disposable project, have Agent create, edit, multi-edit and delete files; verify backups and ownership receipts are recorded with workspace-relative paths and available SHA-256 values.
- [ ] Confirm baseline Git state is captured only when `gitRead` permission allows it; pre-existing changed files remain unattributed.
- [ ] Change one Agent-modified file manually after the Agent write; verify audit reports that current content no longer matches the recorded after hash.
- [ ] Run Agent Change Review and inspect before/after changed-line preview, backup path and additional unrelated changed paths.
- [ ] Decline a restore; confirm nothing changes. Approve a restore; verify destination re-check and recovery-point backup.
- [ ] Verify no automatic bulk rollback or restore is performed.

## Project memory and task continuity

- [ ] Save a project decision with approval, close/reopen the same host and verify the decision is available for the same selected native workspace.
- [ ] Confirm memory is not visible after selecting a different workspace fingerprint.
- [ ] Interrupt a task with recorded operations, close/reopen the same host and confirm checkpoint is imported as interrupted.
- [ ] Explicitly resume it; inspect unknown operations before retrying. Confirm RunMind does not automatically replay tool calls.
- [ ] Verify memory bounds and that it contains redacted summaries/metadata rather than source contents or complete logs.
- [ ] Confirm Standalone, PowerToys Run and Command Palette may have separate memory profiles.

## Certification decision

- [ ] No regressions in P0–P6 acceptance tests.
- [ ] Record Windows build logs and any unavailable provider/counter explicitly.
- [ ] Fix failures and repeat the affected checklist in all three hosts.

Do not mark RunMind 3.3.0 Windows-certified until Release x64 builds and required P0–P6 live acceptance items pass on the target Windows installation.
