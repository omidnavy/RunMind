# RunMind development-server test fixture

Dependency-free local HTTP server used for Windows runtime validation. Select the repository root as the native workspace, then use `test-fixtures/development-server` as `working_directory`.

Scripts:
- `dev`: HTTP server on `127.0.0.1:4317`
- `dev-alt`: second HTTP server on `127.0.0.1:4318`
- `fail`: exits with code 17 to exercise startup-failure reporting

The server exposes `/health` and prints a deliberately fake `token=...` log line to verify best-effort log redaction. Do not put real credentials in this fixture.
