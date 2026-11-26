# RunMind development-server test fixture

Dependency-free local HTTP server used for Windows runtime validation.

You can select either:

- **The fixture folder itself** (`test-fixtures\development-server`) as the native workspace. In this case leave `working_directory` empty and use `server.cjs` as the debugger `script_path`.
- **The repository root** as the native workspace. In this case use `test-fixtures/development-server` as `working_directory` and `test-fixtures/development-server/server.cjs` as the debugger `script_path`.

Do not combine the fixture-folder selection with the repository-relative path. The native bridge now normalizes this duplicate-prefix case too, but the examples above are the canonical usage.

Scripts:
- `dev`: HTTP server on `127.0.0.1:4317`
- `dev-alt`: second HTTP server on `127.0.0.1:4318`
- `fail`: exits with code 17 to exercise startup-failure reporting

The server exposes `/health` and prints a deliberately fake `token=...` log line to verify best-effort log redaction. Do not put real credentials in this fixture.
