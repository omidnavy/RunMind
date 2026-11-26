# Project understanding and diagnostics

These are separate developer capabilities built on the same persistent project index.

## Project Understanding

Project Understanding is the codebase intelligence layer. It maps architecture, entry points, modules, dependency direction, exports, symbols, references, calls, and important execution paths. It uses the persistent index, JavaScript AST analysis, TypeScript compiler/checker analysis where supported, module-resolution evidence, source inspection, and a bounded SVG asset-reference graph. SVG is indexed as a first-class vector asset; local `href`/`xlink:href` links and references from HTML/CSS are normalized relative to their owning files. SVG is not treated as a programming language. It is intended to be reused by implementation, debugging, code review, and impact analysis.

It does not run build commands, install dependencies, or change project files. Import-graph heuristics are discovery hints; important relationships must be checked in source.

## Project Diagnostics

Project Diagnostics is the operational health/readiness layer. It checks manifest validity, dependency and lockfile consistency, configured toolchains, project scripts, build/test/lint configuration, JavaScript/TypeScript test discovery, environment requirements, and—when explicitly approved—installed runtime/tool versions and actual build/test/audit results.

Static observations, live environment probes, and executed commands must be reported separately. The workflow is read-only by default. It does not install, upgrade, repair, format, or modify source/configuration files.

## Project Mission Control

Project Mission Control is the read-only structured control center over the same persistent index. It reports index freshness/completeness, architecture coverage and examples, full static diagnostic checks, release criteria, prioritized evidence-backed next actions and optional test-impact results for explicitly supplied changed paths. If the index is older than five minutes, it attempts an incremental refresh and discloses a failed refresh rather than silently presenting stale data as current. Its Settings dashboard is rendered with safe DOM APIs and the Agent tool returns the same versioned report. It does not become a second index or execute commands.

## Test Impact Analysis

Test Impact Analysis accepts explicit workspace-relative changed paths and follows reverse static import dependencies for JavaScript/TypeScript changes to find direct and transitive JavaScript/TypeScript test candidates. Other indexed file types are reported as unanalysed. Filename-only matches are low-confidence suggestions. It never infers a Git diff, runs tests, or treats heuristic relationships as proof; dynamic imports and external test harnesses remain limitations.

## Release Readiness

Release Readiness checks static release hygiene: version metadata, changelog/README presence, CI configuration, test discovery, package-manager/lockfile consistency, build/test scripts, and diagnostic errors. Git cleanliness, live vulnerability results, builds/tests, signatures, SBOMs, package artifacts and publication are separate verification gates and are not claimed by static checks.

## Relationship to Build & Test and Debugging

Project Diagnostics can identify which build/test commands are configured and whether prerequisites appear ready. Build & Test executes selected project commands only after displaying the command and receiving fresh approval. Debugging investigates runtime behavior using the relevant runtime debugger, logs, and reproduction steps.

All three consume Project Understanding rather than duplicating its index or symbol graph.

## Workspace behavior

The selected conversation workspace is now native-host-owned and shared with Agent file operations through a path-contained file bridge. Project Understanding, Project Diagnostics, Project Mission Control, Test Impact Analysis and static Release Readiness use this same workspace. Live environment probes and build/test commands use relative paths inside it and require fresh approval. Browser-only contexts without a native host retain the browser directory-picker fallback.
