---
name: coding
description: Implement maintainable code changes in an existing project, following its architecture and conventions.
---
# Coding

1. Inspect the relevant project overview, instructions, files, call paths, and existing tests before editing.
2. Preserve the project's language, module system, formatting, naming, and dependency choices. Do not introduce a framework or abstraction without a concrete need.
3. Make the smallest coherent change that fully satisfies the request. Reuse existing utilities and avoid duplicate sources of truth.
4. Keep unrelated user changes intact. Never use destructive Git commands or push changes unless explicitly requested.
5. Update tests and documentation when behavior or contracts change.
6. Run the most relevant available validation. Distinguish checks actually run from checks merely recommended.
7. Finish with a concise summary of files changed, behavior, tests, and known limitations.
