# API and backend testing

## Scope

Adds `api_test_run` to the shared Agent Core. It executes a bounded sequence of structured HTTP requests using the Node.js built-in `fetch`; it adds no runtime package dependency and does not invoke a shell, project test framework, or arbitrary script.

## Supported requests

- HTTP methods: GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS.
- JSON and text request bodies, custom headers, and relative or same-origin absolute paths.
- Status-code expectations (default: any 2xx response).
- Response-header assertions: exists, equals, contains.
- JSON-path assertions: exists, equals, contains, type, one_of, greater_than. Paths support dotted properties and numeric array indexes.
- Body substring assertions, bounded response previews, response timing, and per-request pass/fail results.
- Secret placeholders such as `{{env:API_TOKEN}}` are expanded from the current user's inherited process environment. Values are not included in the approval preview or tool result; common credential fields and sensitive response headers are redacted.

## Safety and bounds

- Every suite requires fresh user approval; no persistent/always-approve permission is offered.
- Disabled in Deep Research and delegated sub-agent tasks.
- The base URL must be HTTP or HTTPS without embedded credentials or fragments. Each request and every redirect must remain on the same origin.
- Maximum 20 sequential requests per suite; methods that change state are allowed only after the explicit approval.
- Per-request timeout: 5–120 seconds; native runner total timeout is capped at five minutes.
- Request body: 64 KB; response body: 1 MB per request; aggregate tool result: 256 KB.
- Redirect limit: five. Response previews are truncated; JSON fields whose names indicate credentials, secrets, tokens, passwords, or API keys are redacted.
- Requests execute as the current Windows user. This is an approved HTTP client, not a network sandbox; the selected project workspace is required for native execution context but project files are not read by the runner.

## Validation

`tests/api-backend.integration.test.cjs` uses a local ephemeral HTTP server to exercise real Node `fetch` behavior, JSON/text responses, request bodies, header and JSON assertions, environment-variable secret expansion, cross-origin redirect blocking, response redaction, and failure reporting. Host wiring checks verify the shared runner is copied to all three native host outputs and dispatched through `AgentHostBridge`.

The portable suite is run with:

```powershell
node --test tests/*.test.cjs
```

Windows C# compilation and host runtime behavior must still be validated with:

```powershell
.\Validate-RunMind-Windows.ps1
```

## Example Agent request

```json
{
  "base_url": "http://127.0.0.1:3000",
  "timeout_seconds": 20,
  "requests": [
    {
      "name": "health",
      "method": "GET",
      "path": "/health",
      "expect": {
        "status": 200,
        "json": [
          { "path": "status", "operator": "equals", "value": "ok" },
          { "path": "uptime", "operator": "greater_than", "value": 0 }
        ]
      }
    },
    {
      "name": "create-item",
      "method": "POST",
      "path": "/api/items",
      "headers": { "Authorization": "Bearer {{env:API_TOKEN}}" },
      "body_json": { "name": "sample" },
      "expect": { "status": 201, "json": [{ "path": "id", "operator": "exists" }] }
    }
  ]
}
```

Set environment variables before launching RunMind so the host process inherits them. For example, in PowerShell:

```powershell
$env:API_TOKEN = 'your-token'
Start-Process 'RunMind.exe'
```

Do not paste real credentials into ordinary prompts when an environment placeholder can be used instead. The runner inherits the environment of the RunMind process; changing a shell variable after RunMind is already running does not update that process.
