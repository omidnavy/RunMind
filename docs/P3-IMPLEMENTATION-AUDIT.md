# RunMind 3.1.0 — P3 implementation audit

## Scope result

All five requested P3 areas are implemented in the source bundle and exposed through the shared Agent Core across Standalone, PowerToys Run and PowerToys Command Palette.

| Requested area | Implementation | Evidence |
|---|---|---|
| Podman/Docker Development Workbench | Fixed CLI allow-list, runtime/inventory, exact container operations, Compose lifecycle and bounded redaction | `Shared/DeveloperInfrastructure.cs`, `Shared/ChatWindow/scripts/agent/tools.js`, `Shared/ChatWindow/scripts/providers.js` |
| SQLite tooling | Existing-file inspect/schema/read/query/EXPLAIN and approved transactional DML/DDL | `Shared/SqliteRunner.cjs`, `Shared/DeveloperInfrastructure.cs`, `Shared/ApiTestRunner.cjs` |
| API contract/OpenAPI testing | OpenAPI 3.0/3.1 example-driven test generation, local refs, explicit target, status-specific schema validation | `Shared/OpenApiTestRunner.cjs`, `Shared/ApiTestRunner.cjs` |
| Better runtime/log correlation | Ephemeral timeline across owned servers, Node Inspector, supplied API/Playwright event slices; ID extraction and redaction | `Shared/ChatWindow/scripts/agent/tools.js` |
| Advanced browser debugging | Console/page exceptions, source locations, request timings/network failures, screenshots and opt-in Playwright trace | `Shared/PlaywrightRunner.cjs`, `Shared/ChatWindow/scripts/agent/tools.js`, `Shared/ChatWindow/scripts/providers.js` |

Five corresponding built-in developer workflows have been added to `Shared/ChatWindow/scripts/agent/developer-workflows.js`.

## Three-host integration

- All three host projects compile `Shared/DeveloperInfrastructure.cs`.
- All three package `SqliteRunner.cjs` and `OpenApiTestRunner.cjs` next to their existing Agent/native bridge files.
- Host package versions are 3.1.0; Command Palette package identity is 3.1.0.0.
- Native dispatch routes are `containers.workbench`, `sqlite.run` and `openapi.test.run`.

## Safety and limits

- Container workbench uses `UseShellExecute=false` and `ArgumentList`; no free-form command string. Each tool call requires fresh approval. No prune, image/volume removal or volume-deleting Compose down.
- SQLite database path remains inside the canonical workspace, rejects reparse/symlink paths, and enforces size/SQL/parameter/row/output limits. Read mode is opened read-only; writes are one statement, one transaction and always freshly approved.
- OpenAPI base target is explicit, HTTP(S), credential-free and approval-gated. Redirects are same-origin. Remote `$ref` fetches are prohibited. Mutations are opt-in.
- Runtime correlation is limited to exact selected RunMind-owned sessions and bounded supplied event slices. Sensitive text/URLs are redacted on the returned timeline. Correlation is not causality.
- Browser traces require explicit approval, are stored locally, and may contain page content. No arbitrary JavaScript evaluation, cookie persistence or live DevTools source stepping.
- SQLite only; MySQL/PostgreSQL are intentionally deferred.
- Source-language analysis remains JS/TS/JSX/TSX/HTML/CSS, with SVG as a first-class asset.

## Verification boundary

Portable Node tests validate runners, integration contracts and simulated browser behavior. They do not exercise an installed Docker/Podman engine or compile/run Windows native code. .NET 10 compilation and P0/P1/P2/P3 Windows acceptance remain pending on a target Windows machine; this bundle must not be called Windows-certified until those checks pass.
