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
6. State what evidence confirms the fix and what could not be tested.
