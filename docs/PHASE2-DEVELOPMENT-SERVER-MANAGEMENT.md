# Phase 2 — Development Server Management

## Scope

The shared Agent Core exposes `development_server_manage` through the existing native process bridge. It is deployed by the same shared source into Standalone, PowerToys Run, and PowerToys Command Palette.

### Actions

- `discover`: inspect the selected project directory's `package.json`, enumerate scripts, identify `dev` / `start` / `serve` as conventional candidates, and report the package manager indicated by `packageManager` or lockfiles.
- `start`: launch a named, existing package script through the installed Node.js and npm CLI. The exact script command and any detected `pre<name>` / `post<name>` lifecycle hooks are read back and shown in the fresh approval prompt before execution. The native bridge compares the approved command and hooks against the current manifest immediately before launch and rejects a changed manifest.
- `list`: list server sessions started by this host.
- `logs`: read bounded stdout/stderr output for one owned session.
- `health`: issue a bounded HTTP health request to the loopback readiness URL supplied at startup.
- `stop`: terminate the process tree only for a server session recorded as launched by this host. Requires fresh approval.

### Bounds and safety

- Up to eight active servers per host.
- Project-relative working directories must remain inside the selected native workspace.
- Script names are validated and must exist in the project's `package.json`; arbitrary executable/command-line arguments are not accepted by this tool.
- Uses Node.js plus the installed `npm-cli.js`; npm executes the selected package script using its normal platform script semantics. The project script itself is arbitrary project code and runs with the current Windows user's permissions.
- Starting and stopping always require fresh approval. The approval for startup includes the actual package script command.
- Readiness and health URLs are restricted to HTTP(S) loopback endpoints (`localhost` or an IP loopback address); automatic redirects are disabled.
- Optional port preflight binds the loopback port briefly; when a port is supplied and no readiness URL is given, RunMind probes `http://127.0.0.1:<port>/`. A detected conflict is reported; the process using that port is never terminated or modified.
- Stdout/stderr capture is capped at 48 KB per session; log reads return at most 120 lines. Common obvious `password`, `secret`, `token`, `api_key`, `Authorization`, and Bearer-token patterns are redacted before logs are returned (this is best-effort, not a guarantee that arbitrary application secrets are recognized).
- Server count, script names, timeouts, and URL lengths are bounded.
- RunMind retains ownership metadata only for processes it started. It does not discover-and-kill external processes. Stopping uses the recorded Process object and its process tree.
- Servers are not silently terminated when RunMind closes: termination is an explicit, separately approved action. Since session handles are in-memory, a server left running after its host exits must be stopped outside that old session; cross-restart adoption is not included in this increment.
- Current discovery/start support targets Node.js projects with `package.json` scripts. It does not yet infer arbitrary Python, .NET, Rust, or other language-specific commands.

## Example workflow

1. Select the native workspace when prompted.
2. Call `development_server_manage` with `action: "discover"` and optionally a project-relative `working_directory`.
3. Choose one of the returned script names, usually `dev`.
4. Start with `action: "start"`, the selected `script`, and optionally `port`, `readiness_url` (for example `http://127.0.0.1:5173/`), and `readiness_timeout_seconds`.
5. Use `list`, `logs`, and `health` while it runs.
6. Stop with `action: "stop"` and the returned `server_id`; approve the process-tree termination prompt.

## Validation

Portable tests assert native dispatch, all-host shared-source presence, tool schema, project-script validation, fresh approvals, loopback-only health checks, port-conflict handling, bounded multi-server/log behavior, and ownership-only termination. These checks do not replace Windows C# compilation or live process lifecycle testing.
