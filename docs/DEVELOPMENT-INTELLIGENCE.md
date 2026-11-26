# Development Intelligence — RunMind 3.3.0

## Git-aware review

Use `git_review_snapshot` for a structured read-only snapshot or `project_development_review` for the combined report. The snapshot separates staged, unstaged, untracked, and conflicted paths; includes staged/unstaged unified diffs and `git diff --check` output; and reports truncation. Untracked contents are not part of Git diff and should be read from the workspace before summarizing them. Impact analysis is based on the current JS/TS/JSX/TSX/HTML/CSS/SVG index and remains static/heuristic where noted.

## Build and test intelligence

`project_build_test_intelligence` reads package manifests, scripts, test file names, common CI workflow files, tooling references and locally recorded RunMind script results. The analysis itself does not execute builds/tests. Use `project_run_script` only after reviewing its fresh approval prompt. It accepts an exact package.json script name and package-relative directory; supported package managers are npm, pnpm, Yarn and Bun when RunMind can identify one unambiguously. Test/build/quality result summaries are redacted and retained in local project memory. A command is flagged as potentially flaky when it has both a pass and a failure; repeated failures need at least two matching failure signatures.

CI workflow files are identified statically. To compare a remote run, the caller must supply the actual CI result in `ci_results` with a suite/script name, status, run ID and completion time as available. RunMind does not fetch remote GitHub/GitLab/Azure/CircleCI execution status in this release.

## Dependencies and environment

`project_dependency_health` action `inspect` checks package.json and npm package-lock v2/v3 evidence, lockfile inventory, exact-version mismatches and manager conflicts. `audit` requires fresh approval and invokes the configured npm/pnpm/Yarn audit and outdated commands. Advisory results reflect the configured source at the moment of execution; outdated JSON formats vary by package-manager version. Bun and other formats are inventoried but not audited in this release.

`project_environment_check` requires fresh approval and runs fixed, read-only version probes for Node.js, npm, pnpm, Yarn, Bun and Git. It compares discovered tools with `engines.node`, `packageManager` and lockfile declarations. It does not install or change anything.

## Agent-owned changes and recovery

RunMind records a receipt after a successful Agent create/edit/multi-edit/delete/restore tool action. A receipt can include a before hash, after hash, automatic backup path and Git status baseline captured immediately before the write. Git baseline capture follows the `gitRead` permission category; if Git read access is declined or no repository is selected, the file write may still proceed but the attribution audit is less complete.

`project_agent_change_audit` displays current status, compares the current file hash against the recorded after hash, and, when a backup exists, presents a bounded changed-line preview. It never restores automatically. Use `restore_file_backup` only after reviewing the exact backup and destination and approving the restore. Existing safety checks reject paths outside `.runmind-backups`, check whether the destination changed after approval, and create a recovery point before overwriting an existing destination.

A path being marked Agent-owned does not mean every later modification to it is still RunMind-owned. If the current hash differs from the receipt, the audit reports that the file no longer matches the recorded write.

## Project memory and task resumption

Memory is stored in the current host's WebView local profile under a key derived from a SHA-256 hash of the selected native workspace path. It is bounded and redacted. It stores decision notes, brief test/build/quality outcomes, checkpoint plans/statuses and operation previews, and Agent-owned change receipts. It does not store source file contents or complete command output.

A checkpoint is eligible for cross-session import only when it was interrupted or marked as needing recovery and the workspace fingerprint matches. The native workspace fingerprint is refreshed when memory is accessed so switching workspace selections within one chat cannot reuse the previous project's memory. The user must explicitly resume it. Operations whose outcome is unknown must be inspected before any retry; RunMind does not automatically replay them. Different host applications may have separate WebView profiles, so memory is not guaranteed to transfer between Standalone, PowerToys Run and Command Palette.

## Evidence limitations

- Git snapshots can be truncated by native output limits. A truncated diff must not be treated as complete.
- Architecture/test impact relies on the bounded static project index; dynamic runtime references and non-standard test frameworks may be missed.
- Flakiness is inferred only from RunMind-recorded runs of the same normalized command.
- A current diff occurring alongside an earlier failure is not proof of causation; run time and commit correlation are not automatically reconstructed.
- CI comparisons are only as reliable as the CI result data supplied by the user.
- Static lock checks fully resolve only supported root npm package-lock v2/v3 records; other lock formats, workspace layout, aliases, URL dependencies and protocol-specific specs may need package-manager-specific interpretation.
- Package-manager audit/outdated commands depend on installed package-manager versions and configured advisory/registry endpoints.
- Project language-aware analysis remains JS/TS/JSX/TSX/HTML/CSS plus SVG assets; all other project languages remain generic text for read/search only.
