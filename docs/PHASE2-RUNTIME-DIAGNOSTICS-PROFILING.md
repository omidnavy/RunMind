# Phase 2 — Node.js Runtime Diagnostics & Profiling

## Scope

This increment extends the existing shared Node Inspector session. It adds no new agent, package, host-specific implementation, or external profiler dependency. Standalone, PowerToys Run, and PowerToys Command Palette use the same `AgentHostBridge` and `NodeDebuggerSession` implementation.

## Agent tools

- `node_runtime_diagnostics`: read the target's V8 heap usage (`usedSize`, `totalSize`, embedder heap and backing storage) and current debugger-session state.
- `node_runtime_cpu_profile`: start/stop V8 sampling at a 1,000-microsecond interval. Stop writes a Chrome DevTools-compatible `.cpuprofile` file and returns node/sample counts, duration, and the top sampled self-time frames.
- `node_runtime_heap_snapshot`: stream `HeapProfiler.addHeapSnapshotChunk` events directly to a local `.heapsnapshot` file rather than retaining the snapshot in the debugger event queue.
- `node_runtime_memory_monitor`: start, stop, and read periodic `Runtime.getHeapUsage` samples with timestamps. Interval is 1–60 seconds, duration 5–600 seconds, and the in-memory ring retains at most 120 samples.

## Artifacts and bounds

- Artifacts are written under `%LOCALAPPDATA%\RunMind\RuntimeArtifacts`.
- CPU profile JSON is limited to 32 MB; Inspector messages are bounded at 36 MB.
- Heap snapshots are streamed to disk and limited to 128 MB. An incomplete/oversized snapshot is discarded.
- Artifact retention keeps at most the 20 newest `.cpuprofile` and `.heapsnapshot` files in the RunMind-owned artifact directory.
- Runtime memory samples are transient and capped at 120 per session.
- Closing a debugger session stops the memory monitor, aborts any incomplete heap snapshot, and best-effort stops active CPU profiling.

## Approval and isolation

- Runtime diagnostics and reading samples are read-only.
- Starting CPU profiling requires fresh approval; stopping it does not.
- Starting a memory monitor requires fresh approval; stopping/reading it does not.
- Heap snapshot capture always requires fresh approval because it can pause the target and consume substantial memory.
- Runtime tools are unavailable to Deep Research and delegated sub-agent tasks.
- The existing Inspector loopback-only restriction and debugger-session ownership rules remain in force. Attaching to an external process never grants RunMind permission to terminate it.

## Known limits

- Heap telemetry is V8 heap usage, not total process working-set/RSS. The current Node Inspector protocol does not expose the Chrome `Performance` domain used by browser targets, so this increment does not claim browser-style performance metrics.
- CPU hotspots are based on V8 sampling and should be interpreted as sampled self-time, not exact wall-clock accounting.
- Heap snapshots are raw V8 DevTools snapshots; they can be opened in Chrome/Edge DevTools Memory tooling. RunMind does not yet provide a full dominator-tree or retained-size UI.
- Memory samples are bounded and session-local; they are not persisted as a long-running telemetry database.
- The native C# bridge still requires the Windows validation script to compile all three host projects. Node Inspector integration tests exercise the actual runtime protocol but do not compile C#.

## Validation

`tests/node-runtime-profiling.integration.test.cjs` starts a real Node Inspector process and verifies `Runtime.getHeapUsage`, V8 CPU profile start/stop and samples, and streamed heap snapshot chunks. Contract checks verify the four native dispatch routes, tool exposure, approval gates, and size/sample bounds. Run the complete `node --test tests/*.test.cjs` suite, then run `Validate-Phase1-Windows.ps1` on Windows to compile and validate all three hosts.
