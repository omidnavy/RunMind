---
name: testing-and-quality
description: Select and run proportionate validation for code and project changes.
---
# Testing and Quality

1. Discover the project's actual test, lint, build, and validation commands; do not invent commands.
2. Choose checks proportional to the change, including regression tests and boundary/error cases.
3. Prefer deterministic tests; isolate external services and use explicit fixtures where appropriate.
4. Run checks and report exact outcomes. Never label static inspection as compilation or runtime testing.
5. Review failure output rather than suppressing or skipping failures.
6. Check the final diff, generated artifacts, manifests, and documentation for consistency.
