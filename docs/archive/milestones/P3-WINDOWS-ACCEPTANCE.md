> **Archive:** Historical development record. This file describes the source state at the time it was written; use the current README, build guides, feature matrix, and feature-specific documentation for present behavior.

# RunMind 3.2.0 — P3 Windows acceptance checklist

Run on the target Windows machine with Node.js 24+ and .NET 10. Capture the complete console output and use the exact packaged source tree.

## Build and shared-host integration

- [ ] Run `./Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64` from PowerShell.
- [ ] Confirm Standalone, PowerToys Run and Command Palette compile successfully.
- [ ] Confirm package versions are 3.2.0 and Command Palette Appx version is 3.2.0.0.
- [ ] Confirm `DeveloperInfrastructure.cs`, `SqliteRunner.cjs`, `OpenApiTestRunner.cjs`, `ApiTestRunner.cjs` and `PlaywrightRunner.cjs` are present beside each host's output executable.
- [ ] Open each host, select one canonical workspace, and confirm all five P3 workflows/tools use it without a second folder picker.
- [ ] Verify each P3 operation displays fresh approval before execution; verify Deep Research/delegated tasks cannot invoke restricted infrastructure tools.

## Podman/Docker workbench

- [ ] With Podman installed, run `runtime_info`, `ps`, `images`, `volumes` and `networks`; confirm accurate availability and bounded output.
- [ ] Repeat with Docker Desktop installed, if available. Confirm runtime selection reports which engine was used.
- [ ] Inspect a known container by exact name/ID, read bounded logs, read one-shot stats, and verify credential redaction before sharing output.
- [ ] Start/stop/restart a disposable test container only after reviewing the fresh confirmation, then run `container_inspect` and check the resulting state independently.
- [ ] Against a disposable Compose project, run `compose_config` and `compose_ps`, then explicitly approve `compose_build` and `compose_up`.
- [ ] Confirm Compose `down` does not pass `--volumes` or remove named/anonymous data volumes. Check volumes before/after.
- [ ] Confirm wrong runtime IDs, `--option` values, absolute paths, traversal and reparse-point compose files are rejected.
- [ ] Confirm output truncation and timeout terminate only the RunMind-owned runtime CLI process; report that running containers/services may continue.

## SQLite

- [ ] Use an existing disposable `.db`, `.sqlite` or `.sqlite3` database inside the selected workspace.
- [ ] Run inspect, tables, schema, foreign-key listing and parameterized query.
- [ ] Run `EXPLAIN QUERY PLAN SELECT ...` through query mode.
- [ ] Confirm read actions do not modify the database and traversal/symlink/outside-workspace database paths are refused.
- [ ] Attempt one parameterized INSERT/UPDATE in the approved execute path and verify the resulting state with a read-only query.
- [ ] Confirm multi-statement SQL, ATTACH/DETACH, VACUUM, PRAGMA writes, transaction control and virtual-table creation are rejected.
- [ ] Confirm database size, SQL, parameter, row and serialized output caps are enforced.
- [ ] Confirm a runtime lacking `node:sqlite` returns a clear compatibility error without installing packages.

## OpenAPI/API contract testing

- [ ] Use a disposable local API and an OpenAPI 3.0 JSON specification with explicit examples/defaults/enums.
- [ ] Confirm base URL is user-supplied and the specification `servers` entry never silently selects the host.
- [ ] Verify path/query/header/body samples and skipped operations when a required example is absent.
- [ ] Check 200/202/204 responses; confirm JSON schemas are applied only to their exact status and schema-less 204 remains valid.
- [ ] Confirm local JSON Pointer refs resolve and remote refs/cycles are rejected without outbound fetches.
- [ ] Confirm POST/PUT/PATCH/DELETE are skipped by default and require explicit fresh approval when enabled.
- [ ] Confirm redirects leaving the approved origin are blocked and response/body/time/operation caps are enforced.
- [ ] If YAML support is desired, confirm it uses an already-installed `yaml`/`js-yaml` package and does not install one.

## Runtime/log correlation

- [ ] Start a RunMind-owned development server and a Node debugger session with a test request/trace ID.
- [ ] Capture browser/API event slices from tool results and pass them with the exact server/debugger IDs.
- [ ] Confirm repeated trace/request/correlation/Journey IDs are grouped across sources and unrelated events remain separate.
- [ ] Confirm server timestamps, supplied browser/API timestamps, Inspector read-time timestamps and unknown timestamps are labelled correctly.
- [ ] Place representative bearer, token, password and URL query secrets in disposable log entries; verify redaction in returned timeline output.
- [ ] Confirm unknown/external process IDs are not scanned and no persistent correlation database/log store is created.

## Advanced browser debugging

- [ ] Run against a disposable local HTTP(S) fixture using the installed Playwright/browser channel.
- [ ] Inspect page landmarks/ARIA snapshot, console messages, uncaught page exceptions and source locations.
- [ ] Inspect network requests, resource type, response status/content metadata, request timing and failed requests.
- [ ] Reproduce one failure with locators learned from page inspection; capture viewport/full-page screenshots.
- [ ] Enable trace capture only after approval; confirm local ZIP opens in Playwright Trace Viewer and is bounded by artifact size/retention.
- [ ] Confirm cookies/login state are not persisted, downloads are disabled, file/privileged navigation is blocked and arbitrary page JavaScript evaluation is unavailable.
- [ ] Confirm expected CORS/CSP/mixed-content policy blocks remain visible and are not misreported as application failures.

## Regression acceptance

- [ ] Complete `docs/P0-WINDOWS-ACCEPTANCE.md`.
- [ ] Complete `docs/P1-WINDOWS-ACCEPTANCE.md`.
- [ ] Complete `docs/P2-WINDOWS-ACCEPTANCE.md`.
- [ ] Review `docs/P3-IMPLEMENTATION-AUDIT.md` and record any unsupported engine/browser-specific behavior.

Do not mark P3 Windows-certified until all applicable host builds and target-machine checks pass. Podman and Docker are separate runtimes; test each installed engine independently.
