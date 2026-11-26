---
name: debugging
description: Diagnose failures through evidence and fix their underlying causes without regressions.
---
# Debugging

1. Reproduce or inspect the exact failure and capture the relevant error, inputs, environment, and execution path.
2. Trace the failing path to its root cause before changing code. Do not mask symptoms with broad catches, retries, or silent fallbacks.
3. Form a testable hypothesis and make a minimal targeted correction.
4. Add a regression test for the failure when the project supports tests.
5. Re-run the failing case and nearby relevant tests; inspect the final diff for collateral changes.
6. For Node.js performance issues, reuse an existing Node debugger session: inspect V8 heap usage, start CPU profiling before reproducing the workload, stop it afterward, and use sampled self-time hotspots as leads rather than exact wall-clock attribution.
7. For suspected memory leaks, collect timestamped heap samples across a representative workload. If object-retention evidence is needed, capture an approved baseline heap snapshot and a second snapshot after the workload, then compare them in Chrome/Edge DevTools Memory tooling. A rising heap alone is not proof of a leak.
8. State what evidence confirms the fix and what could not be tested.
