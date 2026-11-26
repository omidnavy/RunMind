# Project Mission Control

Project Mission Control is RunMind's read-only project control center. It aggregates the existing persistent project index and the P1 analysis services into one versioned report. It is available as the `project_mission_control` Agent tool, through the built-in Project Mission Control workflow, and in Agent Settings. All three hosts share the same `Shared/ChatWindow` implementation.

## Design goals

- Give one trustworthy overview without creating a second index, database, background service or command runner.
- Put important findings and evidence in front of the user rather than returning opaque scores or counters alone.
- Make stale or partial evidence explicit.
- Keep architecture understanding distinct from operational diagnostics and static release hygiene.
- Let users supply changed paths explicitly for test-impact analysis without pretending to infer a Git diff.
- Keep the report read-only and safe to render even when project files contain untrusted text.

## Report sections

| Section | Information |
|---|---|
| Overall status | `blocked`, `attention`, `incomplete` or `static-checks-passed`, with an explanation. A static pass is not release approval. |
| Index | Root, timestamp, age, freshness, indexed/scanned counts, bytes read, reused/removed/skipped counts, truncation reason and refresh outcome. |
| Project Understanding | Module boundaries, entry-point candidates, local import edges, resolved/unresolved bindings, high-coupling examples, circular dependency paths and parser coverage. |
| Project Diagnostics | Static manifest, package-manager/lockfile, script, test-discovery, configuration and import-graph checks with severity and evidence paths. |
| Release Readiness | Each static criterion, package-manager/build/test script suggestions, and gates not verified by static analysis. Suggested commands are not executed. |
| Test Impact | Optional changed paths, analyzed/unanalysed/unknown inputs, affected module count, test candidates and direct/high, transitive/medium or filename-only/low confidence. |
| Recommended next actions | De-duplicated findings ordered critical → high → medium → low, with source area and supporting paths. |
| Evidence and limitations | Supported language boundary, static-analysis constraints and verification operations that were not performed. |

## Freshness behavior

A report considers an index fresh for five minutes from its recorded `updatedAt` value. If older or missing a valid timestamp, Mission Control waits for any already-running automatic refresh, then attempts `buildProjectIndex({ force: false })` to incrementally refresh the existing index. It reloads the persisted index before generating the report. It never forces a full rebuild solely because the index is stale.

If refresh fails, Mission Control keeps the available index, returns the refresh error, and marks the snapshot stale/incomplete. It must never silently label old evidence current. Normal periodic index refresh remains active while the chat is visible.

## Status semantics

- **Blocked:** static diagnostic errors or release-readiness blockers were found.
- **Incomplete:** the index remains stale, the index scan was truncated, or its evidence freshness cannot be established.
- **Attention:** static warnings or circular dependency groups need review.
- **Static checks passed:** no issues were found by the current static checks. This does not prove runtime health or release readiness.

High-coupling modules are a heuristic and are surfaced as review recommendations; they do not independently make the overall status fail.

## Agent use

Call `project_mission_control` with an optional `changed_paths` array containing workspace-relative paths. The report returns structured evidence. Use `project_understanding`, `project_diagnostics`, `project_test_impact` or `project_release_readiness` to investigate individual sections more deeply. Always cite exact workspace-relative paths and differentiate static facts, inferred candidates and unverified operational claims.

Do not infer changed paths from Git unless a separate Git operation has actually supplied them. Do not state that tests are affected with certainty: impact is based on the static import graph and may miss dynamic imports, generated references, conditional runtime loading and external test harness behavior.

## Settings dashboard

Open **Agent Settings → Project → Project intelligence → Run Mission Control**. The optional changed-path text area accepts one workspace-relative path per line (up to 100 paths). Leave it blank to omit Test Impact Analysis. The dashboard displays the same report returned by the Agent tool.

The UI creates elements through DOM APIs and writes untrusted project evidence with `textContent`. It does not interpolate project text into `innerHTML`. Detailed diagnostics, release criteria, architecture examples, recommendations and verification gaps are expandable to keep the dashboard navigable.

## Safety and verification boundary

Mission Control is read-only. It does not run shell commands, Node/npm scripts, builds, tests, audits, Git operations, browser automation or native system probes. It does not write project files, install dependencies, modify package metadata, commit, tag, sign, package or publish anything.

The release section explicitly leaves these gates unverified unless another approved workflow actually performs them:

- Git working-tree cleanliness and exact changed-file inventory.
- Actual Node/package-manager/.NET builds and tests.
- Live dependency vulnerability results.
- Artifact signatures, provenance and SBOM generation/verification.
- Target Windows packaging and runtime acceptance.
- Release authorization and publication/deployment.

## Language boundary

Language-aware project analysis remains limited to JavaScript, TypeScript, JSX/TSX, HTML, CSS and SVG. Other source files can remain in the generic text index for reading/searching, but they are not semantically parsed or included in JavaScript/TypeScript test-impact dependency analysis. They are reported as unanalysed when supplied as changed paths.
