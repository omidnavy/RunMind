# Developer Infrastructure — design and operation contract

## Architecture

Developer Infrastructure reuses the existing shared Agent Core and canonical native-owned workspace. The shared native bridge is compiled into the three Full Agent Windows hosts (Standalone, PowerToys Run and Command Palette). Runner is a separate restricted Quick Host. Node-based SQLite/OpenAPI runners are copied beside each host executable. No separate background service, database store, container daemon manager or second Agent Core is introduced.

## Podman/Docker Development Workbench

### Actions

- Runtime: `runtime_info`.
- Inventory: `ps`, `images`, `volumes`, `networks`.
- Exact container: `container_inspect`, `container_logs`, `container_stats`, `container_start`, `container_stop`, `container_restart`.
- Compose: `compose_config`, `compose_ps`, `compose_up`, `compose_down`, `compose_build`, `compose_logs`.

The native bridge selects Podman/Docker from PATH, builds a finite `ProcessStartInfo.ArgumentList`, disables shell execution, bounds stdout/stderr and timeouts, and redacts common credential assignments, bearer tokens and URI credentials. Compose file paths must be normalized workspace-relative `compose.yaml`, `compose.yml`, `docker-compose.yaml` or `docker-compose.yml`, contained in the workspace and free of reparse-point ancestors. Container names/IDs must match a strict single-identifier pattern.

Every action requires fresh Agent approval, including reads because inspect/config/log output can contain application information. Compose build may execute Containerfile/Dockerfile instructions; Compose up starts workloads and may pull images; container start/stop/restart changes an existing container's state. No prune/delete/image-remove/volume-remove or arbitrary CLI arguments exist. Compose down does not use `--volumes` or `--remove-orphans`. Command success does not claim application health or independent state verification.

## SQLite Workbench

Uses Node's built-in `node:sqlite` with `DatabaseSync`. Only existing workspace-contained `.db`, `.sqlite` and `.sqlite3` regular files are accepted. Reads open `readOnly: true`. `inspect`, `tables` and `schema` report integrity, schema, columns and foreign keys. `query` accepts one SELECT/WITH/EXPLAIN or a strict read-only PRAGMA allowlist and supports bound parameters. `execute` accepts one INSERT/UPDATE/DELETE/REPLACE or ordinary CREATE/ALTER/DROP statement, starts an immediate transaction and commits/rolls back as one operation. It blocks ATTACH/DETACH/VACUUM, transaction-control statements, PRAGMA writes and virtual tables. The approval screen previews target DB, SQL and parameter count; the SQL is not automatically rewritten. Rows/output/database/SQL/parameter limits are enforced.

Node 24's `node:sqlite` module is documented as a release-candidate API in the current Node 24 documentation. Runtime compatibility must be checked on the actual installed Node build. This is SQLite only; MySQL/PostgreSQL are future scope.

## OpenAPI contract testing

Accepts an existing workspace-relative OpenAPI 3.0/3.1 document. JSON is parsed with built-ins. YAML uses `yaml` or `js-yaml` only if already installed. The runner selects examples/defaults/enums, skips required parameters or bodies without usable samples, caps generated operations at 20, and resolves only local JSON Pointer `$ref` references (no outbound network fetch). Request path parameters are encoded; query/header examples are applied. Only HTTP(S) `base_url` supplied by the user determines the host; the document's `servers` section never selects the target. Every request and redirect remains on the approved origin. Mutating methods are excluded by default and require explicit enablement and fresh approval.

Declared 2xx statuses are checked independently. A JSON schema is associated with its declared response status; it is never accidentally applied to a different status (for example, a body schema for 200 is not applied to a schema-less 204). The response validator supports a bounded JSON Schema subset: types, required/properties/additionalProperties, enum/const, numeric/string/array/object bounds, uniqueItems, and allOf/anyOf/oneOf with depth and output caps. Full JSON Schema conformance, remote refs, callbacks, OAuth negotiation, and fuzzing are not claimed.

## Runtime/log correlation

`runtime_log_correlation` accepts up to eight exact RunMind-owned development-server IDs and up to eight exact Node Inspector session IDs. It may additionally receive at most 100 explicitly supplied browser events and 100 API events, each bounded to 8 KB. It creates an ephemeral, maximum-500-item timeline, recognizes traceparent/trace IDs/request IDs/correlation IDs/Journey IDs, and groups identifiers found in multiple source families. Server logs are redacted for common credentials; URLs redact credential-bearing query parameters and fragments. Browser/API event timestamps are labelled as supplied-source time; Inspector events are marked read-time when no source timestamp exists. A shared identifier is contextual correlation evidence only—not causality, exact clock synchronization or proof of a shared transaction. Nothing is persisted as a second event store.

## Advanced browser debugging

The shared Playwright runner exposes structured `inspect_page`, `inspect_console` and `inspect_network` actions alongside navigation, locator interactions, assertions and screenshots. Console errors/warnings and page exceptions carry bounded text/source locations; network diagnostics include request method, safe URL, resource type, status, content-type/size/cache data and Playwright resource timing where available. Expected CORS/CSP/mixed-content policy blocks remain visible and are not silently merged with ordinary application errors. Optional `capture_trace` stores a bounded local Playwright trace ZIP with screenshots and DOM snapshots; its contents may be sensitive. A new ephemeral browser context is used; cookies/login state are not persisted, downloads are disabled, service workers are blocked, and arbitrary page JavaScript evaluation is not exposed.

This is advanced browser diagnostics and trace-assisted debugging, not a full source-level DevTools debugger. Live browser JS breakpoints/step execution are not included in the current Playwright browser workflow.
