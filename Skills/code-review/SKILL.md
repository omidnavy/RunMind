---
name: code-review
description: Review code changes for correctness, security, regressions, and maintainability.
---
# Code Review

1. Review the complete diff against the requested behavior and existing contracts.
2. Prioritize correctness, security boundaries, data loss, permissions, concurrency, compatibility, and regressions.
3. Trace callers and downstream consumers before changing shared contracts.
4. Look for unsafe process invocation, path traversal, injection, secret exposure, race conditions, unbounded work, and misleading success reporting.
5. Report findings by severity with file/line references and concrete impact. Do not invent findings or praise in place of analysis.
6. Re-review after fixes and verify that the final diff contains only intended changes.
