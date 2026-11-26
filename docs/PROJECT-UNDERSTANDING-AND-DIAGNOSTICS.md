# Project Understanding and Project Diagnostics

These are separate developer capabilities built on the same persistent project index.

## Project Understanding

Project Understanding is the codebase intelligence layer. It maps architecture, entry points, modules, dependency direction, exports, symbols, references, calls, and important execution paths. It uses the persistent index, JavaScript AST analysis, TypeScript compiler/checker analysis where supported, module-resolution evidence, and source inspection. It is intended to be reused by implementation, debugging, code review, and impact analysis.

It does not run build commands, install dependencies, or change project files. Import-graph heuristics are discovery hints; important relationships must be checked in source.

## Project Diagnostics

Project Diagnostics is the operational health/readiness layer. It checks manifest validity, dependency and lockfile consistency, configured toolchains, project scripts, build/test/lint configuration, test discovery, environment requirements, and—when explicitly approved—installed runtime/tool versions and actual build/test/audit results.

Static observations, live environment probes, and executed commands must be reported separately. The workflow is read-only by default. It does not install, upgrade, repair, format, or modify source/configuration files.

## Relationship to Build & Test and Debugging

Project Diagnostics can identify which build/test commands are configured and whether prerequisites appear ready. Build & Test executes selected project commands only after displaying the command and receiving fresh approval. Debugging investigates runtime behavior using the relevant runtime debugger, logs, and reproduction steps.

All three consume Project Understanding rather than duplicating its index or symbol graph.

## Workspace behavior in RunMind 2.7.0

The selected conversation workspace is now native-host-owned and shared with Agent file operations through a path-contained file bridge. Project Understanding and static Project Diagnostics use this same workspace. Live environment probes and build/test commands use relative paths inside it and require fresh approval. Browser-only contexts without a native host retain the browser directory-picker fallback.
