# RunMind 3.0.0 — P3 Developer Infrastructure

RunMind 3.0.0 retains the full P0/P1/P2 implementation and adds the requested P3 developer infrastructure across Standalone, PowerToys Run and PowerToys Command Palette.

## Included capabilities

1. **Podman/Docker Development Workbench** — direct fixed-argument CLI access for runtime info; container, image, volume and network inventory; exact container inspect, logs, one-shot stats, start, stop and restart; and Compose config, ps, up, down, build and logs. Every call is approved. No arbitrary shell or command-argument passthrough. Compose down does not remove volumes or orphans. State changes report `verification: not_performed` until a separate inspection/health check is performed.
2. **SQLite Workbench** — inspect quick-check/integrity, tables, schema, columns and foreign keys; read-only parameterized query and EXPLAIN; one explicitly approved DML/DDL statement in a transaction. Workspace-contained existing database files only; 512 MB DB cap, one statement, 32 KB SQL, 100 parameters, 200 rows and 256 KB output. SQLite only.
3. **OpenAPI Contract Testing** — OpenAPI 3.0/3.1 JSON built in; YAML through an already installed `yaml` or `js-yaml` parser. Bounded example-driven GET/HEAD/OPTIONS request generation; POST/PUT/PATCH/DELETE excluded by default and require explicit approval. Local JSON Pointer refs only, explicit user-selected base URL, same-origin redirect restriction, response status verification and per-status JSON Schema validation. Missing examples are skipped, not fabricated.
4. **Runtime/Log Correlation** — ephemeral timeline from exact RunMind-owned development-server and Node Inspector sessions plus explicitly supplied API/Playwright event slices. Recognizes traceparent/trace IDs, request IDs, correlation IDs and Journey IDs. Timestamp provenance is explicit. No persistent event store or causal inference.
5. **Advanced Browser Debugging** — structured Playwright page/ARIA inspection, console and page exception inspection with source locations, failed request/HTTP error classification, resource timing, screenshots and optional trace ZIP. Temporary browser context, no cookies/login persistence, downloads disabled, explicit approval. Trace artifacts may contain sensitive DOM content/screenshots. No arbitrary JavaScript evaluation or live browser-source breakpoints.

## Boundaries

- MySQL and PostgreSQL are intentionally deferred.
- Database creation/deletion/migration tooling is not included.
- No Docker/Podman installation, daemon/WSL setup, prune, image deletion, volume deletion or arbitrary CLI passthrough.
- YAML parsing does not install dependencies.
- OpenAPI remote refs, OAuth flows, payload fuzzing and the full JSON Schema vocabulary are not implemented.
- Runtime correlation is response-only and does not persist a second log store.
- Playwright trace is not a complete Playwright Test-runner trace with assertion history; the current automation runner records its browser actions, snapshots, screenshots and network activity.
- Windows/.NET compilation and live Windows/Podman/Docker acceptance must be performed on a target Windows installation before certification.
